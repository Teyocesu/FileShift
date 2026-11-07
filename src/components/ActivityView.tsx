import React, { useRef, useState } from 'react';
import {
  CheckCircle2,
  ChevronRight,
  Download,
  FileAudio,
  FileCode2,
  FileImage,
  FileText,
  FileVideo,
  FolderOpen,
  Layers,
  MousePointerClick,
  RefreshCw,
  ShieldAlert,
  ShieldCheck,
  Upload,
  XCircle,
} from 'lucide-react';
import type {
  ConversionJob,
  ConversionPreset,
  UserSettings,
  WorkspaceSampleFile,
} from '../core/types';
import type { TranslationDict } from '../i18n';

interface ActivityViewProps {
  t: TranslationDict;
  settings: UserSettings;
  jobs: ConversionJob[];
  sampleFiles: WorkspaceSampleFile[];
  isConverting: boolean;
  onConvertSampleFiles: (
    fileNames: string[],
    targetFormat: string,
    presetId?: string | null,
    triggeredBy?: 'ContextMenu' | 'DragDrop'
  ) => Promise<void>;
  onUploadAndConvert: (
    file: File,
    targetFormat: string,
    presetId?: string | null
  ) => Promise<void>;
  onTestCorruptFile: () => Promise<void>;
  onCancelJob: (jobId: string) => Promise<void>;
  onRetryJob: (jobId: string) => Promise<void>;
  onClearCompleted: () => Promise<void>;
}

function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function getCategoryIcon(category: string) {
  switch (category) {
    case 'image':
    case 'svg':
      return <FileImage className="w-4 h-4 text-teal-400 shrink-0" />;
    case 'video':
      return <FileVideo className="w-4 h-4 text-sky-400 shrink-0" />;
    case 'audio':
      return <FileAudio className="w-4 h-4 text-amber-400 shrink-0" />;
    case 'pdf':
      return <FileText className="w-4 h-4 text-rose-400 shrink-0" />;
    default:
      return <FileCode2 className="w-4 h-4 text-slate-400 shrink-0" />;
  }
}

export const ActivityView: React.FC<ActivityViewProps> = ({
  t,
  settings,
  jobs,
  sampleFiles,
  isConverting,
  onConvertSampleFiles,
  onUploadAndConvert,
  onTestCorruptFile,
  onCancelJob,
  onRetryJob,
  onClearCompleted,
}) => {
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [selectedSampleNames, setSelectedSampleNames] = useState<string[]>([
    'arquitectura-diagrama.png',
  ]);
  const [openMenuForFile, setOpenMenuForFile] = useState<string | null>(
    'arquitectura-diagrama.png'
  );
  const [showMoreFormatsMenu, setShowMoreFormatsMenu] = useState(false);
  const [uploadTargetFormat, setUploadTargetFormat] = useState<string>('webp');
  const [selectedPresetId, setSelectedPresetId] = useState<string>('');
  const [isDraggingOver, setIsDraggingOver] = useState(false);

  const activeMenuFile =
    sampleFiles.find((f) => f.name === openMenuForFile) || sampleFiles[0] || null;

  // Determine batch selection if multiple files of the same category are checked
  const activeSelectionFiles = sampleFiles.filter((f) =>
    selectedSampleNames.includes(f.name)
  );
  const effectiveContextFiles =
    activeMenuFile && selectedSampleNames.includes(activeMenuFile.name) && activeSelectionFiles.length > 1
      ? activeSelectionFiles.filter((f) => f.category === activeMenuFile.category)
      : activeMenuFile
      ? [activeMenuFile]
      : [];

  const toggleSampleSelection = (fileName: string) => {
    setSelectedSampleNames((prev) =>
      prev.includes(fileName)
        ? prev.filter((item) => item !== fileName)
        : [...prev, fileName]
    );
  };

  const handleFileInputChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;
    for (let i = 0; i < files.length; i++) {
      await onUploadAndConvert(
        files[i],
        uploadTargetFormat,
        selectedPresetId || null
      );
    }
    e.target.value = '';
  };

  const handleDrop = async (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setIsDraggingOver(false);
    const files = e.dataTransfer.files;
    if (!files || files.length === 0) return;
    for (let i = 0; i < files.length; i++) {
      await onUploadAndConvert(
        files[i],
        uploadTargetFormat,
        selectedPresetId || null
      );
    }
  };

  const getVerbLabel = (sourceCategory: string, targetFmt: string): string => {
    const upper = targetFmt.toUpperCase();
    if (
      sourceCategory === 'video' &&
      ['mp3', 'wav', 'flac', 'aac', 'm4a', 'ogg', 'opus'].includes(targetFmt)
    ) {
      return settings.language === 'es' ? `Extraer ${upper}` : `Extract ${upper}`;
    }
    return settings.language === 'es' ? `Convertir a ${upper}` : `Convert to ${upper}`;
  };

  return (
    <div className="space-y-8">
      {/* Priority #1: Windows 11 Explorer Modern Context Menu Workspace */}
      <section
        aria-label={t.explorerTitle}
        className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6"
      >
        <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4 pb-5 border-b border-[#1E2532]">
          <div>
            <h2 className="text-lg font-semibold text-slate-100">{t.explorerTitle}</h2>
            <p className="text-xs text-slate-400 mt-1">{t.explorerSubtitle}</p>
          </div>
          <div className="flex flex-wrap items-center gap-3 text-xs text-slate-400">
            <span>CLSID: {'{D87E4B9A-6C21-4F08-9E3A-5A18C42B7101}'}</span>
            <span aria-hidden="true">·</span>
            <span>
              {settings.contextMenuEnabled
                ? 'IExplorerCommand Activo'
                : 'Menú Contextual Desactivado'}
            </span>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 pt-5 items-start">
          {/* Left 7 cols: Real files in workspace-runtime/explorer-files */}
          <div className="lg:col-span-7 space-y-2">
            <div className="flex items-center justify-between text-xs text-slate-400 pb-2 px-2">
              <span>Archivos reales en disco (clic derecho o botón para abrir submenú)</span>
              <span className="tabular-nums">
                {selectedSampleNames.length} seleccionado(s)
              </span>
            </div>

            <div className="divide-y divide-[#1E2532] border border-[#1E2532] rounded-lg bg-[#0B0D11]">
              {sampleFiles.map((file) => {
                const isSelected = selectedSampleNames.includes(file.name);
                const isMenuOpen = openMenuForFile === file.name;
                return (
                  <div
                    key={file.name}
                    onContextMenu={(e) => {
                      e.preventDefault();
                      setOpenMenuForFile(file.name);
                      setShowMoreFormatsMenu(false);
                    }}
                    className={`flex items-center justify-between gap-3 px-3.5 py-2.5 transition-colors ${
                      isMenuOpen
                        ? 'bg-teal-950/30'
                        : isSelected
                        ? 'bg-slate-900/80'
                        : 'hover:bg-slate-900/40'
                    }`}
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <input
                        type="checkbox"
                        aria-label={`Seleccionar ${file.name}`}
                        checked={isSelected}
                        onChange={() => toggleSampleSelection(file.name)}
                        className="w-4 h-4 accent-teal-500 rounded cursor-pointer"
                      />
                      {getCategoryIcon(file.category)}
                      <div className="min-w-0">
                        <div className="text-sm font-medium text-slate-100 truncate">
                          {file.name}
                        </div>
                        <div className="flex items-center gap-2 text-xs text-slate-400 font-mono tabular-nums">
                          <span>{file.detectedFormat.toUpperCase()}</span>
                          <span aria-hidden="true">·</span>
                          <span>{formatBytes(file.sizeBytes)}</span>
                          <span aria-hidden="true">·</span>
                          <span title={`SHA-256: ${file.sha256}`}>
                            sha256:{file.sha256.slice(0, 10)}…
                          </span>
                        </div>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 shrink-0">
                      <a
                        href={`/api/files/download?path=${encodeURIComponent(file.fullPath)}`}
                        className="px-2.5 py-1.5 text-xs text-slate-300 hover:text-white bg-slate-800/70 hover:bg-slate-800 rounded-md transition-colors whitespace-nowrap shrink-0"
                        title={`Descargar ${file.name}`}
                      >
                         Original
                      </a>
                      <button
                        type="button"
                        onClick={() => {
                          setOpenMenuForFile(file.name);
                          setShowMoreFormatsMenu(false);
                        }}
                        className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors whitespace-nowrap shrink-0 ${
                          isMenuOpen
                            ? 'bg-teal-500 text-slate-950 font-semibold'
                            : 'bg-[#18202C] text-slate-200 hover:bg-slate-800 border border-[#263042]'
                        }`}
                      >
                        <MousePointerClick className="w-3.5 h-3.5" />
                        <span>{t.convertToMenu}</span>
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Right 5 cols: Authentic Windows 11 Acrylic Context Menu Preview & Direct Trigger */}
          <div className="lg:col-span-5 bg-[#0E131B] border border-[#242E42] rounded-xl p-4 shadow-xl">
            {!settings.contextMenuEnabled ? (
              <div className="text-xs text-amber-300 py-6 text-center">
                La integración con el menú contextual está desactivada en Configuración → Menú Contextual.
              </div>
            ) : activeMenuFile ? (
              <div className="space-y-3">
                <div className="flex items-center justify-between border-b border-[#1E2532] pb-2.5">
                  <div className="min-w-0">
                    <div className="text-xs text-slate-400">
                      Menú Contextual Windows 11 (IExplorerCommand)
                    </div>
                    <div className="text-sm font-semibold text-teal-300 truncate mt-0.5">
                      {effectiveContextFiles.length > 1
                        ? `${effectiveContextFiles.length} archivos (${activeMenuFile.category.toUpperCase()})`
                        : activeMenuFile.name}
                    </div>
                  </div>
                  <span className="text-xs font-mono text-slate-400 tabular-nums">
                    .{activeMenuFile.detectedFormat}
                  </span>
                </div>

                {/* Primary Submenu Items (Configured Favorites in Order) */}
                <div className="space-y-1" role="menu" aria-label="Submenú Convertir a">
                  {activeMenuFile.favoriteTargets.map((targetFmt, idx) => (
                    <button
                      key={targetFmt}
                      type="button"
                      role="menuitem"
                      disabled={isConverting}
                      onClick={() =>
                        onConvertSampleFiles(
                          effectiveContextFiles.map((f) => f.name),
                          targetFmt,
                          null,
                          'ContextMenu'
                        )
                      }
                      className="w-full flex items-center justify-between px-3 py-2 text-xs font-medium text-slate-100 hover:bg-teal-500/15 hover:text-teal-200 rounded-lg transition-colors text-left group disabled:opacity-50"
                    >
                      <span className="flex items-center gap-2.5">
                        <span className="w-5 h-5 rounded bg-teal-500/20 text-teal-300 font-mono text-[11px] flex items-center justify-center font-semibold">
                          {idx + 1}
                        </span>
                        <span>{getVerbLabel(activeMenuFile.category, targetFmt)}</span>
                      </span>
                      <span className="text-[11px] font-mono text-slate-400 group-hover:text-teal-300">
                        .{targetFmt === 'jpeg' ? 'jpg' : targetFmt}
                      </span>
                    </button>
                  ))}

                  {/* Expandable "Más formatos…" cascading submenu */}
                  {activeMenuFile.otherTargets.length > 0 && (
                    <div className="pt-1 border-t border-[#1E2532]">
                      <button
                        type="button"
                        onClick={() => setShowMoreFormatsMenu((prev) => !prev)}
                        className="w-full flex items-center justify-between px-3 py-2 text-xs text-slate-300 hover:bg-slate-800/80 rounded-lg transition-colors"
                      >
                        <span>{t.moreFormats}</span>
                        <ChevronRight
                          className={`w-3.5 h-3.5 transition-transform ${
                            showMoreFormatsMenu ? 'rotate-90' : ''
                          }`}
                        />
                      </button>

                      {showMoreFormatsMenu && (
                        <div className="mt-1 pl-3 space-y-1 border-l border-[#263147]">
                          {activeMenuFile.otherTargets.map((targetFmt) => (
                            <button
                              key={targetFmt}
                              type="button"
                              role="menuitem"
                              disabled={isConverting}
                              onClick={() =>
                                onConvertSampleFiles(
                                  effectiveContextFiles.map((f) => f.name),
                                  targetFmt,
                                  null,
                                  'ContextMenu'
                                )
                              }
                              className="w-full flex items-center justify-between px-3 py-1.5 text-xs text-slate-300 hover:bg-teal-500/15 hover:text-teal-200 rounded-md transition-colors text-left"
                            >
                              <span>
                                {getVerbLabel(activeMenuFile.category, targetFmt)}
                              </span>
                              <span className="text-[11px] font-mono text-slate-400">
                                .{targetFmt === 'jpeg' ? 'jpg' : targetFmt}
                              </span>
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            ) : null}
          </div>
        </div>
      </section>

      {/* Drag & Drop + File Picker Conversion Bar */}
      <section
        aria-label={t.dropTitle}
        onDragOver={(e) => {
          e.preventDefault();
          setIsDraggingOver(true);
        }}
        onDragLeave={() => setIsDraggingOver(false)}
        onDrop={handleDrop}
        className={`border border-dashed rounded-xl p-6 transition-colors ${
          isDraggingOver
            ? 'border-teal-400 bg-teal-950/20'
            : 'border-[#263042] bg-[#12161E]/70'
        }`}
      >
        <input
          ref={fileInputRef}
          type="file"
          multiple
          onChange={handleFileInputChange}
          className="hidden"
          aria-label="Subir archivos desde tu dispositivo"
        />

        <div className="flex flex-col xl:flex-row xl:items-center justify-between gap-5">
          <div className="flex items-center gap-4 min-w-0 flex-1">
            <div className="w-11 h-11 rounded-xl bg-teal-500/10 border border-teal-500/30 flex items-center justify-center shrink-0">
              <Layers className="w-5 h-5 text-teal-400" />
            </div>
            <div className="min-w-0 flex-1">
              <h3 className="text-base font-semibold text-slate-100">{t.dropTitle}</h3>
              <p className="text-xs text-slate-400 mt-0.5 leading-relaxed">{t.dropSubtitle}</p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-3 shrink-0">
            <div className="flex items-center gap-2">
              <label htmlFor="select-upload-format" className="text-xs text-slate-400">
                Destino:
              </label>
              <select
                id="select-upload-format"
                value={uploadTargetFormat}
                onChange={(e) => setUploadTargetFormat(e.target.value)}
                className="bg-[#0B0D11] border border-[#263042] rounded-lg px-3 py-2 text-xs text-slate-100 focus:outline-none focus:border-teal-500"
              >
                <optgroup label="Imágenes">
                  <option value="webp">WebP</option>
                  <option value="jpeg">JPEG</option>
                  <option value="png">PNG</option>
                  <option value="avif">AVIF</option>
                  <option value="bmp">BMP</option>
                  <option value="gif">GIF</option>
                  <option value="tiff">TIFF</option>
                  <option value="ico">ICO</option>
                  <option value="pdf">PDF</option>
                </optgroup>
                <optgroup label="Video">
                  <option value="mp4">MP4 (H.264)</option>
                  <option value="webm">WebM (VP9)</option>
                  <option value="mkv">MKV</option>
                  <option value="mov">MOV</option>
                  <option value="avi">AVI</option>
                </optgroup>
                <optgroup label="Audio">
                  <option value="mp3">MP3</option>
                  <option value="wav">WAV</option>
                  <option value="flac">FLAC</option>
                  <option value="aac">AAC</option>
                  <option value="m4a">M4A</option>
                  <option value="ogg">OGG</option>
                  <option value="opus">Opus</option>
                </optgroup>
              </select>
            </div>

            <div className="flex items-center gap-2">
              <label htmlFor="select-upload-preset" className="text-xs text-slate-400">
                Preset:
              </label>
              <select
                id="select-upload-preset"
                value={selectedPresetId}
                onChange={(e) => {
                  const pid = e.target.value;
                  setSelectedPresetId(pid);
                  const found: ConversionPreset | undefined = settings.presets.find(
                    (p) => p.id === pid
                  );
                  if (found) setUploadTargetFormat(found.targetFormat);
                }}
                className="bg-[#0B0D11] border border-[#263042] rounded-lg px-3 py-2 text-xs text-slate-100 focus:outline-none focus:border-teal-500"
              >
                <option value="">Sin preset (Calidad {settings.defaultQuality}%)</option>
                {settings.presets.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} (.{p.targetFormat})
                  </option>
                ))}
              </select>
            </div>

            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              className="flex items-center gap-2 px-4 py-2 text-xs font-semibold bg-teal-500 hover:bg-teal-400 text-slate-950 rounded-lg transition-colors whitespace-nowrap shrink-0"
            >
              <Upload className="w-3.5 h-3.5" />
              <span>{t.chooseFiles}</span>
            </button>

            <button
              type="button"
              onClick={onTestCorruptFile}
              className="flex items-center gap-1.5 px-3 py-2 text-xs font-medium bg-[#18202C] hover:bg-slate-800 text-slate-300 border border-[#263042] rounded-lg transition-colors whitespace-nowrap shrink-0"
              title="Inyecta un archivo con cabecera binaria corrupta para verificar que el motor lo rechaza sin crear salidas vacías"
            >
              <ShieldAlert className="w-3.5 h-3.5 text-amber-400" />
              <span>{t.testCorruptFile}</span>
            </button>
          </div>
        </div>
      </section>

      {/* Conversion Queue & Recent History */}
      <section
        aria-label={t.queueTitle}
        className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6"
      >
        <div className="flex items-center justify-between pb-4 border-b border-[#1E2532]">
          <div>
            <h2 className="text-lg font-semibold text-slate-100">{t.queueTitle}</h2>
            <p className="text-xs text-slate-400 mt-0.5">
              Escritura atómica mediante archivo temporal verificado · Invariante SHA-256 del original activo
            </p>
          </div>
          {jobs.length > 0 && (
            <button
              type="button"
              onClick={onClearCompleted}
              className="px-3 py-1.5 text-xs font-medium text-slate-300 hover:text-white bg-slate-800/80 hover:bg-slate-800 rounded-lg transition-colors whitespace-nowrap shrink-0"
            >
              {t.clearCompleted}
            </button>
          )}
        </div>

        {jobs.length === 0 ? (
          <div className="py-12 text-center space-y-3">
            <FolderOpen className="w-8 h-8 text-slate-500 mx-auto" />
            <p className="text-sm text-slate-300">{t.emptyActivity}</p>
            <p className="text-xs text-slate-500 max-w-md mx-auto">
              Elegí un destino en el panel del menú contextual de Windows 11 arriba para ejecutar una conversión real con FFmpeg, ImageMagick o Ghostscript.
            </p>
          </div>
        ) : (
          <div className="divide-y divide-[#1E2532] mt-2">
            {jobs.map((job) => (
              <div
                key={job.id}
                className="py-4 flex flex-col lg:flex-row lg:items-center justify-between gap-4"
              >
                <div className="space-y-1.5 min-w-0 flex-1">
                  <div className="flex items-center gap-2.5 flex-wrap">
                    {job.status === 'Completed' && (
                      <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
                    )}
                    {job.status === 'Failed' && (
                      <XCircle className="w-4 h-4 text-rose-400 shrink-0" />
                    )}
                    {job.status !== 'Completed' && job.status !== 'Failed' && (
                      <RefreshCw className="w-4 h-4 text-sky-400 animate-spin shrink-0" />
                    )}

                    <span className="text-sm font-semibold text-slate-100 truncate">
                      {job.fileName}
                    </span>
                    <span className="text-xs text-slate-400">→</span>
                    <span className="text-sm font-semibold text-teal-300">
                      {job.outputFileName || `.${job.targetFormat}`}
                    </span>
                  </div>

                  {/* Clean unboxed metadata with typographic separators */}
                  <div className="flex flex-wrap items-center gap-2 text-xs text-slate-400 font-mono tabular-nums">
                    <span>Estado: {job.status}</span>
                    <span aria-hidden="true">·</span>
                    <span>Motor: {job.engineUsed}</span>
                    <span aria-hidden="true">·</span>
                    <span>Origen: { job.options.triggeredBy }</span>
                    <span aria-hidden="true">·</span>
                    <span>
                      Tamaño: {formatBytes(job.inputSizeBytes)} →{' '}
                      {formatBytes(job.outputSizeBytes)}
                    </span>
                    {job.probeSummary && (
                      <>
                        <span aria-hidden="true">·</span>
                        <span className="text-slate-300">{job.probeSummary}</span>
                      </>
                    )}
                  </div>

                  {job.originalIntactVerified && (
                    <div className="flex items-center gap-1.5 text-xs text-emerald-400 font-mono tabular-nums">
                      <ShieldCheck className="w-3.5 h-3.5 shrink-0" />
                      <span>
                        {t.originalIntact} ({job.originalSha256?.slice(0, 12)}…)
                      </span>
                    </div>
                  )}

                  {job.errorMessage && (
                    <div className="text-xs text-rose-400 font-medium">
                      Error verificado: {job.errorMessage}
                    </div>
                  )}

                  {(job.status === 'Running' ||
                    job.status === 'Preparing' ||
                    job.status === 'Validating') && (
                    <div className="w-full max-w-md bg-slate-800 h-1.5 rounded-full overflow-hidden mt-2">
                      <div
                        className="bg-teal-400 h-full transition-all duration-200"
                        style={{ width: `${job.progressPercent}%` }}
                      />
                    </div>
                  )}
                </div>

                <div className="flex items-center gap-2 shrink-0">
                  {job.status === 'Completed' && job.outputPath && (
                    <a
                      href={`/api/files/download?path=${encodeURIComponent(job.outputPath)}`}
                      className="flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold bg-teal-500 hover:bg-teal-400 text-slate-950 rounded-lg transition-colors whitespace-nowrap shrink-0"
                    >
                      <Download className="w-3.5 h-3.5" />
                      <span>{t.downloadResult}</span>
                    </a>
                  )}

                  {(job.status === 'Failed' || job.status === 'Cancelled') && (
                    <button
                      type="button"
                      onClick={() => onRetryJob(job.id)}
                      className="flex items-center gap-1.5 px-3 py-2 text-xs font-medium bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg transition-colors whitespace-nowrap shrink-0"
                    >
                      <RefreshCw className="w-3.5 h-3.5" />
                      <span>{t.retryJob}</span>
                    </button>
                  )}

                  {(job.status === 'Queued' ||
                    job.status === 'Preparing' ||
                    job.status === 'Running') && (
                    <button
                      type="button"
                      onClick={() => onCancelJob(job.id)}
                      className="px-3 py-2 text-xs font-medium bg-rose-950/60 hover:bg-rose-900/60 text-rose-200 rounded-lg transition-colors whitespace-nowrap shrink-0"
                    >
                      {t.cancelJob}
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
};
