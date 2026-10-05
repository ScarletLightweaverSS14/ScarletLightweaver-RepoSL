param([string] $RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$vorbis=Join-Path $RepoRoot 'bin/Content.Client/VorbisPizza.dll'
Add-Type -Path $vorbis
# The game's decoder targets .NET 6; PowerShell runs it on the compatible .NET 10 runtime.
Add-Type -ReferencedAssemblies $vorbis,System.Collections,System.Memory -CompilerOptions '/nowarn:1701' -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using NVorbis;

public static class NullstarAudio
{
    const int Rate=44100;
    static string Root;
    static readonly Dictionary<string,float[]> Sources=new Dictionary<string,float[]>();
    static float[] Load(string file)
    {
        if(Sources.TryGetValue(file,out var cached))return cached;
        if(file.StartsWith("source:")) {
            // Public CC0 pack: 24-bit stereo PCM WAV. Walk chunks instead of assuming a 44-byte header.
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(Root,"output/nullstar/source/audio",file.Substring(7))))) {
                if(new string(reader.ReadChars(4))!="RIFF")throw new Exception("Not RIFF");
                reader.ReadInt32();if(new string(reader.ReadChars(4))!="WAVE")throw new Exception("Not WAVE");
                int channels=0,bits=0,rate=0;byte[] pcm=null;
                while(reader.BaseStream.Position+8<=reader.BaseStream.Length){
                    string id=new string(reader.ReadChars(4));int size=reader.ReadInt32();long end=reader.BaseStream.Position+size+(size%2);
                    if(id=="fmt "){if(reader.ReadInt16()!=1)throw new Exception("Expected PCM");channels=reader.ReadInt16();rate=reader.ReadInt32();reader.ReadInt32();reader.ReadInt16();bits=reader.ReadInt16();}
                    if(id=="data")pcm=reader.ReadBytes(size);
                    reader.BaseStream.Position=end;
                }
                if(pcm==null||bits!=24||rate!=Rate)throw new Exception("Unexpected source WAV format");
                var mono=new float[pcm.Length/(channels*3)];float peak=0;
                for(int i=0;i<mono.Length;i++){
                    for(int c=0;c<channels;c++){int p=(i*channels+c)*3;int sample=(pcm[p]|pcm[p+1]<<8|pcm[p+2]<<16);if((sample&0x800000)!=0)sample|=unchecked((int)0xff000000);mono[i]+=sample/(8388608f*channels);}
                    peak=Math.Max(peak,Math.Abs(mono[i]));
                }
                if(peak<.001)throw new Exception("Silent source");
                int first=0,last=mono.Length-1;while(first<last&&Math.Abs(mono[first])<peak*.01)first++;while(last>first&&Math.Abs(mono[last])<peak*.01)last--;
                var result=new float[last-first+1];for(int i=0;i<result.Length;i++)result[i]=mono[first+i]/peak;
                Sources[file]=result;return result;
            }
        }
        using(var reader=new VorbisReader(Path.Combine(Root,"Resources/Audio",file))) {
            reader.Initialize();
            int channels=reader.Channels,count=checked((int)reader.TotalSamples*channels);
            float[] data=new float[count];int read=0;
            while(read<count){int n=reader.ReadSamples(data.AsSpan(read));if(n==0)break;read+=n*channels;}
            int frames=read/channels;float[] mono=new float[frames];float peak=0;
            for(int i=0;i<frames;i++){for(int c=0;c<channels;c++)mono[i]+=data[i*channels+c]/channels;peak=Math.Max(peak,Math.Abs(mono[i]));}
            if(peak<.0001f)throw new Exception("Silent source: "+file);
            int first=0,last=frames-1;
            while(first<last&&Math.Abs(mono[first])<peak*.003)first++;
            while(last>first&&Math.Abs(mono[last])<peak*.003)last--;
            int length=(int)((last-first+1)*(double)Rate/reader.SampleRate);
            float[] result=new float[length];
            for(int i=0;i<length;i++){
                double p=first+i*(double)reader.SampleRate/Rate;int a=(int)p,b=Math.Min(a+1,last);
                result[i]=(float)((mono[a]+(mono[b]-mono[a])*(p-a))/peak);
            }
            Sources[file]=result;return result;
        }
    }
    static void Layer(float[] mix,string file,double start,double pitch,double gain,double duration,bool reverse=false,double lowpass=18000)
    {
        var src=Load(file);int offset=(int)(start*Rate),length=Math.Min((int)(duration*Rate),(int)(src.Length/pitch));
        double filtered=0,filter=1-Math.Exp(-2*Math.PI*lowpass/Rate);
        for(int i=0;i<length&&offset+i<mix.Length;i++){
            double p=i*pitch;if(reverse)p=src.Length-1-p;
            int a=(int)p,b=Math.Min(a+1,src.Length-1);
            double sample=src[a]+(src[b]-src[a])*(p-a);
            filtered+=filter*(sample-filtered);
            double edge=Math.Min(1,Math.Min(i/(Rate*.004),(length-1-i)/(Rate*.045)));
            mix[offset+i]+=(float)(filtered*gain*Math.Max(0,edge));
        }
    }
    static void Ring(float[] mix,double start,double frequency,double gain,double decay)
    {
        for(int i=(int)(start*Rate);i<mix.Length;i++){
            double t=i/(double)Rate-start;
            double wave=Math.Sin(2*Math.PI*frequency*t)+.25*Math.Sin(2*Math.PI*frequency*1.414*t);
            mix[i]+=(float)(wave*gain*Math.Min(1,t/.008)*Math.Exp(-t/decay));
        }
    }
    static void Save(string file,float[] mix)
    {
        // Remove DC, fade both ends and leave at least 3 dB of headroom.
        double mean=0;foreach(float v in mix)mean+=v;mean/=mix.Length;
        double peak=0;
        for(int i=0;i<mix.Length;i++){
            double fade=Math.Min(1,Math.Min(i/(Rate*.004),(mix.Length-1-i)/(Rate*.035)));
            mix[i]=(float)((mix[i]-mean)*Math.Max(0,fade));peak=Math.Max(peak,Math.Abs(mix[i]));
        }
        double gain=peak>.70?.70/peak:1;
        using(var w=new BinaryWriter(File.Create(file))){
            w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+mix.Length*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(Rate);w.Write(Rate*2);w.Write((short)2);w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));w.Write(mix.Length*2);
            foreach(float sample in mix)w.Write((short)Math.Round(sample*gain*32767));
        }
    }
    public static void Build(string root,string destination)
    {
        Root=root;Directory.CreateDirectory(destination);
        for(int v=0;v<2;v++){
            var swing=new float[(int)(Rate*.62)];
            Layer(swing,"Weapons/slash.ogg",0,.80+v*.05,.64,.40);
            Layer(swing,"Effects/glass_crack1.ogg",.01,.65+v*.04,.10,.20,true,1800);
            Ring(swing,.06,190+v*23,.035,.10);
            Save(Path.Combine(destination,"swing"+(v+1)+".wav"),swing);

            var hit=new float[(int)(Rate*.48)];
            Layer(hit,"Weapons/pierce.ogg",0,.83+v*.06,.50,.30);
            Layer(hit,"Effects/metal_thud1.ogg",0,.76,.22,.18,false,850);
            Layer(hit,"Effects/glass_crack2.ogg",.025,1.08+v*.09,.14,.26);
            Ring(hit,.015,660+v*70,.03,.095);
            Save(Path.Combine(destination,"hit"+(v+1)+".wav"),hit);
        }
        var glance=new float[(int)(Rate*.48)];
        Layer(glance,"Weapons/block_metal1.ogg",0,.86,.48,.38);
        Layer(glance,"Effects/glass_crack3.ogg",.018,1.12,.13,.24);
        Ring(glance,.015,790,.025,.11);
        Save(Path.Combine(destination,"glance.wav"),glance);

        var wield=new float[(int)(Rate*.72)];
        Layer(wield,"Items/unsheath.ogg",0,.78,.45,.55);
        Layer(wield,"Effects/glass_crack1.ogg",.03,.67,.065,.20,true,1600);
        Ring(wield,.11,330,.028,.15);
        Save(Path.Combine(destination,"wield.wav"),wield);

        var unwield=new float[(int)(Rate*.45)];
        Layer(unwield,"Effects/metal_scrape1.ogg",0,.82,.28,.30,false,3200);
        Layer(unwield,"Effects/metal_thud1.ogg",.05,.9,.16,.18,false,900);
        Ring(unwield,.045,250,.015,.075);
        Save(Path.Combine(destination,"unwield.wav"),unwield);

        // Release hits immediately with the shockwave. The eventual extraction owns its lead-in.
        // Long recorded blast bodies carry the event; crystal is only an identifying accent.
        var unbound=new float[(int)(Rate*8.5)];
        Layer(unbound,"Effects/explosion1.ogg",0,.82,.50,3.8,false,6500);
        Layer(unbound,"Effects/explosion6.ogg",.025,.76,.82,7.1,false,2600);
        Layer(unbound,"Effects/explosionfar.ogg",.12,.90,.40,8.3,false,950);
        Layer(unbound,"Effects/break_stone.ogg",0,.65,.19,1.2,false,4200);
        Layer(unbound,"Effects/glass_crack2.ogg",.08,.68,.085,.7,false,6000);
        Layer(unbound,"Effects/glass_crack3.ogg",.34,.55,.035,.8,false,3300);
        // A descending pressure fundamental, not a sustained synth note.
        double phase=0;
        for(int i=0;i<(int)(Rate*1.8);i++){
            double t=i/(double)Rate;
            phase+=2*Math.PI*(32+42*Math.Exp(-t/.16))/Rate;
            unbound[i]+=(float)(Math.Sin(phase)*.17*Math.Min(1,t/.012)*Math.Exp(-t/.38));
        }
        // Sparse, filtered reflections give the release space without an audible repeated bang.
        var dry=(float[])unbound.Clone();
        double[] delays={.173,.347,.619};
        double[] gains={.16,.10,.055};
        for(int tap=0;tap<delays.Length;tap++){
            int offset=(int)(delays[tap]*Rate);double filtered=0;
            double filter=1-Math.Exp(-2*Math.PI*(1500-tap*350)/Rate);
            for(int i=offset;i<unbound.Length;i++){
                filtered+=filter*(dry[i-offset]-filtered);
                unbound[i]+=(float)(filtered*gains[tap]);
            }
        }
        Ring(unbound,.4,138,.007,1.3);
        for(int i=0;i<unbound.Length;i++){
            double remaining=(unbound.Length-1-i)/(double)Rate;
            unbound[i]*=(float)Math.Min(1,Math.Pow(remaining/2.2,2));
        }
        Save(Path.Combine(destination,"unbound.wav"),unbound);

        var charge=new float[(int)(Rate*.8)];
        Layer(charge,"source:swish-9.wav",.12,.3,.40,.65,true,1400);
        Layer(charge,"Effects/glass_crack1.ogg",.35,.65,.12,.45,true,2400);
        for(int i=0;i<charge.Length;i++){
            double t=i/(double)Rate,p=t/.8;
            charge[i]=(float)(charge[i]*(.2+p)+Math.Sin(2*Math.PI*(70*t+75*t*t))*.10*p*p);
        }
        Save(Path.Combine(destination,"rift-charge.wav"),charge);

        var dash=new float[(int)(Rate*.36)];
        Layer(dash,"source:swish-7.wav",0,.95,.58,.30,false,7000);
        Layer(dash,"source:swish-2.wav",.02,.7,.18,.30,true,2000);
        Save(Path.Combine(destination,"rift-dash.wav"),dash);

        // Recorded heavy swishes carry the cut; a falling pressure transient and short crystal accent
        // give it weight. Filtered reflections create a spatial tear, not a long musical chord.
        var cleave=new float[(int)(Rate*2.4)];
        Layer(cleave,"source:swish-9.wav",0,.43,1.5,.8,false,9000);
        Layer(cleave,"source:swish-7.wav",.018,.68,.80,.5,false,13000);
        Layer(cleave,"source:swish-5.wav",.07,.37,.42,.6,false,2300);
        Layer(cleave,"Effects/glass_crack2.ogg",.025,.62,.14,.65,false,4800);
        double cutPhase=0;
        for(int i=0;i<(int)(Rate*.65);i++){
            double t=i/(double)Rate;
            cutPhase+=2*Math.PI*(48+120*Math.Exp(-t/.04))/Rate;
            cleave[i]+=(float)(Math.Sin(cutPhase)*.28*Math.Min(1,t/.007)*Math.Exp(-t/.10));
            cleave[i]=(float)(Math.Tanh(cleave[i]*1.4)*.75);
        }
        var cutDry=(float[])cleave.Clone();
        for(int tap=0;tap<4;tap++){
            int delay=(int)((.09+tap*.137)*Rate);double filtered=0;
            double filter=1-Math.Exp(-2*Math.PI*(3800-tap*650)/Rate);
            for(int i=delay;i<cleave.Length;i++){
                filtered+=filter*(cutDry[i-delay]-filtered);
                cleave[i]+=(float)(filtered*.20*Math.Pow(.62,tap));
            }
        }
        Ring(cleave,.13,107,.012,.55);
        Save(Path.Combine(destination,"rift-cleave.wav"),cleave);
    }
}
'@
$destination=Join-Path $RepoRoot 'Resources/Audio/_Starlight/Weapons/Nullstar'
[NullstarAudio]::Build($RepoRoot,$destination)
Write-Output 'Built 11 mono PCM cues from attributed repository and CC0 swish sounds.'
