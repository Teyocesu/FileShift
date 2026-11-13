#!/usr/bin/env node
import path from 'node:path';
import {
  ConversionQueueEngine,
  runSystemDoctor,
} from '../src/core/engineRunner';
import {
  FORMAT_DESCRIPTORS,
  createDefaultUserSettings,
  getSupportedTargetsForFormat,
} from '../src/core/formatMatrix';
import type { ConflictPolicy } from '../src/core/types';

async function main(): Promise<number> {
  const args = process.argv.slice(2);
  if (args.length === 0 || args[0] === '--help' || args[0] === '-h' || args[0] === 'help') {
    printHelp();
    return 0;
  }

  const subcommand = args[0].toLowerCase();

  if (subcommand === 'formats') {
    console.log('FileShift CLI — Matriz de Formatos y Conversiones Soportadas');
    console.log('─'.repeat(78));
    for (const desc of Object.values(FORMAT_DESCRIPTORS)) {
      const targets = getSupportedTargetsForFormat(desc.id, {
        libreOfficeAvailable: true,
        avifAvailable: true,
      });
      console.log(
        `  ${desc.displayName.padEnd(10)} (.${desc.extension.padEnd(4)}) [${desc.category.padEnd(8)}] -> ${targets.join(', ')}`
      );
    }
    return 0;
  }

  if (subcommand === 'doctor') {
    console.log('FileShift Doctor — Diagnóstico Real de Motores y Capacidades');
    console.log('─'.repeat(78));
    const diagnostics = await runSystemDoctor();
    for (const d of diagnostics) {
      const badge = d.available ? '[DISPONIBLE]' : '[OPCIONAL/NO DETECTADO]';
      console.log(`${badge.padEnd(24)} ${d.name.padEnd(32)} | ${d.version}`);
      console.log(`  Binario: ${d.binaryPath} | Licencia: ${d.license}`);
    }
    console.log('─'.repeat(78));
    const coreOk = diagnostics
      .filter((d) => d.id === 'ffmpeg' || d.id === 'imagemagick')
      .every((d) => d.available);
    console.log(
      coreOk
        ? 'Resultado: MOTORES PRINCIPALES OPERATIVOS (100% Local-First).'
        : 'Resultado: Faltan motores principales en PATH.'
    );
    return coreOk ? 0 : 2;
  }

  if (subcommand === 'convert') {
    const rest = args.slice(1);
    const inputFiles: string[] = [];
    let targetFormat: string | null = null;
    let presetName: string | null = null;
    let quality = 85;
    let longestEdgePx: number | null = null;
    let customOutputDir: string | null = null;
    let conflictPolicy: ConflictPolicy = 'AutoRename';

    for (let i = 0; i < rest.length; i++) {
      const token = rest[i];
      if (token === '--to' && i + 1 < rest.length) {
        targetFormat = rest[++i];
      } else if (token === '--preset' && i + 1 < rest.length) {
        presetName = rest[++i];
      } else if (token === '--quality' && i + 1 < rest.length) {
        quality = Math.max(1, Math.min(100, Number(rest[++i]) || 85));
      } else if (token === '--resize' && i + 1 < rest.length) {
        longestEdgePx = Number(rest[++i]) || null;
      } else if (token === '--out-dir' && i + 1 < rest.length) {
        customOutputDir = rest[++i];
      } else if (token === '--overwrite') {
        conflictPolicy = 'Overwrite';
      } else if (token === '--skip') {
        conflictPolicy = 'Skip';
      } else if (token === '--') {
        inputFiles.push(...rest.slice(i + 1));
        break;
      } else if (!token.startsWith('-')) {
        inputFiles.push(token);
      }
    }

    if (presetName) {
      const defaults = createDefaultUserSettings(process.cwd());
      const preset = defaults.presets.find(
        (p) =>
          p.id.toLowerCase() === presetName!.toLowerCase() ||
          p.name.toLowerCase().includes(presetName!.toLowerCase()) ||
          (presetName!.toLowerCase() === 'web' && p.id === 'preset-web-webp')
      );
      if (preset) {
        targetFormat = targetFormat ?? preset.targetFormat;
        quality = preset.quality;
        longestEdgePx = longestEdgePx ?? preset.longestEdgePx;
      }
    }

    if (inputFiles.length === 0 || !targetFormat) {
      console.error(
        'Error: Debés indicar al menos un archivo y un formato destino (--to <formato> o --preset <nombre>).'
      );
      return 1;
    }

    const queue = new ConversionQueueEngine(4);
    const jobs = inputFiles.map((file) =>
      queue.enqueue(path.resolve(file), targetFormat!, {
        quality,
        longestEdgePx,
        customOutputDir,
        conflictPolicy,
        presetId: presetName,
        triggeredBy: 'CLI',
      })
    );

    let failedCount = 0;
    for (const job of jobs) {
      const finished = await queue.waitForJob(job.id);
      if (finished.status === 'Completed') {
        console.log(
          `[OK] ${finished.inputPath} -> ${finished.outputPath} (${finished.outputSizeBytes} bytes | Original SHA-256 intacto: ${finished.originalIntactVerified})`
        );
      } else {
        console.error(`[ERROR] ${finished.inputPath}: ${finished.errorMessage}`);
        failedCount++;
      }
    }

    return failedCount === 0 ? 0 : 1;
  }

  console.error(`Comando no reconocido: ${subcommand}`);
  printHelp();
  return 1;
}

function printHelp(): void {
  console.log(`FileShift CLI — Convertidor Universal de Archivos
Uso:
  fileshift convert <archivo...> --to <formato> [--quality 1-100] [--resize px] [--out-dir carpeta]
  fileshift convert <archivo...> --preset <web|mp4|mp3...>
  fileshift formats
  fileshift doctor`);
}

main()
  .then((code) => process.exit(code))
  .catch((err) => {
    console.error(err);
    process.exit(1);
  });
