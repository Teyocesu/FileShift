# THIRD_PARTY_NOTICES.md — Avisos de Licencias y Software de Terceros

**FileShift** es software libre y de código abierto distribuido bajo la licencia **MIT**.
Para realizar conversiones locales sin servicios en la nube, FileShift invoca o se integra con motores multimedia de código abierto cuyas licencias y condiciones de atribución se detallan a continuación.

---

## 1. FFmpeg y ffprobe
- **Sitio oficial**: https://ffmpeg.org
- **Licencia**: GNU Lesser General Public License (LGPL) v2.1 o posterior / GNU General Public License (GPL) v2 o v3 según las bibliotecas de codificación habilitadas en la compilación distribuida (por ejemplo `libx264`, `libx265`).
- **Uso en FileShift**: FileShift invoca `ffmpeg` y `ffprobe` como procesos externos independientes mediante línea de comandos (`ProcessStartInfo.ArgumentList`), sin enlazar estáticamente ni modificar su código fuente.
- **Obligaciones de redistribución**:
  - Si se distribuye un instalador que incluya binarios precompilados de FFmpeg con codecs GPL (como `x264`), debe acompañarse del texto completo de la licencia GPL y la oferta o enlace al código fuente exacto de la compilación de FFmpeg utilizada (p. ej. compilaciones oficiales de BtbN o gyan.dev).
  - Si se utiliza una compilación LGPL sin componentes GPL, debe mantenerse el aviso de copyright de FFmpeg y su licencia LGPL v2.1+.

---

## 2. ImageMagick
- **Sitio oficial**: https://imagemagick.org
- **Licencia**: ImageMagick License (compatible con Apache 2.0, permisiva con obligación de atribución).
- **Copyright**: Copyright © 1999 ImageMagick Studio LLC.
- **Uso en FileShift**: Procesamiento, rasterización, redimensionamiento, corrección EXIF y conversión de imágenes estáticas y animadas mediante proceso externo.
- **Aviso requerido**:
  > Licensed under the ImageMagick License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at https://imagemagick.org/script/license.php

---

## 3. Ghostscript / PDFium (Procesamiento de PDF)
- **PDFium**:
  - **Sitio oficial**: https://pdfium.googlesource.com/pdfium/
  - **Licencia**: BSD 3-Clause License (Google LLC).
  - **Uso en Windows**: Renderizado rápido nativo de páginas PDF a mapas de bits (`PNG` / `JPEG`) sin restricciones copyleft.
- **Ghostscript (Entorno Linux / Fallback CLI)**:
  - **Sitio oficial**: https://www.ghostscript.com
  - **Licencia**: GNU Affero General Public License (AGPL) v3.
  - **Nota importante de distribución**: En la distribución de Windows 11 (`FileShift`), el adaptador predeterminado utiliza **PDFium** (BSD-3-Clause) para renderizado de PDF a imagen e **ImageMagick / PdfSharp (MIT)** para la creación de PDF desde imágenes, evitando obligaciones AGPL en el instalador de Windows. Cuando el usuario dispone de Ghostscript instalado en su sistema, FileShift puede detectarlo como motor local opcional.

---

## 4. LibreOffice (Adaptador Opcional para Documentos Office)
- **Sitio oficial**: https://www.libreoffice.org
- **Licencia**: Mozilla Public License v2.0 (MPL 2.0).
- **Uso en FileShift**: FileShift **no empaqueta ni descarga silenciosamente** LibreOffice. Si el usuario tiene LibreOffice instalado en su equipo (`soffice.exe`), FileShift lo detecta automáticamente y habilita la conversión local de documentos (`DOCX`, `ODT`, `XLSX`, `PPTX` → `PDF`) mediante ejecución headless (`--headless --convert-to pdf`).

---

## 5. Bibliotecas del Ecosistema .NET y Web
- **.NET Runtime & WPF**: MIT License — Copyright © .NET Foundation and Contributors.
- **xUnit.net**: Apache License 2.0 — Copyright © .NET Foundation.
- **React & Lucide Icons (Workbench UI)**: MIT License / ISC License.
