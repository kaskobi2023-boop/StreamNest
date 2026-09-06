[CmdletBinding()]
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectDirectory = $PSScriptRoot
$workspaceDirectory = Split-Path -Parent $projectDirectory
$distDirectory = Join-Path $workspaceDirectory "dist"
$projectPath = Join-Path $projectDirectory "ChzzkDownloader.csproj"
[xml]$projectFile = Get-Content -LiteralPath $projectPath
$version = @($projectFile.Project.PropertyGroup.Version) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "프로젝트 버전을 확인하지 못했습니다: $projectPath"
}
$packageName = "StreamNestDownloader-$version-$Runtime"
$outputDirectory = Join-Path $distDirectory $packageName
$stagingDirectory = Join-Path $distDirectory ".staging-$packageName"
$archivePath = Join-Path $distDirectory "$packageName.zip"
$hashPath = "$archivePath.sha256"
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) {
    $dotnetCommand.Source
} else {
    Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw ".NET SDK 10을 찾을 수 없습니다. https://dotnet.microsoft.com/download 에서 설치해주세요."
}

function Assert-PathInsideDist {
    param([Parameter(Mandatory = $true)][string]$Path)

    $distRoot = [System.IO.Path]::GetFullPath($distDirectory).TrimEnd('\') + '\'
    $candidate = [System.IO.Path]::GetFullPath($Path)
    if (-not $candidate.StartsWith($distRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "배포 폴더 밖의 경로는 변경할 수 없습니다: $candidate"
    }
}

function Test-PackagedTool {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $Path @Arguments *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 실행 점검에 실패했습니다. 종료 코드: $LASTEXITCODE"
    }
}

New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null
Assert-PathInsideDist -Path $stagingDirectory
Assert-PathInsideDist -Path $outputDirectory

if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}

try {
    & $dotnetPath publish $projectPath `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $stagingDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "배포 빌드에 실패했습니다."
    }

    Get-ChildItem -LiteralPath $stagingDirectory -Filter "Microsoft.Web.WebView2.*.xml" -File |
        Remove-Item -Force

    Copy-Item -LiteralPath (Join-Path $projectDirectory "README.md") -Destination $stagingDirectory -Force

    $legalSections = @(
        @{ Title = "StreamNest Downloader License"; Path = (Join-Path $projectDirectory "LICENSE") },
        @{ Title = "Third-Party Notices"; Path = (Join-Path $projectDirectory "THIRD_PARTY_NOTICES.md") },
        @{ Title = "FFmpeg GPL License"; Path = (Join-Path $projectDirectory "Licenses\FFmpeg-GPL-3.0.txt") },
        @{ Title = "FFmpeg Corresponding Source Information"; Path = (Join-Path $projectDirectory "Licenses\FFmpeg-SOURCE.md") },
        @{ Title = "Node.js License"; Path = (Join-Path $projectDirectory "Licenses\node-LICENSE.txt") },
        @{ Title = "yt-dlp Third-Party Licenses"; Path = (Join-Path $projectDirectory "Licenses\yt-dlp-THIRD_PARTY_LICENSES.txt") }
    )
    $legalBuilder = [System.Text.StringBuilder]::new()
    foreach ($section in $legalSections) {
        if (-not (Test-Path -LiteralPath $section.Path -PathType Leaf)) {
            throw "법적 고지 원본이 누락되었습니다: $($section.Path)"
        }
        [void]$legalBuilder.AppendLine(("=" * 80))
        [void]$legalBuilder.AppendLine($section.Title)
        [void]$legalBuilder.AppendLine(("=" * 80))
        [void]$legalBuilder.AppendLine()
        [void]$legalBuilder.AppendLine([System.IO.File]::ReadAllText($section.Path))
        [void]$legalBuilder.AppendLine()
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingDirectory "LEGAL_NOTICES.txt"),
        $legalBuilder.ToString(),
        [System.Text.UTF8Encoding]::new($false))

    $sourceDirectory = Join-Path $stagingDirectory "Licenses\Source"
    New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $projectDirectory "Licenses\Source\ffmpeg-8.1.2.tar.xz") `
        -Destination $sourceDirectory `
        -Force

    $archiveDirectory = Join-Path $stagingDirectory "Archive"
    New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null

    $requiredFiles = @(
        "StreamNestDownloader.exe",
        "README.md",
        "LEGAL_NOTICES.txt",
        "Licenses\Source\ffmpeg-8.1.2.tar.xz",
        "Tools\yt-dlp.exe",
        "Tools\ffmpeg.exe",
        "Tools\ffprobe.exe",
        "Tools\node.exe",
        "Tools\yt-dlp-plugins\streamnest\yt_dlp_plugins\extractor\streamnest_chzzk.py",
        "Tools\yt-dlp-plugins\streamnest\yt_dlp_plugins\extractor\streamnest_packed.py"
    )
    $requiredDirectories = @("Archive")
    foreach ($relativePath in $requiredFiles) {
        $packagedPath = Join-Path $stagingDirectory $relativePath
        if (-not (Test-Path -LiteralPath $packagedPath -PathType Leaf)) {
            throw "배포 파일이 누락되었습니다: $relativePath"
        }
    }
    foreach ($relativePath in $requiredDirectories) {
        $packagedPath = Join-Path $stagingDirectory $relativePath
        if (-not (Test-Path -LiteralPath $packagedPath -PathType Container)) {
            throw "배포 폴더가 누락되었습니다: $relativePath"
        }
    }

    $debugSymbols = @(Get-ChildItem -LiteralPath $stagingDirectory -Filter "*.pdb" -File -Recurse)
    if ($debugSymbols.Count -gt 0) {
        throw "공개 배포본에 디버그 심볼이 포함되어 있습니다: $($debugSymbols[0].FullName)"
    }

    $packagedFiles = @(Get-ChildItem -LiteralPath $stagingDirectory -File -Recurse)
    if ($packagedFiles.Count -ne $requiredFiles.Count) {
        $unexpectedFiles = $packagedFiles |
            Where-Object { $requiredFiles -notcontains $_.FullName.Substring($stagingDirectory.Length + 1) } |
            ForEach-Object { $_.FullName.Substring($stagingDirectory.Length + 1) }
        throw "배포 파일 수가 예상과 다릅니다. 예상 $($requiredFiles.Count)개, 실제 $($packagedFiles.Count)개. 추가 파일: $($unexpectedFiles -join ', ')"
    }

    Test-PackagedTool -Name "yt-dlp" -Path (Join-Path $stagingDirectory "Tools\yt-dlp.exe") -Arguments @("--version")
    Test-PackagedTool -Name "FFmpeg" -Path (Join-Path $stagingDirectory "Tools\ffmpeg.exe") -Arguments @("-version")
    Test-PackagedTool -Name "ffprobe" -Path (Join-Path $stagingDirectory "Tools\ffprobe.exe") -Arguments @("-version")

    if (Test-Path -LiteralPath $outputDirectory) {
        Remove-Item -LiteralPath $outputDirectory -Recurse -Force
    }
    Move-Item -LiteralPath $stagingDirectory -Destination $outputDirectory

    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    Compress-Archive -Path (Join-Path $outputDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::Open(
        $archivePath,
        [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach ($relativePath in $requiredDirectories) {
            $directoryEntry = $relativePath.TrimEnd('\') + '/'
            if ($null -eq $archive.GetEntry($directoryEntry)) {
                [void]$archive.CreateEntry($directoryEntry)
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $archiveEntries = $archive.Entries.FullName.Replace('/', '\')
        foreach ($relativePath in $requiredFiles) {
            if ($archiveEntries -notcontains $relativePath) {
                throw "ZIP 검증 실패: $relativePath 파일이 없습니다."
            }
        }
        foreach ($relativePath in $requiredDirectories) {
            $directoryEntry = $relativePath.TrimEnd('\') + '\'
            if ($archiveEntries -notcontains $directoryEntry) {
                throw "ZIP 검증 실패: $relativePath 폴더가 없습니다."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $hashPath -Value "$hash  $(Split-Path -Leaf $archivePath)" -Encoding ascii
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}

Write-Host "배포 폴더: $outputDirectory" -ForegroundColor Green
Write-Host "ZIP: $archivePath" -ForegroundColor Green
Write-Host "SHA-256: $hashPath" -ForegroundColor Green
