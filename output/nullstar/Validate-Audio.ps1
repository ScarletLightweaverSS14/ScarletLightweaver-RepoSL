param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$root=Join-Path $RepoRoot 'Resources/Audio/_Starlight/Weapons/Nullstar'
$report=[Collections.Generic.List[string]]::new()
$files=@(Get-ChildItem -LiteralPath $root -Filter '*.wav')
if($files.Count -ne 11){throw 'Expected eleven Nullstar cues'}
foreach($file in $files){
    $bytes=[IO.File]::ReadAllBytes($file.FullName)
    if([Text.Encoding]::ASCII.GetString($bytes,0,4) -ne 'RIFF' -or [Text.Encoding]::ASCII.GetString($bytes,8,4) -ne 'WAVE'){throw "Not WAV: $file"}
    if([BitConverter]::ToInt32($bytes,4) -ne $bytes.Length-8 -or [BitConverter]::ToInt32($bytes,40) -ne $bytes.Length-44){throw 'Invalid RIFF/data length'}
    if([BitConverter]::ToInt16($bytes,20) -ne 1 -or [BitConverter]::ToInt16($bytes,22) -ne 1 -or [BitConverter]::ToInt16($bytes,34) -ne 16){throw 'Expected mono 16-bit PCM'}
    $rate=[BitConverter]::ToInt32($bytes,24)
    if($rate -ne 44100){throw 'Unexpected sample rate'}
    $samples=($bytes.Length-44)/2;$peak=0.0;$sum=0.0;$squares=0.0
    for($i=44;$i -lt $bytes.Length;$i+=2){
        $sample=[BitConverter]::ToInt16($bytes,$i)/32767.0
        $peak=[Math]::Max($peak,[Math]::Abs($sample));$sum+=$sample;$squares+=$sample*$sample
    }
    $rms=[Math]::Sqrt($squares/$samples);$duration=$samples/$rate
    if($peak -gt 0.701 -or $rms -lt .002 -or [Math]::Abs($sum/$samples) -gt .01){throw "Clipping, silence or DC offset: $file"}
    $maxDuration=switch($file.Name){'unbound.wav'{8.51}'rift-charge.wav'{.81}'rift-cleave.wav'{2.41}'rift-dash.wav'{.37}default{.75}}
    if($duration -gt $maxDuration -or $duration -lt .3){throw "Unexpected duration: $file"}
    if([BitConverter]::ToInt16($bytes,44) -ne 0 -or [BitConverter]::ToInt16($bytes,$bytes.Length-2) -ne 0){throw "Unfaded endpoints: $file"}
    $report.Add(('PASS {0}: {1:N2}s, mono 44.1kHz/16bit PCM, peak {2:N1} dBFS, RMS {3:N1} dBFS, silent endpoints' -f $file.Name,$duration,(20*[Math]::Log10($peak)),(20*[Math]::Log10($rms))))
}
$report.Add('Technical audio checks only; subjective balance and the combined in-game mix need listening.')
$report | Set-Content "$PSScriptRoot/audio-validation.txt" -Encoding utf8
$report

