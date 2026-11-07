import { spawn, type ChildProcess } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import {
  FORMAT_DESCRIPTORS,
  canConvertFormat,
  inspectFileSignature,
  normalizeFormatId,
  resolveSafeOutputPath,
} from './formatMatrix';
import type {
  AutomationRule,
  ConversionJob,
  ConversionOptions,
  EngineDiagnostic,
  JobStatus,
} from './types';

export function computeFileSha256(filePath: string): string {
  const hash = crypto.createHash('sha256');
  const data = fs.readFileSync(filePath);
  hash.update(data);
  return hash.digest('hex');
}

export function spawnSafeProcess(
  binary: string,
  args: string[],
  options: { timeoutMs?: number; onChild?: (child: ChildProcess) => void } = {}
): Promise<{ code: number; stdout: string; stderr: string }> {
  return new Promise((resolve, reject) => {
    const child = spawn(binary, args, {
      shell: false,
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    if (options.onChild) {
      options.onChild(child);
    }

    let stdout = '';
    let stderr = '';
    let settled = false;

    const timer = options.timeoutMs
      ? setTimeout(() => {
          if (!settled) {
            settled = true;
            child.kill('SIGKILL');
            reject(new Error(`Tiempo de ejecución agotado (${options.timeoutMs} ms) para ${binary}`));
          }
        }, options.timeoutMs)
      : null;

    child.stdout?.on('data', (chunk: Buffer) => {
      stdout += chunk.toString('utf8');
    });

    child.stderr?.on('data', (chunk: Buffer) => {
      stderr += chunk.toString('utf8');
    });

    child.on('error', (err) => {
      if (timer) clearTimeout(timer);
      if (!settled) {
        settled = true;
        reject(err);
      }
    });

    child.on('close', (code) => {
      if (timer) clearTimeout(timer);
      if (!settled) {
        settled = true;
        resolve({ code: code ?? 1, stdout, stderr });
      }
    });
  });
}

export async function probeMediaSummary(filePath: string): Promise<string | null> {
  try {
    const res = await spawnSafeProcess(
      'ffprobe',
      [
        '-v',
        'error',
        '-show_entries',
        'format=format_name,duration:stream=codec_name,codec_type,width,height,sample_rate',
        '-of',
        'json',
        filePath,
      ],
      { timeoutMs: 10000 }
    );
    if (res.code !== 0) return null;
    const parsed = JSON.parse(res.stdout);
    const streams: Array<Record<string, unknown>> = parsed.streams || [];
    const parts: string[] = [];
    for (const s of streams) {
      if (s.codec_type === 'video') {
        parts.push(`video:${s.codec_name}${s.width ? ` ${s.width}x${s.height}` : ''}`);
      } else if (s.codec_type === 'audio') {
        parts.push(`audio:${s.codec_name}${s.sample_rate ? ` ${s.sample_rate}Hz` : ''}`);
      }
    }
    const dur = parsed.format?.duration ? `${Number(parsed.format.duration).toFixed(2)}s` : '';
    return [parts.join(', '), dur].filter(Boolean).join(' · ') || null;
  } catch {
    return null;
  }
}

export async function runSystemDoctor(): Promise<EngineDiagnostic[]> {
  const checks: Array<{
    id: string;
    name: string;
    candidates: string[];
    arg: string;
    capabilities: string[];
    license: string;
  }> = [
    {
      id: 'ffmpeg',
      name: 'FFmpeg Transcoder Engine',
      candidates: ['ffmpeg'],
      arg: '-version',
      capabilities: ['MP4 (H.264)', 'MKV', 'MOV', 'WebM (VP9)', 'AVI', 'GIF (2-pass palette)', 'MP3', 'WAV', 'FLAC', 'AAC', 'M4A', 'OGG', 'Opus'],
      license: 'LGPL v2.1+ / GPL v2+',
    },
    {
      id: 'ffprobe',
      name: 'ffprobe Stream Inspector',
      candidates: ['ffprobe'],
      arg: '-version',
      capabilities: ['Inspección de contenedores', 'Validación de codecs de audio/video', 'Cálculo de duración real'],
      license: 'LGPL v2.1+ / GPL v2+',
    },
    {
      id: 'imagemagick',
      name: 'ImageMagick Raster & Vector Engine',
      candidates: ['convert', 'magick'],
      arg: '-version',
      capabilities: ['PNG', 'JPEG', 'WebP', 'BMP', 'GIF animado', 'TIFF', 'ICO', 'SVG raster', 'Imágenes → PDF'],
      license: 'ImageMagick License (Apache 2.0 compatible)',
    },
    {
      id: 'ghostscript',
      name: 'Ghostscript / PDF Engine',
      candidates: ['gs'],
      arg: '--version',
      capabilities: ['PDF → PNG', 'PDF → JPEG', 'Exportación por rango de páginas (-dFirstPage/-dLastPage)', 'Modo aislado -dSAFER'],
      license: 'BSD-3-Clause (PDFium en Windows) / AGPLv3 (Ghostscript CLI)',
    },
    {
      id: 'libreoffice',
      name: 'LibreOffice Headless (Opcional)',
      candidates: ['soffice', 'libreoffice'],
      arg: '--version',
      capabilities: ['DOCX → PDF', 'ODT → PDF', 'XLSX → PDF', 'PPTX → PDF'],
      license: 'MPL 2.0 (Adaptador opcional sin descarga forzosa)',
    },
  ];

  const results: EngineDiagnostic[] = [];
  for (const item of checks) {
    let found = false;
    for (const bin of item.candidates) {
      try {
        const res = await spawnSafeProcess(bin, [item.arg], { timeoutMs: 5000 });
        if (res.code === 0) {
          const firstLine = (res.stdout || res.stderr).split('\n')[0]?.trim() || 'Disponible';
          results.push({
            id: item.id,
            name: item.name,
            available: true,
            version: firstLine,
            binaryPath: bin,
            capabilities: item.capabilities,
            license: item.license,
          });
          found = true;
          break;
        }
      } catch {
        // Continue to next candidate
      }
    }
    if (!found) {
      results.push({
        id: item.id,
        name: item.name,
        available: false,
        version: 'No instalado en este entorno (opcional)',
        binaryPath: 'N/A',
        capabilities: item.capabilities,
        license: item.license,
      });
    }
  }

  return results;
}

export class ConversionQueueEngine {
  private jobs: Map<string, ConversionJob> = new Map();
  private activeProcesses: Map<string, ChildProcess> = new Map();
  private cancelledIds: Set<string> = new Set();
  private activeCount = 0;
  private pendingQueue: string[] = [];
  private maxConcurrency = 4;
  private listeners: Set<(job: ConversionJob) => void> = new Set();

  constructor(maxConcurrency = 4) {
    this.maxConcurrency = Math.max(1, Math.min(16, maxConcurrency));
  }

  public setMaxConcurrency(limit: number): void {
    this.maxConcurrency = Math.max(1, Math.min(16, limit));
    this.pumpQueue();
  }

  public onJobUpdated(listener: (job: ConversionJob) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  public getJobs(): ConversionJob[] {
    return Array.from(this.jobs.values()).sort(
      (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
    );
  }

  public getJob(jobId: string): ConversionJob | undefined {
    return this.jobs.get(jobId);
  }

  public clearCompletedJobs(): void {
    for (const [id, job] of this.jobs.entries()) {
      if (job.status === 'Completed' || job.status === 'Failed' || job.status === 'Cancelled') {
        this.jobs.delete(id);
      }
    }
  }

  public enqueue(inputPath: string, targetFormat: string, rawOptions: ConversionOptions = {}): ConversionJob {
    const fullInputPath = path.resolve(inputPath);
    const ext = normalizeFormatId(path.extname(fullInputPath));
    const normTarget = normalizeFormatId(targetFormat);
    const id = crypto.randomBytes(6).toString('hex');

    const options: Required<ConversionOptions> = {
      quality: rawOptions.quality ?? 85,
      longestEdgePx: rawOptions.longestEdgePx ?? null,
      audioBitrateKbps: rawOptions.audioBitrateKbps ?? 192,
      audioSampleRate: rawOptions.audioSampleRate ?? null,
      stripMetadata: rawOptions.stripMetadata ?? false,
      backgroundHex: rawOptions.backgroundHex ?? '#FFFFFF',
      pageRange: rawOptions.pageRange ?? null,
      conflictPolicy: rawOptions.conflictPolicy ?? 'AutoRename',
      customOutputDir: rawOptions.customOutputDir ?? null,
      presetId: rawOptions.presetId ?? null,
      triggeredBy: rawOptions.triggeredBy ?? 'DragDrop',
    };

    const job: ConversionJob = {
      id,
      inputPath: fullInputPath,
      fileName: path.basename(fullInputPath),
      sourceFormat: ext || 'unknown',
      targetFormat: normTarget,
      outputPath: null,
      outputFileName: null,
      engineUsed: 'Pending',
      status: 'Queued',
      progressPercent: 0,
      createdAt: new Date().toISOString(),
      startedAt: null,
      completedAt: null,
      errorMessage: null,
      inputSizeBytes: fs.existsSync(fullInputPath) ? fs.statSync(fullInputPath).size : 0,
      outputSizeBytes: null,
      originalSha256: null,
      postOriginalSha256: null,
      originalIntactVerified: false,
      probeSummary: null,
      options,
    };

    this.jobs.set(id, job);
    this.pendingQueue.push(id);
    this.emit(job);
    this.pumpQueue();
    return job;
  }

  public cancelJob(jobId: string): boolean {
    const job = this.jobs.get(jobId);
    if (!job) return false;
    if (job.status === 'Completed' || job.status === 'Failed' || job.status === 'Cancelled') {
      return false;
    }

    this.cancelledIds.add(jobId);
    const child = this.activeProcesses.get(jobId);
    if (child) {
      child.kill('SIGKILL');
    }

    job.status = 'Cancelled';
    job.completedAt = new Date().toISOString();
    job.errorMessage = 'Conversión cancelada por el usuario sin publicar archivos parciales.';
    this.emit(job);
    return true;
  }

  public waitForJob(jobId: string, timeoutMs = 45000): Promise<ConversionJob> {
    return new Promise((resolve, reject) => {
      const existing = this.jobs.get(jobId);
      if (
        existing &&
        (existing.status === 'Completed' || existing.status === 'Failed' || existing.status === 'Cancelled')
      ) {
        resolve(existing);
        return;
      }

      const timer = setTimeout(() => {
        unsub();
        reject(new Error(`Timeout esperando el trabajo ${jobId}`));
      }, timeoutMs);

      const unsub = this.onJobUpdated((updated) => {
        if (
          updated.id === jobId &&
          (updated.status === 'Completed' || updated.status === 'Failed' || updated.status === 'Cancelled')
        ) {
          clearTimeout(timer);
          unsub();
          resolve(updated);
        }
      });
    });
  }

  private pumpQueue(): void {
    while (this.activeCount < this.maxConcurrency && this.pendingQueue.length > 0) {
      const nextId = this.pendingQueue.shift()!;
      const job = this.jobs.get(nextId);
      if (!job || this.cancelledIds.has(nextId)) continue;

      this.activeCount++;
      this.executeJob(job).finally(() => {
        this.activeCount--;
        this.pumpQueue();
      });
    }
  }

  private async executeJob(job: ConversionJob): Promise<void> {
    let tempOutputPath: string | null = null;

    try {
      this.updateStatus(job, 'Preparing', 5);
      job.startedAt = new Date().toISOString();

      if (!fs.existsSync(job.inputPath)) {
        throw new Error(`El archivo de entrada no existe: ${job.inputPath}`);
      }

      // Compute original SHA-256 before touching anything (INV-01)
      job.originalSha256 = computeFileSha256(job.inputPath);
      job.inputSizeBytes = fs.statSync(job.inputPath).size;

      // Inspect binary magic signature
      const signature = inspectFileSignature(job.inputPath);
      if (!signature.valid) {
        throw new Error(signature.reason || 'Firma binaria inválida o archivo corrupto.');
      }

      job.sourceFormat = signature.detectedFormat;

      if (!canConvertFormat(job.sourceFormat, job.targetFormat, { avifAvailable: true })) {
        throw new Error(
          `Conversión no admitida entre ${job.sourceFormat.toUpperCase()} y ${job.targetFormat.toUpperCase()}.`
        );
      }

      const finalOutputPath = resolveSafeOutputPath(
        job.inputPath,
        job.targetFormat,
        job.options.customOutputDir,
        job.options.conflictPolicy
      );

      // ConflictPolicy === 'Skip' when destination already exists
      if (finalOutputPath === null) {
        job.postOriginalSha256 = computeFileSha256(job.inputPath);
        job.originalIntactVerified = job.originalSha256 === job.postOriginalSha256;
        job.probeSummary = 'Omitido (el archivo de destino ya existía)';
        this.updateStatus(job, 'Completed', 100);
        job.completedAt = new Date().toISOString();
        this.emit(job);
        return;
      }

      const targetDesc = FORMAT_DESCRIPTORS[job.targetFormat];
      const tempExt = targetDesc ? targetDesc.extension : job.targetFormat;
      tempOutputPath = path.join(
        path.dirname(finalOutputPath),
        `.fileshift.tmp.${job.id}.${tempExt}`
      );

      if (this.cancelledIds.has(job.id)) {
        throw new Error('Operación cancelada.');
      }

      this.updateStatus(job, 'Running', 25);

      const srcCategory = FORMAT_DESCRIPTORS[job.sourceFormat]?.category;

      if (srcCategory === 'pdf') {
        job.engineUsed = 'Ghostscript';
        await this.runPdfToImage(job, tempOutputPath);
      } else if (srcCategory === 'image' || srcCategory === 'svg') {
        job.engineUsed = 'ImageMagick';
        await this.runImageMagick(job, tempOutputPath);
      } else if (srcCategory === 'video' || srcCategory === 'audio') {
        job.engineUsed = 'FFmpeg';
        await this.runFFmpeg(job, tempOutputPath);
      } else {
        throw new Error(`Motor no disponible para la categoría: ${srcCategory}`);
      }

      if (this.cancelledIds.has(job.id)) {
        throw new Error('Operación cancelada.');
      }

      this.updateStatus(job, 'Validating', 88);

      if (!fs.existsSync(tempOutputPath)) {
        throw new Error('El motor no generó el archivo temporal esperado.');
      }

      const tempStat = fs.statSync(tempOutputPath);
      if (tempStat.size === 0) {
        throw new Error('El motor produjo un archivo de 0 bytes; resultado descartado.');
      }

      const outSignature = inspectFileSignature(tempOutputPath);
      if (!outSignature.valid) {
        throw new Error(`El archivo generado no superó la verificación binaria: ${outSignature.reason}`);
      }

      // Atomic move from temporary file to final path
      fs.renameSync(tempOutputPath, finalOutputPath);
      tempOutputPath = null;

      job.outputPath = finalOutputPath;
      job.outputFileName = path.basename(finalOutputPath);
      job.outputSizeBytes = fs.statSync(finalOutputPath).size;

      // Verify invariant INV-01: original file is 100% untouched
      job.postOriginalSha256 = computeFileSha256(job.inputPath);
      job.originalIntactVerified = job.originalSha256 === job.postOriginalSha256;

      if (!job.originalIntactVerified) {
        throw new Error('Error crítico de invariante: el hash SHA-256 del archivo original cambió.');
      }

      if (job.engineUsed === 'FFmpeg') {
        job.probeSummary = await probeMediaSummary(finalOutputPath);
      } else {
        job.probeSummary = `${outSignature.detectedFormat.toUpperCase()} verificado · ${job.outputSizeBytes} bytes`;
      }

      job.completedAt = new Date().toISOString();
      this.updateStatus(job, 'Completed', 100);
    } catch (err) {
      if (tempOutputPath && fs.existsSync(tempOutputPath)) {
        try {
          fs.unlinkSync(tempOutputPath);
        } catch {
          // ignore cleanup failure
        }
      }

      // Verify original file is still intact even on failure/cancellation
      if (fs.existsSync(job.inputPath) && job.originalSha256) {
        job.postOriginalSha256 = computeFileSha256(job.inputPath);
        job.originalIntactVerified = job.originalSha256 === job.postOriginalSha256;
      }

      if (this.cancelledIds.has(job.id) || job.status === 'Cancelled') {
        job.status = 'Cancelled';
        job.completedAt = new Date().toISOString();
        this.emit(job);
      } else {
        job.errorMessage = err instanceof Error ? err.message : String(err);
        job.completedAt = new Date().toISOString();
        this.updateStatus(job, 'Failed', job.progressPercent);
      }
    } finally {
      this.activeProcesses.delete(job.id);
    }
  }

  private async runImageMagick(job: ConversionJob, tempOutputPath: string): Promise<void> {
    if (job.targetFormat === 'pdf') {
      await this.convertImageToPdf(job, tempOutputPath);
      return;
    }

    const args: string[] = [job.inputPath, '-auto-orient'];
    const targetDesc = FORMAT_DESCRIPTORS[job.targetFormat];

    // Explicit background compositing when target lacks alpha channel (JPEG, BMP, PDF)
    if (targetDesc && !targetDesc.supportsAlpha) {
      args.push('-background', job.options.backgroundHex || '#FFFFFF', '-alpha', 'remove', '-alpha', 'off');
    }

    if (job.options.longestEdgePx && job.options.longestEdgePx > 0) {
      args.push('-resize', `${job.options.longestEdgePx}x${job.options.longestEdgePx}>`);
    }

    if (job.targetFormat === 'ico') {
      args.push('-resize', '256x256>');
    }

    if (job.options.stripMetadata) {
      args.push('-strip');
    }

    args.push('-quality', String(Math.max(1, Math.min(100, job.options.quality))));
    args.push(tempOutputPath);

    const res = await spawnSafeProcess('convert', args, {
      timeoutMs: 30000,
      onChild: (c) => this.activeProcesses.set(job.id, c),
    });

    if (res.code !== 0) {
      throw new Error(`ImageMagick falló (código ${res.code}): ${res.stderr.trim()}`);
    }
  }

  private async convertImageToPdf(job: ConversionJob, tempOutputPath: string): Promise<void> {
    const intermediateJpg = `${tempOutputPath}.intermediate.jpg`;
    try {
      const args: string[] = [
        job.inputPath,
        '-auto-orient',
        '-background',
        job.options.backgroundHex || '#FFFFFF',
        '-alpha',
        'remove',
        '-alpha',
        'off',
      ];
      if (job.options.longestEdgePx && job.options.longestEdgePx > 0) {
        args.push('-resize', `${job.options.longestEdgePx}x${job.options.longestEdgePx}>`);
      }
      args.push('-quality', String(Math.max(1, Math.min(100, job.options.quality))), intermediateJpg);

      const res = await spawnSafeProcess('convert', args, {
        timeoutMs: 25000,
        onChild: (c) => this.activeProcesses.set(job.id, c),
      });
      if (res.code !== 0 || !fs.existsSync(intermediateJpg)) {
        throw new Error(`No se pudo rasterizar la imagen para PDF: ${res.stderr.trim()}`);
      }

      const idRes = await spawnSafeProcess('identify', ['-format', '%w %h', intermediateJpg], {
        timeoutMs: 10000,
      });
      const [wStr, hStr] = idRes.stdout.trim().split(/\s+/);
      const width = Math.max(72, parseInt(wStr || '640', 10));
      const height = Math.max(72, parseInt(hStr || '400', 10));

      const jpgBytes = fs.readFileSync(intermediateJpg);
      const pdfBuffer = buildSinglePageJpegPdf(jpgBytes, width, height);
      fs.writeFileSync(tempOutputPath, pdfBuffer);
    } finally {
      if (fs.existsSync(intermediateJpg)) {
        try {
          fs.unlinkSync(intermediateJpg);
        } catch {
          // ignore
        }
      }
    }
  }

  private async runPdfToImage(job: ConversionJob, tempOutputPath: string): Promise<void> {
    const device = job.targetFormat === 'jpeg' ? 'jpeg' : 'png16m';
    let firstPage = 1;
    let lastPage = 1;

    if (job.options.pageRange) {
      const parts = job.options.pageRange.split('-').map((p) => parseInt(p.trim(), 10));
      if (!Number.isNaN(parts[0]) && parts[0] >= 1) {
        firstPage = parts[0];
        lastPage = !Number.isNaN(parts[1]) && parts[1] >= firstPage ? parts[1] : firstPage;
      }
    }

    const args = [
      '-dSAFER',
      '-dBATCH',
      '-dNOPAUSE',
      `-sDEVICE=${device}`,
      '-r150',
      `-dFirstPage=${firstPage}`,
      `-dLastPage=${lastPage}`,
      `-sOutputFile=${tempOutputPath}`,
      job.inputPath,
    ];

    const res = await spawnSafeProcess('gs', args, {
      timeoutMs: 30000,
      onChild: (c) => this.activeProcesses.set(job.id, c),
    });

    if (res.code !== 0) {
      throw new Error(`Ghostscript falló (código ${res.code}): ${res.stderr.trim()}`);
    }
  }

  private async runFFmpeg(job: ConversionJob, tempOutputPath: string): Promise<void> {
    const args: string[] = ['-y', '-hide_banner', '-nostdin', '-i', job.inputPath];
    const target = job.targetFormat;
    const targetDesc = FORMAT_DESCRIPTORS[target];
    const crf = Math.max(18, Math.min(36, Math.round(51 - (job.options.quality / 100) * 33)));

    if (target === 'gif') {
      const scalePart = job.options.longestEdgePx
        ? `scale=${job.options.longestEdgePx}:-1:flags=lanczos,`
        : 'scale=320:-1:flags=lanczos,';
      args.push('-vf', `fps=10,${scalePart}split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse`, '-loop', '0');
    } else if (targetDesc?.category === 'video') {
      if (job.options.longestEdgePx && job.options.longestEdgePx > 0) {
        args.push('-vf', `scale='min(${job.options.longestEdgePx},iw)':-2`);
      }

      if (target === 'webm') {
        args.push('-c:v', 'libvpx-vp9', '-crf', String(crf), '-b:v', '0', '-c:a', 'libopus');
      } else if (target === 'avi') {
        args.push('-c:v', 'mpeg4', '-q:v', '4', '-c:a', 'libmp3lame');
      } else {
        args.push(
          '-c:v',
          'libx264',
          '-preset',
          'ultrafast',
          '-crf',
          String(crf),
          '-pix_fmt',
          'yuv420p',
          '-c:a',
          'aac',
          '-b:a',
          `${job.options.audioBitrateKbps || 192}k`
        );
      }
    } else if (targetDesc?.category === 'audio') {
      args.push('-vn');
      if (job.options.audioSampleRate) {
        args.push('-ar', String(job.options.audioSampleRate));
      }
      const br = `${job.options.audioBitrateKbps || 192}k`;
      switch (target) {
        case 'mp3':
          args.push('-c:a', 'libmp3lame', '-b:a', br);
          break;
        case 'wav':
          args.push('-c:a', 'pcm_s16le');
          break;
        case 'flac':
          args.push('-c:a', 'flac');
          break;
        case 'aac':
        case 'm4a':
          args.push('-c:a', 'aac', '-b:a', br);
          break;
        case 'ogg':
          args.push('-c:a', 'libvorbis', '-b:a', br);
          break;
        case 'opus':
          args.push('-c:a', 'libopus', '-b:a', br);
          break;
      }
    }

    if (job.options.stripMetadata) {
      args.push('-map_metadata', '-1');
    }

    args.push(tempOutputPath);

    const res = await spawnSafeProcess('ffmpeg', args, {
      timeoutMs: 45000,
      onChild: (c) => this.activeProcesses.set(job.id, c),
    });

    if (res.code !== 0) {
      throw new Error(`FFmpeg falló (código ${res.code}): ${res.stderr.slice(-400).trim()}`);
    }
  }

  private updateStatus(job: ConversionJob, status: JobStatus, progressPercent: number): void {
    job.status = status;
    job.progressPercent = progressPercent;
    this.emit(job);
  }

  private emit(job: ConversionJob): void {
    for (const listener of this.listeners) {
      listener({ ...job });
    }
  }
}

export class FolderWatcherAutomationManager {
  private queue: ConversionQueueEngine;
  private generatedOutputPaths: Set<string> = new Set();
  private recentlyProcessed: Map<string, number> = new Map();

  constructor(queue: ConversionQueueEngine) {
    this.queue = queue;
    this.queue.onJobUpdated((job) => {
      if (job.outputPath) {
        this.generatedOutputPaths.add(path.resolve(job.outputPath));
      }
    });
  }

  public registerGeneratedOutput(filePath: string): void {
    this.generatedOutputPaths.add(path.resolve(filePath));
  }

  public async processCandidateFile(
    rule: AutomationRule,
    candidatePath: string
  ): Promise<ConversionJob | null> {
    if (!rule.enabled) return null;

    const fullPath = path.resolve(candidatePath);
    const baseName = path.basename(fullPath);

    // Ignore hidden or temporary files
    if (baseName.startsWith('.') || baseName.includes('.fileshift.tmp.')) {
      return null;
    }

    // Prevent infinite loops: never re-convert a file generated by FileShift
    if (this.generatedOutputPaths.has(fullPath)) {
      return null;
    }

    const ext = normalizeFormatId(path.extname(fullPath));
    const normTarget = normalizeFormatId(rule.targetFormat);

    // Never convert a file that is already in the rule's target format
    if (ext === normTarget) {
      return null;
    }

    if (
      rule.allowedInputFormats.length > 0 &&
      !rule.allowedInputFormats.map(normalizeFormatId).includes(ext)
    ) {
      return null;
    }

    // Deduplicate rapid duplicate events within 5 seconds
    const now = Date.now();
    const last = this.recentlyProcessed.get(`${rule.id}:${fullPath}`);
    if (last && now - last < 5000) {
      return null;
    }
    this.recentlyProcessed.set(`${rule.id}:${fullPath}`, now);

    // Wait for file write stability
    const stable = await this.waitForWriteStability(fullPath);
    if (!stable) return null;

    const job = this.queue.enqueue(fullPath, normTarget, {
      conflictPolicy: rule.conflictPolicy,
      customOutputDir: rule.outputDirectory,
      presetId: rule.presetId,
      triggeredBy: 'Automation',
    });

    const finished = await this.queue.waitForJob(job.id);
    if (finished.outputPath) {
      this.generatedOutputPaths.add(path.resolve(finished.outputPath));
    }
    return finished;
  }

  private async waitForWriteStability(filePath: string): Promise<boolean> {
    let prevSize = -1;
    let stableCount = 0;

    for (let i = 0; i < 15; i++) {
      if (!fs.existsSync(filePath)) return false;
      const stat = fs.statSync(filePath);
      if (stat.size > 0 && stat.size === prevSize) {
        stableCount++;
        if (stableCount >= 2) return true;
      } else {
        stableCount = 0;
        prevSize = stat.size;
      }
      await new Promise((r) => setTimeout(r, 40));
    }
    return false;
  }
}

export async function ensureSampleFilesAsync(samplesDir: string): Promise<void> {
  fs.mkdirSync(samplesDir, { recursive: true });

  const pngPath = path.join(samplesDir, 'arquitectura-diagrama.png');
  if (!fs.existsSync(pngPath)) {
    await spawnSafeProcess('convert', [
      '-size',
      '640x400',
      'xc:#0F172A',
      '-fill',
      '#14B8A6',
      '-draw',
      'roundrectangle 60,60 580,340 24,24',
      '-fill',
      '#0B0D11',
      '-draw',
      'roundrectangle 90,90 550,310 16,16',
      '-fill',
      '#2DD4BF',
      '-draw',
      'circle 220,200 220,145',
      '-fill',
      '#38BDF8',
      '-draw',
      'rectangle 310,150 500,250',
      pngPath,
    ]);
  }

  const svgPath = path.join(samplesDir, 'insignia-vectorial.svg');
  if (!fs.existsSync(svgPath)) {
    const svgContent = `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
  <rect width="512" height="512" rx="64" fill="#0F172A"/>
  <rect x="64" y="64" width="384" height="384" rx="40" fill="#1E293B" stroke="#14B8A6" stroke-width="12"/>
  <circle cx="256" cy="256" r="108" fill="#14B8A6"/>
  <path d="M210 256 L245 290 L310 220" stroke="#0B0D11" stroke-width="20" fill="none" stroke-linecap="round"/>
</svg>`;
    fs.writeFileSync(svgPath, svgContent, 'utf8');
  }

  const mp4Path = path.join(samplesDir, 'captura-demo.mp4');
  if (!fs.existsSync(mp4Path)) {
    await spawnSafeProcess('ffmpeg', [
      '-y',
      '-hide_banner',
      '-f',
      'lavfi',
      '-i',
      'testsrc=duration=1.5:size=320x240:rate=15',
      '-f',
      'lavfi',
      '-i',
      'sine=frequency=440:duration=1.5',
      '-c:v',
      'libx264',
      '-preset',
      'ultrafast',
      '-pix_fmt',
      'yuv420p',
      '-c:a',
      'aac',
      '-b:a',
      '128k',
      mp4Path,
    ]);
  }

  const wavPath = path.join(samplesDir, 'sintetizador-estudio.wav');
  if (!fs.existsSync(wavPath)) {
    await spawnSafeProcess('ffmpeg', [
      '-y',
      '-hide_banner',
      '-f',
      'lavfi',
      '-i',
      'sine=frequency=528:duration=1.5',
      '-c:a',
      'pcm_s16le',
      wavPath,
    ]);
  }

  const pdfPath = path.join(samplesDir, 'especificacion-tecnica.pdf');
  if (!fs.existsSync(pdfPath) && fs.existsSync(pngPath)) {
    const tmpJpg = path.join(samplesDir, '.tmp-pdf-seed.jpg');
    await spawnSafeProcess('convert', [pngPath, '-quality', '90', tmpJpg]);
    if (fs.existsSync(tmpJpg)) {
      const jpgBytes = fs.readFileSync(tmpJpg);
      fs.writeFileSync(pdfPath, buildSinglePageJpegPdf(jpgBytes, 640, 400));
      fs.unlinkSync(tmpJpg);
    }
  }
}

export function buildSinglePageJpegPdf(jpegBytes: Buffer, width: number, height: number): Buffer {
  const chunks: Buffer[] = [];
  const offsets: number[] = [0];
  let offset = 0;

  const pushStr = (s: string) => {
    const b = Buffer.from(s, 'binary');
    chunks.push(b);
    offset += b.length;
  };
  const pushBuf = (b: Buffer) => {
    chunks.push(b);
    offset += b.length;
  };

  pushStr('%PDF-1.4\n%\xFF\xFF\xFF\xFF\n');

  // 1: Catalog
  offsets.push(offset);
  pushStr('1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n');

  // 2: Pages
  offsets.push(offset);
  pushStr('2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n');

  // 3: Page
  offsets.push(offset);
  pushStr(
    `3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << /Im0 4 0 R >> >> /MediaBox [0 0 ${width} ${height}] /Contents 5 0 R >>\nendobj\n`
  );

  // 4: Image XObject (DCTDecode)
  offsets.push(offset);
  pushStr(
    `4 0 obj\n<< /Type /XObject /Subtype /Image /Width ${width} /Height ${height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpegBytes.length} >>\nstream\n`
  );
  pushBuf(jpegBytes);
  pushStr('\nendstream\nendobj\n');

  // 5: Page Content Stream
  const contentStream = `q\n${width} 0 0 ${height} 0 0 cm\n/Im0 Do\nQ\n`;
  offsets.push(offset);
  pushStr(
    `5 0 obj\n<< /Length ${Buffer.byteLength(contentStream, 'binary')} >>\nstream\n${contentStream}endstream\nendobj\n`
  );

  const xrefOffset = offset;
  pushStr(`xref\n0 ${offsets.length}\n0000000000 65535 f \n`);
  for (let i = 1; i < offsets.length; i++) {
    pushStr(`${String(offsets[i]).padStart(10, '0')} 00000 n \n`);
  }
  pushStr(
    `trailer\n<< /Size ${offsets.length} /Root 1 0 R >>\nstartxref\n${xrefOffset}\n%%EOF\n`
  );

  return Buffer.concat(chunks);
}

