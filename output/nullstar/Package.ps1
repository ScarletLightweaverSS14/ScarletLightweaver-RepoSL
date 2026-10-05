param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
$files=[Collections.Generic.List[string]]::new()
foreach($folder in @('Nullstar.rsi','NullstarInhands.rsi','NullstarBelt.rsi','NullstarUnbound.rsi','NullstarRift.rsi','NullstarCleave.rsi')){
    Get-ChildItem -LiteralPath "$RepoRoot/Resources/Textures/_Starlight/Admeme/Scarlet/$folder" -File | ForEach-Object {$files.Add($_.FullName)}
}
$files.Add((Join-Path $RepoRoot 'Resources/Prototypes/_Starlight/Admeme/scarlet/nullstar.yml'))
Get-ChildItem -LiteralPath "$RepoRoot/Resources/Audio/_Starlight/Weapons/Nullstar" -File | ForEach-Object {$files.Add($_.FullName)}
Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object {$_.Extension -in '.md','.png','.ps1','.txt','.html','.cs','.json'} | ForEach-Object {$files.Add($_.FullName)}
Get-ChildItem -LiteralPath "$PSScriptRoot/source" -File -Recurse | ForEach-Object {$files.Add($_.FullName)}
foreach($file in @('Content.Shared/_Starlight/Weapons/Melee/NullstarUnboundComponent.cs','Content.Shared/_Starlight/Weapons/Melee/NullstarUnboundSystem.cs','Content.Client/_Starlight/Weapons/NullstarUnboundVisualSystem.cs','Content.Shared/_Starlight/Weapons/Melee/NullstarRiftCleaveComponent.cs','Content.Shared/_Starlight/Weapons/Melee/NullstarRiftCleaveSystem.cs','Content.Shared/_Starlight/Weapons/Melee/NullstarRiftCleaveSystem.Dash.cs','Content.Client/_Starlight/Weapons/NullstarRiftVisualSystem.cs','Resources/Locale/en-US/_Starlight/weapons/nullstar.ftl')){$files.Add((Join-Path $RepoRoot $file))}
$stream=[IO.File]::Open("$PSScriptRoot/nullstar-stage4.zip",[IO.FileMode]::Create)
$archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($file in $files){
        $relative=[IO.Path]::GetRelativePath($RepoRoot,$file).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}finally{$archive.Dispose();$stream.Dispose()}
Write-Output "Packaged $($files.Count) files into nullstar-stage4.zip"




