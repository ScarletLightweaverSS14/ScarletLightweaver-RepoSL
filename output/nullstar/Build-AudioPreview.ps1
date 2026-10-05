param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$root=Join-Path $RepoRoot 'Resources/Audio/_Starlight/Weapons/Nullstar'
$cues=[ordered]@{}
foreach($name in @('swing1','swing2','hit1','hit2','glance','wield','unwield','unbound','rift-charge','rift-dash','rift-cleave')){
    $bytes=[IO.File]::ReadAllBytes("$root/$name.wav");$count=($bytes.Length-44)/2
    $peaks=for($bin=0;$bin -lt 96;$bin++){
        $peak=0.0
        for($i=[int][Math]::Floor($bin*$count/96);$i -lt [Math]::Floor(($bin+1)*$count/96);$i++){$peak=[Math]::Max($peak,[Math]::Abs([BitConverter]::ToInt16($bytes,44+$i*2)/32767.0))}
        [Math]::Round($peak,4)
    }
    $db=switch -Wildcard ($name){'hit*'{-2}'wield'{-5}'unwield'{-6}'unbound'{-3}'rift-charge'{-3}'rift-dash'{0}'rift-cleave'{1}default{-4}}
    $variation=switch -Wildcard ($name){'swing*'{.04}'hit*'{.05}'glance'{.05}default{.03}}
    $cues[$name]=@{label=$name;src='data:audio/wav;base64,'+[Convert]::ToBase64String($bytes);db=$db;variation=$variation;duration=$count/44100;peaks=@($peaks)}
}
(Get-Content "$PSScriptRoot/audio-preview.template.html" -Raw).Replace('__CUES__',($cues|ConvertTo-Json -Depth 5 -Compress)) | Set-Content "$PSScriptRoot/audio-preview.html" -Encoding utf8
Write-Output 'Built standalone audio-preview.html with all eleven cues.'



