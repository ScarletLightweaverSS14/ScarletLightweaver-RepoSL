param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$assets=[ordered]@{}
foreach($name in @('ring','debris','flare','rift')){
    $file="$RepoRoot/Resources/Textures/_Starlight/Admeme/Scarlet/NullstarUnbound.rsi/$name.png"
    $assets[$name]='data:image/png;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes($file))
}
$assets.audio='data:audio/wav;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes("$RepoRoot/Resources/Audio/_Starlight/Weapons/Nullstar/unbound.wav"))
(Get-Content "$PSScriptRoot/unbound-preview.template.html" -Raw).Replace('__ASSETS__',($assets|ConvertTo-Json -Compress)) | Set-Content "$PSScriptRoot/unbound-preview.html" -Encoding utf8
