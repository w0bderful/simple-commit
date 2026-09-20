$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class DingSynth {
    public static void Write(string path) {
        const int rate=44100;int count=(int)(rate*1.05);double[] samples=new double[count];double peak=0;
        for(int i=0;i<count;i++){
            double t=(double)i/rate;
            double attack=1-Math.Exp(-t/0.0025);
            double tail=Math.Min(1,Math.Max(0,(1.05-t)/0.08));
            double v=attack*tail*(Math.Sin(2*Math.PI*1568*t)*Math.Exp(-7*t)
                +0.28*Math.Sin(2*Math.PI*3141*t)*Math.Exp(-11*t)
                +0.10*Math.Sin(2*Math.PI*4230*t)*Math.Exp(-16*t));
            samples[i]=v;peak=Math.Max(peak,Math.Abs(v));
        }
        using(var w=new BinaryWriter(File.Create(path))){
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(count*2);
            for(int i=0;i<count;i++)w.Write((short)Math.Round(samples[i]/peak*0.60*32767));
        }
    }
}
'@
[DingSynth]::Write((Join-Path $PSScriptRoot 'ding.wav'))
