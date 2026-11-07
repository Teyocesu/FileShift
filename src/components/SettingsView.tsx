import React, { useState } from 'react';
import {
  CheckCircle2,
  Cpu,
  ListOrdered,
  Plus,
  RotateCcw,
  Settings,
  Sliders,
  Terminal,
  Trash2,
} from 'lucide-react';
import type {
  ConflictPolicy,
  ConversionPreset,
  EngineDiagnostic,
  MediaCategory,
  UserSettings,
} from '../core/types';
import type { TranslationDict } from '../i18n';

interface SettingsViewProps {
  t: TranslationDict;
  settings: UserSettings;
  diagnostics: EngineDiagnostic[];
  initialSubTab?: 'general' | 'menu' | 'presets' | 'doctor';
  onUpdateSettings: (patch: Partial<UserSettings>) => Promise<void>;
  onResetMenuCategory: (category?: MediaCategory) => Promise<void>;
  onCreatePreset: (payload: {
    name: string;
    targetFormat: string;
    quality: number;
    longestEdgePx: number | null;
    audioBitrateKbps: number | null;
    stripMetadata: boolean;
  }) => Promise<void>;
  onDeletePreset: (presetId: string) => Promise<void>;
  onExecuteCli: (args: string[]) => Promise<{
    command: string;
    exitCode: number;
    stdout: string;
    stderr: string;
  }>;
}

const MENU_CATEGORY_CONFIG: Array<{
  category: MediaCategory;
  titleEs: string;
  titleEn: string;
  subtitleEs: string;
  subtitleEn: string;
  groups: Array<{ label: string; formats: string[] }>;
}> = [
  {
    category: 'image',
    titleEs: 'Imágenes',
    titleEn: 'Images',
    subtitleEs: 'Ofrece JPEG, PNG y WebP por defecto en el menú principal.',
    subtitleEn: 'Offers JPEG, PNG and WebP by default in the primary menu.',
    groups: [
      {
        label: 'Images',
        formats: ['avif', 'bmp', 'gif', 'ico', 'jpeg', 'png', 'tiff', 'webp', 'pdf'],
      },
    ],
  },
  {
    category: 'svg',
    titleEs: 'SVG Vectorial',
    titleEn: 'SVG',
    subtitleEs: 'Ofrece PNG, JPEG y WebP por defecto.',
    subtitleEn: 'Offers PNG, JPEG and WebP by default.',
    groups: [
      {
        label: 'Raster & PDF',
        formats: ['png', 'jpeg', 'webp', 'bmp', 'tiff', 'ico', 'pdf'],
      },
    ],
  },
  {
    category: 'video',
    titleEs: 'Video',
    titleEn: 'Video',
    subtitleEs: 'Ofrece MP4, MOV, GIF y extracción directa de MP3.',
    subtitleEn: 'Offers MP4, MOV, GIF and direct MP3 extraction.',
    groups: [
      { label: 'Video & Animación', formats: ['mp4', 'mov', 'webm', 'mkv', 'avi', 'gif'] },
      { label: 'Solo Audio (Extraer)', formats: ['mp3', 'wav', 'flac', 'aac', 'm4a', 'ogg', 'opus'] },
    ],
  },
  {
    category: 'audio',
    titleEs: 'Audio',
    titleEn: 'Audio',
    subtitleEs: 'Ofrece MP3, M4A y WAV por defecto.',
    subtitleEn: 'Offers MP3, M4A and WAV by default.',
    groups: [
      { label: 'Audio', formats: ['aac', 'flac', 'm4a', 'mp3', 'ogg', 'opus', 'wav'] },
    ],
  },
  {
    category: 'pdf',
    titleEs: 'Documentos PDF',
    titleEn: 'PDFs',
    subtitleEs: 'Ofrece exportación a PNG y JPEG.',
    subtitleEn: 'Offers export to PNG and JPEG.',
    groups: [{ label: 'Imágenes', formats: ['png', 'jpeg'] }],
  },
];

const PRESET_FORMAT_GROUPS = [
  { label: 'Images', formats: ['jpeg', 'png', 'webp', 'avif', 'gif', 'tiff', 'bmp', 'ico'] },
  { label: 'Video', formats: ['mp4', 'mov', 'webm', 'mkv', 'avi'] },
  { label: 'Audio', formats: ['mp3', 'wav', 'flac', 'aac', 'm4a', 'ogg', 'opus'] },
];

export const SettingsView: React.FC<SettingsViewProps> = ({
  t,
  settings,
  diagnostics,
  initialSubTab = 'general',
  onUpdateSettings,
  onResetMenuCategory,
  onCreatePreset,
  onDeletePreset,
  onExecuteCli,
}) => {
  const [subTab, setSubTab] = useState<'general' | 'menu' | 'presets' | 'doctor'>(
    initialSubTab
  );

  // New Preset Form State
  const [presetName, setPresetName] = useState('');
  const [presetTarget, setPresetTarget] = useState('webp');
  const [presetQuality, setPresetQuality] = useState('85');
  const [presetLongestEdge, setPresetLongestEdge] = useState('');
  const [presetAudioBitrate, setPresetAudioBitrate] = useState('192');
  const [presetError, setPresetError] = useState<string | null>(null);

  // CLI Runner State
  const [cliOutput, setCliOutput] = useState<{
    command: string;
    exitCode: number;
    stdout: string;
    stderr: string;
  } | null>(null);
  const [runningCli, setRunningCli] = useState(false);

  const toggleMenuFavorite = async (category: MediaCategory, formatId: string) => {
    const current = settings.menuFavorites[category] || [];
    const exists = current.includes(formatId);
    const nextList = exists
      ? current.filter((item) => item !== formatId)
      : [...current, formatId];

    await onUpdateSettings({
      menuFavorites: {
        ...settings.menuFavorites,
        [category]: nextList,
      },
    });
  };

  const handleSavePreset = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!presetName.trim()) {
      setPresetError(
        settings.language === 'es'
          ? 'Ingresá un nombre para el preset (ej. web, redes, archivo).'
          : 'Please enter a preset name (e.g., web).'
      );
      return;
    }
    setPresetError(null);
    const q = Math.max(1, Math.min(100, Number(presetQuality) || 85));
    const edge = presetLongestEdge.trim() ? Number(presetLongestEdge) : null;
    const br = presetAudioBitrate.trim() ? Number(presetAudioBitrate) : null;

    await onCreatePreset({
      name: presetName.trim(),
      targetFormat: presetTarget,
      quality: q,
      longestEdgePx: edge,
      audioBitrateKbps: br,
      stripMetadata: false,
    });

    setPresetName('');
    setPresetLongestEdge('');
  };

  const runCliQuickCommand = async (args: string[]) => {
    setRunningCli(true);
    try {
      const res = await onExecuteCli(args);
      setCliOutput(res);
    } finally {
      setRunningCli(false);
    }
  };

  const isAudioOrVideoPreset = [
    'mp4',
    'mov',
    'webm',
    'mkv',
    'avi',
    'mp3',
    'wav',
    'flac',
    'aac',
    'm4a',
    'ogg',
    'opus',
  ].includes(presetTarget);

  return (
    <div className="space-y-6">
      {/* Sub-navigation bar (General | Menú Contextual | Presets | Doctor & CLI — NO License tab!) */}
      <div className="flex justify-center">
        <div
          role="tablist"
          aria-label="Secciones de configuración"
          className="inline-flex items-center gap-1 p-1 bg-[#12161E] border border-[#1E2532] rounded-xl"
        >
          <button
            type="button"
            role="tab"
            aria-selected={subTab === 'general'}
            onClick={() => setSubTab('general')}
            className={`flex items-center gap-2 px-4 py-2 text-xs font-medium rounded-lg transition-colors whitespace-nowrap shrink-0 ${
              subTab === 'general'
                ? 'bg-[#1E293B] text-white shadow-sm'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            <Settings className="w-3.5 h-3.5" />
            <span>{t.settingsGeneral}</span>
          </button>

          <button
            type="button"
            role="tab"
            aria-selected={subTab === 'menu'}
            onClick={() => setSubTab('menu')}
            className={`flex items-center gap-2 px-4 py-2 text-xs font-medium rounded-lg transition-colors whitespace-nowrap shrink-0 ${
              subTab === 'menu'
                ? 'bg-[#1E293B] text-white shadow-sm'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            <ListOrdered className="w-3.5 h-3.5" />
            <span>{t.settingsMenu}</span>
          </button>

          <button
            type="button"
            role="tab"
            aria-selected={subTab === 'presets'}
            onClick={() => setSubTab('presets')}
            className={`flex items-center gap-2 px-4 py-2 text-xs font-medium rounded-lg transition-colors whitespace-nowrap shrink-0 ${
              subTab === 'presets'
                ? 'bg-[#1E293B] text-white shadow-sm'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            <Sliders className="w-3.5 h-3.5" />
            <span>{t.settingsPresets}</span>
          </button>

          <button
            type="button"
            role="tab"
            aria-selected={subTab === 'doctor'}
            onClick={() => setSubTab('doctor')}
            className={`flex items-center gap-2 px-4 py-2 text-xs font-medium rounded-lg transition-colors whitespace-nowrap shrink-0 ${
              subTab === 'doctor'
                ? 'bg-[#1E293B] text-white shadow-sm'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            <Terminal className="w-3.5 h-3.5" />
            <span>{t.settingsDoctor}</span>
          </button>
        </div>
      </div>

      {/* TAB 1: GENERAL SETTINGS */}
      {subTab === 'general' && (
        <div className="max-w-3xl mx-auto space-y-6">
          <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6 space-y-5">
            <h3 className="text-sm font-semibold text-slate-300">Conversión y archivos</h3>

            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-4 border-b border-[#1E2532]">
              <div>
                <div className="text-sm font-medium text-slate-100">
                  {t.saveLocationLabel}
                </div>
                <div className="text-xs text-slate-400">
                  Por defecto guarda el resultado en la misma carpeta sin tocar el original
                </div>
              </div>
              <select
                aria-label={t.saveLocationLabel}
                value={settings.saveNextToOriginal ? 'original' : 'custom'}
                onChange={(e) =>
                  onUpdateSettings({
                    saveNextToOriginal: e.target.value === 'original',
                  })
                }
                className="bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-xs text-slate-100"
              >
                <option value="original">{t.saveNextToOriginal}</option>
                <option value="custom">{t.saveCustomFolder}</option>
              </select>
            </div>

            {!settings.saveNextToOriginal && (
              <div className="pb-4 border-b border-[#1E2532]">
                <label
                  htmlFor="custom-out-dir"
                  className="block text-xs text-slate-300 mb-1.5"
                >
                  Ruta de carpeta de salida personalizada
                </label>
                <input
                  id="custom-out-dir"
                  type="text"
                  value={settings.customOutputDirectory}
                  onChange={(e) =>
                    onUpdateSettings({ customOutputDirectory: e.target.value })
                  }
                  placeholder="C:\Users\Usuario\Converted"
                  className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-xs font-mono text-slate-100"
                />
              </div>
            )}

            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-4 border-b border-[#1E2532]">
              <div>
                <div className="text-sm font-medium text-slate-100">
                  {t.concurrencyLabel}
                </div>
                <div className="text-xs text-slate-400">
                  Límite de procesos simultáneos de FFmpeg / ImageMagick
                </div>
              </div>
              <select
                aria-label={t.concurrencyLabel}
                value={String(settings.maxConcurrency)}
                onChange={(e) =>
                  onUpdateSettings({ maxConcurrency: Number(e.target.value) })
                }
                className="bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-xs text-slate-100 tabular-nums"
              >
                <option value="1">1 trabajo</option>
                <option value="2">2 trabajos</option>
                <option value="4">Auto (4)</option>
                <option value="8">8 trabajos</option>
                <option value="16">16 trabajos</option>
              </select>
            </div>

            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
              <div>
                <div className="text-sm font-medium text-slate-100">
                  {t.conflictPolicyLabel}
                </div>
                <div className="text-xs text-slate-400">
                  Comportamiento seguro cuando ya existe un archivo con el mismo nombre
                </div>
              </div>
              <select
                aria-label={t.conflictPolicyLabel}
                value={settings.defaultConflictPolicy}
                onChange={(e) =>
                  onUpdateSettings({
                    defaultConflictPolicy: e.target.value as ConflictPolicy,
                  })
                }
                className="bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-xs text-slate-100"
              >
                <option value="AutoRename">{t.conflictAutoRename}</option>
                <option value="Skip">{t.conflictSkip}</option>
                <option value="Overwrite">{t.conflictOverwrite}</option>
              </select>
            </div>
          </section>

          <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6 space-y-4">
            <h3 className="text-sm font-semibold text-slate-300">{t.whenDoneTitle}</h3>

            <div className="flex items-center justify-between pb-3 border-b border-[#1E2532]">
              <span className="text-sm text-slate-100">{t.showNotification}</span>
              <button
                type="button"
                role="switch"
                aria-checked={settings.showNotifications}
                aria-label={t.showNotification}
                onClick={() =>
                  onUpdateSettings({ showNotifications: !settings.showNotifications })
                }
                className={`w-11 h-6 rounded-full transition-colors p-0.5 flex items-center ${
                  settings.showNotifications
                    ? 'bg-teal-500 justify-end'
                    : 'bg-slate-700 justify-start'
                }`}
              >
                <span className="w-5 h-5 rounded-full bg-white shadow" />
              </button>
            </div>

            <div className="flex items-center justify-between pb-3 border-b border-[#1E2532]">
              <span className="text-sm text-slate-100">{t.revealInExplorer}</span>
              <button
                type="button"
                role="switch"
                aria-checked={settings.revealInExplorerWhenDone}
                aria-label={t.revealInExplorer}
                onClick={() =>
                  onUpdateSettings({
                    revealInExplorerWhenDone: !settings.revealInExplorerWhenDone,
                  })
                }
                className={`w-11 h-6 rounded-full transition-colors p-0.5 flex items-center ${
                  settings.revealInExplorerWhenDone
                    ? 'bg-teal-500 justify-end'
                    : 'bg-slate-700 justify-start'
                }`}
              >
                <span className="w-5 h-5 rounded-full bg-white shadow" />
              </button>
            </div>

            <div className="flex items-center justify-between pb-3 border-b border-[#1E2532]">
              <div>
                <div className="text-sm text-slate-100">{t.keepInBackground}</div>
                <div className="text-xs text-slate-400">
                  Mantiene activas las automatizaciones de carpetas al cerrar la ventana
                </div>
              </div>
              <button
                type="button"
                role="switch"
                aria-checked={settings.keepRunningInBackground}
                aria-label={t.keepInBackground}
                onClick={() =>
                  onUpdateSettings({
                    keepRunningInBackground: !settings.keepRunningInBackground,
                  })
                }
                className={`w-11 h-6 rounded-full transition-colors p-0.5 flex items-center ${
                  settings.keepRunningInBackground
                    ? 'bg-teal-500 justify-end'
                    : 'bg-slate-700 justify-start'
                }`}
              >
                <span className="w-5 h-5 rounded-full bg-white shadow" />
              </button>
            </div>

            <div className="flex items-center justify-between">
              <span className="text-sm text-slate-100">{t.languageLabel}</span>
              <select
                aria-label={t.languageLabel}
                value={settings.language}
                onChange={(e) =>
                  onUpdateSettings({ language: e.target.value as 'es' | 'en' })
                }
                className="bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-xs text-slate-100"
              >
                <option value="es">Español</option>
                <option value="en">English</option>
              </select>
            </div>
          </section>
        </div>
      )}

      {/* TAB 2: CONTEXT MENU FAVORITES ORDERING */}
      {subTab === 'menu' && (
        <div className="space-y-5">
          <div className="bg-[#12161E] border border-[#1E2532] rounded-xl p-5 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <h3 className="text-sm font-semibold text-slate-100">
                Integración con el Menú Contextual Moderno de Windows 11 (IExplorerCommand)
              </h3>
              <p className="text-xs text-slate-400 mt-1">{t.menuInstructions}</p>
            </div>
            <div className="flex items-center gap-3 shrink-0">
              <span className="text-xs text-slate-300">Menú activo</span>
              <button
                type="button"
                role="switch"
                aria-checked={settings.contextMenuEnabled}
                aria-label="Activar integración con el menú contextual"
                onClick={() =>
                  onUpdateSettings({
                    contextMenuEnabled: !settings.contextMenuEnabled,
                  })
                }
                className={`w-11 h-6 rounded-full transition-colors p-0.5 flex items-center ${
                  settings.contextMenuEnabled
                    ? 'bg-teal-500 justify-end'
                    : 'bg-slate-700 justify-start'
                }`}
              >
                <span className="w-5 h-5 rounded-full bg-white shadow" />
              </button>
            </div>
          </div>

          {MENU_CATEGORY_CONFIG.map((sec) => {
            const favs = settings.menuFavorites[sec.category] || [];
            return (
              <section
                key={sec.category}
                className="bg-[#12161E] border border-[#1E2532] rounded-xl p-5 space-y-4"
              >
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <h4 className="text-sm font-semibold text-slate-100">
                      {settings.language === 'es' ? sec.titleEs : sec.titleEn}
                    </h4>
                    <p className="text-xs text-slate-400 mt-0.5">
                      {settings.language === 'es' ? sec.subtitleEs : sec.subtitleEn}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => onResetMenuCategory(sec.category)}
                    className="flex items-center gap-1 text-xs text-slate-400 hover:text-slate-200 transition-colors whitespace-nowrap shrink-0"
                  >
                    <RotateCcw className="w-3 h-3" />
                    <span>{t.restoreDefaults}</span>
                  </button>
                </div>

                <div className="space-y-3">
                  {sec.groups.map((grp) => (
                    <div
                      key={grp.label}
                      className="flex flex-col sm:flex-row sm:items-center gap-3"
                    >
                      <span className="text-xs text-slate-400 w-36 shrink-0">
                        {grp.label}
                      </span>
                      <div className="flex flex-wrap items-center gap-2">
                        {grp.formats.map((fmt) => {
                          const orderIndex = favs.indexOf(fmt);
                          const isFav = orderIndex !== -1;
                          return (
                            <button
                              key={fmt}
                              type="button"
                              onClick={() => toggleMenuFavorite(sec.category, fmt)}
                              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors whitespace-nowrap shrink-0 ${
                                isFav
                                  ? 'bg-teal-500/20 border border-teal-400 text-teal-200'
                                  : 'bg-[#0B0D11] border border-[#263042] text-slate-300 hover:border-slate-600'
                              }`}
                            >
                              {isFav && (
                                <span className="w-4 h-4 rounded-full bg-teal-400 text-slate-950 font-mono text-[10px] font-bold flex items-center justify-center">
                                  {orderIndex + 1}
                                </span>
                              )}
                              <span>{fmt.toUpperCase()}</span>
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  ))}
                </div>
              </section>
            );
          })}
        </div>
      )}

      {/* TAB 3: PRESETS */}
      {subTab === 'presets' && (
        <div className="space-y-6">
          <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6">
            <h3 className="text-base font-semibold text-slate-100 pb-4 border-b border-[#1E2532]">
              Presets guardados ({settings.presets.length})
            </h3>

            {settings.presets.length === 0 ? (
              <div className="py-8 text-center text-xs text-slate-400">
                {t.presetsEmpty}
              </div>
            ) : (
              <div className="divide-y divide-[#1E2532] mt-2">
                {settings.presets.map((preset: ConversionPreset) => (
                  <div
                    key={preset.id}
                    className="py-3.5 flex items-center justify-between gap-4"
                  >
                    <div>
                      <div className="text-sm font-semibold text-slate-100">
                        {preset.name}
                      </div>
                      <div className="text-xs font-mono text-slate-400 tabular-nums mt-0.5">
                        Destino: {preset.targetFormat.toUpperCase()} · Calidad:{' '}
                        {preset.quality}%
                        {preset.longestEdgePx
                          ? ` · Lado máx: ${preset.longestEdgePx}px`
                          : ''}
                        {preset.audioBitrateKbps
                          ? ` · Bitrate: ${preset.audioBitrateKbps} kbps`
                          : ''}
                      </div>
                    </div>
                    <button
                      type="button"
                      aria-label={`Eliminar preset ${preset.name}`}
                      onClick={() => onDeletePreset(preset.id)}
                      className="p-2 text-slate-400 hover:text-rose-400 rounded-lg transition-colors"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </section>

          <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6">
            <h3 className="text-base font-semibold text-slate-100 pb-4 border-b border-[#1E2532]">
              {t.newPresetTitle}
            </h3>

            <form onSubmit={handleSavePreset} className="space-y-5 pt-4">
              <div>
                <label
                  htmlFor="preset-name-input"
                  className="block text-xs text-slate-300 mb-1.5"
                >
                  Nombre
                </label>
                <input
                  id="preset-name-input"
                  type="text"
                  value={presetName}
                  onChange={(e) => setPresetName(e.target.value)}
                  placeholder="Nombre, ej. Web Optimizado 1280px"
                  className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-sm text-slate-100 focus:outline-none focus:border-teal-500"
                />
              </div>

              <div className="space-y-3">
                <div className="text-xs text-slate-300">Formato destino</div>
                {PRESET_FORMAT_GROUPS.map((grp) => (
                  <div
                    key={grp.label}
                    className="flex flex-col sm:flex-row sm:items-center gap-3"
                  >
                    <span className="text-xs text-slate-400 w-20 shrink-0">
                      {grp.label}
                    </span>
                    <div className="flex flex-wrap gap-2">
                      {grp.formats.map((fmt) => (
                        <button
                          key={fmt}
                          type="button"
                          onClick={() => setPresetTarget(fmt)}
                          className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors whitespace-nowrap shrink-0 ${
                            presetTarget === fmt
                              ? 'bg-teal-500 text-slate-950 font-semibold'
                              : 'bg-[#0B0D11] border border-[#263042] text-slate-300 hover:border-slate-600'
                          }`}
                        >
                          {fmt.toUpperCase()}
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <div>
                  <label
                    htmlFor="preset-quality-input"
                    className="block text-xs text-slate-300 mb-1.5"
                  >
                    Calidad (1–100)
                  </label>
                  <input
                    id="preset-quality-input"
                    type="number"
                    min={1}
                    max={100}
                    value={presetQuality}
                    onChange={(e) => setPresetQuality(e.target.value)}
                    className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-sm font-mono text-slate-100 tabular-nums"
                  />
                </div>

                <div>
                  <label
                    htmlFor="preset-edge-input"
                    className="block text-xs text-slate-300 mb-1.5"
                  >
                    Lado más largo (px, opcional)
                  </label>
                  <input
                    id="preset-edge-input"
                    type="number"
                    min={16}
                    max={8192}
                    value={presetLongestEdge}
                    onChange={(e) => setPresetLongestEdge(e.target.value)}
                    placeholder="Ej. 1920"
                    className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-sm font-mono text-slate-100 tabular-nums"
                  />
                </div>

                {isAudioOrVideoPreset && (
                  <div>
                    <label
                      htmlFor="preset-bitrate-input"
                      className="block text-xs text-slate-300 mb-1.5"
                    >
                      Bitrate de audio (kbps)
                    </label>
                    <input
                      id="preset-bitrate-input"
                      type="number"
                      min={64}
                      max={512}
                      value={presetAudioBitrate}
                      onChange={(e) => setPresetAudioBitrate(e.target.value)}
                      className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-sm font-mono text-slate-100 tabular-nums"
                    />
                  </div>
                )}
              </div>

              {presetError && (
                <div className="text-xs text-rose-400">{presetError}</div>
              )}

              <div className="flex justify-end">
                <button
                  type="submit"
                  className="flex items-center gap-2 px-5 py-2 text-xs font-semibold bg-teal-500 hover:bg-teal-400 text-slate-950 rounded-lg transition-colors"
                >
                  <Plus className="w-4 h-4" />
                  <span>{t.savePreset}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      )}

      {/* TAB 4: DOCTOR & LIVE CLI */}
      {subTab === 'doctor' && (
        <div className="space-y-6">
          <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6 space-y-4">
            <div className="flex items-center justify-between border-b border-[#1E2532] pb-4">
              <div className="flex items-center gap-2.5">
                <Cpu className="w-4 h-4 text-teal-400" />
                <h3 className="text-base font-semibold text-slate-100">
                  Estado Real de Motores de Conversión (`fileshift doctor`)
                </h3>
              </div>
              <span className="text-xs font-mono text-emerald-400">
                100% Local · Sin APIs Cloud
              </span>
            </div>

            <div className="divide-y divide-[#1E2532]">
              {diagnostics.map((diag) => (
                <div
                  key={diag.id}
                  className="py-3.5 flex flex-col md:flex-row md:items-center justify-between gap-3"
                >
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <CheckCircle2
                        className={`w-4 h-4 ${
                          diag.available ? 'text-emerald-400' : 'text-amber-400'
                        }`}
                      />
                      <span className="text-sm font-semibold text-slate-100">
                        {diag.name}
                      </span>
                      <span className="text-xs font-mono text-slate-400">
                        ({diag.binaryPath})
                      </span>
                    </div>
                    <div className="text-xs font-mono text-slate-400">
                      {diag.version} · Licencia: {diag.license}
                    </div>
                    <div className="text-xs text-slate-400">
                      Capacidades: {diag.capabilities.join(', ')}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6 space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-[#1E2532] pb-4">
              <div>
                <h3 className="text-base font-semibold text-slate-100">
                  Consola CLI Interactiva (`fileshift`)
                </h3>
                <p className="text-xs text-slate-400 mt-0.5">
                  Ejecutá comandos reales contra el binario CLI sin abrir ventanas adicionales
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  disabled={runningCli}
                  onClick={() => runCliQuickCommand(['doctor'])}
                  className="px-3 py-1.5 text-xs font-mono bg-[#0B0D11] hover:bg-slate-800 border border-[#263042] text-teal-300 rounded-lg transition-colors"
                >
                  fileshift doctor
                </button>
                <button
                  type="button"
                  disabled={runningCli}
                  onClick={() => runCliQuickCommand(['formats'])}
                  className="px-3 py-1.5 text-xs font-mono bg-[#0B0D11] hover:bg-slate-800 border border-[#263042] text-teal-300 rounded-lg transition-colors"
                >
                  fileshift formats
                </button>
                <button
                  type="button"
                  disabled={runningCli}
                  onClick={() =>
                    runCliQuickCommand([
                      'convert',
                      'arquitectura-diagrama.png',
                      '--to',
                      'webp',
                      '--quality',
                      '85',
                    ])
                  }
                  className="px-3 py-1.5 text-xs font-mono bg-teal-500 hover:bg-teal-400 text-slate-950 font-semibold rounded-lg transition-colors"
                >
                  fileshift convert arquitectura-diagrama.png --to webp
                </button>
              </div>
            </div>

            {cliOutput && (
              <div className="bg-[#0B0D11] border border-[#263042] rounded-lg p-4 font-mono text-xs space-y-2 overflow-x-auto">
                <div className="flex items-center justify-between text-slate-400 border-b border-[#1E2532] pb-2">
                  <span>$ {cliOutput.command}</span>
                  <span>Exit Code: {cliOutput.exitCode}</span>
                </div>
                {cliOutput.stdout && (
                  <pre className="text-slate-200 whitespace-pre-wrap">
                    {cliOutput.stdout}
                  </pre>
                )}
                {cliOutput.stderr && (
                  <pre className="text-rose-400 whitespace-pre-wrap">
                    {cliOutput.stderr}
                  </pre>
                )}
              </div>
            )}
          </section>
        </div>
      )}
    </div>
  );
};
