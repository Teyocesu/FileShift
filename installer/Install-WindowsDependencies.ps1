param([switch]$BuildTools)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$temp = Join-Path ([IO.Path]::GetTempPath()) "FileShift-$([guid]::NewGuid())"
$engines = Join-Path $env:LOCALAPPDATA 'FileShift/engines'

function Get-VerifiedDownload($Url, $Hash) {
    $path = Join-Path $temp ([IO.Path]::GetFileName($Url))
    Write-Host "Descargando $Url"
    Invoke-WebRequest -Uri $Url -OutFile $path -UseBasicParsing -TimeoutSec 180
    if ((Get-FileHash -LiteralPath $path).Hash -ne $Hash) { throw "SHA-256 incorrecto: $Url" }
    return $path
}

function Install-Program($Path, $Arguments) {
    Write-Host "Instalando $([IO.Path]::GetFileName($Path))"
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -PassThru
    if (!$process.WaitForExit(180000)) {
        $process.Kill()
        throw "Tiempo de instalación agotado: $Path"
    }
    if ($process.ExitCode -ne 0) { throw "$Path terminó con código $($process.ExitCode)" }
}

New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $archive = Get-VerifiedDownload 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip' '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
    Expand-Archive -LiteralPath $archive -DestinationPath $temp
    $ffmpeg = Join-Path $engines 'ffmpeg'
    New-Item -ItemType Directory -Force -Path $ffmpeg | Out-Null
    Copy-Item "$temp/ffmpeg-9.0.2-essentials_build/bin/*.exe", "$temp/ffmpeg-9.0.2-essentials_build/LICENSE" $ffmpeg -Force
    $magick = Join-Path $engines 'imagemagick'
    $gs = Join-Path $engines 'ghostscript'
    $gsSetup = Get-VerifiedDownload 'https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/download/gs10080/gs10080w64.exe' '52a91b8bf09298788d7a57b9206127026c23eacd75405f0a131e26dc381dce50'
    if ($BuildTools) {
        $sevenZip = Join-Path $env:ProgramFiles '7-Zip/7z.exe'
        $magickArchive = Get-VerifiedDownload 'https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-35/ImageMagick-7.1.2-35-portable-Q16-HDRI-x64.7z' '34db4364da26c88d6b8cc5bbc1fff22b85d00d3b399c82fe85ee4790efacbbf1'
        & $sevenZip x $magickArchive "-o$magick" -y
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo extraer ImageMagick.' }
        & $sevenZip x $gsSetup "-o$gs" -y
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo extraer Ghostscript.' }
    } else {
        $magickSetup = Get-VerifiedDownload 'https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-35/ImageMagick-7.1.2-35-Q16-HDRI-x64-dll.exe' '32cc21fe23f7d7b7d9133905a2dc5aac94f574a07740911d948eb88b09a0b432'
        Install-Program $magickSetup ('/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="{0}"' -f $magick)
        Write-Host "Completá el asistente de Ghostscript usando la carpeta $gs"
        Install-Program $gsSetup "/D=$gs"
    }

    foreach ($binary in @("$ffmpeg/ffmpeg.exe", "$ffmpeg/ffprobe.exe", "$magick/magick.exe", "$gs/bin/gswin64c.exe")) {
        if (!(Test-Path -LiteralPath $binary -PathType Leaf)) { throw "Falta $binary" }
    }
    if ($BuildTools) {
        $inno = Join-Path $engines 'inno'
        $setup = Get-VerifiedDownload 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_0/innosetup-6.7.0.exe' 'f45c7d68d1e660cf13877ec36738a5179ce72a33414f9959d35e99b68c52a697'
        Install-Program $setup ('/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="{0}"' -f $inno)
        if (!(Test-Path "$inno/ISCC.exe")) { throw 'Falta ISCC.exe' }
        if ($env:GITHUB_PATH) { @($ffmpeg, $magick, "$gs/bin", $inno) | Out-File $env:GITHUB_PATH -Encoding utf8 -Append }
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}
