param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Generated artwork is the source of all visual content. This exporter performs
# native-size sampling, palette conversion, hand placement and RSI frame packing.
Add-Type -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

public static class HadalSpriteExport
{
    static readonly Color[] Palette = Array.ConvertAll(new string[] {
        "#090A13", "#11121F", "#1B1D2D", "#292E40", "#414A60", "#637288",
        "#261338", "#3C2057", "#583079", "#7745A3", "#9963CD", "#BD94EB",
        "#E2D0FA", "#47798D", "#89BFD3", "#C4E4ED"
    }, ColorTranslator.FromHtml);

    static Color Quantize(Color c)
    {
        if (c.A < 128) return Color.FromArgb(0,0,0,0);
        int best = Int32.MaxValue;
        Color chosen = Palette[0];
        foreach (Color p in Palette)
        {
            int dr = c.R-p.R, dg = c.G-p.G, db = c.B-p.B;
            int d = dr*dr + dg*dg + db*db;
            if (d < best) { best = d; chosen = p; }
        }
        return chosen;
    }

    public static Bitmap ItemFrame(Bitmap source, int index)
    {
        int x0 = index % 2 * source.Width / 2;
        int y0 = index / 2 * source.Height / 2;
        int x1 = (index % 2 + 1) * source.Width / 2;
        int y1 = (index / 2 + 1) * source.Height / 2;
        int top = y1, bottom = y0;
        for (int y=y0; y<y1; y++)
            for (int x=x0; x<x1; x++)
                if (source.GetPixel(x,y).A >= 200)
                { top=Math.Min(top,y); bottom=Math.Max(bottom,y); }
        double scale = 59.0 / (bottom-top);
        // Locate the grip, rather than centering on mist beyond the blade.
        int gripY = top + (int)((bottom-top)*0.80);
        int minX=x1, maxX=x0;
        for (int x=x0; x<x1; x++)
            if (source.GetPixel(x,gripY).A >= 200)
            { minX=Math.Min(minX,x); maxX=Math.Max(maxX,x); }
        double anchorX = (minX+maxX)*0.5;
        Bitmap output = new Bitmap(32,64,PixelFormat.Format32bppArgb);
        for (int y=0; y<64; y++)
            for (int x=0; x<32; x++)
            {
                int sx=(int)Math.Round(anchorX+(x-16)/scale);
                int sy=(int)Math.Round(top+(y-2)/scale);
                if (sx>=x0 && sx<x1 && sy>=y0 && sy<y1)
                    output.SetPixel(x,y,Quantize(source.GetPixel(sx,sy)));
            }
        return output;
    }

    public static Bitmap HandFrame(Bitmap item, bool left, int direction)
    {
        // S, N, E, W. Grip anchors follow the existing hypereutactic in-hands.
        double[] rightX={23,40,31,26}, leftX={40,23,38,32};
        double[] rightY={34,36,34,34}, leftY={34,36,34,34};
        double[] rightAngle={-32,32,32,-32}, leftAngle={32,-32,32,-32};
        double angle=(left?leftAngle[direction]:rightAngle[direction])*Math.PI/180;
        double ax=left?leftX[direction]:rightX[direction];
        double ay=left?leftY[direction]:rightY[direction];
        double co=Math.Cos(angle), si=Math.Sin(angle), scale=0.70;
        Bitmap output=new Bitmap(64,64,PixelFormat.Format32bppArgb);
        for (int y=0; y<64; y++)
            for (int x=0; x<64; x++)
            {
                double dx=(x-ax)/scale, dy=(y-ay)/scale;
                int sx=(int)Math.Round(co*dx+si*dy+16);
                int sy=(int)Math.Round(-si*dx+co*dy+52);
                if (sx>=0 && sx<32 && sy>=0 && sy<64)
                    output.SetPixel(x,y,item.GetPixel(sx,sy));
            }
        return output;
    }

    static bool Amethyst(Color c) { return c.A != 0 && c.R > c.G*1.25 && c.B > c.R+15; }
    static Bitmap RegisterPulse(Bitmap quiet, Bitmap generatedPhase)
    {
        // Generated phases can drift by a pixel after reduction. Keep the quiet
        // frame's rigid geometry and read only animated material from the variants.
        Bitmap registered=(Bitmap)quiet.Clone();
        for (int y=6;y<39;y++)
            for (int x=0;x<32;x++)
            {
                Color original=quiet.GetPixel(x,y), phase=generatedPhase.GetPixel(x,y);
                if (x>=12 && x<=18 && Amethyst(original) && Amethyst(phase))
                    registered.SetPixel(x,y,phase);
            }
        return registered;
    }

    static Bitmap BlendPulse(Bitmap a, Bitmap b)
    {
        Bitmap result=(Bitmap)a.Clone();
        for (int y=6;y<39;y++) for(int x=12;x<=18;x++)
        {
            Color ca=a.GetPixel(x,y),cb=b.GetPixel(x,y);
            if (Amethyst(ca) && Amethyst(cb))
                result.SetPixel(x,y,Quantize(Color.FromArgb(255,(ca.R+cb.R)/2,(ca.G+cb.G)/2,(ca.B+cb.B)/2)));
        }
        return result;
    }

    public static void ExportBelt(string sourceFile,string beltPath,string previewPath)
    {
        Directory.CreateDirectory(beltPath);
        using(Bitmap source=new Bitmap(sourceFile))
        using(Bitmap small=new Bitmap(12,24,PixelFormat.Format32bppArgb))
        {
            int top=source.Height,bottom=0,left=source.Width,right=0;
            for(int y=0;y<source.Height;y++) for(int x=0;x<source.Width;x++)
                if(source.GetPixel(x,y).A>=200){top=Math.Min(top,y);bottom=Math.Max(bottom,y);left=Math.Min(left,x);right=Math.Max(right,x);}
            double scale=23.0/(bottom-top),cx=(left+right)*0.5;
            for(int y=0;y<24;y++)for(int x=0;x<12;x++)
            {
                int sx=(int)Math.Round(cx+(x-5.5)/scale),sy=(int)Math.Round(top+y/scale);
                if(sx>=0&&sx<source.Width&&sy>=0&&sy<source.Height)small.SetPixel(x,y,Quantize(source.GetPixel(sx,sy)));
            }
            Bitmap[] frames=new Bitmap[4];
            double[] xs={10,22,16,17},angles={12,-12,8,-8};
            for(int d=0;d<4;d++)
            {
                frames[d]=new Bitmap(32,32,PixelFormat.Format32bppArgb);
                double a=angles[d]*Math.PI/180,co=Math.Cos(a),si=Math.Sin(a);
                for(int y=0;y<32;y++)for(int x=0;x<32;x++)
                {
                    double dx=x-xs[d],dy=y-15;
                    int sx=(int)Math.Round(co*dx+si*dy+6),sy=(int)Math.Round(-si*dx+co*dy+8);
                    if(d==1||d==3)sx=11-sx;
                    if(sx>=0&&sx<12&&sy>=0&&sy<24)frames[d].SetPixel(x,y,small.GetPixel(sx,sy));
                }
            }
            using(Bitmap sheet=Pack(frames,2)){Save(sheet,Path.Combine(beltPath,"equipped-BELT.png"));Enlarge(sheet,Path.Combine(previewPath,"belt-preview.png"),6);}
            foreach(Bitmap frame in frames)frame.Dispose();
        }
    }

    public static Bitmap Pack(Bitmap[] frames, int columns)
    {
        int w=frames[0].Width, h=frames[0].Height;
        Bitmap output=new Bitmap(columns*w,((frames.Length+columns-1)/columns)*h,PixelFormat.Format32bppArgb);
        for (int i=0;i<frames.Length;i++)
            for (int y=0;y<h;y++)
                for (int x=0;x<w;x++)
                    output.SetPixel((i%columns)*w+x,(i/columns)*h+y,frames[i].GetPixel(x,y));
        return output;
    }

    public static void Save(Bitmap image, string path) { image.Save(path,ImageFormat.Png); }

    public static void ExportEffect(string sourceFile, string effectPath, string previewPath)
    {
        Directory.CreateDirectory(effectPath);
        using (Bitmap source=new Bitmap(sourceFile))
        {
            Bitmap[] frames=new Bitmap[4];
            for (int i=0;i<4;i++)
            {
                frames[i]=new Bitmap(64,64,PixelFormat.Format32bppArgb);
                for (int y=0;y<64;y++)
                    for (int x=0;x<64;x++)
                    {
                        int sx=(int)((i%2+(x+0.5)/64)*source.Width/2);
                        int sy=(int)((i/2+(y+0.5)/64)*source.Height/2);
                        frames[i].SetPixel(x,y,Quantize(source.GetPixel(sx,sy)));
                    }
            }
            using (Bitmap sheet=Pack(frames,4))
            {
                Save(sheet,Path.Combine(effectPath,"pressure-cut.png"));
                Enlarge(sheet,Path.Combine(previewPath,"swing-preview.png"),4);
            }
            foreach (Bitmap frame in frames) frame.Dispose();
        }
    }

    public static void Enlarge(Bitmap source, string path, int zoom)
    {
        using (Bitmap output=new Bitmap(source.Width*zoom,source.Height*zoom,PixelFormat.Format32bppArgb))
        {
            for (int y=0;y<output.Height;y++)
                for (int x=0;x<output.Width;x++)
                    output.SetPixel(x,y,source.GetPixel(x/zoom,y/zoom));
            Save(output,path);
        }
    }

    public static void Export(string sourceFile, string itemPath, string handPath, string previewPath)
    {
        Directory.CreateDirectory(itemPath); Directory.CreateDirectory(handPath); Directory.CreateDirectory(previewPath);
        using (Bitmap source=new Bitmap(sourceFile))
        {
            Bitmap[] items=new Bitmap[4];
            for (int i=0;i<4;i++) items[i]=ItemFrame(source,i);
            for (int i=1;i<4;i++)
            {
                Bitmap raw=items[i];
                items[i]=RegisterPulse(items[0],raw);
                raw.Dispose();
            }
            Save(items[0],Path.Combine(itemPath,"off.png"));
            Save(items[1],Path.Combine(itemPath,"icon.png"));
            Bitmap[] idle=new Bitmap[8];
            for(int i=0;i<4;i++){idle[i*2]=(Bitmap)items[i].Clone();idle[i*2+1]=BlendPulse(items[i],items[(i+1)%4]);}
            using (Bitmap strip=Pack(idle,8))
            {
                Save(strip,Path.Combine(itemPath,"on.png"));
                Save(strip,Path.Combine(itemPath,"idle.png"));
            }
            Bitmap[] core=new Bitmap[8];
            for(int i=0;i<8;i++)
            {
                core[i]=new Bitmap(32,64,PixelFormat.Format32bppArgb);
                for(int y=6;y<39;y++)for(int x=12;x<=18;x++)
                    if(Amethyst(idle[i].GetPixel(x,y)))core[i].SetPixel(x,y,idle[i].GetPixel(x,y));
            }
            using(Bitmap strip=Pack(core,8))Save(strip,Path.Combine(itemPath,"core.png"));
            foreach(Bitmap frame in core)frame.Dispose();
            using (Bitmap overview=Pack(items,4))
                Enlarge(overview,Path.Combine(previewPath,"item-preview.png"),8);
            for (int hand=0;hand<2;hand++)
            {
                bool left=hand==0; string name=left?"left":"right";
                Bitmap[] on=new Bitmap[32];
                for (int direction=0;direction<4;direction++)
                {
                    for (int phase=0;phase<8;phase++)
                        on[direction*8+phase]=HandFrame(idle[phase],left,direction);
                }
                using (Bitmap sheet=Pack(on,8))
                {
                    Save(sheet,Path.Combine(handPath,"inhand-"+name+".png"));
                    Save(sheet,Path.Combine(handPath,"wielded-inhand-"+name+".png"));
                }
                using (Bitmap sheet=Pack(on,8))
                {
                    Save(sheet,Path.Combine(handPath,"inhand-"+name+"-on.png"));
                    Save(sheet,Path.Combine(handPath,"wielded-inhand-"+name+"-on.png"));
                    Enlarge(sheet,Path.Combine(previewPath,"inhand-"+name+"-preview.png"),4);
                }
                foreach (Bitmap image in on) image.Dispose();
            }
            foreach (Bitmap image in idle) image.Dispose();
            foreach (Bitmap image in items) image.Dispose();
        }
    }
}
'@

$textureRoot = Join-Path $RepoRoot 'Resources/Textures/_Starlight/Admeme/Scarlet'
$itemPath = Join-Path $textureRoot 'HadalBlade.rsi'
$handPath = Join-Path $textureRoot 'HadalBladeInhands.rsi'
$effectPath = Join-Path $textureRoot 'HadalBladeEffects.rsi'
$beltPath = Join-Path $textureRoot 'HadalBladeBelt.rsi'
[HadalSpriteExport]::Export("$PSScriptRoot/source/item-atlas-v2.png", $itemPath, $handPath, $PSScriptRoot)
[HadalSpriteExport]::ExportEffect("$PSScriptRoot/source/swing-atlas.png", $effectPath, $PSScriptRoot)
[HadalSpriteExport]::ExportBelt("$PSScriptRoot/source/belt-source.png", $beltPath, $PSScriptRoot)

$credit = "AI-generated for Scarlet using OpenAI image generation, based on Scarlet's approved Hadal Blade concept; native pixel export and RSI assembly by Codex. No existing game sprite pixels copied."
$idleDelays = @(1.25,0.2,0.2,0.2,0.3,0.2,0.3,0.55)
$itemMeta = [ordered]@{
    version = 1; license = 'CC-BY-SA-3.0'; copyright = $credit
    size = @{x = 32; y = 64}
    states = @(@{name='icon'}, @{name='off'}, @{name='on'; delays=@(,$idleDelays)}, @{name='idle'; delays=@(,$idleDelays)}, @{name='core'; delays=@(,$idleDelays)})
}
$handStates = @()
foreach ($hand in @('left','right')) {
    foreach ($prefix in @('inhand','wielded-inhand')) {
        $handStates += @{name="$prefix-$hand"; directions=4; delays=@($idleDelays,$idleDelays,$idleDelays,$idleDelays)}
        $handStates += @{name="$prefix-$hand-on"; directions=4; delays=@($idleDelays,$idleDelays,$idleDelays,$idleDelays)}
    }
}
$handMeta = [ordered]@{
    version=1; license='CC-BY-SA-3.0'; copyright=$credit
    size=@{x=64;y=64}; states=$handStates
}
$itemMeta | ConvertTo-Json -Depth 10 | Set-Content "$itemPath/meta.json" -Encoding utf8
$handMeta | ConvertTo-Json -Depth 10 | Set-Content "$handPath/meta.json" -Encoding utf8
$effectMeta = [ordered]@{
    version=1; license='CC-BY-SA-3.0'; copyright=$credit
    size=@{x=64;y=64}
    states=@(@{name='pressure-cut'; delays=@(,@(0.055,0.07,0.09,0.12))})
}
$effectMeta | ConvertTo-Json -Depth 10 | Set-Content "$effectPath/meta.json" -Encoding utf8
$beltMeta = [ordered]@{ version=1; license='CC-BY-SA-3.0'; copyright=$credit; size=@{x=32;y=32}; states=@(@{name='equipped-BELT';directions=4}) }
$beltMeta | ConvertTo-Json -Depth 10 | Set-Content "$beltPath/meta.json" -Encoding utf8
Write-Output "Exported item, both hand sets and swing effect to $textureRoot"
