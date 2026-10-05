param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
# Native asset conversion only: all artwork comes from the generated source.
Add-Type -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
public static class NullstarExport
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
    static Bitmap Sample(Bitmap source)
    {
        int top=source.Height,bottom=0;
        for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
            if(source.GetPixel(x,y).A>=200){top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        int gripY=top+(int)((bottom-top)*.86),left=source.Width,right=0;
        for(int x=0;x<source.Width;x++)if(source.GetPixel(x,gripY).A>=200){left=Math.Min(left,x);right=Math.Max(right,x);}
        double center=(left+right)*.5,scale=49.0/(bottom-top);
        Bitmap item=new Bitmap(32,64,PixelFormat.Format32bppArgb);
        for(int y=0;y<64;y++)for(int x=0;x<32;x++){
            double sx=center+(x-16)/scale,sy=top+(y-7)/scale;
            item.SetPixel(x,y,Reduce(source,sx,sy,1/scale,0));
        }
        return item;
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
    static Bitmap Place(Bitmap source,int size,double ax,double ay,double sourceX,double sourceY,double scale,double degrees)
    {
        Bitmap output=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        double a=degrees*Math.PI/180,co=Math.Cos(a),si=Math.Sin(a);
        for(int y=0;y<size;y++)for(int x=0;x<size;x++){
            double dx=(x-ax)/scale,dy=(y-ay)/scale;
            double sx=co*dx+si*dy+sourceX,sy=-si*dx+co*dy+sourceY;
            output.SetPixel(x,y,Reduce(source,sx,sy,1/scale,a));
        }
        return output;
    }
    static Bitmap Pack(Bitmap[] frames)
    {
        int w=frames[0].Width,h=frames[0].Height;
        Bitmap sheet=new Bitmap(w*2,h*2,PixelFormat.Format32bppArgb);
        for(int f=0;f<4;f++)for(int y=0;y<h;y++)for(int x=0;x<w;x++)sheet.SetPixel((f%2)*w+x,(f/2)*h+y,frames[f].GetPixel(x,y));
        return sheet;
    }
    public static void Export(string sourceFile,string root)
    {
        string itemPath=Path.Combine(root,"Nullstar.rsi"),handPath=Path.Combine(root,"NullstarInhands.rsi"),beltPath=Path.Combine(root,"NullstarBelt.rsi");
        Directory.CreateDirectory(itemPath);Directory.CreateDirectory(handPath);Directory.CreateDirectory(beltPath);
        using(Bitmap source=new Bitmap(sourceFile))using(Bitmap item=Sample(source)){
            item.Save(Path.Combine(itemPath,"icon.png"),ImageFormat.Png);
            // S, N, E, W. Reuse established hand anchors, with Nullstar's shorter blade.
            double[] rx={23,40,31,26},lx={40,23,38,32},ys={34,36,34,34},ra={-32,32,32,-32},la={32,-32,32,-32};
            for(int hand=0;hand<2;hand++){
                bool left=hand==0;string name=left?"left":"right";Bitmap[] frames=new Bitmap[4];
                for(int d=0;d<4;d++)frames[d]=Place(item,64,left?lx[d]:rx[d],ys[d],16,49,.70,left?la[d]:ra[d]);
                using(Bitmap sheet=Pack(frames)){
                    sheet.Save(Path.Combine(handPath,"inhand-"+name+".png"),ImageFormat.Png);
                    sheet.Save(Path.Combine(handPath,"wielded-inhand-"+name+".png"),ImageFormat.Png);
                }
                foreach(Bitmap f in frames)f.Dispose();
            }
            Bitmap[] belt=new Bitmap[4];double[] bx={10,22,16,17},angles={192,168,188,172};
            for(int d=0;d<4;d++)belt[d]=Place(item,32,bx[d],12,16,49,.40,angles[d]);
            using(Bitmap sheet=Pack(belt))sheet.Save(Path.Combine(beltPath,"equipped-BELT.png"),ImageFormat.Png);
            foreach(Bitmap f in belt)f.Dispose();
        }
    }
}
'@
$textureRoot=Join-Path $RepoRoot 'Resources/Textures/_Starlight/Admeme/Scarlet'
[NullstarExport]::Export("$PSScriptRoot/source/nullstar-native.png",$textureRoot)
$credit="AI-generated for Scarlet using OpenAI image generation from Scarlet's Nullstar design brief; native pixel export and RSI assembly by Codex. No existing weapon artwork copied."
function Write-Meta($Name,$Width,$Height,$States) {
    [ordered]@{version=1;license='CC-BY-SA-3.0';copyright=$credit;size=@{x=$Width;y=$Height};states=$States} |
        ConvertTo-Json -Depth 8 | Set-Content "$textureRoot/$Name/meta.json" -Encoding utf8
}
Write-Meta 'Nullstar.rsi' 32 64 @(@{name='icon'})
$states=@()
foreach($hand in @('left','right')){foreach($prefix in @('inhand','wielded-inhand')){$states+=@{name="$prefix-$hand";directions=4}}}
Write-Meta 'NullstarInhands.rsi' 64 64 $states
Write-Meta 'NullstarBelt.rsi' 32 32 @(@{name='equipped-BELT';directions=4})
Write-Output 'Exported Nullstar item, both hands, and belt sprites.'


