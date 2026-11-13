import fs from 'node:fs';
import path from 'node:path';
import type { ConflictPolicy, FormatDescriptor, MediaCategory, UserSettings } from './types';

export const FORMAT_DESCRIPTORS: Record<string, FormatDescriptor> = {
  jpeg: {
    id: 'jpeg',
    displayName: 'JPEG',
    extension: 'jpg',
    category: 'image',
    engine: 'ImageMagick',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'image/jpeg',
  },
  png: {
    id: 'png',
    displayName: 'PNG',
    extension: 'png',
    category: 'image',
    engine: 'ImageMagick',
    lossless: true,
    supportsAlpha: true,
    supportsAnimation: false,
    mimeType: 'image/png',
  },
  webp: {
    id: 'webp',
    displayName: 'WebP',
    extension: 'webp',
    category: 'image',
    engine: 'ImageMagick',
    lossless: false,
    supportsAlpha: true,
    supportsAnimation: true,
    mimeType: 'image/webp',
  },
  avif: {
    id: 'avif',
    displayName: 'AVIF',
    extension: 'avif',
    category: 'image',
    engine: 'ImageMagick',
    lossless: false,
    supportsAlpha: true,
    supportsAnimation: false,
    mimeType: 'image/avif',
  },
  bmp: {
    id: 'bmp',
    displayName: 'BMP',
    extension: 'bmp',
    category: 'image',
    engine: 'ImageMagick',
    lossless: true,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'image/bmp',
  },
  gif: {
    id: 'gif',
    displayName: 'GIF',
    extension: 'gif',
    category: 'image',
    engine: 'ImageMagick',
    lossless: false,
    supportsAlpha: true,
    supportsAnimation: true,
    mimeType: 'image/gif',
  },
  tiff: {
    id: 'tiff',
    displayName: 'TIFF',
    extension: 'tiff',
    category: 'image',
    engine: 'ImageMagick',
    lossless: true,
    supportsAlpha: true,
    supportsAnimation: false,
    mimeType: 'image/tiff',
  },
  ico: {
    id: 'ico',
    displayName: 'ICO',
    extension: 'ico',
    category: 'image',
    engine: 'ImageMagick',
    lossless: true,
    supportsAlpha: true,
    supportsAnimation: false,
    mimeType: 'image/x-icon',
  },
  svg: {
    id: 'svg',
    displayName: 'SVG',
    extension: 'svg',
    category: 'svg',
    engine: 'ImageMagick',
    lossless: true,
    supportsAlpha: true,
    supportsAnimation: false,
    mimeType: 'image/svg+xml',
  },
  mp4: {
    id: 'mp4',
    displayName: 'MP4',
    extension: 'mp4',
    category: 'video',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: true,
    mimeType: 'video/mp4',
  },
  mkv: {
    id: 'mkv',
    displayName: 'MKV',
    extension: 'mkv',
    category: 'video',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: true,
    mimeType: 'video/x-matroska',
  },
  mov: {
    id: 'mov',
    displayName: 'MOV',
    extension: 'mov',
    category: 'video',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: true,
    mimeType: 'video/quicktime',
  },
  webm: {
    id: 'webm',
    displayName: 'WebM',
    extension: 'webm',
    category: 'video',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: true,
    supportsAnimation: true,
    mimeType: 'video/webm',
  },
  avi: {
    id: 'avi',
    displayName: 'AVI',
    extension: 'avi',
    category: 'video',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: true,
    mimeType: 'video/x-msvideo',
  },
  mp3: {
    id: 'mp3',
    displayName: 'MP3',
    extension: 'mp3',
    category: 'audio',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'audio/mpeg',
  },
  wav: {
    id: 'wav',
    displayName: 'WAV',
    extension: 'wav',
    category: 'audio',
    engine: 'FFmpeg',
    lossless: true,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'audio/wav',
  },
  flac: {
    id: 'flac',
    displayName: 'FLAC',
    extension: 'flac',
    category: 'audio',
    engine: 'FFmpeg',
    lossless: true,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'audio/flac',
  },
  aac: {
    id: 'aac',
    displayName: 'AAC',
    extension: 'aac',
    category: 'audio',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'audio/aac',
  },
  m4a: {
    id: 'm4a',
    displayName: 'M4A',
    extension: 'm4a',
    category: 'audio',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'audio/mp4',
  },
  ogg: {
    id: 'ogg',
    displayName: 'OGG',
    extension: 'ogg',
    category: 'audio',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'audio/ogg',
  },
  opus: {
    id: 'opus',
    displayName: 'Opus',
    extension: 'opus',
    category: 'audio',
    engine: 'FFmpeg',
    lossless: false,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'audio/opus',
  },
  pdf: {
    id: 'pdf',
    displayName: 'PDF',
    extension: 'pdf',
    category: 'pdf',
    engine: 'Ghostscript',
    lossless: true,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'application/pdf',
  },
  docx: {
    id: 'docx',
    displayName: 'DOCX',
    extension: 'docx',
    category: 'document',
    engine: 'LibreOffice',
    lossless: true,
    supportsAlpha: false,
    supportsAnimation: false,
    mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  },
};

export function normalizeFormatId(raw: string): string {
  const cleaned = raw.trim().replace(/^\./, '').toLowerCase();
  if (cleaned === 'jpg') return 'jpeg';
  if (cleaned === 'tif') return 'tiff';
  return cleaned;
}

export function getSupportedTargetsForFormat(
  sourceFormat: string,
  options: { libreOfficeAvailable?: boolean; avifAvailable?: boolean } = {}
): string[] {
  const norm = normalizeFormatId(sourceFormat);
  const desc = FORMAT_DESCRIPTORS[norm];
  if (!desc) return [];

  const avifOk = options.avifAvailable ?? false;
  const imageOutputs = ['jpeg', 'png', 'webp', ...(avifOk ? ['avif'] : []), 'bmp', 'gif', 'tiff', 'ico', 'pdf'];

  switch (desc.category) {
    case 'image':
      return imageOutputs.filter((t) => t !== norm);
    case 'svg':
      return ['png', 'jpeg', 'webp', 'bmp', 'tiff', 'ico', 'pdf'];
    case 'video':
      return ['mp4', 'mkv', 'mov', 'webm', 'avi', 'gif', 'mp3', 'wav', 'flac', 'aac', 'm4a', 'ogg', 'opus'].filter(
        (t) => t !== norm
      );
    case 'audio':
      return ['mp3', 'wav', 'flac', 'aac', 'm4a', 'ogg', 'opus'].filter((t) => t !== norm);
    case 'pdf':
      return ['png', 'jpeg'];
    case 'document':
      return options.libreOfficeAvailable ? ['pdf'] : [];
    default:
      return [];
  }
}

export function canConvertFormat(
  sourceFormat: string,
  targetFormat: string,
  options: { libreOfficeAvailable?: boolean; avifAvailable?: boolean } = {}
): boolean {
  const targets = getSupportedTargetsForFormat(sourceFormat, options);
  const normTarget = normalizeFormatId(targetFormat);
  return targets.includes(normTarget);
}

export interface BinarySignatureInspection {
  valid: boolean;
  detectedFormat: string;
  category: MediaCategory | 'unknown';
  reason?: string;
}

export function inspectBufferSignature(buffer: Buffer, fileNameHint = ''): BinarySignatureInspection {
  if (!buffer || buffer.length === 0) {
    return {
      valid: false,
      detectedFormat: 'unknown',
      category: 'unknown',
      reason: 'El archivo de entrada está vacío (0 bytes).',
    };
  }

  const ext = normalizeFormatId(path.extname(fileNameHint));

  // PNG: 89 50 4E 47 0D 0A 1A 0A
  if (
    buffer.length >= 8 &&
    buffer[0] === 0x89 &&
    buffer[1] === 0x50 &&
    buffer[2] === 0x4e &&
    buffer[3] === 0x47 &&
    buffer[4] === 0x0d &&
    buffer[5] === 0x0a &&
    buffer[6] === 0x1a &&
    buffer[7] === 0x0a
  ) {
    return { valid: true, detectedFormat: 'png', category: 'image' };
  }

  // JPEG: FF D8 FF
  if (buffer.length >= 3 && buffer[0] === 0xff && buffer[1] === 0xd8 && buffer[2] === 0xff) {
    return { valid: true, detectedFormat: 'jpeg', category: 'image' };
  }

  // GIF: GIF87a or GIF89a
  if (buffer.length >= 6) {
    const sig6 = buffer.subarray(0, 6).toString('ascii');
    if (sig6 === 'GIF87a' || sig6 === 'GIF89a') {
      return { valid: true, detectedFormat: 'gif', category: 'image' };
    }
  }

  // BMP: BM
  if (buffer.length >= 2 && buffer[0] === 0x42 && buffer[1] === 0x4d) {
    return { valid: true, detectedFormat: 'bmp', category: 'image' };
  }

  // TIFF: II*\0 or MM\0*
  if (
    buffer.length >= 4 &&
    ((buffer[0] === 0x49 && buffer[1] === 0x49 && buffer[2] === 0x2a && buffer[3] === 0x00) ||
      (buffer[0] === 0x4d && buffer[1] === 0x4d && buffer[2] === 0x00 && buffer[3] === 0x2a))
  ) {
    return { valid: true, detectedFormat: 'tiff', category: 'image' };
  }

  // ICO: 00 00 01 00
  if (buffer.length >= 4 && buffer[0] === 0x00 && buffer[1] === 0x00 && buffer[2] === 0x01 && buffer[3] === 0x00) {
    return { valid: true, detectedFormat: 'ico', category: 'image' };
  }

  // RIFF (WEBP, WAVE, AVI)
  if (buffer.length >= 12 && buffer.subarray(0, 4).toString('ascii') === 'RIFF') {
    const fourCc = buffer.subarray(8, 12).toString('ascii');
    if (fourCc === 'WEBP') return { valid: true, detectedFormat: 'webp', category: 'image' };
    if (fourCc === 'WAVE') return { valid: true, detectedFormat: 'wav', category: 'audio' };
    if (fourCc === 'AVI ') return { valid: true, detectedFormat: 'avi', category: 'video' };
  }

  // ISO BMFF (ftyp at offset 4)
  if (buffer.length >= 12 && buffer.subarray(4, 8).toString('ascii') === 'ftyp') {
    const brand = buffer.subarray(8, 12).toString('ascii');
    if (brand === 'avif' || brand === 'avis') return { valid: true, detectedFormat: 'avif', category: 'image' };
    if (brand === 'M4A ' || brand === 'M4B ' || ext === 'm4a')
      return { valid: true, detectedFormat: 'm4a', category: 'audio' };
    if (brand === 'qt  ' || ext === 'mov') return { valid: true, detectedFormat: 'mov', category: 'video' };
    return { valid: true, detectedFormat: 'mp4', category: 'video' };
  }

  // Matroska / WebM: 1A 45 DF A3
  if (
    buffer.length >= 4 &&
    buffer[0] === 0x1a &&
    buffer[1] === 0x45 &&
    buffer[2] === 0xdf &&
    buffer[3] === 0xa3
  ) {
    const windowStr = buffer.subarray(0, Math.min(buffer.length, 64)).toString('ascii').toLowerCase();
    if (windowStr.includes('webm') || ext === 'webm') {
      return { valid: true, detectedFormat: 'webm', category: 'video' };
    }
    return { valid: true, detectedFormat: 'mkv', category: 'video' };
  }

  // FLAC: fLaC
  if (buffer.length >= 4 && buffer.subarray(0, 4).toString('ascii') === 'fLaC') {
    return { valid: true, detectedFormat: 'flac', category: 'audio' };
  }

  // Ogg / Opus: OggS
  if (buffer.length >= 4 && buffer.subarray(0, 4).toString('ascii') === 'OggS') {
    const windowAscii = buffer.subarray(0, Math.min(buffer.length, 64)).toString('ascii');
    if (windowAscii.includes('OpusHead') || ext === 'opus') {
      return { valid: true, detectedFormat: 'opus', category: 'audio' };
    }
    return { valid: true, detectedFormat: 'ogg', category: 'audio' };
  }

  // MP3 / AAC: ID3 or MPEG/ADTS sync word
  if (buffer.length >= 3 && buffer.subarray(0, 3).toString('ascii') === 'ID3') {
    return { valid: true, detectedFormat: ext === 'aac' ? 'aac' : 'mp3', category: 'audio' };
  }
  if (buffer.length >= 2 && buffer[0] === 0xff && (buffer[1] & 0xe0) === 0xe0) {
    return { valid: true, detectedFormat: ext === 'aac' ? 'aac' : 'mp3', category: 'audio' };
  }

  // PDF: %PDF-
  if (buffer.length >= 5 && buffer.subarray(0, 5).toString('ascii') === '%PDF-') {
    return { valid: true, detectedFormat: 'pdf', category: 'pdf' };
  }

  // ZIP / Office OpenXML
  if (buffer.length >= 4 && buffer[0] === 0x50 && buffer[1] === 0x4b && buffer[2] === 0x03 && buffer[3] === 0x04) {
    if (['docx', 'odt', 'xlsx', 'pptx'].includes(ext)) {
      return { valid: true, detectedFormat: ext, category: 'document' };
    }
  }

  // SVG XML
  const textSample = buffer.subarray(0, Math.min(buffer.length, 256)).toString('utf8').trimStart();
  if (
    textSample.toLowerCase().startsWith('<svg') ||
    (textSample.toLowerCase().startsWith('<?xml') && textSample.toLowerCase().includes('<svg'))
  ) {
    return { valid: true, detectedFormat: 'svg', category: 'svg' };
  }

  return {
    valid: false,
    detectedFormat: 'unknown',
    category: 'unknown',
    reason: `Firma binaria inválida o archivo corrupto (${fileNameHint || 'sin nombre'}).`,
  };
}

export function inspectFileSignature(filePath: string): BinarySignatureInspection {
  if (!fs.existsSync(filePath)) {
    return {
      valid: false,
      detectedFormat: 'unknown',
      category: 'unknown',
      reason: `El archivo no existe: ${filePath}`,
    };
  }
  const stat = fs.statSync(filePath);
  if (stat.size === 0) {
    return {
      valid: false,
      detectedFormat: 'unknown',
      category: 'unknown',
      reason: 'El archivo está vacío (0 bytes).',
    };
  }

  const fd = fs.openSync(filePath, 'r');
  try {
    const buf = Buffer.alloc(Math.min(256, stat.size));
    fs.readSync(fd, buf, 0, buf.length, 0);
    return inspectBufferSignature(buf, path.basename(filePath));
  } finally {
    fs.closeSync(fd);
  }
}

export function resolveSafeOutputPath(
  inputPath: string,
  targetFormat: string,
  customOutputDir: string | null | undefined,
  conflictPolicy: ConflictPolicy
): string | null {
  const normTarget = normalizeFormatId(targetFormat);
  const descriptor = FORMAT_DESCRIPTORS[normTarget];
  const ext = descriptor ? descriptor.extension : normTarget;

  const fullInput = path.resolve(inputPath);
  const outDir = customOutputDir && customOutputDir.trim() ? path.resolve(customOutputDir) : path.dirname(fullInput);
  fs.mkdirSync(outDir, { recursive: true });

  const parsed = path.parse(fullInput);
  let candidate = path.join(outDir, `${parsed.name}.${ext}`);

  // Never allow candidate output path to collide with original input path
  if (path.resolve(candidate) === fullInput) {
    candidate = path.join(outDir, `${parsed.name} (converted).${ext}`);
  }

  if (!fs.existsSync(candidate)) {
    return candidate;
  }

  if (conflictPolicy === 'Skip') {
    return null;
  }

  if (conflictPolicy === 'Overwrite') {
    return candidate;
  }

  // Default: AutoRename -> file (1).ext, file (2).ext...
  for (let i = 1; i < 10000; i++) {
    const numbered = path.join(outDir, `${parsed.name} (${i}).${ext}`);
    if (!fs.existsSync(numbered) && path.resolve(numbered) !== fullInput) {
      return numbered;
    }
  }

  return path.join(outDir, `${parsed.name} (${Date.now()}).${ext}`);
}

export function buildContextMenuTargets(
  sourceFormat: string,
  menuFavorites: Record<MediaCategory, string[]>
): { favoriteTargets: string[]; otherTargets: string[] } {
  const norm = normalizeFormatId(sourceFormat);
  const desc = FORMAT_DESCRIPTORS[norm];
  if (!desc) return { favoriteTargets: [], otherTargets: [] };

  const allCompatible = getSupportedTargetsForFormat(norm);
  const configuredFavs = (menuFavorites[desc.category] || []).map(normalizeFormatId);

  const favoriteTargets = configuredFavs.filter((f) => allCompatible.includes(f));
  const otherTargets = allCompatible.filter((f) => !favoriteTargets.includes(f));

  return { favoriteTargets, otherTargets };
}

export function createDefaultUserSettings(watchedBaseDir: string): UserSettings {
  return {
    saveNextToOriginal: true,
    customOutputDirectory: '',
    defaultQuality: 85,
    maxConcurrency: 4,
    defaultConflictPolicy: 'AutoRename',
    showNotifications: true,
    revealInExplorerWhenDone: false,
    keepRunningInBackground: true,
    launchAtWindowsStartup: false,
    contextMenuEnabled: true,
    classicMenuFallbackEnabled: false,
    language: 'es',
    menuFavorites: {
      image: ['jpeg', 'png', 'webp'],
      svg: ['png', 'jpeg', 'webp'],
      video: ['mp4', 'mov', 'gif', 'mp3'],
      audio: ['mp3', 'm4a', 'wav'],
      pdf: ['png', 'jpeg'],
      document: ['pdf'],
    },
    presets: [
      {
        id: 'preset-web-webp',
        name: 'Imagen para Web (WebP)',
        targetFormat: 'webp',
        category: 'image',
        quality: 82,
        longestEdgePx: 1920,
        audioBitrateKbps: null,
        stripMetadata: true,
      },
      {
        id: 'preset-jpeg-hq',
        name: 'JPEG de Calidad Alta',
        targetFormat: 'jpeg',
        category: 'image',
        quality: 92,
        longestEdgePx: null,
        audioBitrateKbps: null,
        stripMetadata: false,
      },
      {
        id: 'preset-video-mp4',
        name: 'Video MP4 Compatible (H.264)',
        targetFormat: 'mp4',
        category: 'video',
        quality: 80,
        longestEdgePx: 1280,
        audioBitrateKbps: 192,
        stripMetadata: false,
      },
      {
        id: 'preset-audio-mp3',
        name: 'Audio MP3 192 kbps',
        targetFormat: 'mp3',
        category: 'audio',
        quality: 85,
        longestEdgePx: null,
        audioBitrateKbps: 192,
        stripMetadata: false,
      },
    ],
    automations: [
      {
        id: 'rule-screenshots-webp',
        name: 'Capturas PNG → WebP',
        enabled: true,
        sourceDirectory: path.join(watchedBaseDir, 'Screenshots'),
        allowedInputFormats: ['png'],
        targetFormat: 'webp',
        presetId: 'preset-web-webp',
        outputDirectory: null,
        copyConvertedFile: true,
        conflictPolicy: 'AutoRename',
        lastTriggeredAt: null,
        lastResult: 'Vigilando carpeta activamente (sin bucles)',
        convertedCount: 0,
      },
      {
        id: 'rule-recordings-mp4',
        name: 'Grabaciones MKV/WebM → MP4',
        enabled: true,
        sourceDirectory: path.join(watchedBaseDir, 'Captures'),
        allowedInputFormats: ['mkv', 'webm', 'mov'],
        targetFormat: 'mp4',
        presetId: 'preset-video-mp4',
        outputDirectory: null,
        copyConvertedFile: false,
        conflictPolicy: 'AutoRename',
        lastTriggeredAt: null,
        lastResult: 'Vigilando carpeta activamente',
        convertedCount: 0,
      },
      {
        id: 'rule-audio-mp3',
        name: 'Notas de voz WAV → MP3',
        enabled: false,
        sourceDirectory: path.join(watchedBaseDir, 'VoiceNotes'),
        allowedInputFormats: ['wav'],
        targetFormat: 'mp3',
        presetId: 'preset-audio-mp3',
        outputDirectory: null,
        copyConvertedFile: false,
        conflictPolicy: 'AutoRename',
        lastTriggeredAt: null,
        lastResult: 'En pausa',
        convertedCount: 0,
      },
    ],
  };
}
