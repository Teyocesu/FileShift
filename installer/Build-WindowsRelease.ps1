param(
    [switch]$Development,
    [string]$CertThumbprint,
    [string]$TimestampUrl
)
$ErrorActionPreference = 'Stop'
if ($Development -and $CertThumbprint) { throw 'Elegí un solo modo de firma.' }
if (!$Development -and (!$CertThumbprint -or !$TimestampUrl)) {
    throw 'La distribución requiere certificado de firma y timestamp RFC 3161.'
}

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command terminó con código $LASTEXITCODE" }
}

$root = (Resolve-Path "$PSScriptRoot/..").Path
$output = Join-Path $root 'dist/win-x64'
$installer = Join-Path $root 'dist/installer'
$stage = Join-Path $root 'dist/identity'
$verified = Join-Path $root 'dist/identity-verified'
$shellBuild = Join-Path $root 'dist/shell'
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}/Windows Kits/10/bin/*/x64/makeappx.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
if (!$sdk) { throw 'Falta MakeAppx en el SDK de Windows.' }
$signTool = Join-Path $sdk.DirectoryName 'signtool.exe'
$mt = Join-Path $sdk.DirectoryName 'mt.exe'
$iscc = (Get-Command ISCC.exe).Source

foreach ($path in @($output, $installer, $stage, $verified)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
New-Item -ItemType Directory -Path $output, $installer, $stage | Out-Null
Invoke-Checked dotnet @('restore', "$root/FileShift.sln")
Invoke-Checked dotnet @('build', "$root/FileShift.sln", '-c', 'Release', '--no-restore')
Invoke-Checked dotnet @('test', "$root/tests/Converter.Tests", '-c', 'Release', '--no-build', '--logger', 'trx', '--results-directory', "$root/dist/test-results")
foreach ($project in @('Converter.Cli', 'Converter.App')) {
    Invoke-Checked dotnet @('publish', "$root/src/$project/$project.csproj", '-c', 'Release',
        '-r', 'win-x64', '--self-contained', 'true', '-o', $output)
}
Invoke-Checked cmake @('-S', "$root/src/Converter.Shell", '-B', $shellBuild, '-A', 'x64')
Invoke-Checked cmake @('--build', $shellBuild, '--config', 'Release')
Copy-Item "$shellBuild/Release/FileShift.Shell.dll" $output
Copy-Item "$PSScriptRoot/Assets" $output -Recurse
Copy-Item "$root/LICENSE", "$root/THIRD_PARTY_NOTICES.md", "$PSScriptRoot/Register-SparsePackage.ps1", "$PSScriptRoot/Unregister-SparsePackage.ps1" $output

$cert = if ($Development) {
    $parameters = @{
        Type = 'CodeSigningCert'; Subject = 'CN=FileShift Development'; CertStoreLocation = 'Cert:/CurrentUser/My'
        KeyExportPolicy = 'NonExportable'; NotAfter = (Get-Date).AddDays(30)
    }
    New-SelfSignedCertificate @parameters
} else {
    Get-Item "Cert:/CurrentUser/My/$CertThumbprint"
}

try {
    if ($Development) {
        Export-Certificate -Cert $cert -FilePath "$installer/FileShift-Development.cer" | Out-Null
        Import-Certificate -FilePath "$installer/FileShift-Development.cer" -CertStoreLocation Cert:/CurrentUser/TrustedPeople | Out-Null
    }
    [xml]$manifest = Get-Content "$PSScriptRoot/AppxManifest.xml" -Raw
    $manifest.Package.Identity.Publisher = $cert.Subject
    $manifest.Save("$stage/AppxManifest.xml")
    Copy-Item "$stage/AppxManifest.xml" $output
    Copy-Item "$PSScriptRoot/Assets" $stage -Recurse
    $fusion = [xml]@'
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="FileShift.App"/>
  <msix xmlns="urn:schemas-microsoft-com:msix.v1" publisher="" packageName="" applicationId=""/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
'@
    $fusion.assembly.msix.publisher = $cert.Subject
    $fusion.assembly.msix.packageName = $manifest.Package.Identity.Name
    $fusion.assembly.msix.applicationId = $manifest.Package.Applications.Application.Id
    $fusionPath = Join-Path $root 'dist/FileShift.App.manifest'
    $fusion.Save($fusionPath)
    Invoke-Checked $mt @('-nologo', '-manifest', $fusionPath, "-outputresource:$output/FileShift.App.exe;#1")
    $msix = "$output/FileShift-Sparse.msix"
    Invoke-Checked $sdk.FullName @('pack', '/d', $stage, '/nv', '/p', $msix)
    $signature = @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint)
    if ($TimestampUrl) { $signature += @('/tr', $TimestampUrl, '/td', 'SHA256') }
    Invoke-Checked $signTool ($signature + $msix)
    if (!$Development) { Invoke-Checked $signTool @('verify', '/pa', $msix) }
    Invoke-Checked $sdk.FullName @('unpack', '/p', $msix, '/d', $verified)
    if (!(Test-Path "$verified/AppxSignature.p7x")) { throw 'El paquete MSIX no contiene firma.' }
    if ((Get-FileHash "$stage/AppxManifest.xml").Hash -ne (Get-FileHash "$verified/AppxManifest.xml").Hash) {
        throw 'El manifiesto extraído no coincide.'
    }
    $previousConfig = $env:FILESHIFT_CONFIG_DIR
    $env:FILESHIFT_CONFIG_DIR = Join-Path $root 'dist/smoke-settings'
    Invoke-Checked "$output/fileshift.exe" @('doctor')
    $smoke = Join-Path $root 'dist/smoke'
    New-Item -ItemType Directory -Path $smoke -Force | Out-Null
    $png = Join-Path $smoke 'prueba ñ shell.png'
    $second = Join-Path $smoke 'segundo shell.png'
    Invoke-Checked magick.exe @('-size', '24x24', 'xc:red', $png)
    Copy-Item -LiteralPath $png -Destination $second -Force
    $hash = (Get-FileHash -LiteralPath $png).Hash
    Invoke-Checked "$output/fileshift.exe" @('convert', '--to', 'webp', '--auto-rename', '--', $png, $second)
    foreach ($inputFile in @($png, $second)) {
        if (!(Test-Path -LiteralPath ([IO.Path]::ChangeExtension($inputFile, '.webp'))) -or
            (Get-FileHash -LiteralPath $inputFile).Hash -ne $hash) { throw 'Falló el smoke test de la CLI.' }
    }
    $env:FILESHIFT_CONFIG_DIR = $previousConfig
    $name = if ($Development) { 'FileShift-Setup-Development-win-x64' } else { 'FileShift-Setup-win-x64' }
    Invoke-Checked $iscc @("/O$installer", "/F$name", "$PSScriptRoot/FileShift.iss")
    $setup = "$installer/$name.exe"
    if (!$Development) {
        Invoke-Checked $signTool ($signature + $setup)
        Invoke-Checked $signTool @('verify', '/pa', $setup)
    }
    foreach ($file in @($setup, $msix, "$output/FileShift.App.exe", "$output/fileshift.exe", "$output/FileShift.Shell.dll")) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Artefacto ausente o vacío: $file"
        }
    }
    Copy-Item $msix, "$PSScriptRoot/Install-WindowsDependencies.ps1" $installer
    Get-ChildItem $installer -File | Get-FileHash |
        ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } |
        Set-Content "$installer/SHA256SUMS.txt"
}
finally {
    if ($Development) {
        Remove-Item "Cert:/CurrentUser/TrustedPeople/$($cert.Thumbprint)" -ErrorAction SilentlyContinue
        Remove-Item "Cert:/CurrentUser/My/$($cert.Thumbprint)"
    }
}
