import React, { useState } from 'react';
import { FolderSync, Play, Plus, Trash2 } from 'lucide-react';
import type { AutomationRule, ConflictPolicy, UserSettings } from '../core/types';
import type { TranslationDict } from '../i18n';

interface AutomationsViewProps {
  t: TranslationDict;
  settings: UserSettings;
  onToggleRule: (ruleId: string, enabled: boolean) => Promise<void>;
  onToggleCopyConverted: (ruleId: string, copyConvertedFile: boolean) => Promise<void>;
  onTriggerTestDrop: (ruleId: string) => Promise<void>;
  onCreateRule: (payload: {
    name: string;
    sourceDirectory: string;
    allowedInputFormats: string[];
    targetFormat: string;
    conflictPolicy: ConflictPolicy;
  }) => Promise<void>;
  onDeleteRule: (ruleId: string) => Promise<void>;
}

export const AutomationsView: React.FC<AutomationsViewProps> = ({
  t,
  settings,
  onToggleRule,
  onToggleCopyConverted,
  onTriggerTestDrop,
  onCreateRule,
  onDeleteRule,
}) => {
  const [name, setName] = useState('');
  const [sourceDir, setSourceDir] = useState('~/Downloads');
  const [allowedInput, setAllowedInput] = useState('png,jpeg');
  const [targetFormat, setTargetFormat] = useState('webp');
  const [conflictPolicy, setConflictPolicy] = useState<ConflictPolicy>('AutoRename');
  const [formError, setFormError] = useState<string | null>(null);
  const [testingRuleId, setTestingRuleId] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setFormError('Ingresá un nombre descriptivo para la regla de automatización.');
      return;
    }
    setFormError(null);
    await onCreateRule({
      name: name.trim(),
      sourceDirectory: sourceDir.trim(),
      allowedInputFormats: allowedInput
        .split(',')
        .map((s) => s.trim().toLowerCase())
        .filter(Boolean),
      targetFormat,
      conflictPolicy,
    });
    setName('');
  };

  return (
    <div className="space-y-8">
      <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6">
        <div className="pb-5 border-b border-[#1E2532]">
          <h2 className="text-lg font-semibold text-slate-100">{t.automationsTitle}</h2>
          <p className="text-xs text-slate-400 mt-1">{t.automationsSub}</p>
        </div>

        <div className="divide-y divide-[#1E2532] mt-2">
          {settings.automations.map((rule: AutomationRule) => (
            <div
              key={rule.id}
              className="py-4 flex flex-col lg:flex-row lg:items-center justify-between gap-4"
            >
              <div className="space-y-1.5">
                <div className="flex items-center gap-3">
                  <span className="text-base font-semibold text-slate-100">
                    {rule.name}
                  </span>
                  <span className="text-xs font-mono text-teal-300">
                    [{rule.allowedInputFormats.join(', ').toUpperCase()} →{' '}
                    {rule.targetFormat.toUpperCase()}]
                  </span>
                </div>

                <div className="text-xs font-mono text-slate-400 tabular-nums">
                  {rule.sourceDirectory} · Colisión: {rule.conflictPolicy} · Convertidos:{' '}
                  {rule.convertedCount}
                </div>

                <div className="flex items-center gap-4 pt-1">
                  <label className="inline-flex items-center gap-2 text-xs text-slate-300 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={rule.copyConvertedFile}
                      onChange={(e) =>
                        onToggleCopyConverted(rule.id, e.target.checked)
                      }
                      className="w-4 h-4 accent-teal-500 rounded"
                    />
                    <span>Copiar archivo convertido al portapapeles</span>
                  </label>
                  {rule.lastResult && (
                    <span className="text-xs text-slate-400">
                      Estado: {rule.lastResult}
                    </span>
                  )}
                </div>
              </div>

              <div className="flex items-center gap-3 shrink-0">
                <button
                  type="button"
                  disabled={!rule.enabled || testingRuleId === rule.id}
                  onClick={async () => {
                    setTestingRuleId(rule.id);
                    try {
                      await onTriggerTestDrop(rule.id);
                    } finally {
                      setTestingRuleId(null);
                    }
                  }}
                  className="flex items-center gap-1.5 px-3 py-2 text-xs font-medium bg-[#18202C] hover:bg-slate-800 text-teal-300 border border-[#263042] rounded-lg transition-colors disabled:opacity-40 whitespace-nowrap shrink-0"
                >
                  <Play className="w-3.5 h-3.5" />
                  <span>
                    {testingRuleId === rule.id
                      ? 'Procesando…'
                      : t.simulateFileDrop}
                  </span>
                </button>

                <button
                  type="button"
                  role="switch"
                  aria-checked={rule.enabled}
                  aria-label={`Activar regla ${rule.name}`}
                  onClick={() => onToggleRule(rule.id, !rule.enabled)}
                  className={`w-11 h-6 rounded-full transition-colors p-0.5 flex items-center ${
                    rule.enabled ? 'bg-teal-500 justify-end' : 'bg-slate-700 justify-start'
                  }`}
                >
                  <span className="w-5 h-5 rounded-full bg-white shadow" />
                </button>

                <button
                  type="button"
                  aria-label={`Eliminar regla ${rule.name}`}
                  onClick={() => onDeleteRule(rule.id)}
                  className="p-2 text-slate-400 hover:text-rose-400 rounded-lg transition-colors"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          ))}
        </div>

        <p className="text-xs text-slate-400 mt-4 pt-4 border-t border-[#1E2532]">
          Cada regla observa su carpeta sin recursión involuntaria, espera la estabilidad de escritura del archivo antes de invocar el motor y registra los hashes de salida para impedir bucles de conversión.
        </p>
      </section>

      {/* Create New Automation Rule */}
      <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6">
        <div className="flex items-center gap-2.5 pb-4 border-b border-[#1E2532]">
          <FolderSync className="w-4 h-4 text-teal-400" />
          <h3 className="text-base font-semibold text-slate-100">{t.newRuleTitle}</h3>
        </div>

        <form onSubmit={handleSubmit} className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-5">
          <div>
            <label htmlFor="rule-name" className="block text-xs text-slate-300 mb-1.5">
              Nombre de la regla
            </label>
            <input
              id="rule-name"
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Ej. HEIC de Descargas → JPEG"
              className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-sm text-slate-100 focus:outline-none focus:border-teal-500"
            />
          </div>

          <div>
            <label htmlFor="rule-dir" className="block text-xs text-slate-300 mb-1.5">
              Carpeta vigilada
            </label>
            <input
              id="rule-dir"
              type="text"
              value={sourceDir}
              onChange={(e) => setSourceDir(e.target.value)}
              className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-sm text-slate-100 font-mono focus:outline-none focus:border-teal-500"
            />
          </div>

          <div>
            <label htmlFor="rule-inputs" className="block text-xs text-slate-300 mb-1.5">
              Formatos de entrada permitidos (separados por coma)
            </label>
            <input
              id="rule-inputs"
              type="text"
              value={allowedInput}
              onChange={(e) => setAllowedInput(e.target.value)}
              placeholder="png, jpeg, webp"
              className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3.5 py-2 text-sm text-slate-100 font-mono focus:outline-none focus:border-teal-500"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label htmlFor="rule-target" className="block text-xs text-slate-300 mb-1.5">
                Formato destino
              </label>
              <select
                id="rule-target"
                value={targetFormat}
                onChange={(e) => setTargetFormat(e.target.value)}
                className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3 py-2 text-sm text-slate-100 focus:outline-none focus:border-teal-500"
              >
                <option value="webp">WebP</option>
                <option value="jpeg">JPEG</option>
                <option value="png">PNG</option>
                <option value="mp4">MP4</option>
                <option value="mp3">MP3</option>
                <option value="pdf">PDF</option>
              </select>
            </div>

            <div>
              <label htmlFor="rule-conflict" className="block text-xs text-slate-300 mb-1.5">
                Política de colisión
              </label>
              <select
                id="rule-conflict"
                value={conflictPolicy}
                onChange={(e) => setConflictPolicy(e.target.value as ConflictPolicy)}
                className="w-full bg-[#0B0D11] border border-[#263042] rounded-lg px-3 py-2 text-sm text-slate-100 focus:outline-none focus:border-teal-500"
              >
                <option value="AutoRename">AutoRename (1)</option>
                <option value="Skip">Omitir</option>
                <option value="Overwrite">Sobrescribir</option>
              </select>
            </div>
          </div>

          {formError && (
            <div className="md:col-span-2 text-xs text-rose-400">{formError}</div>
          )}

          <div className="md:col-span-2 flex justify-end pt-2">
            <button
              type="submit"
              className="flex items-center gap-2 px-4 py-2 text-xs font-semibold bg-teal-500 hover:bg-teal-400 text-slate-950 rounded-lg transition-colors"
            >
              <Plus className="w-4 h-4" />
              <span>Crear regla de automatización</span>
            </button>
          </div>
        </form>
      </section>
    </div>
  );
};
