param([string]$InstallDir = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $InstallDir).Path
$msix = Join-Path $resolved 'FileShift-Sparse.msix'
$cli = Join-Path $resolved 'fileshift.exe'
& $cli doctor
if ($LASTEXITCODE -ne 0) { throw 'Faltan motores de conversión. Ejecutá Install-WindowsDependencies.ps1.' }

Get-AppxPackage -Name FileShift.UniversalConverter | ForEach-Object {
    Remove-AppxPackage -Package $_.PackageFullName
}
Add-AppxPackage -Path $msix -ExternalLocation $resolved
if (!(Get-AppxPackage -Name FileShift.UniversalConverter)) { throw 'No se registró la identidad de FileShift.' }
