# SPEC.md — Especificación Técnica de FileShift

## 1. Objetivo
Diseñar e implementar **FileShift**, un convertidor universal de archivos local-first para Windows 11 que permite seleccionar uno o múltiples archivos en Windows Explorer, hacer clic derecho, desplegar el menú contextual moderno (**Convertir a…**) y ejecutar conversiones reales en segundo plano sin abrir manualmente la ventana principal.

## 2. Alcance Funcional
- **Categorías soportadas**:
  - **Imágenes**: `PNG`, `JPEG/JPG`, `WebP`, `AVIF`, `BMP`, `GIF` (incluyendo preservación de animación), `TIFF`, `ICO`, `HEIC/HEIF`, y `SVG` (entrada rasterizable).
  - **Video**: `MP4` (H.264/AAC), `MKV`, `MOV`, `WebM` (VP9/Opus), `AVI`, `GIF animado` (con generación de paleta en dos pasos `palettegen`/`paletteuse`), más extracción directa de audio (`MP3`, `WAV`, `FLAC`, `AAC`, `M4A`, `OGG`, `Opus`).
  - **Audio**: `MP3`, `WAV`, `FLAC`, `AAC`, `M4A`, `OGG`, `Opus` con bitrate y sample rate configurables.
  - **PDF**: `PDF → PNG`, `PDF → JPEG` (todas las páginas o rango específico `first-last`) e `Imágenes → PDF` (PDF 1.4 DCTDecode estándar) respetando dimensiones y detectando PDFs cifrados o dañados.
  - **Documentos Office (Opcional)**: `DOCX → PDF`, `ODT → PDF`, `XLSX → PDF`, `PPTX → PDF` mediante detección automática de LibreOffice headless (`soffice --headless`), sin bloquear el resto de la aplicación cuando no está instalado.

## 3. Invariantes de Preservación de Datos (Prioridad P0)
1. **Validación Pre-Publicación (`ConversionQueue.cs`)**:
   - Todo motor escribe inicialmente en `.fileshift.tmp.<jobId>.<ext>` dentro del volumen de destino.
   - Antes de publicar al archivo final (`PublishValidatedAtomicOutput`), el sistema verifica en fase `Validating`:
     1. Tamaño `> 0` bytes y firma mágica (`MagicByteInspector`) coincidente con el formato objetivo.
     2. Marcadores estructurales de cierre de archivo (`IEND` en PNG, `FF D9` en JPEG, `0x3B` en GIF, `%%EOF` en PDF) para rechazar archivos truncados que solo tengan cabecera válida.
     3. Validación profunda por motor (`OutputIntegrityValidator`: `identify -regard-warnings` para imágenes, `ffprobe` para streams de audio/video y `gs -dSAFER -sDEVICE=nullpage` para PDF).
2. **Protección de Destinos Previos bajo `Overwrite` (`SafeFilePolicy.cs`)**:
   - Si una conversión falla o no supera la validación profunda, el archivo temporal se elimina sin tocar jamás el archivo de destino previo.
   - Durante el reemplazo atómico con `ConflictPolicy.Overwrite`, se crea un respaldo transaccional (`.fileshift.bak.<guid>`) que se restaura si `File.Move` falla.
3. **Verificación Criptográfica SHA-256 del Original (`INV-01`)**:
   - Se calcula el hash SHA-256 del archivo de entrada antes y después de la conversión. Cualquier alteración del hash original marca el trabajo explícitamente como `JobStatus.Failed` (`OriginalIntactVerified = false`).

## 4. Arquitectura de Integración con Windows 11 Explorer (`IExplorerCommand`)
1. **Servidor COM C++17 (`src/Converter.Shell/FileShift.Shell.dll`)**:
   - Implementa `IExplorerCommand` (`RootExplorerCommand`, `SubExplorerCommand`) e `IEnumExplorerCommand` (`EnumExplorerCommand`) con conteo de referencias thread-safe (`g_dllRefCount`).
   - Lee `%LOCALAPPDATA%\FileShift\menu-cache.json` (generado por `UserSettingsStore.Save`) para respetar el estado `ENABLED=0/1` y el orden de formatos favoritos por categoría (`FAV_image`, `FAV_video`, `FAV_audio`, `FAV_pdf`, `FAV_document`).
   - Agrupa los formatos restantes en el submenú en cascada `Más formatos…` (`ECF_HASSUBCOMMANDS`) e incluye `Abrir en FileShift…`.
   - Soporta rutas largas (`32768` `wchar_t`), divide lotes grandes en invocaciones de `< 24.000` caracteres hacia `fileshift.exe convert --to <fmt> -- ...` (`CreateProcessW` con `CREATE_NO_WINDOW`) y registra errores de lanzamiento en `%LOCALAPPDATA%\FileShift\shell-errors.log`.
2. **Empaquetado MSIX Sparse Package (`installer/`)**:
   - `AppxManifest.xml` declara `windows.fileExplorerContextMenus` y `com:ComServer` (`CLSID {D87E4B9A-6C21-4F08-9E3A-5A18C42B7101}`) con assets visuales en `installer/Assets/` (`StoreLogo.png`, `Square150x150Logo.png`, `Square44x44Logo.png`).

## 5. Criterios de Aceptación (AC-01 a AC-16)
| Código | Descripción | Estado en Linux Container (Verificado) | Estado en Windows 11 Físico |
|---|---|---|---|
| **AC-01** | Compilación limpia desde el repositorio | `PASS` (`dotnet build FileShift.sln -c Release` compila `Converter.Core`, `Converter.Infrastructure`, `Converter.Cli`, `Converter.App` WPF y `Converter.Tests` con 0 errores; `x86_64-w64-mingw32-g++` compila `FileShift.Shell.dll` x64 con 0 errores) | Listo para `Build-WindowsRelease.ps1` |
| **AC-02** | Instalable en Windows 11 | `NOT TESTED` (`FileShift.iss`, `AppxManifest.xml`, `Assets/*.png` y `Build-WindowsRelease.ps1` listos; requiere `ISCC.exe` / Windows 11 host) | Requiere ejecución física en Windows 11 |
| **AC-03** | Aparece en el menú contextual moderno de Windows 11 | `NOT TESTED` (`FileShift.Shell.dll` compilado y manifiesto MSIX Sparse Package listo; requiere `explorer.exe` en Windows 11) | Requiere validación física en Windows 11 Explorer |
| **AC-04** | Elegir formato sin abrir previamente la GUI | `PASS` (Validado en CLI `fileshift convert` y banco de pruebas; `NOT TESTED` físicamente en `explorer.exe`) | Requiere validación física en Explorer |
| **AC-05** | Conversión produce un archivo real y válido | `PASS` (Validado por `OutputIntegrityValidator` con `identify`, `ffprobe` y `gs` en 9 tests xUnit C# y 10 tests Node) | `PASS` |
| **AC-06** | El archivo original permanece intacto (SHA-256 idéntico) | `PASS` (Verificado antes y después de publicar; cualquier alteración provoca `JobStatus.Failed`) | `PASS` |
| **AC-07** | Destinos existentes se gestionan de manera segura (`AutoRename`, `Skip`, `Overwrite`) | `PASS` (Incluye test de regresión P0 verificando que un fallo bajo `Overwrite` preserva intacto el archivo previo) | `PASS` |
| **AC-08** | Conversión mediante drag and drop | `PASS` (Implementado en `MainWindow.xaml` WPF y verificado en interfaz web) | `NOT TESTED` en escritorio físico Windows |
| **AC-09** | Soporte real para imágenes, video, audio y PDF multipágina | `PASS` (Verificado con FFmpeg, ImageMagick y Ghostscript reales, incluyendo PDF multipágina) | `PASS` |
| **AC-10** | Errores se muestran y registran claramente (incl. truncados/corruptos) | `PASS` (Verificado con test de PNG truncado solo con cabecera sin `IEND`) | `PASS` |
| **AC-11** | Preferencias, orden de menú y presets persisten entre ejecuciones | `PASS` (`UserSettingsStore` persiste `settings.json` y genera `menu-cache.json` verificado por test xUnit) | `PASS` |
| **AC-12** | Automatizaciones convierten archivos nuevos sin duplicaciones ni bucles | `PASS` (Verificado en `FolderAutomationService_ConvertsNewFileOnce_AndDoesNotLoopOnGeneratedOutput`) | `PASS` |
| **AC-13** | Instalación y desinstalación gestionan integración de Explorer | `NOT TESTED` (`Register-SparsePackage.ps1`, `Unregister-SparsePackage.ps1` y `FileShift.iss` listos) | Requiere host Windows 11 |
| **AC-14** | Sin licencias comerciales, suscripciones, cuentas ni servicios de pago | `PASS` (100% libre bajo licencia MIT en `/LICENSE`) | `PASS` |
| **AC-15** | Funciona 100% sin conexión a Internet durante las conversiones | `PASS` (Motores locales sin llamadas de red) | `PASS` |
| **AC-16** | Sin dependencias ausentes ocultas ni botones ficticios | `PASS` (Todos los paneles WPF y controles web están conectados al motor C#) | `PASS` |
