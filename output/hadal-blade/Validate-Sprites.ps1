param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$schema = Join-Path $RepoRoot 'Tools/Schemas/rsi.json'
$textureRoot = Join-Path $RepoRoot 'Resources/Textures/_Starlight/Admeme/Scarlet'
$stateCount = 0
$frameCount = 0
$report = [System.Collections.Generic.List[string]]::new()
foreach ($folder in @('HadalBlade.rsi','HadalBladeInhands.rsi','HadalBladeEffects.rsi','HadalBladeBelt.rsi')) {
    $dir = Join-Path $textureRoot $folder
    $json = Get-Content "$dir/meta.json" -Raw
    if (-not (Test-Json -Json $json -SchemaFile $schema)) { throw "Invalid metadata: $folder" }
    $meta = $json | ConvertFrom-Json
    $declaredNames = @($meta.states | ForEach-Object { "$($_.name).png" })
    foreach ($file in Get-ChildItem $dir -File) {
        if ($file.Name -ne 'meta.json' -and $file.Name -notin $declaredNames) {
            throw "Undeclared file: $($file.FullName)"
        }
    }
    foreach ($state in $meta.states) {
        $directions = if ($null -ne $state.directions) { [int]$state.directions } else { 1 }
        $expected = $directions
        if ($null -ne $state.delays) {
            if ($state.delays.Count -ne $directions) { throw "Direction/delay mismatch: $($state.name)" }
            $totals = @($state.delays | ForEach-Object {
                foreach ($delay in $_) { if ($delay -le 0) { throw 'Nonpositive frame delay' } }
                [math]::Round(($_ | Measure-Object -Sum).Sum, 3)
            } | Select-Object -Unique)
            if ($totals.Count -ne 1) { throw "Unequal direction durations: $($state.name)" }
            $expected = ($state.delays | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
        }
        $bitmap = [System.Drawing.Bitmap]::new("$dir/$($state.name).png")
        try {
            $w=[int]$meta.size.x; $h=[int]$meta.size.y
            if ($bitmap.Width % $w -or $bitmap.Height % $h) { throw "Invalid sheet dimensions: $($state.name)" }
            $columns = [int]($bitmap.Width/$w)
            $actual = $columns * ($bitmap.Height/$h)
            if ($actual -ne $expected) { throw "Frame count mismatch: $($state.name): $actual vs $expected" }
            $colors=[System.Collections.Generic.HashSet[int]]::new()
            for ($frame=0; $frame -lt $actual; $frame++) {
                $opaque=0
                for ($y=0; $y -lt $h; $y++) {
                    for ($x=0; $x -lt $w; $x++) {
                        $color=$bitmap.GetPixel(($frame%$columns)*$w+$x,[int][math]::Floor($frame/$columns)*$h+$y)
                        if ($color.A -notin @(0,255)) { throw "Partial alpha: $($state.name)" }
                        if ($color.A -eq 255) {
                            $opaque++; [void]$colors.Add($color.ToArgb())
                            if ($x -eq 0 -or $y -eq 0 -or $x -eq $w-1 -or $y -eq $h-1) {
                                throw "Sprite touches frame boundary: $($state.name), frame $frame, $x,$y"
                            }
                        } elseif ($color.R -or $color.G -or $color.B) { throw 'Nonzero transparent RGB' }
                    }
                }
                if ($opaque -eq 0) { throw "Empty frame: $($state.name)" }
            }
            if ($colors.Count -gt 16) { throw "Palette exceeds 16 colors: $($state.name)" }
            $report.Add("PASS $folder/$($state.name): ${w}x${h}, $actual frames, $directions directions, $($colors.Count) colors")
            $stateCount++; $frameCount += $actual
        } finally { $bitmap.Dispose() }
    }
}

# Idle animation stays entirely inside the physical blade.
$idle=[System.Drawing.Bitmap]::new("$textureRoot/HadalBlade.rsi/on.png")
try {
    for ($phase=1;$phase -lt 8;$phase++) {
        $changed=0
        for ($y=0;$y -lt 64;$y++) {
            for ($x=0;$x -lt 32;$x++) {
                $a=$idle.GetPixel($x,$y); $b=$idle.GetPixel($x+32*$phase,$y)
                if ($a.ToArgb() -ne $b.ToArgb()) { $changed++ }
                if ($y -ge 39 -and $a.ToArgb() -ne $b.ToArgb()) { throw 'Idle grip/guard drift' }
                if ($a.A -ne $b.A) { throw 'Idle silhouette drift or stray particle' }
            }
        }
        if ($changed -eq 0) { throw 'Duplicate idle phase' }
        $report.Add("PASS idle phase ${phase}: $changed changed pixels; fixed grip, guard and blade silhouette")
    }
} finally { $idle.Dispose() }
$report.Add("Validated 4 RSI folders, $stateCount states and $frameCount frames against the repository JSON schema and native-image checks.")
$report.Add('File checks only. See separate engine-check logs; in-game appearance requires a client session.')
$report | Set-Content "$PSScriptRoot/validation.txt" -Encoding utf8
$report
