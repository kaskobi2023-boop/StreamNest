[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
# Restore only project-local executables and notices. No system installation,
# browser profile, login session or security setting is changed.
& (Join-Path $PSScriptRoot 'ChzzkDownloader\Update-Tools.ps1') -Version '2026.08.19'
& (Join-Path $PSScriptRoot 'ChzzkDownloader\Update-FFmpeg.ps1')
& (Join-Path $PSScriptRoot 'ChzzkDownloader\Update-YouTubeJsRuntime.ps1')
Write-Host 'Verified project-local tool dependencies restored.' -ForegroundColor Green
