# AGENTS.md — Directrices de Ingeniería para FileShift

## 1. Propósito del Sistema
**FileShift** es un convertidor universal de archivos para Windows 11, 100% gratuito, local-first y de código abierto (MIT). Su prioridad absoluta es la conversión directa desde el **menú contextual moderno de Windows 11** (`IExplorerCommand`) sin abrir obligatoriamente la interfaz gráfica, preservando siempre la integridad del archivo de entrada.

## 2. Reglas e Invariantes Inquebrantables
1. **Invariante de Integridad del Original (`INV-01`)**: Ningún proceso de conversión, automatización o cancelación puede modificar, truncar ni eliminar el archivo original de entrada.
2. **Escritura Atómica Temporal (`INV-02`)**: Toda salida se escribe primero en un archivo dentro de una carpeta temporal aislada (`.fileshift.tmp.<id>`) dentro del mismo volumen de destino. Solo tras validar que el código de salida del motor es `0` y el archivo temporal tiene tamaño `> 0` y cabecera válida se publica al nombre final.
3. **Política No Destructiva ante Colisiones (`INV-03`)**: Por defecto (`AutoRename`), si `archivo.webp` ya existe, se genera `archivo (1).webp`, `archivo (2).webp`, etc., sin sobrescribir archivos previos salvo autorización explícita (`Overwrite`).
4. **Ejecución Segura de Procesos (`INV-04`)**: Prohibido concatenar cadenas de comandos en shells (`cmd.exe /c` o `sh -c`). En .NET se utiliza exclusivamente `ProcessStartInfo.ArgumentList` y en Node/CLI `child_process.spawn(executable, argsArray, { shell: false })`.
5. **Ligereza en `explorer.exe` (`INV-05`)**: La extensión COM (`FileShift.Shell.dll`) lee únicamente la extensión del `IShellItemArray` y un archivo ligero de configuración (`%LOCALAPPDATA%\FileShift\menu-cache.json`) en memoria. Jamás carga FFmpeg, ImageMagick ni el runtime de .NET dentro del proceso `explorer.exe`. Al invocar `Invoke()`, lanza `fileshift-cli.exe convert ...` fuera de proceso (`CreateProcessW` con `CREATE_NO_WINDOW`).
6. **Cero Restricciones Comerciales (`INV-06`)**: Prohibido añadir telemetría, cuentas de usuario, pruebas de 7 días, validaciones de licencia remotas o límites artificiales de lote.

## 3. Estructura del Repositorio
```text
FileShift.sln
src/
  Converter.Core/            # Dominio, matriz de formatos, firmas mágicas, cola concurrente, políticas de colisión
  Converter.Infrastructure/  # Adaptadores reales: FFmpegAdapter, ImageMagickAdapter, PdfAdapter, LibreOfficeAdapter
  Converter.Cli/             # Ejecutable CLI (fileshift convert, formats, doctor) invocado por Explorer y terminal
  Converter.Shell/           # Extensión C++17 Win32 COM (IExplorerCommand, IEnumExplorerCommand) para Windows 11
  Converter.App/             # Aplicación de escritorio WPF (.NET 10) + System Tray + FileSystemWatcher
installer/
  AppxManifest.xml           # Manifiesto Sparse Package para windows.fileExplorerContextMenus
  FileShift.iss              # Script de Inno Setup 6 + registro MSIX Sparse Package y clásico reversible
  Register-SparsePackage.ps1 # Script auxiliar de firma de desarrollo y registro de identidad MSIX
tests/
  Converter.Tests/           # Suite de pruebas de integración y regresión xUnit (.NET)
```
