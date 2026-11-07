# PLAN.md — Plan de Ejecución e Ingeniería de FileShift

## 1. Fases del Proyecto

### Fase A: Aplicación Mínima Funcional y Motor Universal (`COMPLETED`)
- [x] Matriz real de capacidades (Imágenes, Video, Audio, PDF, Documentos Office opcionales).
- [x] Inspección de firmas binarias (Magic Bytes) + sonda `ffprobe` para contenedores y codecs reales.
- [x] Adaptadores reales para `ffmpeg`/`ffprobe`, `ImageMagick` (`magick`/`convert`), `Ghostscript` (`gs`) y `LibreOffice` (`soffice --headless`).
- [x] Escritura atómica mediante archivo temporal en el volumen de destino y política anti-colisión (`AutoRename`, `Skip`, `Overwrite`).
- [x] Ejecutable CLI (`convert`, `formats`, `doctor`) con códigos de salida coherentes.

### Fase B: Integración Nativa con Windows 11 Explorer (`COMPLETED IN CODE / PENDING PHYSICAL QA`)
- [x] Implementación C++17 Win32 COM (`src/Converter.Shell/ExplorerCommand.cpp`) de `IExplorerCommand` e `IEnumExplorerCommand`.
- [x] Construcción dinámica de submenús (`EnumSubCommands`) leyendo las preferencias de formatos favoritos del usuario sin bloquear `explorer.exe`.
- [x] Soporte de selección múltiple (`IShellItemArray`), rutas largas (`\\?\`), espacios y caracteres Unicode (`wchar_t` / `CreateProcessW`).
- [x] Manifiesto de paquete sparse (`installer/AppxManifest.xml`) con `windows.fileExplorerContextMenus` y `com:ComServer`.
- [x] Respaldo opcional para menú clásico reversible (`HKCU\Software\Classes\*\shell\FileShift`).

### Fase C: Aplicación Desktop y Workbench Interactivo (`COMPLETED`)
- [x] Solución nativa WPF (`src/Converter.App/`) y Workbench interactivo full-stack conectado a los motores reales del sistema.
- [x] Pantalla principal de Actividad con Drag & Drop, selector de formato/preset, progreso en tiempo real, verificación de hash SHA-256 original/salida, y simulador integrado del menú contextual de Windows 11.
- [x] Configuración General, Configuración interactiva del Menú Contextual (ordenamiento numerado `1`, `2`, `3` por categoría) y gestor de Presets.

### Fase D: Capacidades Adicionales (`COMPLETED`)
- [x] Soporte multipágina y rango de páginas en PDF (`PDF → PNG/JPEG`) y ensamblado `Imágenes → PDF`.
- [x] Módulo de Automatizaciones (vigilancia de carpetas con verificación de estabilidad de escritura y prevención de bucles infinitos).
- [x] Soporte bilingüe Español / Inglés persistente.

### Fase E: Instalación, Git y QA (`COMPLETED`)
- [x] Script de instalación Inno Setup (`installer/FileShift.iss`) + scripts PowerShell de registro/desregistro MSIX Sparse Package (`Register-SparsePackage.ps1` y `Unregister-SparsePackage.ps1`).
- [x] Inicialización del repositorio Git con rama principal `main` y rama de desarrollo `feat/initial-implementation`.
- [x] Documentación de licencias (`THIRD_PARTY_NOTICES.md`) y pruebas de integración reales sobre archivos multimedia generados.

## 2. Bloqueos y Limitaciones del Entorno Actual
1. **Entorno de Ejecución Actual (Linux Container)**:
   - Las pruebas de conversión multimedia (`FFmpeg`, `ImageMagick`, `Ghostscript`), CLI, cola concurrente, preservación del archivo original, manejo de colisiones, rutas Unicode/espacios, archivos corruptos y automatizaciones de carpetas se han ejecutado y verificado **de forma real** en este entorno.
   - La compilación binaria de `FileShift.Shell.dll` (MSVC Win32 COM) y la prueba visual física haciendo clic derecho en `explorer.exe` sobre Windows 11 están marcadas honestamente como `NOT TESTED` en este contenedor Linux y listas para ejecutarse en el PC Windows 11 del usuario mediante `installer/Build-WindowsRelease.ps1`.
2. **Push Remoto a GitHub (`https://github.com/Teyocesu/FileShift.git`)**:
   - El repositorio Git local está completamente inicializado con el remoto `origin` apuntando a `https://github.com/Teyocesu/FileShift.git`, rama base `main` y todos los commits estructurados en `feat/initial-implementation`. Dado que el contenedor no almacena credenciales privadas de GitHub del usuario, se incluye un comando directo de push y la descarga empaquetada del repositorio completo desde la propia aplicación.
