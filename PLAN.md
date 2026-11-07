# PLAN.md — Plan de Remediación de Auditoría e Ingeniería de FileShift

## 1. Estado de Fases y Remediación

### Prioridad P0: Correctness y Preservación de Datos (`COMPLETED & VERIFIED`)
- [x] **Validación Pre-Publicación (`ConversionQueue.cs`)**: El archivo temporal se valida íntegramente en estado `Validating` **antes** de invocar `PublishValidatedAtomicOutput`.
- [x] **Validación Estructural y por Motor (`OutputIntegrityValidator.cs`)**: Comprobación de cabeceras mágicas, marcadores de fin de archivo (`IEND` en PNG, `FF D9` en JPEG, `0x3B` en GIF, `%%EOF` en PDF) y validación profunda con `identify`, `ffprobe` y `gs -sDEVICE=nullpage`.
- [x] **Protección de Destino Previo en `Overwrite` (`SafeFilePolicy.cs`)**: Si la conversión falla o la salida es inválida/truncada, el archivo existente en el destino permanece intacto.
- [x] **Invariante SHA-256 Estricto (`INV-01`)**: Si el SHA-256 del archivo original cambia, el trabajo falla explícitamente (`JobStatus.Failed`, nunca `Completed`).
- [x] **Suite xUnit C# (`tests/Converter.Tests/ConversionPipelineTests.cs`)**: 9 pruebas automatizadas pasando en `.NET 10.0.403`.

### Prioridad P1: Aplicación Nativa WPF y Unificación de Motor (`COMPLETED & COMPILED`)
- [x] **Compilación Real de WPF (`src/Converter.App/`)**: Corregido `MainWindow.xaml` (eliminados `StackPanel.Spacing` y `RowDefinition.Width`) y habilitado `EnableWindowsTargeting`. `dotnet build FileShift.sln -c Release` compila XAML y C# con 0 errores.
- [x] **Funcionalidad Completa en `MainWindow.xaml.cs`**: Vistas operativas para **Actividad y Cola**, **Automatizaciones**, **Configuración General y Doctor**, **Menú Contextual Win11** y **Presets**, conectadas a `ConversionQueue`, `FolderAutomationService`, `UserSettingsStore`, `FormatMatrix` y `DoctorService`.
- [x] **Unificación de Motor**: `server.ts` y `engineRunner.ts` delegan al binario compilado C# (`src/Converter.Cli/bin/Release/net10.0/fileshift.dll --json`), evitando mantener dos motores de conversión divergentes.

### Prioridad P1: Windows 11 Explorer (`IExplorerCommand`) y Empaquetado (`COMPLETED & COMPILED / PENDING PHYSICAL WIN11 QA`)
- [x] **Extensión COM C++17 (`src/Converter.Shell/`)**: Lee `%LOCALAPPDATA%\FileShift\menu-cache.json` escrito por `UserSettingsStore`, ordena formatos favoritos por categoría, agrupa formatos adicionales en el submenú en cascada `Más formatos…` (`ECF_HASSUBCOMMANDS`), soporta rutas de hasta 32.768 caracteres, divide lotes extensos y registra fallos en `shell-errors.log`.
- [x] **Compilación Binaria Cruzada**: Compilado `dist/win-x64/FileShift.Shell.dll` (`PE32+ executable (DLL) x86-64, for MS Windows`) mediante `x86_64-w64-mingw32-g++` sin advertencias ni errores.
- [x] **Assets MSIX y Script de Release**: Creados `installer/Assets/StoreLogo.png`, `Square150x150Logo.png` y `Square44x44Logo.png`; actualizado `Build-WindowsRelease.ps1` para empaquetar `FileShift-Sparse.msix` (`makeappx.exe`) e invocar Inno Setup (`ISCC.exe`).

### Prioridad P1: PDF Multipágina y Licencias (`COMPLETED & VERIFIED`)
- [x] **PDF Multipágina (`PdfAdapter.cs`)**: Detecta número de páginas con Ghostscript, exporta todas las páginas por defecto (`1..N`) o el rango indicado (`--pages 1-3`), y rechaza PDFs cifrados o dañados.
- [x] **Licencias**: Añadido `/LICENSE` (MIT) y verificado `/THIRD_PARTY_NOTICES.md`.
