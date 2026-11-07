param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\..\dist\win-x64"
)

$ErrorActionPreference = "Stop"
Write-Host "=== FileShift Windows 11 Release Builder ===" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "[1/4] Compilando Converter.Cli y Converter.App (.NET 10)..." -ForegroundColor Yellow
dotnet publish "$PSScriptRoot\..\src\Converter.Cli\Converter.Cli.csproj" -c $Configuration -r win-x64 --self-contained false -o $OutputDir
dotnet publish "$PSScriptRoot\..\src\Converter.App\Converter.App.csproj" -c $Configuration -r win-x64 --self-contained false -o $OutputDir

Write-Host "[2/4] Compilando extensión nativa COM C++17 (FileShift.Shell.dll)..." -ForegroundColor Yellow
$shellBuildDir = "$PSScriptRoot\..\src\Converter.Shell\build"
New-Item -ItemType Directory -Force -Path $shellBuildDir | Out-Null
cmake -S "$PSScriptRoot\..\src\Converter.Shell" -B $shellBuildDir -A x64
cmake --build $shellBuildDir --config $Configuration
Copy-Item "$shellBuildDir\$Configuration\FileShift.Shell.dll" -Destination $OutputDir -Force

Write-Host "[3/4] Copiando manifiesto MSIX Sparse Package y avisos de licencia..." -ForegroundColor Yellow
Copy-Item "$PSScriptRoot\AppxManifest.xml" -Destination $OutputDir -Force
Copy-Item "$PSScriptRoot\..\THIRD_PARTY_NOTICES.md" -Destination $OutputDir -Force

Write-Host "[4/4] Ejecutando pruebas automatizadas (Converter.Tests)..." -ForegroundColor Yellow
dotnet test "$PSScriptRoot\..\tests\Converter.Tests\Converter.Tests.csproj" -c $Configuration

Write-Host "Compilación finalizada en: $OutputDir" -ForegroundColor Green
