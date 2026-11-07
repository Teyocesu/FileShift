import express from 'express';
import fs from 'node:fs';
import path from 'node:path';
import { createServer as createViteServer } from 'vite';
import {
  ConversionQueueEngine,
  FolderWatcherAutomationManager,
  computeFileSha256,
  ensureSampleFilesAsync,
  runSystemDoctor,
  spawnSafeProcess,
} from './src/core/engineRunner';
import {
  FORMAT_DESCRIPTORS,
  buildContextMenuTargets,
  createDefaultUserSettings,
  getSupportedTargetsForFormat,
  inspectFileSignature,
  normalizeFormatId,
} from './src/core/formatMatrix';
import type {
  AutomationRule,
  ConversionPreset,
  MediaCategory,
  UserSettings,
  WorkspaceSampleFile,
} from './src/core/types';

const ROOT_DIR = process.cwd();
const RUNTIME_DIR = path.join(ROOT_DIR, 'workspace-runtime');
const SAMPLES_DIR = path.join(RUNTIME_DIR, 'explorer-files');
const UPLOADS_DIR = path.join(RUNTIME_DIR, 'uploads');
const WATCHED_BASE_DIR = path.join(RUNTIME_DIR, 'watched');
const SETTINGS_FILE = path.join(RUNTIME_DIR, 'settings.json');

fs.mkdirSync(SAMPLES_DIR, { recursive: true });
fs.mkdirSync(UPLOADS_DIR, { recursive: true });
fs.mkdirSync( path.join(WATCHED_BASE_DIR, 'Screenshots'), { recursive: true });
fs.mkdirSync( path.join(WATCHED_BASE_DIR, 'Captures'), { recursive: true });
fs.mkdirSync( path.join(WATCHED_BASE_DIR, 'VoiceNotes'), { recursive: true });

function loadSettings(): UserSettings {
  const defaults = createDefaultUserSettings(WATCHED_BASE_DIR);
  if (fs.existsSync(SETTINGS_FILE)) {
    try {
      const parsed = JSON.parse(fs.readFileSync(SETTINGS_FILE, 'utf8'));
      return {
        ...defaults,
        ...parsed,
        menuFavorites: {
          ...defaults.menuFavorites,
          ...(parsed.menuFavorites || {}),
        },
      };
    } catch {
      return defaults;
    }
  }
  fs.writeFileSync(SETTINGS_FILE, JSON.stringify(defaults, null, 2), 'utf8');
  return defaults;
}

function saveSettings(settings: UserSettings): void {
  fs.mkdirSync(path.dirname(SETTINGS_FILE), { recursive: true });
  fs.writeFileSync(SETTINGS_FILE, JSON.stringify(settings, null, 2), 'utf8');
}

let currentSettings = loadSettings();
const queueEngine = new ConversionQueueEngine(currentSettings.maxConcurrency);
const automationManager = new FolderWatcherAutomationManager(queueEngine);

function listExplorerWorkspaceFiles(settings: UserSettings): WorkspaceSampleFile[] {
  if (!fs.existsSync(SAMPLES_DIR)) return [];
  const entries = fs.readdirSync(SAMPLES_DIR, { withFileTypes: true });
  const files: WorkspaceSampleFile[] = [];

  for (const entry of entries) {
    if (!entry.isFile() || entry.name.startsWith('.')) continue;
    const fullPath = path.join(SAMPLES_DIR, entry.name);
    const stat = fs.statSync(fullPath);
    const sig = inspectFileSignature(fullPath);
    if (!sig.valid || sig.category === 'unknown') continue;

    const compatibleTargets = getSupportedTargetsForFormat(sig.detectedFormat, {
      avifAvailable: true,
      libreOfficeAvailable: false,
    });
    const { favoriteTargets, otherTargets } = buildContextMenuTargets(
      sig.detectedFormat,
      settings.menuFavorites
    );

    files.push({
      name: entry.name,
      relativePath: path.relative(ROOT_DIR, fullPath),
      fullPath,
      sizeBytes: stat.size,
      detectedFormat: sig.detectedFormat,
      category: sig.category as MediaCategory,
      sha256: computeFileSha256(fullPath),
      compatibleTargets,
      favoriteTargets,
      otherTargets,
    });
  }

  return files.sort((a, b) => a.name.localeCompare(b.name));
}

const NATIVE_REPO_FILES = [
  { path: 'FileShift.sln', category: 'Solution', description: 'Solución Visual Studio 2022 (.NET 10 + C++ Win32 COM)' },
  { path: 'src/Converter.Shell/ExplorerCommand.h', category: 'Shell COM (C++)', description: 'Declaración COM de IExplorerCommand, IEnumExplorerCommand y CLSID para Windows 11' },
  { path: 'src/Converter.Shell/ExplorerCommand.cpp', category: 'Shell COM (C++)', description: 'Implementación nativa C++17 del menú contextual moderno sin bloquear explorer.exe' },
  { path: 'src/Converter.Shell/dllmain.cpp', category: 'Shell COM (C++)', description: 'IClassFactory, DllGetClassObject y ciclo de vida COM del servidor InProc/Surrogate' },
  { path: 'src/Converter.Shell/CMakeLists.txt', category: 'Shell COM (C++)', description: 'Configuración de compilación CMake para FileShift.Shell.dll (x64)' },
  { path: 'installer/AppxManifest.xml', category: 'Installer & MSIX', description: 'Manifiesto Sparse Package MSIX con windows.fileExplorerContextMenus y com:ComServer' },
  { path: 'installer/FileShift.iss', category: 'Installer & MSIX', description: 'Script de instalador Inno Setup 6 con registro y desinstalación limpia' },
  { path: 'installer/Build-WindowsRelease.ps1', category: 'Installer & MSIX', description: 'Script reproducible de compilación de la solución .NET 10 + C++ COM' },
  { path: 'installer/Register-SparsePackage.ps1', category: 'Installer & MSIX', description: 'Script de registro de identidad MSIX Sparse Package en Windows 11' },
  { path: 'installer/Unregister-SparsePackage.ps1', category: 'Installer & MSIX', description: 'Script de desregistro y limpieza completa de Explorer al desinstalar' },
  { path: 'src/Converter.Core/Models.cs', category: 'Converter.Core (C#)', description: 'Modelos de dominio, estados de trabajo, presets, reglas y configuración' },
  { path: 'src/Converter.Core/MagicByteInspector.cs', category: 'Converter.Core (C#)', description: 'Inspección de cabeceras binarias (Magic Bytes) para validación real' },
  { path: 'src/Converter.Core/FormatMatrix.cs', category: 'Converter.Core (C#)', description: 'Matriz de conversiones compatibles por categoría y motor' },
  { path: 'src/Converter.Core/SafeFilePolicy.cs', category: 'Converter.Core (C#)', description: 'Invariante SHA-256 del archivo original, temporales atómicos y política anti-colisión' },
  { path: 'src/Converter.Core/ConversionQueue.cs', category: 'Converter.Core (C#)', description: 'Cola concurrente con semáforo, cancelación y validación post-conversión' },
  { path: 'src/Converter.Core/FolderAutomationService.cs', category: 'Converter.Core (C#)', description: 'Vigilancia con FileSystemWatcher, estabilidad de escritura y prevención de bucles' },
  { path: 'src/Converter.Infrastructure/FFmpegAdapter.cs', category: 'Infrastructure (C#)', description: 'Adaptador FFmpeg/ffprobe con ArgumentList seguro y paleta GIF en 2 pasos' },
  { path: 'src/Converter.Infrastructure/ImageMagickAdapter.cs', category: 'Infrastructure (C#)', description: 'Adaptador ImageMagick 7 con auto-orientación EXIF y compositing de alfa' },
  { path: 'src/Converter.Infrastructure/PdfAdapter.cs', category: 'Infrastructure (C#)', description: 'Adaptador PDF con aislamiento -dSAFER y soporte de rango de páginas' },
  { path: 'src/Converter.Infrastructure/LibreOfficeAdapter.cs', category: 'Infrastructure (C#)', description: 'Adaptador opcional para LibreOffice Headless (DOCX/ODT/XLSX/PPTX -> PDF)' },
  { path: 'src/Converter.Cli/Program.cs', category: 'Converter.Cli (C#)', description: 'Ejecutable CLI nativo (fileshift convert | formats | doctor)' },
  { path: 'src/Converter.App/MainWindow.xaml', category: 'Converter.App (WPF)', description: 'Ventana principal WPF con soporte DPI PerMonitorV2 y Drag & Drop' },
  { path: 'src/Converter.App/MainWindow.xaml.cs', category: 'Converter.App (WPF)', description: 'Lógica de interfaz WPF conectada a ConversionQueue' },
  { path: 'tests/Converter.Tests/ConversionPipelineTests.cs', category: 'Tests (xUnit)', description: 'Pruebas de invariantes en C#/.NET' },
  { path: 'SPEC.md', category: 'Canonical Docs', description: 'Especificación técnica, arquitectura COM y tabla de criterios AC-01 a AC-16' },
  { path: 'PLAN.md', category: 'Canonical Docs', description: 'Plan de ingeniería por fases (Fase A a Fase E) y estado' },
  { path: 'HANDOFF.md', category: 'Canonical Docs', description: 'Estado actual, evidencia de pruebas y guía para QA físico en Windows 11' },
  { path: 'AGENTS.md', category: 'Canonical Docs', description: 'Invariantes de seguridad y arquitectura del repositorio' },
  { path: 'THIRD_PARTY_NOTICES.md', category: 'Canonical Docs', description: 'Avisos de licencias de FFmpeg, ImageMagick, PDFium/Ghostscript y LibreOffice' },
  { path: 'README.md', category: 'Canonical Docs', description: 'Documentación principal e instrucciones reproducibles de compilación e instalación' },
];

async function startServer() {
  await ensureSampleFilesAsync(SAMPLES_DIR);
  const cachedDiagnostics = await runSystemDoctor();

  const app = express();
  app.use(express.json({ limit: '50mb' }));

  app.get('/api/state', async (_req, res) => {
    const sampleFiles = listExplorerWorkspaceFiles(currentSettings);
    res.json({
      settings: currentSettings,
      jobs: queueEngine.getJobs(),
      sampleFiles,
      diagnostics: cachedDiagnostics,
      formats: FORMAT_DESCRIPTORS,
    });
  });

  app.post('/api/convert/sample', async (req, res) => {
    try {
      const {
        fileNames,
        targetFormat,
        quality,
        longestEdgePx,
        audioBitrateKbps,
        pageRange,
        presetId,
        conflictPolicy,
        triggeredBy,
      } = req.body as {
        fileNames: string[];
        targetFormat: string;
        quality?: number;
        longestEdgePx?: number | null;
        audioBitrateKbps?: number | null;
        pageRange?: string | null;
        presetId?: string | null;
        conflictPolicy?: 'AutoRename' | 'Skip' | 'Overwrite';
        triggeredBy?: 'ContextMenu' | 'DragDrop' | 'Automation' | 'CLI';
      };

      if (!Array.isArray(fileNames) || fileNames.length === 0 || !targetFormat) {
        res.status(400).json({ error: 'Se requiere al menos un archivo y un formato de destino.' });
        return;
      }

      let resolvedQuality = quality ?? currentSettings.defaultQuality;
      let resolvedEdge = longestEdgePx ?? null;
      let resolvedBitrate = audioBitrateKbps ?? 192;
      let resolvedTarget = targetFormat;

      if (presetId) {
        const preset = currentSettings.presets.find((p) => p.id === presetId);
        if (preset) {
          resolvedTarget = preset.targetFormat;
          resolvedQuality = preset.quality;
          resolvedEdge = preset.longestEdgePx;
          resolvedBitrate = preset.audioBitrateKbps ?? 192;
        }
      }

      const customOutputDir =
        !currentSettings.saveNextToOriginal && currentSettings.customOutputDirectory.trim()
          ? currentSettings.customOutputDirectory.trim()
          : null;

      const enqueued = fileNames.map((name) => {
        const safeName = path.basename(name);
        const fullInput = path.join(SAMPLES_DIR, safeName);
        return queueEngine.enqueue(fullInput, resolvedTarget, {
          quality: resolvedQuality,
          longestEdgePx: resolvedEdge,
          audioBitrateKbps: resolvedBitrate,
          pageRange: pageRange ?? null,
          presetId: presetId ?? null,
          conflictPolicy: conflictPolicy ?? currentSettings.defaultConflictPolicy,
          customOutputDir,
          triggeredBy: triggeredBy ?? 'ContextMenu',
        });
      });

      // Wait up to 4 seconds so fast conversions return completed state immediately
      await Promise.race([
        Promise.all(enqueued.map((j) => queueEngine.waitForJob(j.id, 4000).catch(() => j))),
        new Promise((r) => setTimeout(r, 4000)),
      ]);

      res.json({
        enqueuedIds: enqueued.map((j) => j.id),
        jobs: queueEngine.getJobs(),
        sampleFiles: listExplorerWorkspaceFiles(currentSettings),
      });
    } catch (err) {
      res.status(500).json({ error: err instanceof Error ? err.message : String(err) });
    }
  });

  app.post('/api/convert/upload', async (req, res) => {
    try {
      const {
        fileName,
        base64Data,
        targetFormat,
        quality,
        longestEdgePx,
        audioBitrateKbps,
        pageRange,
        presetId,
        conflictPolicy,
      } = req.body as {
        fileName: string;
        base64Data: string;
        targetFormat: string;
        quality?: number;
        longestEdgePx?: number | null;
        audioBitrateKbps?: number | null;
        pageRange?: string | null;
        presetId?: string | null;
        conflictPolicy?: 'AutoRename' | 'Skip' | 'Overwrite';
      };

      if (!fileName || !base64Data || !targetFormat) {
        res.status(400).json({ error: 'Faltan datos del archivo o el formato de destino.' });
        return;
      }

      const safeBaseName = path.basename(fileName);
      const targetUploadPath = path.join(SAMPLES_DIR, safeBaseName);
      const buffer = Buffer.from(base64Data, 'base64');
      fs.writeFileSync(targetUploadPath, buffer);

      let resolvedQuality = quality ?? currentSettings.defaultQuality;
      let resolvedEdge = longestEdgePx ?? null;
      let resolvedBitrate = audioBitrateKbps ?? 192;
      let resolvedTarget = targetFormat;

      if (presetId) {
        const preset = currentSettings.presets.find((p) => p.id === presetId);
        if (preset) {
          resolvedTarget = preset.targetFormat;
          resolvedQuality = preset.quality;
          resolvedEdge = preset.longestEdgePx;
          resolvedBitrate = preset.audioBitrateKbps ?? 192;
        }
      }

      const job = queueEngine.enqueue(targetUploadPath, resolvedTarget, {
        quality: resolvedQuality,
        longestEdgePx: resolvedEdge,
        audioBitrateKbps: resolvedBitrate,
        pageRange: pageRange ?? null,
        presetId: presetId ?? null,
        conflictPolicy: conflictPolicy ?? currentSettings.defaultConflictPolicy,
        triggeredBy: 'DragDrop',
      });

      await queueEngine.waitForJob(job.id, 6000).catch(() => job);

      res.json({
        job: queueEngine.getJob(job.id),
        jobs: queueEngine.getJobs(),
        sampleFiles: listExplorerWorkspaceFiles(currentSettings),
      });
    } catch (err) {
      res.status(500).json({ error: err instanceof Error ? err.message : String(err) });
    }
  });

  app.post('/api/convert/corrupt-test', async (_req, res) => {
    const corruptFile = path.join(UPLOADS_DIR, 'archivo-corrupto-prueba.png');
    fs.writeFileSync(corruptFile, Buffer.from([0x00, 0x11, 0x22, 0x33, 0xff, 0x00]));
    const job = queueEngine.enqueue(corruptFile, 'webp', {
      triggeredBy: 'DragDrop',
    });
    await queueEngine.waitForJob(job.id, 3000).catch(() => job);
    res.json({
      job: queueEngine.getJob(job.id),
      jobs: queueEngine.getJobs(),
    });
  });

  app.post('/api/jobs/:id/cancel', (req, res) => {
    const ok = queueEngine.cancelJob(req.params.id);
    res.json({ cancelled: ok, jobs: queueEngine.getJobs() });
  });

  app.post('/api/jobs/:id/retry', async (req, res) => {
    const existing = queueEngine.getJob(req.params.id);
    if (!existing) {
      res.status(404).json({ error: 'Trabajo no encontrado.' });
      return;
    }
    const retried = queueEngine.enqueue(existing.inputPath, existing.targetFormat, existing.options);
    await queueEngine.waitForJob(retried.id, 4000).catch(() => retried);
    res.json({ job: queueEngine.getJob(retried.id), jobs: queueEngine.getJobs() });
  });

  app.delete('/api/jobs/completed', (_req, res) => {
    queueEngine.clearCompletedJobs();
    res.json({ jobs: queueEngine.getJobs() });
  });

  app.get('/api/files/download', (req, res) => {
    const rawPath = String(req.query.path || '');
    if (!rawPath) {
      res.status(400).send('Ruta requerida');
      return;
    }
    const resolved = path.resolve(rawPath);
    if (!resolved.startsWith(RUNTIME_DIR) || !fs.existsSync(resolved)) {
      res.status(404).send('Archivo no encontrado');
      return;
    }
    res.download(resolved, path.basename(resolved));
  });

  app.put('/api/settings', (req, res) => {
    const patch = req.body as Partial<UserSettings>;
    currentSettings = {
      ...currentSettings,
      ...patch,
      menuFavorites: patch.menuFavorites
        ? { ...currentSettings.menuFavorites, ...patch.menuFavorites }
        : currentSettings.menuFavorites,
    };
    queueEngine.setMaxConcurrency(currentSettings.maxConcurrency);
    saveSettings(currentSettings);
    res.json({
      settings: currentSettings,
      sampleFiles: listExplorerWorkspaceFiles(currentSettings),
    });
  });

  app.post('/api/settings/reset-menu', (req, res) => {
    const { category } = req.body as { category?: MediaCategory };
    const defaults = createDefaultUserSettings(WATCHED_BASE_DIR).menuFavorites;
    if (category && defaults[category]) {
      currentSettings.menuFavorites[category] = [...defaults[category]];
    } else {
      currentSettings.menuFavorites = { ...defaults };
    }
    saveSettings(currentSettings);
    res.json({
      settings: currentSettings,
      sampleFiles: listExplorerWorkspaceFiles(currentSettings),
    });
  });

  app.post('/api/presets', (req, res) => {
    const { name, targetFormat, quality, longestEdgePx, audioBitrateKbps, stripMetadata } =
      req.body as {
        name: string;
        targetFormat: string;
        quality?: number;
        longestEdgePx?: number | null;
        audioBitrateKbps?: number | null;
        stripMetadata?: boolean;
      };

    if (!name || !name.trim() || !targetFormat) {
      res.status(400).json({ error: 'El nombre del preset y el formato de destino son obligatorios.' });
      return;
    }

    const normFmt = normalizeFormatId(targetFormat);
    const desc = FORMAT_DESCRIPTORS[normFmt];
    if (!desc) {
      res.status(400).json({ error: `Formato desconocido: ${targetFormat}` });
      return;
    }

    const newPreset: ConversionPreset = {
      id: `preset-${Date.now()}`,
      name: name.trim(),
      targetFormat: normFmt,
      category: desc.category,
      quality: Math.max(1, Math.min(100, Number(quality) || 85)),
      longestEdgePx: longestEdgePx ? Number(longestEdgePx) : null,
      audioBitrateKbps: audioBitrateKbps ? Number(audioBitrateKbps) : null,
      stripMetadata: Boolean(stripMetadata),
    };

    currentSettings.presets = [newPreset, ...currentSettings.presets];
    saveSettings(currentSettings);
    res.json({ settings: currentSettings, preset: newPreset });
  });

  app.delete('/api/presets/:id', (req, res) => {
    currentSettings.presets = currentSettings.presets.filter((p) => p.id !== req.params.id);
    saveSettings(currentSettings);
    res.json({ settings: currentSettings });
  });

  app.post('/api/automations', (req, res) => {
    const { name, sourceDirectory, allowedInputFormats, targetFormat, conflictPolicy } =
      req.body as {
        name: string;
        sourceDirectory?: string;
        allowedInputFormats?: string[];
        targetFormat: string;
        conflictPolicy?: 'AutoRename' | 'Skip' | 'Overwrite';
      };

    if (!name || !name.trim() || !targetFormat) {
      res.status(400).json({ error: 'Nombre y formato destino son requeridos para la regla.' });
      return;
    }

    const folderName = name.trim().replace(/[^a-zA-Z0-9_-]/g, '_');
    const resolvedDir =
      sourceDirectory && sourceDirectory.trim()
        ? sourceDirectory.trim()
        : path.join(WATCHED_BASE_DIR, folderName);

    fs.mkdirSync(resolvedDir, { recursive: true });

    const newRule: AutomationRule = {
      id: `rule-${Date.now()}`,
      name: name.trim(),
      enabled: true,
      sourceDirectory: resolvedDir,
      allowedInputFormats:
        Array.isArray(allowedInputFormats) && allowedInputFormats.length > 0
          ? allowedInputFormats.map(normalizeFormatId)
          : ['png', 'jpeg'],
      targetFormat: normalizeFormatId(targetFormat),
      presetId: null,
      outputDirectory: null,
      copyConvertedFile: true,
      conflictPolicy: conflictPolicy ?? 'AutoRename',
      lastTriggeredAt: null,
      lastResult: 'Regla activa y observando carpeta',
      convertedCount: 0,
    };

    currentSettings.automations = [newRule, ...currentSettings.automations];
    saveSettings(currentSettings);
    res.json({ settings: currentSettings, rule: newRule });
  });

  app.patch('/api/automations/:id', (req, res) => {
    const { enabled, copyConvertedFile } = req.body as {
      enabled?: boolean;
      copyConvertedFile?: boolean;
    };
    currentSettings.automations = currentSettings.automations.map((r) => {
      if (r.id !== req.params.id) return r;
      const nextEnabled = enabled !== undefined ? enabled : r.enabled;
      return {
        ...r,
        enabled: nextEnabled,
        copyConvertedFile: copyConvertedFile !== undefined ? copyConvertedFile : r.copyConvertedFile,
        lastResult: nextEnabled ? 'Vigilando carpeta activamente' : 'En pausa',
      };
    });
    saveSettings(currentSettings);
    res.json({ settings: currentSettings });
  });

  app.delete('/api/automations/:id', (req, res) => {
    currentSettings.automations = currentSettings.automations.filter((r) => r.id !== req.params.id);
    saveSettings(currentSettings);
    res.json({ settings: currentSettings });
  });

  app.post('/api/automations/:id/trigger-test', async (req, res) => {
    try {
      const rule = currentSettings.automations.find((r) => r.id === req.params.id);
      if (!rule) {
        res.status(404).json({ error: 'Regla de automatización no encontrada.' });
        return;
      }

      if (!rule.enabled) {
        res.status(400).json({ error: 'La regla está en pausa. Activala primero para procesar archivos.' });
        return;
      }

      fs.mkdirSync(rule.sourceDirectory, { recursive: true });
      const sampleExt = rule.allowedInputFormats[0] || 'png';
      let templateFile = path.join(SAMPLES_DIR, 'arquitectura-diagrama.png');
      if (sampleExt === 'wav') {
        templateFile = path.join(SAMPLES_DIR, 'sintetizador-estudio.wav');
      } else if (['mp4', 'mkv', 'webm', 'mov'].includes(sampleExt)) {
        templateFile = path.join(SAMPLES_DIR, 'captura-demo.mp4');
      }

      const droppedFileName = `auto-drop-${Date.now().toString().slice(-5)}.${
        sampleExt === 'mkv' || sampleExt === 'webm' ? 'mp4' : sampleExt
      }`;
      const droppedFullPath = path.join(rule.sourceDirectory, droppedFileName);
      fs.copyFileSync(templateFile, droppedFullPath);

      // Ensure rule allows the dropped sample extension for testing
      const effectiveRule: AutomationRule = {
        ...rule,
        allowedInputFormats: Array.from(
          new Set([...rule.allowedInputFormats, normalizeFormatId(path.extname(droppedFullPath))])
        ),
      };

      const job = await automationManager.processCandidateFile(effectiveRule, droppedFullPath);

      currentSettings.automations = currentSettings.automations.map((r) => {
        if (r.id !== rule.id) return r;
        return {
          ...r,
          lastTriggeredAt: new Date().toISOString(),
          lastResult:
            job && job.status === 'Completed'
              ? `Convertido automáticamente: ${droppedFileName} → ${job.outputFileName} (sin bucle)`
              : `Error al procesar ${droppedFileName}`,
          convertedCount: r.convertedCount + (job?.status === 'Completed' ? 1 : 0),
        };
      });
      saveSettings(currentSettings);

      res.json({
        job,
        settings: currentSettings,
        jobs: queueEngine.getJobs(),
      });
    } catch (err) {
      res.status(500).json({ error: err instanceof Error ? err.message : String(err) });
    }
  });

  app.post('/api/cli/execute', async (req, res) => {
    const { args } = req.body as { args: string[] };
    if (!Array.isArray(args) || args.length === 0) {
      res.status(400).json({ error: 'Se requieren argumentos para la CLI.' });
      return;
    }

    const cliScript = path.join(ROOT_DIR, 'cli', 'fileshift-cli.ts');
    const resolvedArgs = args.map((token) => {
      if (fs.existsSync(path.join(SAMPLES_DIR, token))) {
        return path.join(SAMPLES_DIR, token);
      }
      return token;
    });

    const proc = await spawnSafeProcess('npx', ['tsx', cliScript, ...resolvedArgs], {
      timeoutMs: 25000,
    });

    res.json({
      command: `fileshift ${args.join(' ')}`,
      exitCode: proc.code,
      stdout: proc.stdout,
      stderr: proc.stderr,
      jobs: queueEngine.getJobs(),
      sampleFiles: listExplorerWorkspaceFiles(currentSettings),
    });
  });

  app.get('/api/repo/files', (req, res) => {
    const requestedPath = req.query.path ? String(req.query.path) : NATIVE_REPO_FILES[1].path;
    const entry = NATIVE_REPO_FILES.find((f) => f.path === requestedPath) || NATIVE_REPO_FILES[1];
    const absPath = path.join(ROOT_DIR, entry.path);
    const content = fs.existsSync(absPath) ? fs.readFileSync(absPath, 'utf8') : '// Archivo no encontrado';

    res.json({
      files: NATIVE_REPO_FILES,
      selectedFile: {
        ...entry,
        content,
      },
    });
  });

  app.get('/api/repo/archive', async (_req, res) => {
    try {
      const archivePath = path.join(RUNTIME_DIR, 'FileShift-Windows11-Repository.tar.gz');
      await spawnSafeProcess('tar', [
        '-czf',
        archivePath,
        '--exclude=node_modules',
        '--exclude=workspace-runtime',
        '--exclude=.playwright-cli',
        '--exclude=dist',
        '-C',
        ROOT_DIR,
        '.',
      ]);
      res.download(archivePath, 'FileShift-Windows11-Repository.tar.gz');
    } catch (err) {
      res.status(500).json({ error: err instanceof Error ? err.message : String(err) });
    }
  });

  const vite = await createViteServer({
    server: { middlewareMode: true },
    appType: 'spa',
  });
  app.use(vite.middlewares);

  const port = Number(process.env.PORT) || 3000;
  app.listen(port, '0.0.0.0', () => {
    console.log(`FileShift server listening on http://localhost:${port}`);
  });
}

startServer();
