# HANDOFF.md — Estado Actual, Evidencia Verificada y Guía de QA en Windows 11

## 1. Resumen de Correcciones de Auditoría (Rama `feat/initial-implementation`)
1. **P0 — Preservación de Datos y Validación Pre-Publicación (`src/Converter.Core/` y `src/Converter.Infrastructure/`)**:
   - `ConversionQueue.cs` ahora ejecuta `ValidateTemporaryOutputBeforePublishAsync` **antes** de `SafeFilePolicy.PublishValidatedAtomicOutput`.
   - `OutputIntegrityValidator.cs` valida la integridad completa del archivo temporal mediante `identify -regard-warnings` (imágenes), `ffprobe` (contenedores y streams de video/audio) y `gs -dSAFER -sDEVICE=nullpage` (PDF), además de verificar los marcadores finales obligatorios (`IEND` en PNG, `FF D9` en JPEG, `0x3B` en GIF, `%%EOF` en PDF).
   - Cualquier alteración en el SHA-256 del archivo original produce `JobStatus.Failed` (`OriginalIntactVerified = false`).
2. **P1 — Aplicación Nativa WPF Compilable y Completa (`src/Converter.App/`)**:
   - Corregido `MainWindow.xaml` y completado `MainWindow.xaml.cs` con las 5 vistas funcionales (**Actividad y Cola**, **Automatizaciones**, **Configuración General**, **Menú Contextual Win11**, **Presets**) conectadas a `UserSettingsStore`, `ConversionQueue`, `FolderAutomationService` y `DoctorService`.
   - `server.ts` y `engineRunner.ts` delegan al CLI compilado `.NET 10` (`src/Converter.Cli/bin/Release/net10.0/fileshift.dll`).
3. **P1 — Extensión `IExplorerCommand` C++17 y Empaquetado MSIX (`src/Converter.Shell/` y `installer/`)**:
   - `ExplorerCommand.cpp` lee `%LOCALAPPDATA%\FileShift\menu-cache.json` (generado automáticamente por `UserSettingsStore.Save`), muestra los formatos favoritos configurados por el usuario, despliega el submenú en cascada `Más formatos…`, soporta rutas de 32.768 `wchar_t` y fragmenta lotes grandes en `CreateProcessW`.
   - Generados los assets MSIX en `installer/Assets/` y actualizado `installer/Build-WindowsRelease.ps1`.
4. **P1 — PDF Multipágina y Licencias**:
   - `PdfAdapter.cs` exporta todas las páginas de PDFs multipágina (`_p2.png`, `_p3.png`, ...) o el rango solicitado, e `ImageMagickAdapter.cs` genera PDF 1.4 válido (`DCTDecode`).
   - Añadido archivo `/LICENSE` (MIT) en la raíz del repositorio.

## 2. Evidencia Verificada en este Entorno (Linux Container)
- **Compilación .NET 10 (`dotnet build FileShift.sln -c Release`)**: `PASS` (0 advertencias, 0 errores; incluye `FileShift.Core.dll`, `FileShift.Infrastructure.dll`, `fileshift.dll`, `FileShift.App.dll` WPF y `Converter.Tests.dll`).
- **Compilación Cruzada C++17 Win32 COM (`x86_64-w64-mingw32-g++`)**: `PASS` (`dist/win-x64/FileShift.Shell.dll: PE32+ executable (DLL) x86-64, for MS Windows`, 0 advertencias, 0 errores).
- **Suite de Pruebas xUnit C# (`dotnet test tests/Converter.Tests/Converter.Tests.csproj -c Release`)**: `PASS` (9/9 pruebas superadas: preservación de destino bajo `Overwrite` ante fallo, rechazo de PNG truncado sin `IEND`, fallo explícito ante alteración SHA-256 original, cancelación limpia, `PNG → WebP/PDF`, `MP4 → WebM/MP3`, `PDF multipágina → PNG`, `FolderAutomationService` sin bucles y `UserSettingsStore` + `menu-cache.json`).

## 3. Elementos que Requieren QA en Windows 11 Físico (`NOT TESTED` en Linux)
1. Registro e inspección visual del menú contextual primario de Windows 11 (`explorer.exe`) mediante `.\installer\Register-SparsePackage.ps1`.
2. Renderizado visual e interacción de ventana de `FileShift.App.exe` (WPF) y bandeja del sistema en una sesión de escritorio Windows 11.
3. Empaquetado final con `makeappx.exe` (Windows 11 SDK) e Inno Setup 6 (`ISCC.exe installer\FileShift.iss`).

## 4. Publicación en GitHub (`https://github.com/Teyocesu/FileShift`)
Este contenedor de ejecución no dispone de tus credenciales privadas de GitHub (`gh auth` / SSH key / PAT). Para publicar la rama `main` y `feat/initial-implementation` en tu repositorio oficial:
```bash
git push -u origin main
git push -u origin feat/initial-implementation
```
