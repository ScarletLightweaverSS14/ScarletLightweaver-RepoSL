param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Join-Path $RepoRoot 'Resources/Textures/_Starlight/Admeme/Scarlet'
$sheet=[Drawing.Bitmap]::new(1080,700)
$g=[Drawing.Graphics]::FromImage($sheet)
$g.Clear([Drawing.ColorTranslator]::FromHtml('#15131F'))
$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
$title=[Drawing.Font]::new('Segoe UI',26,[Drawing.FontStyle]::Bold)
$label=[Drawing.Font]::new('Segoe UI',12)
$white=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#E4DAF2'))
$muted=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#ACA2BD'))
$tile=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#4B4855'))
function Draw-Frame($path,$x,$y,$w,$h,$col,$row,$scale){
    $bitmap=[Drawing.Bitmap]::new("$root/$path")
    try{
        $dest=[Drawing.Rectangle]::new($x,$y,$w*$scale,$h*$scale)
        $g.FillRectangle($tile,$dest)
        $g.DrawImage($bitmap,$dest,$col*$w,$row*$h,$w,$h,[Drawing.GraphicsUnit]::Pixel)
    }finally{$bitmap.Dispose()}
}
try{
    $g.DrawString('NULLSTAR',$title,$white,24,15)
    $g.DrawString('Sacred stone-metal / amethyst veins / a distant star held inside the blade',$label,$muted,27,61)
    $g.DrawString('Nullstar',$label,$white,24,98)
    $g.DrawString('Hadal comparison',$label,$muted,184,98)
    Draw-Frame 'Nullstar.rsi/icon.png' 24 125 32 64 0 0 4
    Draw-Frame 'HadalBlade.rsi/icon.png' 184 125 32 64 0 0 4
    $g.DrawString('Same frame size and zoom',$label,$muted,24,392)
    $g.DrawString('Nullstar at native size',$label,$white,24,455)
    Draw-Frame 'Nullstar.rsi/icon.png' 24 487 32 64 0 0 1
    $g.DrawString('Static sprite stage',$label,$muted,24,582)
    $g.DrawString('Core animation and VFX come later.',$label,$muted,24,608)
    $directions=@('South','North','East','West')
    foreach($hand in @('right','left')){
        $y=if($hand -eq 'right'){125}else{323}
        $g.DrawString("$hand hand",$label,$white,450,($y-27))
        for($d=0;$d -lt 4;$d++){
            Draw-Frame "NullstarInhands.rsi/inhand-$hand.png" (450+$d*150) $y 64 64 ($d%2) ([Math]::Floor($d/2)) 2
            $g.DrawString($directions[$d],$label,$muted,(450+$d*150),($y+131))
        }
    }
    $g.DrawString('Belt overlay',$label,$white,450,502)
    for($d=0;$d -lt 4;$d++){
        Draw-Frame 'NullstarBelt.rsi/equipped-BELT.png' (450+$d*150) 533 32 32 ($d%2) ([Math]::Floor($d/2)) 3
        $g.DrawString($directions[$d],$label,$muted,(450+$d*150),636)
    }
    $sheet.Save("$PSScriptRoot/contact-sheet.png",[Drawing.Imaging.ImageFormat]::Png)
}finally{$g.Dispose();$sheet.Dispose();$title.Dispose();$label.Dispose();$white.Dispose();$muted.Dispose();$tile.Dispose()}
Write-Output 'Built Nullstar contact sheet.'
