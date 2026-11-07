param(
    [string]$InstallDir = "$PSScriptRoot\..\dist\win-x64"
)

$ErrorActionPreference = "Stop"
$manifestPath = Join-Path $InstallDir "AppxManifest.xml"

if (-not (Test-Path $manifestPath)) {
    throw "No se encontró AppxManifest.xml en '$InstallDir'. Ejecutá primero Build-WindowsRelease.ps1."
}

Write-Host "Registrando Sparse Package MSIX de FileShift para el menú contextual moderno de Windows 11..." -ForegroundColor Cyan
Add-AppxPackage -Register $manifestPath -ExternalLocation $InstallDir
Write-Host "Integración registrada correctamente. El menú 'Convertir a…' ya está activo en Windows Explorer." -ForegroundColor Green
