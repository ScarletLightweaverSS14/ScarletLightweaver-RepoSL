param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
# Native asset conversion only: all artwork comes from the generated source.
Add-Type -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
public static class NullstarUnboundExport
{
    static readonly Color[] Palette=Array.ConvertAll(new string[]{
        "#090A12","#14141E","#222130","#343241","#494654","#645F70","#868093","#B3A6C8",
        "#21112E","#382047","#513168","#71488F","#9364B6","#B889D6","#D5B5EF","#F4E6FF"
    },ColorTranslator.FromHtml);
    static Color Quantize(Color c)
    {
        if(c.A<128)return Color.FromArgb(0,0,0,0);
        int best=int.MaxValue;Color chosen=Palette[0];
        foreach(Color p in Palette){int r=c.R-p.R,g=c.G-p.G,b=c.B-p.B;int d=r*r+g*g+b*b;if(d<best){best=d;chosen=p;}}
        return chosen;
    }
    // Coverage sampling prevents subpixel edges/grips from disappearing during reduction.
    // The finished RSI still uses a strict palette and binary alpha.
    static Color Reduce(Bitmap source,double sx,double sy,double footprint,double angle)
    {
        double co=Math.Cos(angle),si=Math.Sin(angle);int alpha=0,r=0,g=0,b=0;
        for(int iy=0;iy<6;iy++)for(int ix=0;ix<6;ix++){
            double dx=((ix+.5)/6-.5)*footprint,dy=((iy+.5)/6-.5)*footprint;
            int px=(int)Math.Round(sx+co*dx+si*dy),py=(int)Math.Round(sy-si*dx+co*dy);
            if(px<0||px>=source.Width||py<0||py>=source.Height)continue;
            Color c=source.GetPixel(px,py);alpha+=c.A;r+=c.R*c.A;g+=c.G*c.A;b+=c.B*c.A;
        }
        if(alpha<36*255*.22)return Color.FromArgb(0,0,0,0);
        return Quantize(Color.FromArgb(255,r/alpha,g/alpha,b/alpha));
    }
    public static void Export(string sourceFile,string root)
    {
        Directory.CreateDirectory(root);
        string[] states={"ring","debris","flare","rift"};
        using(Bitmap source=new Bitmap(sourceFile)) {
            int cellX=source.Width/2,cellY=source.Height/2;
            for(int i=0;i<4;i++) using(Bitmap output=new Bitmap(128,128,PixelFormat.Format32bppArgb)) {
                for(int y=1;y<127;y++)for(int x=1;x<127;x++)
                    output.SetPixel(x,y,Reduce(source,(i%2)*cellX+(x+.5+(i==0?2:0))*cellX/128.0,(i/2)*cellY+(y+.5+(i==0?2:0))*cellY/128.0,cellX/128.0,0));
                output.Save(Path.Combine(root,states[i]+".png"),ImageFormat.Png);
            }
        }
    }
}
'@
$destination=Join-Path $RepoRoot 'Resources/Textures/_Starlight/Admeme/Scarlet/NullstarUnbound.rsi'
[NullstarUnboundExport]::Export("$PSScriptRoot/source/unbound-effects.png",$destination)
[ordered]@{version=1;license='CC-BY-SA-3.0';copyright='Original AI-generated Nullstar Unbound artwork for Scarlet using OpenAI image generation; palette reduction and RSI assembly by Codex.';size=@{x=128;y=128};states=@(@{name='ring'},@{name='debris'},@{name='flare'},@{name='rift'})} | ConvertTo-Json -Depth 6 | Set-Content "$destination/meta.json" -Encoding utf8
Write-Output 'Exported four 128x128 Unbound effect layers.'

