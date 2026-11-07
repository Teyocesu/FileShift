$ErrorActionPreference = "SilentlyContinue"
Write-Host "Eliminando registro de Sparse Package MSIX de FileShift..." -ForegroundColor Yellow

$pkg = Get-AppxPackage -Name "FileShift.UniversalConverter"
if ($null -ne $pkg) {
    Remove-AppxPackage -Package $pkg.PackageFullName
    Write-Host "Paquete FileShift.UniversalConverter desregistrado limpiamente." -ForegroundColor Green
} else {
    Write-Host "No se encontró un paquete FileShift registrado." -ForegroundColor Gray
}

Remove-Item -Path "HKCU:\Software\Classes\*\shell\FileShift" -Recurse -Force -ErrorAction SilentlyContinue
