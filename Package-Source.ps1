#requires -Version 7.0
[CmdletBinding()]
param([string]$DestinationDirectory = [Environment]::GetFolderPath('Desktop'))

$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
[xml]$project = Get-Content -LiteralPath (Join-Path $sourceRoot 'ChzzkDownloader\ChzzkDownloader.csproj')
$version = @($project.Project.PropertyGroup.Version) | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
$packageName = "StreamNest-$version-GitHub-source"
$destinationRoot = [IO.Path]::GetFullPath($DestinationDirectory)
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
$zipPath = Join-Path $destinationRoot "$packageName.zip"
if (Test-Path -LiteralPath $zipPath) { throw "Refusing to replace an existing source archive: $zipPath" }

# Explicit project roots/extensions: never sweep the workspace, diagnostics,
# old releases, profiles, settings or downloaded media into a public archive.
$files = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($name in @('README.md', 'CHANGELOG.md', 'LICENSE', '.gitignore', 'global.json', 'requirements-dev.txt', 'Setup-Tools.ps1', 'Package-Source.ps1')) {
    $files.Add((Get-Item -LiteralPath (Join-Path $sourceRoot $name)))
}
$allowedExtensions = @('.cs', '.xaml', '.csproj', '.ps1', '.py', '.md', '.txt', '.ico', '.png')
foreach ($folder in @('ChzzkDownloader', 'ChzzkDownloader.Tests', 'ChzzkDownloader.IntegrationTests', 'PluginTests')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $sourceRoot $folder) -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName).Replace('\', '/')
        if ($relative -match '/(bin|obj|TestResults|Archive|downloads|output|parts|partials|__pycache__|\.vs|\.venv|Licenses/Source)/') { continue }
        if ($file.Name -notin @('LICENSE', '.gitignore') -and $file.Extension -notin $allowedExtensions) { continue }
        if ($relative -match '/Tools/' -and $relative -notmatch '/Tools/yt-dlp-plugins/.+\.py$') { continue }
        if ($file.Name -match '^(settings|cookies|request-|\.env)' -or $file.Length -gt 50MB) { throw "Unexpected source file: $relative" }
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Links are not allowed: $relative" }
        $files.Add($file)
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$hashes = [Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName).Replace('\', '/')
        if ($relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)) { throw 'Source path escaped the repository root.' }
        $hashes.Add($relative, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName,
            "$packageName/$relative", [IO.Compression.CompressionLevel]::Optimal)
    }
    $manifest = $archive.CreateEntry("$packageName/SOURCE-FILES.sha256")
    $writer = [IO.StreamWriter]::new($manifest.Open(), [Text.UTF8Encoding]::new($false))
    try { foreach ($pair in $hashes.GetEnumerator()) { $writer.WriteLine("$($pair.Value)  $($pair.Key)") } }
    finally { $writer.Dispose() }
}
finally { $archive.Dispose() }

# Reopen and hash every compressed entry, not just the ZIP filename.
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($archive.Entries.Count -ne $hashes.Count + 1) { throw 'Source archive entry count mismatch.' }
    foreach ($entry in $archive.Entries) {
        $relative = $entry.FullName.Substring($packageName.Length + 1)
        if ($relative -eq 'SOURCE-FILES.sha256') { continue }
        $stream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
        if ($actual -ne $hashes[$relative]) { throw "Source archive hash mismatch: $relative" }
    }
}
finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zipPath.sha256" -Value "$zipHash  $packageName.zip" -Encoding ascii
[pscustomobject]@{ Zip = $zipPath; SourceFiles = $hashes.Count; Bytes = (Get-Item -LiteralPath $zipPath).Length; SHA256 = $zipHash }
