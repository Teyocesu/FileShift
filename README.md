# FileShift — Convertidor Universal de Archivos para Windows 11

**FileShift** es una herramienta de escritorio rápida, privada, local-first y 100% gratuita para convertir imágenes, videos, audio, documentos PDF y archivos de Office directamente desde el **menú contextual moderno de Windows 11** (`IExplorerCommand`), la interfaz gráfica o la línea de comandos.

- **Sin cuentas, suscripciones, marcas de agua ni límites artificiales.**
- **100% Local-First**: Ningún archivo, nombre, ruta ni metadato sale de tu equipo.
- **Invariante de Seguridad**: El archivo original jamás se modifica ni elimina; todas las salidas se escriben de forma atómica a través de archivos temporales verificados en el volumen de destino.

---

## 1. Arquitectura del Proyecto

```text
FileShift.sln
├── src/
│   ├── Converter.Core/            # Núcleo C#: Matriz de formatos, inspección Magic Bytes, cola concurrente, políticas de archivos
│   ├── Converter.Infrastructure/  # Adaptadores seguros (ProcessStartInfo.ArgumentList): FFmpeg, ImageMagick, PDF, LibreOffice
│   ├── Converter.Cli/             # CLI nativa (fileshift-cli convert | formats | doctor) invocada por Explorer y terminal
│   ├── Converter.Shell/           # Extensión C++17 Win32 COM (IExplorerCommand / IEnumExplorerCommand) para Windows 11
│   └── Converter.App/             # Aplicación de escritorio WPF (.NET 10) con Actividad, Automatizaciones, Menú y Presets
├── installer/
│   ├── AppxManifest.xml           # Manifiesto MSIX Sparse Package (windows.fileExplorerContextMenus)
│   ├── FileShift.iss              # Instalador Inno Setup 6 con registro y desinstalación limpia
│   ├── Build-WindowsRelease.ps1   # Script reproducible de compilación C# + C++ + empaquetado
│   ├── Register-SparsePackage.ps1 # Registro de identidad MSIX Sparse Package para menú moderno de Windows 11
│   └── Unregister-SparsePackage.ps1
├── tests/
│   └── Converter.Tests/           # Pruebas de integración y regresión xUnit (.NET 10)
└── cli/ & src/core/               # Motor multiplataforma ejecutable en vivo + Workbench interactivo con FFmpeg/ImageMagick reales
```

---

## 2. Formatos y Conversiones Soportadas

| Categoría | Formatos de Entrada | Formatos de Salida | Motor Utilizado |
|---|---|---|---|
| **Imágenes** | `PNG`, `JPEG/JPG`, `WebP`, `AVIF`, `BMP`, `GIF`, `TIFF`, `ICO`, `HEIC`, `SVG` | `PNG`, `JPEG`, `WebP`, `AVIF`, `BMP`, `GIF`, `TIFF`, `ICO`, `PDF` | ImageMagick (`magick` / `convert`) |
| **Video** | `MP4`, `MKV`, `MOV`, `WebM`, `AVI` | `MP4` (H.264/AAC), `MKV`, `MOV`, `WebM` (VP9/Opus), `AVI`, `GIF` (paleta 2 pasos) + Extracción a `MP3`, `WAV`, `FLAC`, `AAC`, `M4A`, `OGG`, `Opus` | FFmpeg + `ffprobe` |
| **Audio** | `MP3`, `WAV`, `FLAC`, `AAC`, `M4A`, `OGG`, `Opus` | `MP3`, `WAV`, `FLAC`, `AAC`, `M4A`, `OGG`, `Opus` | FFmpeg + `ffprobe` |
| **PDF** | `PDF` (todas las páginas o rango `1-3`) | `PNG`, `JPEG` | PDFium / Ghostscript / ImageMagick |
| **Office (Opcional)** | `DOCX`, `ODT`, `XLSX`, `PPTX` | `PDF` | LibreOffice Headless (`soffice --headless`) |

---

## 3. Compilación e Instalación en Windows 11

### Requisitos de compilación
- Windows 11 (22H2 / 23H2 / 24H2)
- .NET 10 SDK (o .NET 8/9 LTS compatible)
- Visual Studio 2022 Build Tools con workload **Desktop development with C++** (para `FileShift.Shell.dll`)
- Inno Setup 6 (opcional, para generar `FileShift-Setup-x64.exe`)

### Pasos reproducibles
1. Clonar el repositorio:
   ```powershell
   git clone https://github.com/Teyocesu/FileShift.git
   cd FileShift
   git checkout feat/initial-implementation
   ```
2. Compilar toda la solución (.NET + extensión C++ COM) y preparar el directorio de distribución:
   ```powershell
   .\installer\Build-WindowsRelease.ps1
   ```
3. Registrar la integración con el menú contextual moderno de Windows 11:
   ```powershell
   .\installer\Register-SparsePackage.ps1
   ```
4. Para desinstalar y limpiar completamente los registros y el paquete sparse:
   ```powershell
   .\installer\Unregister-SparsePackage.ps1
   ```

---

## 4. Uso desde la Línea de Comandos (CLI)

```powershell
# Convertir una imagen a WebP conservando el original
fileshift convert "captura.png" --to webp

# Convertir un video MKV a MP4 o extraer su audio en MP3
fileshift convert "grabacion.mkv" --to mp4
fileshift convert "grabacion.mkv" --to mp3 --quality 90

# Aplicar un preset guardado
fileshift convert "foto.jpg" --preset web

# Listar la matriz completa de formatos y motores disponibles
fileshift formats

# Ejecutar diagnóstico de motores, versiones, codecs e integración Shell
fileshift doctor
```

---

## 5. Licencia
Distribuido bajo la licencia **MIT**. Consultá `THIRD_PARTY_NOTICES.md` para los avisos de licencia de FFmpeg, ImageMagick, PDFium/Ghostscript y LibreOffice.
