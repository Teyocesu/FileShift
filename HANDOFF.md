# HANDOFF.md — Estado Actual, Evidencia y Próxima Acción

## 1. Estado Actual
**FileShift v1.0.0** se encuentra implementado de extremo a extremo:
- **Código Nativo Windows 11 (`src/Converter.*` + `installer/`)**:
  - `src/Converter.Core/`: Modelos de dominio, detector de formatos por firmas mágicas (`MagicByteInspector.cs`), matriz de capacidades (`FormatMatrix.cs`), cola concurrente (`ConversionQueue.cs`), política segura de archivos (`SafeFilePolicy.cs`) y motor de automatizaciones (`FolderAutomationService.cs`).
  - `src/Converter.Infrastructure/`: Adaptadores reales con `ProcessStartInfo.ArgumentList` para `FFmpegAdapter.cs` (con paleta de 2 pasos para GIF y progreso por duración), `ImageMagickAdapter.cs` (con auto-orientación EXIF, fondo explícito para destinos sin alfa y preservación de frames animados), `PdfAdapter.cs` y `LibreOfficeAdapter.cs`.
  - `src/Converter.Cli/`: CLI completa (`Program.cs`) con subcomandos `convert`, `formats` y `doctor`.
  - `src/Converter.Shell/`: Extensión nativa C++17 Win32 COM (`ExplorerCommand.h`, `ExplorerCommand.cpp`, `dllmain.cpp`, `FileShift.Shell.def`, `CMakeLists.txt`) que implementa `IExplorerCommand` e `IEnumExplorerCommand` para el menú contextual moderno de Windows 11.
  - `src/Converter.App/`: Aplicación de escritorio WPF (.NET 10) con tema oscuro de precisión, Drag & Drop, bandeja del sistema, pestañas de Actividad, Automatizaciones y Ajustes (General, Menú, Presets).
  - `installer/`: `AppxManifest.xml` (Sparse Package con `windows.fileExplorerContextMenus`), `FileShift.iss` (Inno Setup), `Build-WindowsRelease.ps1`, `Register-SparsePackage.ps1` y `Unregister-SparsePackage.ps1`.
- **Motor y Aplicación Ejecutable en Tiempo Real (Linux / Multiplataforma)**:
  - Ejecuta conversiones reales en disco usando `/usr/bin/ffmpeg`, `/usr/bin/ffprobe`, `/usr/bin/convert` (ImageMagick) y `/usr/bin/gs` (Ghostscript).
  - Incluye CLI ejecutable (`npx tsx cli/fileshift-cli.ts`) y suite de pruebas automatizadas (`npx --yes tsx --test src/core/converter.test.ts`).

## 2. Evidencia de Verificación Automática
1. **Pruebas de Integración Reales (`src/core/converter.test.ts`)**:
   - Conversión real de imagen `PNG → WebP` y `PNG → JPEG` verificando cabeceras binarias (`RIFF....WEBP` y `FF D8 FF`).
   - Conversión real de video `MP4 → WebM` y `MP4 → GIF` verificando contenedores y flujos con `ffprobe`.
   - Extracción real de audio `MP4 → MP3` y conversión `WAV → FLAC` verificando cabeceras y duración.
   - Conversión real de `PDF → PNG` e `Imagen → PDF` verificando firma `%PDF-`.
   - Verificación criptográfica SHA-256 de que el archivo original permanece 100% idéntico antes y después de cada conversión.
   - Política de colisión `AutoRename`: genera `archivo (1).ext` sin sobrescribir el archivo existente.
   - Manejo de rutas con espacios y caracteres Unicode (`¡Prueba España 日本語 2026!.png`).
   - Rechazo seguro de archivos corruptos/truncados sin dejar archivos temporales ni salidas vacías (`0 bytes`).
   - Prevención de bucles en reglas de automatización de carpetas.

## 3. Próxima Acción en Windows 11 Físico
1. Clonar o descomprimir el repositorio en Windows 11 con .NET SDK y Visual Studio 2022 Build Tools (C++ Desktop workload):
   ```powershell
   git checkout feat/initial-implementation
   .\installer\Build-WindowsRelease.ps1
   ```
2. Ejecutar `.\installer\Register-SparsePackage.ps1` para registrar el certificado de desarrollo temporal y el paquete sparse MSIX (`FileShift.SparsePackage`), habilitando de inmediato la entrada **Convertir con FileShift** en el menú contextual primario de Windows 11 Explorer.
3. Realizar la lista de comprobación física de Explorer detallada en `SPEC.md` y fusionar la Pull Request `feat/initial-implementation` hacia `main`.
