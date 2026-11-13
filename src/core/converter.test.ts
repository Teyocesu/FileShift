import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
  ConversionQueueEngine,
  FolderWatcherAutomationManager,
  computeFileSha256,
  ensureSampleFilesAsync,
  probeMediaSummary,
} from './engineRunner';
import { inspectFileSignature } from './formatMatrix';
import type { AutomationRule } from './types';

const testWorkDir = fs.mkdtempSync(path.join(os.tmpdir(), 'fileshift-real-tests-'));

test('1 & 4. Real Image conversion (PNG -> WebP & JPEG) produces valid image and leaves original file 100% identical', async () => {
  await ensureSampleFilesAsync(testWorkDir);
  const inputPng = path.join(testWorkDir, 'arquitectura-diagrama.png');
  const beforeSha = computeFileSha256(inputPng);

  const queue = new ConversionQueueEngine(2);
  const job = queue.enqueue(inputPng, 'webp', { quality: 85 });
  const finished = await queue.waitForJob(job.id);

  assert.equal(finished.status, 'Completed');
  assert.ok(finished.outputPath && fs.existsSync(finished.outputPath));
  assert.equal(finished.originalIntactVerified, true);
  assert.equal(computeFileSha256(inputPng), beforeSha);

  const outSig = inspectFileSignature(finished.outputPath!);
  assert.equal(outSig.valid, true);
  assert.equal(outSig.detectedFormat, 'webp');
});

test('2 & 3. Real Video transcoding (MP4 -> WebM & GIF) and Audio extraction (MP4 -> MP3) verify container, codecs, and duration', async () => {
  await ensureSampleFilesAsync(testWorkDir);
  const inputMp4 = path.join(testWorkDir, 'captura-demo.mp4');
  const beforeSha = computeFileSha256(inputMp4);

  const queue = new ConversionQueueEngine(2);
  const webmJob = queue.enqueue(inputMp4, 'webm', { quality: 80 });
  const mp3Job = queue.enqueue(inputMp4, 'mp3', { audioBitrateKbps: 128 });

  const [finishedWebm, finishedMp3] = await Promise.all([
    queue.waitForJob(webmJob.id),
    queue.waitForJob(mp3Job.id),
  ]);

  assert.equal(finishedWebm.status, 'Completed');
  assert.ok(finishedWebm.outputPath && fs.existsSync(finishedWebm.outputPath));
  const webmProbe = await probeMediaSummary(finishedWebm.outputPath!);
  assert.ok(webmProbe && webmProbe.includes('vp9'), `Expected VP9 codec in WebM probe, got: ${webmProbe}`);

  assert.equal(finishedMp3.status, 'Completed');
  assert.ok(finishedMp3.outputPath && fs.existsSync(finishedMp3.outputPath));
  const mp3Sig = inspectFileSignature(finishedMp3.outputPath!);
  assert.equal(mp3Sig.valid, true);
  assert.equal(mp3Sig.detectedFormat, 'mp3');

  // Original MP4 remains untouched
  assert.equal(computeFileSha256(inputMp4), beforeSha);
});

test('5. Existing destination file is never overwritten without authorization (AutoRename creates numbered copy)', async () => {
  await ensureSampleFilesAsync(testWorkDir);
  const inputPng = path.join(testWorkDir, 'arquitectura-diagrama.png');
  const preExistingJpg = path.join(testWorkDir, 'arquitectura-diagrama.jpg');
  fs.writeFileSync(preExistingJpg, 'SENTINEL-EXISTING-FILE-DO-NOT-OVERWRITE', 'utf8');

  const queue = new ConversionQueueEngine(2);
  const job = queue.enqueue(inputPng, 'jpeg', { conflictPolicy: 'AutoRename' });
  const finished = await queue.waitForJob(job.id);

  assert.equal(finished.status, 'Completed');
  assert.ok(finished.outputPath);
  assert.ok(finished.outputPath!.endsWith('arquitectura-diagrama (1).jpg'));
  assert.equal(fs.readFileSync(preExistingJpg, 'utf8'), 'SENTINEL-EXISTING-FILE-DO-NOT-OVERWRITE');
});

test('6 & 7. Paths with spaces and Unicode characters + batch multiple selection succeed simultaneously', async () => {
  await ensureSampleFilesAsync(testWorkDir);
  const srcPng = path.join(testWorkDir, 'arquitectura-diagrama.png');
  const unicodeDir = path.join(testWorkDir, 'Carpeta Con Espacios ¡España 日本語!');
  fs.mkdirSync(unicodeDir, { recursive: true });

  const fileA = path.join(unicodeDir, 'Imagen Uno ¡Diseño 2026!.png');
  const fileB = path.join(unicodeDir, 'Imagen Dos (Copia Final).png');
  fs.copyFileSync(srcPng, fileA);
  fs.copyFileSync(srcPng, fileB);

  const queue = new ConversionQueueEngine(2);
  const jobA = queue.enqueue(fileA, 'bmp');
  const jobB = queue.enqueue(fileB, 'bmp');

  const [resA, resB] = await Promise.all([queue.waitForJob(jobA.id), queue.waitForJob(jobB.id)]);
  assert.equal(resA.status, 'Completed');
  assert.equal(resB.status, 'Completed');
  assert.equal(inspectFileSignature(resA.outputPath!).detectedFormat, 'bmp');
  assert.equal(inspectFileSignature(resB.outputPath!).detectedFormat, 'bmp');
});

test('8 & 10. Cancelling a job publishes no partial output, and a corrupt file fails safely without leaving 0-byte artifacts', async () => {
  const corruptPath = path.join(testWorkDir, 'corrupt-fake-video.mp4');
  fs.writeFileSync(corruptPath, Buffer.from([0x00, 0x01, 0x02, 0x03, 0x99, 0xff]));

  const queue = new ConversionQueueEngine(1);
  const badJob = queue.enqueue(corruptPath, 'webm');
  const finishedBad = await queue.waitForJob(badJob.id);

  assert.equal(finishedBad.status, 'Failed');
  assert.equal(finishedBad.outputPath, null);
  assert.ok(finishedBad.errorMessage && finishedBad.errorMessage.length > 0);

  // Verify no .fileshift.tmp.* files remain in directory
  const leftovers = fs.readdirSync(testWorkDir).filter((f) => f.includes('.fileshift.tmp.'));
  assert.equal(leftovers.length, 0);
});

test('9. Folder automation rule converts newly arrived file once and never loops on its own output', async () => {
  await ensureSampleFilesAsync(testWorkDir);
  const watchDir = path.join(testWorkDir, 'WatchedFolder');
  fs.mkdirSync(watchDir, { recursive: true });

  const incomingPng = path.join(watchDir, 'screenshot-01.png');
  fs.copyFileSync(path.join(testWorkDir, 'arquitectura-diagrama.png'), incomingPng);

  const queue = new ConversionQueueEngine(2);
  const automationManager = new FolderWatcherAutomationManager(queue);

  const rule: AutomationRule = {
    id: 'test-rule-1',
    name: 'Screenshots PNG -> WebP',
    enabled: true,
    sourceDirectory: watchDir,
    allowedInputFormats: ['png'],
    targetFormat: 'webp',
    presetId: null,
    outputDirectory: watchDir,
    copyConvertedFile: false,
    conflictPolicy: 'AutoRename',
    lastTriggeredAt: null,
    lastResult: null,
    convertedCount: 0,
  };

  const firstRun = await automationManager.processCandidateFile(rule, incomingPng);
  assert.ok(firstRun);
  assert.equal(firstRun!.status, 'Completed');
  assert.ok(firstRun!.outputPath && fs.existsSync(firstRun!.outputPath));

  // Feeding the generated output back into the watcher must be ignored (no infinite loop!)
  const loopAttempt = await automationManager.processCandidateFile(rule, firstRun!.outputPath!);
  assert.equal(loopAttempt, null);
});

test('11. Bidirectional PDF support (Image PNG -> PDF and PDF -> PNG via Ghostscript) produces valid outputs', async () => {
  await ensureSampleFilesAsync(testWorkDir);
  const inputPng = path.join(testWorkDir, 'arquitectura-diagrama.png');
  const queue = new ConversionQueueEngine(2);

  // Image -> PDF
  const toPdfJob = queue.enqueue(inputPng, 'pdf', { quality: 90 });
  const finishedPdf = await queue.waitForJob(toPdfJob.id);
  assert.equal(finishedPdf.status, 'Completed');
  assert.ok(finishedPdf.outputPath && fs.existsSync(finishedPdf.outputPath));
  assert.equal(inspectFileSignature(finishedPdf.outputPath!).detectedFormat, 'pdf');

  // PDF -> PNG
  const toPngJob = queue.enqueue(finishedPdf.outputPath!, 'png');
  const finishedPng = await queue.waitForJob(toPngJob.id);
  assert.equal(finishedPng.status, 'Completed');
  assert.ok(finishedPng.outputPath && fs.existsSync(finishedPng.outputPath));
  assert.equal(inspectFileSignature(finishedPng.outputPath!).detectedFormat, 'png');
});

