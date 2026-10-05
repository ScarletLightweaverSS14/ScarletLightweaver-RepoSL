param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
# Native asset conversion only: all artwork comes from the generated source.
Add-Type -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
public static class NullstarCleaveExport
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
            Color c=source.GetPixel(px,py);if(c.A<180)continue;alpha+=c.A;r+=c.R*c.A;g+=c.G*c.A;b+=c.B*c.A;
        }
        if(alpha<36*255*.22)return Color.FromArgb(0,0,0,0);
        return Quantize(Color.FromArgb(255,r/alpha,g/alpha,b/alpha));
    }
    public static void Export(string sourceFile,string root)
    {
        Directory.CreateDirectory(root);
        string[] states={"core","streak","arc"};
        using(Bitmap source=new Bitmap(sourceFile)) {
            int[] bounds={0,390,630,1024}; int[] widths={48,120,120}; int[] heights={48,24,48}; for(int i=0;i<3;i++) {
                int top=source.Height,bottom=0,left=source.Width,right=0;
                for(int y=bounds[i];y<bounds[i+1];y++)for(int x=0;x<source.Width;x++){
                    if(source.GetPixel(x,y).A<180)continue;
                    left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                }
                int ox=(128-widths[i])/2,oy=(64-heights[i])/2; double fx=(right-left+1)/(double)widths[i],fy=(bottom-top+1)/(double)heights[i];
                using(Bitmap output=new Bitmap(128,64,PixelFormat.Format32bppArgb)) {
                    for(int y=oy;y<oy+heights[i];y++)for(int x=ox;x<ox+widths[i];x++)
                        output.SetPixel(x,y,Reduce(source,left+(x-ox+.5)*fx,top+(y-oy+.5)*fy,fx,fy,0));
                    output.Save(Path.Combine(root,states[i]+".png"),ImageFormat.Png);
                }
            }
        }
    }
}
'@
$destination=Join-Path $RepoRoot 'Resources/Textures/_Starlight/Admeme/Scarlet/NullstarCleave.rsi'
[NullstarCleaveExport]::Export("$PSScriptRoot/source/rift-dash.png",$destination)
[ordered]@{version=1;license='CC-BY-SA-3.0';copyright='Original AI-generated Rift Cleave artwork for Scarlet using OpenAI image generation; native palette conversion and RSI assembly by Codex.';size=@{x=128;y=64};states=@(@{name='core'},@{name='streak'},@{name='arc'})} | ConvertTo-Json -Depth 6 | Set-Content "$destination/meta.json" -Encoding utf8
Write-Output 'Exported core, dash wake and crescent into three 128x64 frames with binary alpha and a 16-color palette.'

