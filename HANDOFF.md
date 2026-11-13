# FileShift · Estado de la primera release

Rama `feat/initial-implementation`. Base `f73850a` e implementación recibida `75a4cbf`. Las dos ramas ya existían en GitHub al iniciar el trabajo. Consultar SPEC para el contrato y PLAN para los criterios de cierre.

## Evidencia nueva

- En `75a4cbf`, la prueba de regresión multipágina falló porque la cola eliminó el destino previo de la segunda página.
- Con staging de todas las salidas, la prueba de regresión y la suite de conversión pasaron en macOS con .NET 10.0.302, Ghostscript, ImageMagick y FFmpeg. La solución .NET compiló con cero advertencias y errores.
- Explorer, instalación y desinstalación físicas siguen pendientes.

## QA físico en Windows 11 x64

Ejecutá estos pasos en una cuenta de pruebas, con copias descartables. Usá el candidato de CI correspondiente a la PR y guardá su SHA.

1. Extraé el artefacto `FileShift-Development-win-x64` de Actions. Compará `Get-FileHash` de los archivos con `SHA256SUMS.txt`. Ejecutá `Install-WindowsDependencies.ps1` desde PowerShell. Aceptá la elevación de los instaladores de motores si Windows la solicita.
2. Importá únicamente el certificado público del candidato en el almacén de pruebas con `Import-Certificate -FilePath .\FileShift-Development.cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople`. Guardá el thumbprint que devuelve. Ejecutá `FileShift-Setup-Development-win-x64.exe` y comprobá `Get-AppxPackage FileShift.UniversalConverter`.
3. Creá una carpeta de QA con copias de un PNG, un MP4 con audio y un PDF de dos páginas. Guardá hashes con `Get-ChildItem -File | Get-FileHash | Export-Csv ..\fileshift-originales.csv -NoTypeInformation`.
4. Cerrá FileShift desde **Salir** en la bandeja. En Explorer, hacé clic derecho sobre el PNG y usá **Convertir a… → WebP** en el menú moderno. Abrí el WebP. Repetí con **Extraer audio MP3** sobre el MP4 y reproducí el audio. Verificá que la aplicación principal siga cerrada.
5. Seleccioná dos PNG con nombres distintos, uno con espacios y acentos, y convertilos juntos. Probá una selección con PNG y MP4. Abrí esa selección en FileShift y verificá que pida un destino válido para el lote. Probá PDF más PNG con un destino común, por ejemplo JPEG. Verificá ambos resultados.
6. Repetí PNG → WebP con AutoRename. Debe aparecer ` (1).webp` y el primer WebP debe conservar su hash. En una prueba aparte, creá una carpeta que se llame igual que la salida de página 2 de un PDF y probá Overwrite. Verificá que el destino previo de página 1 siga intacto.
7. Creá un `corrupto.png` descartable con `Set-Content .\corrupto.png 'archivo inválido'`. Convertí desde Explorer y comprobá que aparezca el error. En WPF, convertí un video suficientemente largo y cancelalo desde Actividad. Esperá el estado cancelado y comprobá que la conversión se detuvo y la carpeta de staging desapareció.
8. Verificá los hashes de originales con `Import-Csv ..\fileshift-originales.csv | ForEach-Object { if ((Get-FileHash -LiteralPath $_.Path).Hash -ne $_.Hash) { throw "Original alterado: $($_.Path)" } }`.
9. En **Menú Contextual Win11**, reordená los favoritos de PDF e imagen y guardá. Verificá el orden desde Explorer. Reiniciá **Explorador de Windows** desde el Administrador de tareas y repetí PNG → WebP.
10. Creá una regla PNG → WebP en una carpeta de QA. Cerrá la ventana con **Mantener en bandeja** habilitado, copiá un PNG y verificá que aparece exactamente un WebP. Volvé desde la bandeja y comprobá la actividad.
11. Salí de FileShift y desinstalalo desde **Configuración → Aplicaciones instaladas**. Verificá que `Get-AppxPackage FileShift.UniversalConverter` no devuelva paquetes y que el comando desaparezca al reiniciar Explorer. Quitá del almacén `Cert:\CurrentUser\TrustedPeople` el certificado cuyo thumbprint anotaste. Los motores y las preferencias quedan disponibles para futuras instalaciones.
