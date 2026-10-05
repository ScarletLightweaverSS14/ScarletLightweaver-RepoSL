param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$old=[Convert]::ToBase64String([IO.File]::ReadAllBytes("$PSScriptRoot/source/unbound-previous.wav"))
$new=[Convert]::ToBase64String([IO.File]::ReadAllBytes("$RepoRoot/Resources/Audio/_Starlight/Weapons/Nullstar/unbound.wav"))
@"
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Nullstar Unbound — audio comparison</title>
<style>body{background:#17121f;color:#e9dcf5;font:17px/1.6 system-ui;max-width:760px;margin:40px auto;padding:20px}section{background:#292033;padding:20px;margin:20px 0;border:1px solid #69517b;border-radius:8px}audio{width:100%}p{color:#c4b4d1}h2{margin-top:0}a{color:#d4a9ff}</style>
<h1>Nullstar Unbound</h1><p>Release audio comparison. Both players use the same −3 dB playback trim. Nothing plays automatically.</p>
<section><h2>Revised · 8.5 seconds</h2><p>Layered explosion body, descending pressure, sparse crystal fracture and a long distant rumble. The initial impact aligns with the release; extraction buildup will be handled separately.</p><audio controls src="data:audio/wav;base64,$new"></audio></section>
<section><h2>Previous · 2.4 seconds</h2><p>Short stone/glass impacts and synthesized resonance.</p><audio controls src="data:audio/wav;base64,$old"></audio></section>
<p><a href="unbound-preview.html">Animation with revised audio</a> · <a href="audio-search.md">Alternative free sound sources and search terms</a></p>
<p>Technical checks passed; the perceived weight and in-game mix still need your listening judgment.</p>
<script>document.querySelectorAll('audio').forEach(a=>{a.volume=10**(-3/20);a.addEventListener('play',()=>document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()}));});</script></html>
"@ | Set-Content "$PSScriptRoot/unbound-audio-comparison.html" -Encoding utf8
