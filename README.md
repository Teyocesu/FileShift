# FileShift

Convertidor local para Windows 11 x64. En Explorer, seleccioná archivos y elegí **Convertir a…** en el menú contextual moderno. Desde la aplicación, añadí o arrastrá archivos, elegí un destino compatible con toda la selección y pulsá **Convertir selección**.

FileShift conserva el original, valida las salidas antes de publicarlas y usa AutoRename por defecto. Funciona con archivos locales y motores gratuitos. LibreOffice habilita las conversiones Office a PDF de forma opcional.

## Windows x64

GitHub Actions compila el motor .NET, la aplicación WPF, la CLI y la extensión COM C++17. El instalador incorpora el runtime .NET. Instalá los motores por separado con `Install-WindowsDependencies.ps1`, que obtiene versiones fijadas y comprueba SHA-256. La conversión se realiza sin descargar componentes.

Los artefactos de CI usan una identidad de desarrollo. Seguí el procedimiento de [QA físico](HANDOFF.md) en un equipo Windows 11 x64. Para distribución general, el publicador debe aportar un certificado de firma de código de confianza y un servidor de timestamp RFC 3161.

Para compilar localmente en Windows, instalá .NET SDK 10.0.404, CMake, Visual Studio Build Tools con C++ x64 y el SDK de Windows 11. Ejecutá `installer/Install-WindowsDependencies.ps1 -BuildTools`, añadí las carpetas de motores e Inno bajo `%LOCALAPPDATA%\FileShift\engines` al PATH de compilación y ejecutá `installer/Build-WindowsRelease.ps1 -Development`. Los artefactos quedan bajo `dist`.

## CLI

```powershell
fileshift convert --to webp -- "captura.png"
fileshift convert --to mp3 -- "grabación.mp4"
fileshift convert --preset preset-web-webp -- "foto.jpg"
fileshift convert --to png --pages 2-4 --skip -- "informe.pdf"
fileshift formats
fileshift doctor
```

`doctor` actualiza los formatos disponibles del menú. Volvé a ejecutarlo si instalás o quitás un motor. Si cerrás la ventana con **Mantener en bandeja** activado, FileShift continúa las automatizaciones. El icono de bandeja permite volver a abrirlo o salir.

El workbench web del archivo original permanece en el árbol durante la validación de paridad. El instalador distribuye el producto nativo. Consultá [SPEC](SPEC.md), [PLAN](PLAN.md) y [licencias de terceros](THIRD_PARTY_NOTICES.md).
