$ErrorActionPreference = 'Stop'
Get-AppxPackage -Name FileShift.UniversalConverter | ForEach-Object {
    Remove-AppxPackage -Package $_.PackageFullName
}
