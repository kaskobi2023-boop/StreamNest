[CmdletBinding()]
param(
    [string]$Version = "8.1.2",
    [string]$ArchiveSha256 = "db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec"
)

$ErrorActionPreference = "Stop"
$toolsDirectory = Join-Path $PSScriptRoot "Tools"
$sourceDirectory = Join-Path $PSScriptRoot "Licenses\Source"
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "ChzzkDownloader-FfmpegUpdate"
$archivePath = Join-Path $temporaryDirectory "ffmpeg-$Version-essentials_build.zip"
$newFfmpegPath = Join-Path $temporaryDirectory "ffmpeg.exe"
$newFfprobePath = Join-Path $temporaryDirectory "ffprobe.exe"
$sourceDownloadPath = Join-Path $temporaryDirectory "ffmpeg-$Version.tar.xz"
$signatureDownloadPath = "$sourceDownloadPath.asc"
$sourceArchivePath = Join-Path $sourceDirectory "ffmpeg-$Version.tar.xz"
$sourceSignaturePath = "$sourceArchivePath.asc"

function Get-RemoteFile {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$Destination,
        [int]$TimeoutSec = 600
    )

    Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing -TimeoutSec $TimeoutSec
}

if (Test-Path -LiteralPath $temporaryDirectory) {
    throw "다른 FFmpeg 업데이트 작업의 임시 폴더가 남아 있습니다: $temporaryDirectory"
}

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null

try {
    $archiveUri = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$Version-essentials_build.zip"
    Write-Host "Gyan FFmpeg $Version 공식 빌드를 받는 중..."
    Get-RemoteFile -Uri $archiveUri -Destination $archivePath

    $actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not $actualArchiveHash.Equals($ArchiveSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "FFmpeg 압축본 SHA-256이 공식 값과 일치하지 않습니다. 실제 값: $actualArchiveHash"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $ffmpegEntries = @($archive.Entries | Where-Object { $_.FullName -match '/bin/ffmpeg\.exe$' })
        $ffprobeEntries = @($archive.Entries | Where-Object { $_.FullName -match '/bin/ffprobe\.exe$' })
        if ($ffmpegEntries.Count -ne 1 -or $ffprobeEntries.Count -ne 1) {
            throw "공식 압축본에서 ffmpeg.exe와 ffprobe.exe를 정확히 찾지 못했습니다."
        }

        [IO.Compression.ZipFileExtensions]::ExtractToFile($ffmpegEntries[0], $newFfmpegPath, $true)
        [IO.Compression.ZipFileExtensions]::ExtractToFile($ffprobeEntries[0], $newFfprobePath, $true)
    }
    finally {
        $archive.Dispose()
    }

    $reportedVersion = (& $newFfmpegPath -version 2>&1 | Select-Object -First 1)
    if ($reportedVersion -notmatch "^ffmpeg version $([regex]::Escape($Version))-" ) {
        throw "압축본의 FFmpeg 버전이 예상과 다릅니다: $reportedVersion"
    }

    Get-RemoteFile -Uri "https://ffmpeg.org/releases/ffmpeg-$Version.tar.xz" -Destination $sourceDownloadPath
    Get-RemoteFile -Uri "https://ffmpeg.org/releases/ffmpeg-$Version.tar.xz.asc" -Destination $signatureDownloadPath -TimeoutSec 120

    Copy-Item -LiteralPath $newFfmpegPath -Destination (Join-Path $toolsDirectory "ffmpeg.exe") -Force
    Copy-Item -LiteralPath $newFfprobePath -Destination (Join-Path $toolsDirectory "ffprobe.exe") -Force
    Copy-Item -LiteralPath $sourceDownloadPath -Destination $sourceArchivePath -Force
    Copy-Item -LiteralPath $signatureDownloadPath -Destination $sourceSignaturePath -Force

    Write-Host $reportedVersion -ForegroundColor Green
    Write-Host "FFmpeg 실행 파일과 대응 소스를 업데이트했습니다." -ForegroundColor Green
}
finally {
    foreach ($path in @($archivePath, $newFfmpegPath, $newFfprobePath, $sourceDownloadPath, $signatureDownloadPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Force
    }
}
