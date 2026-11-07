export type MediaCategory = 'image' | 'svg' | 'video' | 'audio' | 'pdf' | 'document';

export type JobStatus =
  | 'Queued'
  | 'Preparing'
  | 'Running'
  | 'Validating'
  | 'Completed'
  | 'Failed'
  | 'Cancelled';

export type ConflictPolicy = 'AutoRename' | 'Skip' | 'Overwrite';

export interface FormatDescriptor {
  id: string;
  displayName: string;
  extension: string;
  category: MediaCategory;
  engine: 'ImageMagick' | 'FFmpeg' | 'Ghostscript' | 'LibreOffice';
  lossless: boolean;
  supportsAlpha: boolean;
  supportsAnimation: boolean;
  mimeType: string;
}

export interface ConversionOptions {
  quality?: number;
  longestEdgePx?: number | null;
  audioBitrateKbps?: number | null;
  audioSampleRate?: number | null;
  stripMetadata?: boolean;
  backgroundHex?: string;
  pageRange?: string | null;
  conflictPolicy?: ConflictPolicy;
  customOutputDir?: string | null;
  presetId?: string | null;
  triggeredBy?: 'ContextMenu' | 'DragDrop' | 'Automation' | 'CLI';
}

export interface ConversionJob {
  id: string;
  inputPath: string;
  fileName: string;
  sourceFormat: string;
  targetFormat: string;
  outputPath: string | null;
  outputFileName: string | null;
  engineUsed: string;
  status: JobStatus;
  progressPercent: number;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  errorMessage: string | null;
  inputSizeBytes: number;
  outputSizeBytes: number | null;
  originalSha256: string | null;
  postOriginalSha256: string | null;
  originalIntactVerified: boolean;
  probeSummary: string | null;
  options: Required<ConversionOptions>;
}

export interface ConversionPreset {
  id: string;
  name: string;
  targetFormat: string;
  category: MediaCategory;
  quality: number;
  longestEdgePx: number | null;
  audioBitrateKbps: number | null;
  stripMetadata: boolean;
}

export interface AutomationRule {
  id: string;
  name: string;
  enabled: boolean;
  sourceDirectory: string;
  allowedInputFormats: string[];
  targetFormat: string;
  presetId: string | null;
  outputDirectory: string | null;
  copyConvertedFile: boolean;
  conflictPolicy: ConflictPolicy;
  lastTriggeredAt: string | null;
  lastResult: string | null;
  convertedCount: number;
}

export interface UserSettings {
  saveNextToOriginal: boolean;
  customOutputDirectory: string;
  defaultQuality: number;
  maxConcurrency: number;
  defaultConflictPolicy: ConflictPolicy;
  showNotifications: boolean;
  revealInExplorerWhenDone: boolean;
  keepRunningInBackground: boolean;
  launchAtWindowsStartup: boolean;
  contextMenuEnabled: boolean;
  classicMenuFallbackEnabled: boolean;
  language: 'es' | 'en';
  menuFavorites: Record<MediaCategory, string[]>;
  presets: ConversionPreset[];
  automations: AutomationRule[];
}

export interface EngineDiagnostic {
  id: string;
  name: string;
  available: boolean;
  version: string;
  binaryPath: string;
  capabilities: string[];
  license: string;
}

export interface WorkspaceSampleFile {
  name: string;
  relativePath: string;
  fullPath: string;
  sizeBytes: number;
  detectedFormat: string;
  category: MediaCategory;
  sha256: string;
  compatibleTargets: string[];
  favoriteTargets: string[];
  otherTargets: string[];
}
