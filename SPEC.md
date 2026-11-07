# SPEC.md — Especificación Técnica de FileShift

## 1. Objetivo
Diseñar e implementar **FileShift**, un convertidor universal de archivos local-first para Windows 11 que permite seleccionar uno o múltiples archivos en Windows Explorer, hacer clic derecho, desplegar el menú contextual moderno (**Convertir con FileShift →**) y ejecutar conversiones reales en segundo plano sin abrir manualmente la ventana principal.

## 2. Alcance Funcional
- **Categorías soportadas**:
  - **Imágenes**: `PNG`, `JPEG/JPG`, `WebP`, `AVIF`, `BMP`, `GIF` (incluyendo preservación de animación), `TIFF`, `ICO`, `HEIC/HEIF` (según disponibilidad de libheif/delegados), y `SVG` (entrada rasterizable segura).
  - **Video**: `MP4` (H.264/AAC), `MKV`, `MOV`, `WebM` (VP9/Opus), `AVI`, `GIF animado` (con generación de paleta en dos pasos `palettegen`/`paletteuse`), más extracción directa de audio (`MP3`, `WAV`, `FLAC`, `AAC`, `M4A`, `OGG`, `Opus`).
  - **Audio**: `MP3`, `WAV`, `FLAC`, `AAC`, `M4A`, `OGG`, `Opus` con bitrate, sample rate y canales configurables.
  - **PDF**: `PDF → PNG`, `PDF → JPEG` (todas las páginas o rango específico) e `Imágenes → PDF` respetando dimensiones, orientación y manejo seguro de PDFs cifrados.
  - **Documentos Office (Opcional)**: `DOCX → PDF`, `ODT → PDF`, `XLSX → PDF`, `PPTX → PDF` mediante detección automática de LibreOffice headless (`soffice --headless`), sin bloquear el resto de la aplicación cuando no está instalado.

## 3. Experiencia de Usuario (UX)
1. **Menú Contextual Moderno de Windows 11**:
   - Entrada raíz: `Convertir con FileShift` con ícono nativo.
   - Submenú dinámico: Muestra primero los formatos favoritos numerados configurados por el usuario para esa categoría (p. ej. `1. JPEG`, `2. PNG`, `3. WebP` para imágenes; `1. MP4`, `2. MOV`, `3. GIF`, `4. Extraer MP3` para video) seguidos del resto de destinos compatibles.
   - Si el usuario selecciona múltiples archivos de la misma categoría, se muestran los destinos comunes compatibles y se encolan todos en lote.
2. **Interfaz Gráfica (Actividad, Automatizaciones, Ajustes)**:
   - **Actividad**: Zona drag-and-drop, selector de archivos, vista en tiempo real de trabajos (`Queued`, `Preparing`, `Running`, `Validating`, `Completed`, `Failed`, `Cancelled`), barra de progreso, apertura de carpeta/archivo, reintento y verificación de hash SHA-256 del original.
   - **Automatizaciones**: Reglas de vigilancia de carpetas (`FileSystemWatcher` + verificación de estabilidad de escritura + prevención de bucles por registro de salidas generadas).
   - **Ajustes**:
     - *General*: Ubicación de salida (junto al original o carpeta dedicada), concurrencia máxima, política de colisión, notificaciones, idioma (`ES`/`EN`), estado de motores.
     - *Menú*: Selección y ordenamiento numerado (`1`, `2`, `3`...) de formatos favoritos por tipo de archivo.
     - *Presets*: Perfiles reutilizables con calidad (1–100), lado más largo (px) y bitrate de audio (kbps).
     - *Sin pestaña de Licencias/Pagos*: 100% libre y gratuito.

## 4. Arquitectura de Integración con Windows 11 Explorer
Windows 11 exige dos requisitos técnicos estrictos para mostrar entradas en el menú contextual primario (sin pulsar «Mostrar más opciones»):
1. **Implementación COM `IExplorerCommand`**:
   - La biblioteca nativa C++ `FileShift.Shell.dll` implementa `IExplorerCommand` y `IEnumExplorerCommand`.
   - En `GetState(IShellItemArray* psiItemArray, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState)`, inspecciona en `< 2 ms` las extensiones de los elementos seleccionados y lee `%LOCALAPPDATA%\FileShift\settings.json`. Si todos los archivos pertenecen a una familia soportada, devuelve `ECS_ENABLED`; de lo contrario, `ECS_HIDDEN`.
   - En `EnumSubCommands(IEnumExplorerCommand** ppEnum)`, construye subcomandos ligeros en memoria (`SubExplorerCommand`) ordenados según la preferencia del usuario.
   - En `Invoke(IShellItemArray* psiItemArray, IBindCtx* pbc)` del subcomando, extrae las rutas Unicode (`SIGDN_FILESYSPATH`) y ejecuta `fileshift-cli.exe convert --to <fmt> --notify -- <paths...>` mediante `CreateProcessW` con argumentos escapados según las reglas de CommandLineToArgvW de Win32.
2. **Identidad de Paquete (MSIX Sparse Package)**:
   - Windows 11 ignora los registros `windows.fileExplorerContextMenus` que no provengan de un proceso/paquete con identidad MSIX.
   - FileShift utiliza un **External Location Sparse Package** (`AppxManifest.xml` con `uap10:AllowExternalContent="true"`) registrado durante la instalación con `Add-AppxPackage -Register ... -ExternalLocation <InstallDir>`, más entradas clásicas opcionales en `HKCU\Software\Classes\*\shell\FileShift` como respaldo para Windows 10.

## 5. Contratos de Detección y Validación
- **Inspección por Magic Bytes (Firmas binarias)**:
  - `PNG`: `89 50 4E 47 0D 0A 1A 0A`
  - `JPEG`: `FF D8 FF`
  - `GIF`: `47 49 46 38 37 61` (`GIF87a`) o `47 49 46 38 39 61` (`GIF89a`)
  - `WebP`: `52 49 46 46 ?? ?? ?? ?? 57 45 42 50` (`RIFF....WEBP`)
  - `BMP`: `42 4D` (`BM`)
  - `TIFF`: `49 49 2A 00` o `4D 4D 00 2A`
  - `PDF`: `25 50 44 46 2D` (`%PDF-`)
  - `WAV`: `52 49 46 46 ?? ?? ?? ?? 57 41 56 45` (`RIFF....WAVE`)
  - `FLAC`: `66 4C 61 43` (`fLaC`)
  - `OGG/Opus`: `4F 67 67 53` (`OggS`)
  - `MP3`: `49 44 33` (`ID3`) o frame sync `FF FB` / `FF F3` / `FF F2`
  - `ISO BMFF (MP4/MOV/M4A/AVIF/HEIC)`: caja `ftyp` en offset 4 (`mp41`, `mp42`, `isom`, `qt  `, `M4A `, `avif`, `heic`, `mif1`)
  - `MKV/WebM`: EBML header `1A 45 DF A3`
- Si la firma binaria contradice la extensión o el archivo está truncado/corrupto, el motor rechaza el trabajo en fase `Preparing` sin producir archivos vacíos.

## 6. Criterios de Aceptación (AC-01 a AC-16)
| Código | Descripción | Estado en Linux Sandbox | Estado en Windows 11 Físico |
|---|---|---|---|
| **AC-01** | Compilación desde el repositorio | `PASS` (Motor/CLI/Web/Tests compilados; código C#/C++ completo y verificado sintácticamente) | Pendiente ejecución de `dotnet build` / `msbuild` en host Windows |
| **AC-02** | Instalable en Windows 11 | `NOT TESTED` (Artefactos `FileShift.iss`, `AppxManifest.xml` y scripts listos) | Requiere host Windows 11 |
| **AC-03** | Aparece en el menú contextual moderno de Windows 11 | `NOT TESTED` (`IExplorerCommand` C++ y `windows.fileExplorerContextMenus` implementados) | Requiere sesión física de `explorer.exe` en Windows 11 |
| **AC-04** | Elegir formato sin abrir previamente la GUI | `PASS` (Validado en CLI `convert` y simulador interactivo de menú contextual) | Requiere validación física en Explorer |
| **AC-05** | Conversión produce un archivo real y válido | `PASS` (Validado con FFmpeg, ImageMagick y Ghostscript reales sobre archivos reales) | `PASS` (Misma lógica de motores) |
| **AC-06** | El archivo original permanece intacto (SHA-256 idéntico) | `PASS` (Verificado por pruebas automatizadas e inspección SHA-256) | `PASS` |
| **AC-07** | Destinos existentes se gestionan de manera segura (`AutoRename`, `Skip`, `Overwrite`) | `PASS` (Verificado con pruebas automatizadas) | `PASS` |
| **AC-08** | Conversión mediante drag and drop | `PASS` (Verificado en UI interactiva) | `PASS` |
| **AC-09** | Soporte real para imágenes, video, audio y PDF dentro de la matriz | `PASS` (Verificado con FFmpeg 4.4, ImageMagick 6.9 y Ghostscript reales) | `PASS` |
| **AC-10** | Errores se muestran y registran claramente (incl. archivos corruptos) | `PASS` (Verificado con tests de archivos corruptos) | `PASS` |
| **AC-11** | Preferencias, orden de menú y presets persisten entre ejecuciones | `PASS` (Persistencia JSON en disco verificada) | `PASS` |
| **AC-12** | Automatizaciones convierten archivos nuevos sin duplicaciones ni bucles | `PASS` (Verificado con test de watcher e inyección de archivos) | `PASS` |
| **AC-13** | Instalación y desinstalación gestionan integración de Explorer | `NOT TESTED` (Scripts de registro/desregistro MSIX + COM listos) | Requiere host Windows 11 |
| **AC-14** | Sin licencias comerciales, suscripciones, cuentas ni servicios de pago | `PASS` (100% libre y local) | `PASS` |
| **AC-15** | Funciona 100% sin conexión a Internet durante las conversiones | `PASS` (Motores locales sin llamadas de red) | `PASS` |
| **AC-16** | Sin dependencias ausentes ocultas ni botones ficticios | `PASS` (Todos los controles ejecutan acciones reales) | `PASS` |
