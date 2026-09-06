[CmdletBinding()]
param(
    [ValidatePattern('^\d{4}\.\d{2}\.\d{2}$')]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$toolsDirectory = Join-Path $PSScriptRoot "Tools"
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null

$ytDlpTarget = Join-Path $toolsDirectory "yt-dlp.exe"
$ytDlpDownload = Join-Path ([System.IO.Path]::GetTempPath()) "chzzk-yt-dlp-$([guid]::NewGuid().ToString('N')).exe"
$licensesDirectory = Join-Path $PSScriptRoot "Licenses"
$thirdPartyLicenseTarget = Join-Path $licensesDirectory "yt-dlp-THIRD_PARTY_LICENSES.txt"
$thirdPartyLicenseDownload = Join-Path ([System.IO.Path]::GetTempPath()) "chzzk-yt-dlp-licenses-$([guid]::NewGuid().ToString('N')).txt"

function Get-RemoteFile {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $curlCommand = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curlCommand) {
        & $curlCommand.Source --fail --location --retry 3 --connect-timeout 20 --output $Destination $Uri
        if ($LASTEXITCODE -ne 0) {
            throw "다운로드 실패: $Uri"
        }
        return
    }

    Invoke-WebRequest -Uri $Uri -OutFile $Destination -TimeoutSec 180
}

try {
    $headers = @{ "User-Agent" = "ChzzkDownloader-ToolUpdater" }
    $releaseUri = if ($Version) { "https://api.github.com/repos/yt-dlp/yt-dlp/releases/tags/$Version" } else { "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest" }
    $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers -TimeoutSec 60
    $asset = @($release.assets | Where-Object { $_.name -eq "yt-dlp.exe" })
    if ($asset.Count -ne 1 -or [string]::IsNullOrWhiteSpace($asset[0].digest)) {
        throw "공식 yt-dlp 릴리스에서 실행 파일 해시를 확인하지 못했습니다."
    }

    $version = [string]$release.tag_name
    $expectedHash = ([string]$asset[0].digest -replace '^sha256:', '').ToUpperInvariant()
    Write-Host "yt-dlp $version 공식 실행 파일을 받는 중..."
    Get-RemoteFile -Uri ([string]$asset[0].browser_download_url) -Destination $ytDlpDownload
    $actualHash = (Get-FileHash -LiteralPath $ytDlpDownload -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "yt-dlp SHA-256이 공식 릴리스 값과 일치하지 않습니다."
    }

    Get-RemoteFile -Uri "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$version/THIRD_PARTY_LICENSES.txt" -Destination $thirdPartyLicenseDownload
    New-Item -ItemType Directory -Path $licensesDirectory -Force | Out-Null
    Copy-Item -LiteralPath $ytDlpDownload -Destination $ytDlpTarget -Force
    Copy-Item -LiteralPath $thirdPartyLicenseDownload -Destination $thirdPartyLicenseTarget -Force

    & $ytDlpTarget --version
    Write-Host "다운로드 엔진과 제3자 라이선스 업데이트 완료" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $ytDlpDownload) {
        Remove-Item -LiteralPath $ytDlpDownload -Force
    }
    if (Test-Path -LiteralPath $thirdPartyLicenseDownload) {
        Remove-Item -LiteralPath $thirdPartyLicenseDownload -Force
    }
}
