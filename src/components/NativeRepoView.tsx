import React, { useEffect, useState } from 'react';
import { Code2, Download, FileCode, FolderGit2, ShieldCheck } from 'lucide-react';

interface RepoFileEntry {
  path: string;
  category: string;
  description: string;
}

export const NativeRepoView: React.FC = () => {
  const [files, setFiles] = useState<RepoFileEntry[]>([]);
  const [selectedPath, setSelectedPath] = useState<string>(
    'src/Converter.Shell/ExplorerCommand.cpp'
  );
  const [selectedContent, setSelectedContent] = useState<string>('');
  const [selectedDescription, setSelectedDescription] = useState<string>('');
  const [loading, setLoading] = useState<boolean>(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    fetch(`/api/repo/files?path=${encodeURIComponent(selectedPath)}`)
      .then((r) => r.json())
      .then((data) => {
        if (!active) return;
        setFiles(data.files || []);
        setSelectedContent(data.selectedFile?.content || '');
        setSelectedDescription(data.selectedFile?.description || '');
        setLoading(false);
      })
      .catch(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [selectedPath]);

  return (
    <div className="space-y-6">
      {/* Top Banner with Git & Windows 11 Build Instructions + Full Tarball Download */}
      <section className="bg-[#12161E] border border-[#1E2532] rounded-xl p-6">
        <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-6">
          <div className="space-y-2">
            <div className="flex items-center gap-2.5">
              <FolderGit2 className="w-5 h-5 text-teal-400" />
              <h2 className="text-lg font-semibold text-slate-100">
                Repositorio Oficial FileShift — Solución Nativa Windows 11 (.NET 10 + C++ Win32 COM)
              </h2>
            </div>
            <p className="text-xs text-slate-400 max-w-3xl">
              Incluye la extensión nativa C++17 <code className="text-teal-300">IExplorerCommand</code> (<code className="text-slate-200">src/Converter.Shell/ExplorerCommand.cpp</code>), el manifiesto MSIX Sparse Package (<code className="text-slate-200">installer/AppxManifest.xml</code>), el instalador Inno Setup (<code className="text-slate-200">installer/FileShift.iss</code>), los proyectos C# (<code className="text-slate-200">Converter.Core</code>, <code className="text-slate-200">Converter.Infrastructure</code>, <code className="text-slate-200">Converter.Cli</code>, <code className="text-slate-200">Converter.App</code>) y la rama <code className="text-teal-300">feat/initial-implementation</code> lista para enviar a <code className="text-slate-200">https://github.com/Teyocesu/FileShift</code>.
            </p>
          </div>

          <div className="flex items-center gap-3 shrink-0">
            <a
              href="/api/repo/archive"
              className="flex items-center gap-2 px-4 py-2.5 text-xs font-semibold bg-teal-500 hover:bg-teal-400 text-slate-950 rounded-lg transition-colors whitespace-nowrap shrink-0"
            >
              <Download className="w-4 h-4" />
              <span>Descargar Repositorio Completo (.tar.gz)</span>
            </a>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mt-5 pt-5 border-t border-[#1E2532] text-xs">
          <div className="bg-[#0B0D11] border border-[#1E2532] rounded-lg p-3.5 space-y-1">
            <div className="font-semibold text-slate-200 flex items-center gap-1.5">
              <ShieldCheck className="w-3.5 h-3.5 text-teal-400" />
              <span>1. Menú Contextual Moderno Win 11</span>
            </div>
            <p className="text-slate-400">
              Implementa <code className="text-slate-300">IExplorerCommand</code> y <code className="text-slate-300">IEnumExplorerCommand</code> en C++ nativo sin cargar FFmpeg ni bloquear <code className="text-slate-300">explorer.exe</code>.
            </p>
          </div>

          <div className="bg-[#0B0D11] border border-[#1E2532] rounded-lg p-3.5 space-y-1">
            <div className="font-semibold text-slate-200 flex items-center gap-1.5">
              <Code2 className="w-3.5 h-3.5 text-teal-400" />
              <span>2. Compilación e Instalador Reproducible</span>
            </div>
            <p className="text-slate-400">
              Ejecutá <code className="text-slate-300">.\installer\Build-WindowsRelease.ps1</code> y <code className="text-slate-300">.\installer\Register-SparsePackage.ps1</code> en Windows 11.
            </p>
          </div>

          <div className="bg-[#0B0D11] border border-[#1E2532] rounded-lg p-3.5 space-y-1">
            <div className="font-semibold text-slate-200 flex items-center gap-1.5">
              <FolderGit2 className="w-3.5 h-3.5 text-teal-400" />
              <span>3. Flujo Git Configurado</span>
            </div>
            <p className="text-slate-400">
              Rama base <code className="text-slate-300">main</code> y rama activa <code className="text-slate-300">feat/initial-implementation</code> con commits atómicos y sin coautores artificiales.
            </p>
          </div>
        </div>
      </section>

      {/* Code Browser Split View */}
      <section className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        <div className="lg:col-span-4 bg-[#12161E] border border-[#1E2532] rounded-xl p-4 space-y-1 max-h-[640px] overflow-y-auto">
          <div className="text-xs font-semibold text-slate-400 px-2 pb-2 border-b border-[#1E2532]">
            Archivos del Repositorio ({files.length})
          </div>
          {files.map((file) => {
            const active = file.path === selectedPath;
            return (
              <button
                key={file.path}
                type="button"
                onClick={() => setSelectedPath(file.path)}
                className={`w-full flex flex-col items-start px-3 py-2 rounded-lg text-left transition-colors ${
                  active
                    ? 'bg-teal-500/15 border border-teal-500/40 text-teal-200'
                    : 'hover:bg-slate-800/60 text-slate-300'
                }`}
              >
                <div className="flex items-center gap-2 w-full">
                  <FileCode className="w-3.5 h-3.5 text-teal-400 shrink-0" />
                  <span className="text-xs font-mono font-medium truncate">
                    {file.path}
                  </span>
                </div>
                <span className="text-[11px] text-slate-400 pl-5 truncate w-full">
                  {file.category}
                </span>
              </button>
            );
          })}
        </div>

        <div className="lg:col-span-8 bg-[#12161E] border border-[#1E2532] rounded-xl overflow-hidden">
          <div className="px-5 py-3.5 border-b border-[#1E2532] flex flex-col sm:flex-row sm:items-center justify-between gap-2 bg-[#0E1219]">
            <div>
              <div className="text-xs font-mono font-semibold text-teal-300">
                {selectedPath}
              </div>
              <div className="text-xs text-slate-400 mt-0.5">
                {selectedDescription}
              </div>
            </div>
            <span className="text-xs font-mono text-slate-400 tabular-nums">
              {selectedContent.split('\n').length} líneas
            </span>
          </div>

          <div className="p-5 bg-[#0B0D11] max-h-[560px] overflow-auto">
            {loading ? (
              <div className="text-xs text-slate-400 py-8 text-center">
                Cargando archivo fuente…
              </div>
            ) : (
              <pre className="text-xs font-mono text-slate-200 leading-relaxed whitespace-pre">
                {selectedContent}
              </pre>
            )}
          </div>
        </div>
      </section>
    </div>
  );
};
