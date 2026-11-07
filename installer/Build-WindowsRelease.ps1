param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\..\dist\win-x64",
    [string]$InstallerOutputDir = "$PSScriptRoot\..\dist\installer",
    [string]$CertThumbprint = ""
)

$ErrorActionPreference = "Stop"
Write-Host "=== FileShift Windows 11 Release & MSIX/Inno Builder ===" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutputDir "Assets") | Out-Null
New-Item -ItemType Directory -Force -Path $InstallerOutputDir | Out-Null

Write-Host "[1/6] Ejecutando suite de pruebas xUnit (Converter.Tests)..." -ForegroundColor Yellow
dotnet test "$PSScriptRoot\..\tests\Converter.Tests\Converter.Tests.csproj" -c $Configuration

Write-Host "[2/6] Publicando Converter.Cli y Converter.App (.NET 10 win-x64)..." -ForegroundColor Yellow
dotnet publish "$PSScriptRoot\..\src\Converter.Cli\Converter.Cli.csproj" -c $Configuration -r win-x64 --self-contained false -o $OutputDir
dotnet publish "$PSScriptRoot\..\src\Converter.App\Converter.App.csproj" -c $Configuration -r win-x64 --self-contained false -o $OutputDir

Write-Host "[3/6] Compilando extensión nativa COM C++17 (FileShift.Shell.dll)..." -ForegroundColor Yellow
$shellBuildDir = "$PSScriptRoot\..\src\Converter.Shell\build"
New-Item -ItemType Directory -Force -Path $shellBuildDir | Out-Null
cmake -S "$PSScriptRoot\..\src\Converter.Shell" -B $shellBuildDir -A x64
cmake --build $shellBuildDir --config $Configuration
$builtDll = Join-Path $shellBuildDir "$Configuration\FileShift.Shell.dll"
if (-not (Test-Path $builtDll)) {
    $builtDll = Join-Path $shellBuildDir "FileShift.Shell.dll"
}
Copy-Item $builtDll -Destination $OutputDir -Force

Write-Host "[4/6] Copiando manifiesto MSIX Sparse Package, Assets visuales y licencias..." -ForegroundColor Yellow
Copy-Item "$PSScriptRoot\AppxManifest.xml" -Destination $OutputDir -Force
Copy-Item "$PSScriptRoot\Assets\*.png" -Destination (Join-Path $OutputDir "Assets") -Force
Copy-Item "$PSScriptRoot\Register-SparsePackage.ps1" -Destination $OutputDir -Force
Copy-Item "$PSScriptRoot\Unregister-SparsePackage.ps1" -Destination $OutputDir -Force
Copy-Item "$PSScriptRoot\..\LICENSE" -Destination $OutputDir -Force
Copy-Item "$PSScriptRoot\..\THIRD_PARTY_NOTICES.md" -Destination $OutputDir -Force

Write-Host "[5/6] Generando paquete de identidad MSIX Sparse Package (FileShift-Sparse.msix)..." -ForegroundColor Yellow
$makeAppx = Get-Command "makeappx.exe" -ErrorAction SilentlyContinue
if (-not $makeAppx) {
    $sdkMakeAppx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if ($sdkMakeAppx) { $makeAppx = $sdkMakeAppx.FullName }
}

if ($makeAppx) {
    $sparseStage = Join-Path $InstallerOutputDir "sparse-stage"
    if (Test-Path $sparseStage) { Remove-Item $sparseStage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $sparseStage "Assets") | Out-Null
    Copy-Item "$PSScriptRoot\AppxManifest.xml" -Destination $sparseStage -Force
    Copy-Item "$PSScriptRoot\Assets\*.png" -Destination (Join-Path $sparseStage "Assets") -Force

    $msixOut = Join-Path $OutputDir "FileShift-Sparse.msix"
    if (Test-Path $msixOut) { Remove-Item $msixOut -Force }
    & $makeAppx pack /d $sparseStage /nv /p $msixOut

    if ($CertThumbprint) {
        $signTool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($signTool) {
            & $signTool.FullName sign /fd SHA256 /sha1 $CertThumbprint $msixOut
        }
    }
} else {
    Write-Warning "makeappx.exe (Windows 11 SDK) no encontrado en PATH; se usará registro directo de AppxManifest.xml en modo desarrollo."
}

Write-Host "[6/6] Compilando instalador final con Inno Setup 6 (ISCC.exe)..." -ForegroundColor Yellow
$iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if (-not $iscc) {
    $defaultIscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (Test-Path $defaultIscc) { $iscc = $defaultIscc }
}

if ($iscc) {
    & $iscc "$PSScriptRoot\FileShift.iss"
    Write-Host "Instalador generado en: $InstallerOutputDir" -ForegroundColor Green
} else {
    Write-Warning "ISCC.exe (Inno Setup 6) no detectado en PATH. Artefactos listos en: $OutputDir"
}

Write-Host "Release completado en: $OutputDir" -ForegroundColor Green
