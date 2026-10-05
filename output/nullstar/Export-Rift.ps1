param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
# Native asset conversion only: all artwork comes from the generated source.
Add-Type -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
public static class NullstarRiftExport
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
    static Color Reduce(Bitmap source,double sx,double sy,double footprintX,double footprintY,double angle)
    {
        double co=Math.Cos(angle),si=Math.Sin(angle);int alpha=0,r=0,g=0,b=0;
        for(int iy=0;iy<6;iy++)for(int ix=0;ix<6;ix++){
            double dx=((ix+.5)/6-.5)*footprintX,dy=((iy+.5)/6-.5)*footprintY;
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
        string[] states={"telegraph","slash","rift"};
        using(Bitmap source=new Bitmap(sourceFile)) {
            for(int i=0;i<3;i++) {
                int top=source.Height,bottom=0,left=source.Width,right=0;
                for(int y=i*source.Height/3;y<(i+1)*source.Height/3;y++)for(int x=0;x<source.Width;x++){
                    if(source.GetPixel(x,y).A<180)continue;
                    left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                }
                double fx=(right-left+1)/120.0,fy=(bottom-top+1)/24.0;
                using(Bitmap output=new Bitmap(128,32,PixelFormat.Format32bppArgb)) {
                    for(int y=4;y<28;y++)for(int x=4;x<124;x++)
                        output.SetPixel(x,y,Reduce(source,left+(x-4+.5)*fx,top+(y-4+.5)*fy,fx,fy,0));
                    output.Save(Path.Combine(root,states[i]+".png"),ImageFormat.Png);
                }
            }
        }
    }
}
'@
$destination=Join-Path $RepoRoot 'Resources/Textures/_Starlight/Admeme/Scarlet/NullstarRift.rsi'
[NullstarRiftExport]::Export("$PSScriptRoot/source/rift-cleave.png",$destination)
[ordered]@{version=1;license='CC-BY-SA-3.0';copyright='Original AI-generated Rift Cleave artwork for Scarlet using OpenAI image generation; native palette conversion and RSI assembly by Codex.';size=@{x=128;y=32};states=@(@{name='telegraph'},@{name='slash'},@{name='rift'})} | ConvertTo-Json -Depth 6 | Set-Content "$destination/meta.json" -Encoding utf8
Write-Output 'Exported three 128x32 Rift Cleave states; artwork occupies 120x24 pixels.'
