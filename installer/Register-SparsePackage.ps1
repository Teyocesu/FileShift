param(
    [string]$InstallDir = "$PSScriptRoot\..\dist\win-x64"
)

$ErrorActionPreference = "Stop"
$resolvedDir = (Resolve-Path $InstallDir).Path
$manifestPath = Join-Path $resolvedDir "AppxManifest.xml"
$msixPath = Join-Path $resolvedDir "FileShift-Sparse.msix"

if (-not (Test-Path $manifestPath)) {
    throw "No se encontró AppxManifest.xml en '$resolvedDir'. Ejecutá primero Build-WindowsRelease.ps1."
}

Write-Host "Registrando Sparse Package MSIX de FileShift para el menú contextual moderno de Windows 11..." -ForegroundColor Cyan

if (Test-Path $msixPath) {
    Add-AppxPackage -Path $msixPath -ExternalLocation $resolvedDir
} else {
    # Modo desarrollo con Developer Mode activo en Windows 11
    Add-AppxPackage -Register $manifestPath -ExternalLocation $resolvedDir
}

Write-Host "Integración registrada en Windows Package Manager. Verificá con Get-AppxPackage FileShift.UniversalConverter." -ForegroundColor Green
