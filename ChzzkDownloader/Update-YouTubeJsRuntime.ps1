[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$nodeVersion = "24.18.0"
$archiveName = "node-v$nodeVersion-win-x64.zip"
$expectedSha256 = "0ae68406b42d7725661da979b1403ec9926da205c6770827f33aac9d8f26e821"
$downloadUri = "https://nodejs.org/dist/v$nodeVersion/$archiveName"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "streamnest-node-$([guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $temporaryRoot $archiveName
$extractPath = Join-Path $temporaryRoot "extract"
$toolsDirectory = Join-Path $PSScriptRoot "Tools"
$licensesDirectory = Join-Path $PSScriptRoot "Licenses"

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri $downloadUri -OutFile $archivePath -TimeoutSec 180

    $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        throw "Node.js archive SHA-256 did not match the pinned official release checksum."
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath
    $distributionRoot = Join-Path $extractPath "node-v$nodeVersion-win-x64"
    $nodeSource = Join-Path $distributionRoot "node.exe"
    $licenseSource = Join-Path $distributionRoot "LICENSE"
    if (!(Test-Path -LiteralPath $nodeSource) -or !(Test-Path -LiteralPath $licenseSource)) {
        throw "The verified Node.js distribution is missing node.exe or LICENSE."
    }

    New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $licensesDirectory -Force | Out-Null
    Copy-Item -LiteralPath $nodeSource -Destination (Join-Path $toolsDirectory "node.exe") -Force
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $licensesDirectory "node-LICENSE.txt") -Force

    & (Join-Path $toolsDirectory "node.exe") --version
    Write-Host "Pinned YouTube JavaScript runtime installed." -ForegroundColor Green
}
finally {
    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
