# Original procedural audio and generator, licensed under MIT.
# No recordings, sample packs, or third-party sounds are used.
$ErrorActionPreference = 'Stop'
$audioDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Resources/Audio/_Voidwalker'))
[IO.Directory]::CreateDirectory($audioDirectory) | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public static class VoidwalkerBeamAudio
{
    const int Rate = 44100;
    static double Sin(double cycles) => Math.Sin(2 * Math.PI * cycles);

    public static void Write(string path, bool charge)
    {
        // One second of mono signed 16-bit PCM, supported natively by RobustToolbox.
        var samples = new short[Rate];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / Rate;
            double value;
            if (charge)
            {
                double phase = 145 * t + 235 * t * t;
                double envelope = Math.Min(1, t / 0.02) * Math.Min(1, (1 - t) / 0.07);
                double chimes = 0;
                for (int n = 0; n < 5; n++)
                {
                    double age = t - (0.04 + n * 0.16);
                    if (age >= 0)
                        chimes += Math.Exp(-age * 16) * (Sin((1050 + n * 190) * age) + 0.35 * Sin((1807 + n * 307) * age));
                }
                value = envelope * (0.17 * Sin(phase) + 0.09 * Sin(phase * 2.01) + 0.1 * chimes);
            }
            else
            {
                // All oscillators/modulators complete whole cycles: this buffer loops without a click.
                double shimmer = 0.55 + 0.45 * Sin(11 * t);
                double crackle = Math.Pow((1 + Sin(23 * t)) / 2, 10);
                value = 0.14 * Sin(98 * t + 0.08 * Sin(3 * t))
                    + 0.10 * Sin(196 * t + 0.1 * Sin(7 * t))
                    + shimmer * (0.08 * Sin(784 * t) + 0.045 * Sin(1176 * t))
                    + crackle * 0.04 * (Sin(4211 * t) + Sin(6803 * t));
            }
            samples[i] = (short)(Math.Clamp(value, -0.85, 0.85) * short.MaxValue);
        }
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
            writer.Write(sample);
    }
}
'@

[VoidwalkerBeamAudio]::Write((Join-Path $audioDirectory 'crystal_charge.wav'), $true)
[VoidwalkerBeamAudio]::Write((Join-Path $audioDirectory 'crystal_beam_loop.wav'), $false)
Write-Output "Generated two original mono PCM beam sounds in $audioDirectory"
