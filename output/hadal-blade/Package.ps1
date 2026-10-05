param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$files = [Collections.Generic.List[string]]::new()
foreach ($folder in @('HadalBlade.rsi', 'HadalBladeInhands.rsi', 'HadalBladeBelt.rsi', 'HadalBladeEffects.rsi')) {
    Get-ChildItem -LiteralPath "$RepoRoot/Resources/Textures/_Starlight/Admeme/Scarlet/$folder" -File | ForEach-Object { $files.Add($_.FullName) }
}
foreach ($relative in @(
    'Resources/Prototypes/_Starlight/Admeme/scarlet/hadal_blade.yml',
    'Resources/Locale/en-US/_Starlight/weapons/hadal-blade.ftl',
    'Content.Shared/_Starlight/Weapons/Melee/HadalBladeComponent.cs',
    'Content.Shared/_Starlight/Weapons/Melee/HadalBladeSystem.cs'
)) { $files.Add((Join-Path $RepoRoot $relative)) }
Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in '.md','.png','.html','.ps1','.yml','.txt','.cs' } | ForEach-Object { $files.Add($_.FullName) }
Get-ChildItem -LiteralPath "$PSScriptRoot/source" -File | ForEach-Object { $files.Add($_.FullName) }

$zipPath = Join-Path $PSScriptRoot 'hadal-blade-sprites.zip'
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($RepoRoot, $file).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $stream.Dispose() }
Write-Output "Packaged $($files.Count) files into $zipPath"
