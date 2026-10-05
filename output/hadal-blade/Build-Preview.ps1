param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$textureRoot=Join-Path $RepoRoot 'Resources/Textures/_Starlight/Admeme/Scarlet'
$assetPaths=[ordered]@{
    off='HadalBlade.rsi/icon.png'; on='HadalBlade.rsi/idle.png'; icon='HadalBlade.rsi/icon.png'
    left='HadalBladeInhands.rsi/wielded-inhand-left.png'; right='HadalBladeInhands.rsi/wielded-inhand-right.png'
    belt='HadalBladeBelt.rsi/equipped-BELT.png'
    swing='HadalBladeEffects.rsi/pressure-cut.png'
}
$data=[ordered]@{}
foreach($entry in $assetPaths.GetEnumerator()) {
    $data[$entry.Key]='data:image/png;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $textureRoot $entry.Value)))
}
$template=Get-Content "$PSScriptRoot/preview.template.html" -Raw
$template.Replace('__ASSET_DATA__',($data|ConvertTo-Json -Compress)) | Set-Content "$PSScriptRoot/preview.html" -Encoding utf8

# Static contact sheet for quick review without a browser.
Add-Type -AssemblyName System.Drawing
$sheet=[System.Drawing.Bitmap]::new(1160,940)
$g=[System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.ColorTranslator]::FromHtml('#161A27'))
$g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::Half
$title=[System.Drawing.Font]::new('Segoe UI',25,[System.Drawing.FontStyle]::Bold)
$label=[System.Drawing.Font]::new('Segoe UI',12)
$white=[System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#DDDCEC'))
$muted=[System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#A9ACC5'))
$tile=[System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#454C59'))
try {
    $g.DrawString('SCARLET''S HADAL BLADE',$title,$white,24,16)
    $g.DrawString('Amethyst heart / abyssal stone / internal crystal pulse',$label,$muted,27,60)
    function Draw-Frame([string]$Path,[int]$X,[int]$Y,[int]$W,[int]$H,[int]$Column,[int]$Row,[int]$Scale) {
        $image=[System.Drawing.Bitmap]::new((Join-Path $textureRoot $Path))
        try {
            $dest=[System.Drawing.Rectangle]::new($X,$Y,$W*$Scale,$H*$Scale)
            $g.FillRectangle($tile,$dest)
            $g.DrawImage($image,$dest,$Column*$W,$Row*$H,$W,$H,[System.Drawing.GraphicsUnit]::Pixel)
        } finally {$image.Dispose()}
    }
    Draw-Frame 'HadalBlade.rsi/idle.png' 24 115 32 64 0 0 4
    Draw-Frame 'HadalBlade.rsi/idle.png' 182 115 32 64 4 0 4
    $g.DrawString('Quiet',$label,$white,24,90)
    $g.DrawString('Pulse',$label,$white,182,90)
    $g.DrawString('32 x 64 item frames',$label,$muted,24,379)
    $g.DrawString('8 idle frames - 3.2 seconds',$label,$white,24,430)
    for($i=0;$i -lt 8;$i++){Draw-Frame 'HadalBlade.rsi/idle.png' (24+($i%4)*80) (458+[Math]::Floor($i/4)*143) 32 64 $i 0 2}
    $directions=@('South','North','East','West')
    $g.DrawString('Right hand - four directions',$label,$white,510,90)
    $g.DrawString('Left hand - four directions',$label,$white,510,291)
    for($d=0;$d -lt 4;$d++){
        Draw-Frame 'HadalBladeInhands.rsi/wielded-inhand-right.png' (510+$d*151) 115 64 64 0 $d 2
        Draw-Frame 'HadalBladeInhands.rsi/wielded-inhand-left.png' (510+$d*151) 315 64 64 0 $d 2
        $g.DrawString($directions[$d],$label,$muted,(510+$d*151),250)
        $g.DrawString($directions[$d],$label,$muted,(510+$d*151),450)
    }
    $g.DrawString('Pressure-cut swing - 335 milliseconds',$label,$white,510,493)
    for($i=0;$i -lt 4;$i++){Draw-Frame 'HadalBladeEffects.rsi/pressure-cut.png' (510+$i*151) 520 64 64 $i 0 2}
    $g.DrawString('Belt overlay - four directions',$label,$white,510,680)
    for($d=0;$d -lt 4;$d++){
        Draw-Frame 'HadalBladeBelt.rsi/equipped-BELT.png' (510+$d*151) 710 32 32 ($d%2) ([Math]::Floor($d/2)) 4
        $g.DrawString($directions[$d],$label,$muted,(510+$d*151),848)
    }
    $g.DrawString('HADAL CRUSH',$label,$white,24,770)
    $g.DrawString('0.7s windup / three pressure crescents',$label,$muted,24,800)
    $g.DrawString('Cold + blunt / brief slow / 25s cooldown',$label,$muted,24,825)
    $g.DrawString('Actual native PNGs shown at integer zoom. Animated, offline preview: preview.html',$label,$muted,24,900)
    $sheet.Save("$PSScriptRoot/contact-sheet.png",[System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $g.Dispose();$sheet.Dispose();$title.Dispose();$label.Dispose();$white.Dispose();$muted.Dispose();$tile.Dispose()
}
Write-Output 'Built preview.html and contact-sheet.png'
