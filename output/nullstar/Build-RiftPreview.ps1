param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$assets=[ordered]@{}
foreach($name in @('telegraph','slash','rift')){
    $assets[$name]='data:image/png;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes("$RepoRoot/Resources/Textures/_Starlight/Admeme/Scarlet/NullstarRift.rsi/$name.png"))
}
foreach($name in @('core','streak','arc')){
    $assets[$name]='data:image/png;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes("$RepoRoot/Resources/Textures/_Starlight/Admeme/Scarlet/NullstarCleave.rsi/$name.png"))
}
$assets.sword='data:image/png;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes("$RepoRoot/Resources/Textures/_Starlight/Admeme/Scarlet/Nullstar.rsi/icon.png"))
$assets.dash='data:audio/wav;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes("$RepoRoot/Resources/Audio/_Starlight/Weapons/Nullstar/rift-dash.wav"))
$assets.charge='data:audio/wav;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes("$RepoRoot/Resources/Audio/_Starlight/Weapons/Nullstar/rift-charge.wav"))
$assets.release='data:audio/wav;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes("$RepoRoot/Resources/Audio/_Starlight/Weapons/Nullstar/rift-cleave.wav"))
(Get-Content "$PSScriptRoot/rift-preview.template.html" -Raw).Replace('__ASSETS__',($assets|ConvertTo-Json -Compress)) | Set-Content "$PSScriptRoot/rift-preview.html" -Encoding utf8
