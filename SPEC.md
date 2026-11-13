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
   - Todo motor escribe en una carpeta exclusiva `.fileshift.tmp.<id>` dentro del directorio de destino. La cola valida todas las salidas antes de publicar.
   - Antes de publicar al archivo final (`PublishOutputs`), el sistema verifica en fase `Validating`:
     1. Tamaño `> 0` bytes y firma mágica (`MagicByteInspector`) coincidente con el formato objetivo.
     2. Marcadores estructurales de cierre de archivo (`IEND` en PNG, `FF D9` en JPEG, `0x3B` en GIF, `%%EOF` en PDF) para rechazar archivos truncados que solo tengan cabecera válida.
     3. Validación profunda por motor (`OutputIntegrityValidator`: `identify -regard-warnings` para imágenes, `ffprobe` para streams de audio/video y `gs -dSAFER -sDEVICE=nullpage` para PDF).
2. **Protección de Destinos Previos bajo `Overwrite` (`SafeFilePolicy.cs`)**:
   - Si una conversión falla o no supera la validación profunda, el archivo temporal se elimina sin tocar jamás el archivo de destino previo.
   - Durante el reemplazo atómico con `ConflictPolicy.Overwrite`, se conserva un respaldo por archivo hasta el commit del lote. Un fallo o cancelación durante la publicación revierte las operaciones completadas. Si la restauración falla se conserva el respaldo y se informa su ruta. Skip omite el lote completo ante cualquier colisión.
3. **Verificación Criptográfica SHA-256 del Original (`INV-01`)**:
   - Se calcula el hash SHA-256 del archivo de entrada antes y después de la conversión. Cualquier alteración del hash original marca el trabajo explícitamente como `JobStatus.Failed` (`OriginalIntactVerified = false`).

## 4. Arquitectura de Integración con Windows 11 Explorer (`IExplorerCommand`)
1. **Servidor COM C++17 (`src/Converter.Shell/FileShift.Shell.dll`)**:
   - Implementa `IExplorerCommand` (`RootExplorerCommand`, `SubExplorerCommand`) e `IEnumExplorerCommand` (`EnumExplorerCommand`) con conteo de referencias thread-safe (`g_dllRefCount`).
   - Lee `%LOCALAPPDATA%\FileShift\menu-cache.json` (generado por `UserSettingsStore.Save`) y `menu-formats.txt` generado por Doctor para filtrar motores y formatos. Respeta el estado `ENABLED=0/1` y el orden de formatos favoritos por categoría (`FAV_image`, `FAV_video`, `FAV_audio`, `FAV_pdf`, `FAV_document`).
   - Muestra favoritos primero, luego los demás formatos compatibles y `Abrir en FileShift…` en un único nivel de subcomandos compatible con Explorer.
   - Soporta rutas largas (`32768` `wchar_t`), divide lotes grandes en invocaciones de `< 24.000` caracteres hacia `fileshift.exe convert --to <fmt> -- ...` (`CreateProcessW` con `CREATE_NO_WINDOW`) y registra errores de lanzamiento en `%LOCALAPPDATA%\FileShift\shell-errors.log`.
2. **Empaquetado MSIX Sparse Package (`installer/`)**:
   - `AppxManifest.xml` declara `windows.fileExplorerContextMenus` y `com:ComServer` (`CLSID {D87E4B9A-6C21-4F08-9E3A-5A18C42B7101}`) con assets visuales en `installer/Assets/` (`StoreLogo.png`, `Square150x150Logo.png`, `Square44x44Logo.png`).

## 5. Criterios de Aceptación (AC-01 a AC-16)
| Código | Descripción |
|---|---|
| **AC-01** | Compilación limpia desde el repositorio |
| **AC-02** | Instalable en Windows 11 |
| **AC-03** | Aparece en el menú contextual moderno de Windows 11 |
| **AC-04** | Elegir formato sin abrir previamente la GUI |
| **AC-05** | Conversión produce un archivo real y válido |
| **AC-06** | El archivo original permanece intacto (SHA-256 idéntico) |
| **AC-07** | Destinos existentes se gestionan de manera segura (`AutoRename`, `Skip`, `Overwrite`) |
| **AC-08** | Conversión mediante drag and drop |
| **AC-09** | Soporte real para imágenes, video, audio y PDF multipágina |
| **AC-10** | Errores se muestran y registran claramente (incl. truncados/corruptos) |
| **AC-11** | Preferencias, orden de menú y presets persisten entre ejecuciones |
| **AC-12** | Automatizaciones convierten archivos nuevos sin duplicaciones ni bucles |
| **AC-13** | Instalación y desinstalación gestionan integración de Explorer |
| **AC-14** | Sin licencias comerciales, suscripciones, cuentas ni servicios de pago |
| **AC-15** | Funciona 100% sin conexión a Internet durante las conversiones |
| **AC-16** | Sin dependencias ausentes ocultas ni botones ficticios |
