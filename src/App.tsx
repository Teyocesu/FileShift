import React, { useCallback, useEffect, useState } from 'react';
import {
  Activity,
  CheckCircle2,
  Code2,
  FolderSync,
  Settings,
  ShieldCheck,
  X,
} from 'lucide-react';
import { ActivityView } from './components/ActivityView';
import { AutomationsView } from './components/AutomationsView';
import { NativeRepoView } from './components/NativeRepoView';
import { SettingsView } from './components/SettingsView';
import type {
  ConflictPolicy,
  ConversionJob,
  EngineDiagnostic,
  MediaCategory,
  UserSettings,
  WorkspaceSampleFile,
} from './core/types';
import { TRANSLATIONS } from './i18n';

type ActiveTab = 'activity' | 'automations' | 'settings' | 'native';

export function App() {
  const [activeTab, setActiveTab] = useState<ActiveTab>('activity');
  const [settings, setSettings] = useState<UserSettings | null>(null);
  const [jobs, setJobs] = useState<ConversionJob[]>([]);
  const [sampleFiles, setSampleFiles] = useState<WorkspaceSampleFile[]>([]);
  const [diagnostics, setDiagnostics] = useState<EngineDiagnostic[]>([]);
  const [isConverting, setIsConverting] = useState(false);
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const showToast = useCallback((msg: string) => {
    setToastMessage(msg);
  }, []);

  useEffect(() => {
    if (!toastMessage) return;
    const timer = setTimeout(() => setToastMessage(null), 4500);
    return () => clearTimeout(timer);
  }, [toastMessage]);

  const fetchState = useCallback(async () => {
    const res = await fetch('/api/state');
    if (!res.ok) return;
    const data = await res.json();
    setSettings(data.settings);
    setJobs(data.jobs || []);
    setSampleFiles(data.sampleFiles || []);
    setDiagnostics(data.diagnostics || []);
  }, []);

  useEffect(() => {
    fetchState();
  }, [fetchState]);

  const handleConvertSampleFiles = async (
    fileNames: string[],
    targetFormat: string,
    presetId?: string | null,
    triggeredBy: 'ContextMenu' | 'DragDrop' = 'ContextMenu'
  ) => {
    setIsConverting(true);
    try {
      const res = await fetch('/api/convert/sample', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fileNames,
          targetFormat,
          presetId,
          triggeredBy,
        }),
      });
      const data = await res.json();
      if (data.jobs) setJobs(data.jobs);
      if (data.sampleFiles) setSampleFiles(data.sampleFiles);
      if (settings?.showNotifications) {
        showToast(
          `Conversión completada: ${fileNames.join(', ')} → ${targetFormat.toUpperCase()} (Original intacto)`
        );
      }
    } finally {
      setIsConverting(false);
    }
  };

  const handleUploadAndConvert = async (
    file: File,
    targetFormat: string,
    presetId?: string | null
  ) => {
    setIsConverting(true);
    try {
      const base64Data = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => {
          const result = String(reader.result || '');
          const commaIdx = result.indexOf(',');
          resolve(commaIdx >= 0 ? result.slice(commaIdx + 1) : result);
        };
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(file);
      });

      const res = await fetch('/api/convert/upload', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fileName: file.name,
          base64Data,
          targetFormat,
          presetId,
        }),
      });
      const data = await res.json();
      if (data.jobs) setJobs(data.jobs);
      if (data.sampleFiles) setSampleFiles(data.sampleFiles);
      if (settings?.showNotifications && data.job?.status === 'Completed') {
        showToast(
          `Archivo convertido: ${file.name} → ${data.job.outputFileName}`
        );
      }
    } finally {
      setIsConverting(false);
    }
  };

  const handleTestCorruptFile = async () => {
    setIsConverting(true);
    try {
      const res = await fetch('/api/convert/corrupt-test', { method: 'POST' });
      const data = await res.json();
      if (data.jobs) setJobs(data.jobs);
      showToast(
        'Prueba de seguridad ejecutada: archivo corrupto rechazado sin producir salidas de 0 bytes.'
      );
    } finally {
      setIsConverting(false);
    }
  };

  const handleCancelJob = async (jobId: string) => {
    const res = await fetch(`/api/jobs/${jobId}/cancel`, { method: 'POST' });
    const data = await res.json();
    if (data.jobs) setJobs(data.jobs);
  };

  const handleRetryJob = async (jobId: string) => {
    const res = await fetch(`/api/jobs/${jobId}/retry`, { method: 'POST' });
    const data = await res.json();
    if (data.jobs) setJobs(data.jobs);
  };

  const handleClearCompleted = async () => {
    const res = await fetch('/api/jobs/completed', { method: 'DELETE' });
    const data = await res.json();
    if (data.jobs) setJobs(data.jobs);
  };

  const handleUpdateSettings = async (patch: Partial<UserSettings>) => {
    const res = await fetch('/api/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(patch),
    });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
    if (data.sampleFiles) setSampleFiles(data.sampleFiles);
  };

  const handleResetMenuCategory = async (category?: MediaCategory) => {
    const res = await fetch('/api/settings/reset-menu', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ category }),
    });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
    if (data.sampleFiles) setSampleFiles(data.sampleFiles);
    showToast('Orden del menú contextual restaurado a valores predeterminados.');
  };

  const handleCreatePreset = async (payload: {
    name: string;
    targetFormat: string;
    quality: number;
    longestEdgePx: number | null;
    audioBitrateKbps: number | null;
    stripMetadata: boolean;
  }) => {
    const res = await fetch('/api/presets', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
    showToast(`Preset guardado: ${payload.name}`);
  };

  const handleDeletePreset = async (presetId: string) => {
    const res = await fetch(`/api/presets/${presetId}`, { method: 'DELETE' });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
  };

  const handleToggleRule = async (ruleId: string, enabled: boolean) => {
    const res = await fetch(`/api/automations/${ruleId}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ enabled }),
    });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
  };

  const handleToggleCopyConverted = async (
    ruleId: string,
    copyConvertedFile: boolean
  ) => {
    const res = await fetch(`/api/automations/${ruleId}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ copyConvertedFile }),
    });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
  };

  const handleTriggerTestDrop = async (ruleId: string) => {
    const res = await fetch(`/api/automations/${ruleId}/trigger-test`, {
      method: 'POST',
    });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
    if (data.jobs) setJobs(data.jobs);
    if (data.job?.status === 'Completed') {
      showToast(
        `Automatización ejecutada: ${data.job.fileName} → ${data.job.outputFileName} (sin bucle)`
      );
    }
  };

  const handleCreateRule = async (payload: {
    name: string;
    sourceDirectory: string;
    allowedInputFormats: string[];
    targetFormat: string;
    conflictPolicy: ConflictPolicy;
  }) => {
    const res = await fetch('/api/automations', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
    showToast(`Regla creada: ${payload.name}`);
  };

  const handleDeleteRule = async (ruleId: string) => {
    const res = await fetch(`/api/automations/${ruleId}`, { method: 'DELETE' });
    const data = await res.json();
    if (data.settings) setSettings(data.settings);
  };

  const handleExecuteCli = async (args: string[]) => {
    const res = await fetch('/api/cli/execute', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ args }),
    });
    const data = await res.json();
    if (data.jobs) setJobs(data.jobs);
    if (data.sampleFiles) setSampleFiles(data.sampleFiles);
    return {
      command: data.command,
      exitCode: data.exitCode,
      stdout: data.stdout,
      stderr: data.stderr,
    };
  };

  if (!settings) {
    return (
      <div className="min-h-screen bg-[#0B0D11] text-slate-200 flex items-center justify-center text-sm">
        Inicializando motores locales de FileShift…
      </div>
    );
  }

  const t = TRANSLATIONS[settings.language] || TRANSLATIONS.es;

  return (
    <div className="min-h-screen bg-[#0B0D11] text-[#F1F5F9] flex flex-col md:flex-row">
      {/* Left Workspace Sidebar */}
      <aside className="w-full md:w-64 bg-[#0E1218] border-b md:border-b-0 md:border-r border-[#1E2532] p-5 flex flex-col justify-between shrink-0">
        <div className="space-y-6">
          {/* Brand Wordmark */}
          <div className="flex items-center justify-between px-2">
            <span className="text-xl font-bold tracking-tight font-display text-white whitespace-nowrap shrink-0">
              FileShift
            </span>
            <button
              type="button"
              aria-label={t.navSettings}
              onClick={() => setActiveTab('settings')}
              className="p-2 text-slate-400 hover:text-white rounded-lg hover:bg-slate-800/70 transition-colors"
            >
              <Settings className="w-4 h-4" />
            </button>
          </div>

          {/* Primary Navigation */}
          <nav aria-label="Navegación principal" className="space-y-1.5">
            <button
              type="button"
              onClick={() => setActiveTab('activity')}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-xl text-sm font-medium transition-colors whitespace-nowrap shrink-0 ${
                activeTab === 'activity'
                  ? 'bg-[#1A2230] text-white'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-slate-900/50'
              }`}
            >
              <Activity className="w-4 h-4 text-teal-400 shrink-0" />
              <span>{t.navActivity}</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('automations')}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-xl text-sm font-medium transition-colors whitespace-nowrap shrink-0 ${
                activeTab === 'automations'
                  ? 'bg-[#1A2230] text-white'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-slate-900/50'
              }`}
            >
              <FolderSync className="w-4 h-4 text-teal-400 shrink-0" />
              <span>{t.navAutomations}</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('settings')}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-xl text-sm font-medium transition-colors whitespace-nowrap shrink-0 ${
                activeTab === 'settings'
                  ? 'bg-[#1A2230] text-white'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-slate-900/50'
              }`}
            >
              <Settings className="w-4 h-4 text-teal-400 shrink-0" />
              <span>{t.navSettings}</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('native')}
              className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-xl text-sm font-medium transition-colors whitespace-nowrap shrink-0 ${
                activeTab === 'native'
                  ? 'bg-[#1A2230] text-white'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-slate-900/50'
              }`}
            >
              <Code2 className="w-4 h-4 text-teal-400 shrink-0" />
              <span>{t.navNativeCode}</span>
            </button>
          </nav>
        </div>

        {/* Bottom Free & Open Source Assurance (Replaces paid trial card from reference) */}
        <div className="mt-6 pt-5 border-t border-[#1E2532] space-y-2 px-2">
          <div className="flex items-center gap-1.5 text-xs font-semibold text-teal-300">
            <ShieldCheck className="w-4 h-4 shrink-0" />
            <span>100% Gratuito y Local</span>
          </div>
          <p className="text-[11px] text-slate-400 leading-relaxed">
            {t.freeForeverDesc}
          </p>
        </div>
      </aside>

      {/* Main Content Area */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Clean 3-Zone Top Bar */}
        <header className="flex items-center justify-between gap-8 px-8 py-5 border-b border-[#1E2532] bg-[#0B0D11]">
          <div>
            <h1 className="text-xl font-bold text-white font-display">
              {activeTab === 'activity' && t.navActivity}
              {activeTab === 'automations' && t.navAutomations}
              {activeTab === 'settings' && t.navSettings}
              {activeTab === 'native' && t.navNativeCode}
            </h1>
          </div>

          <div className="hidden lg:flex items-center gap-2 text-xs text-slate-400 font-mono tabular-nums">
            <span> Trabajos totales: {jobs.length}</span>
            <span aria-hidden="true">·</span>
            <span>Concurrencia máx: {settings.maxConcurrency}</span>
            <span aria-hidden="true">·</span>
            <span>Política: {settings.defaultConflictPolicy}</span>
          </div>

          <div className="flex items-center gap-3 shrink-0">
            <a
              href="/api/repo/archive"
              className="px-4 py-2 text-xs font-semibold bg-teal-500 hover:bg-teal-400 text-slate-950 rounded-lg transition-colors whitespace-nowrap shrink-0"
            >
              Descargar Solución Win11 (.tar.gz)
            </a>
          </div>
        </header>

        {/* Notification Toast */}
        {toastMessage && (
          <div
            role="status"
            className="mx-8 mt-5 px-4 py-3 bg-[#112224] border border-teal-500/40 rounded-xl flex items-center justify-between gap-4 text-xs text-teal-100"
          >
            <div className="flex items-center gap-2.5">
              <CheckCircle2 className="w-4 h-4 text-teal-400 shrink-0" />
              <span>{toastMessage}</span>
            </div>
            <button
              type="button"
              aria-label="Cerrar notificación"
              onClick={() => setToastMessage(null)}
              className="text-slate-400 hover:text-white"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        )}

        {/* Active View */}
        <main className="p-8 max-w-6xl w-full mx-auto flex-1">
          {activeTab === 'activity' && (
            <ActivityView
              t={t}
              settings={settings}
              jobs={jobs}
              sampleFiles={sampleFiles}
              isConverting={isConverting}
              onConvertSampleFiles={handleConvertSampleFiles}
              onUploadAndConvert={handleUploadAndConvert}
              onTestCorruptFile={handleTestCorruptFile}
              onCancelJob={handleCancelJob}
              onRetryJob={handleRetryJob}
              onClearCompleted={handleClearCompleted}
            />
          )}

          {activeTab === 'automations' && (
            <AutomationsView
              t={t}
              settings={settings}
              onToggleRule={handleToggleRule}
              onToggleCopyConverted={handleToggleCopyConverted}
              onTriggerTestDrop={handleTriggerTestDrop}
              onCreateRule={handleCreateRule}
              onDeleteRule={handleDeleteRule}
            />
          )}

          {activeTab === 'settings' && (
            <SettingsView
              t={t}
              settings={settings}
              diagnostics={diagnostics}
              onUpdateSettings={handleUpdateSettings}
              onResetMenuCategory={handleResetMenuCategory}
              onCreatePreset={handleCreatePreset}
              onDeletePreset={handleDeletePreset}
              onExecuteCli={handleExecuteCli}
            />
          )}

          {activeTab === 'native' && <NativeRepoView />}
        </main>
      </div>
    </div>
  );
}

export default App;
