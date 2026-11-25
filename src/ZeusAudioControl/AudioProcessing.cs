using NAudio.Dsp;
using NAudio.Wave;

namespace ZeusAudioControl;

internal sealed class LocalEq : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly double[] frequencies;
    private readonly BiQuadFilter[][] filters;
    private int channel;
    private float headroom;
    private Settings? pending;

    private sealed record Settings(double[] Gains, double Bass);

    public LocalEq(ISampleProvider source, double[] frequencies, double[] gains, double bass)
    {
        this.source = source;
        this.frequencies = frequencies;
        filters = new BiQuadFilter[WaveFormat.Channels][];
        Apply(new Settings(gains, bass));
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    public void SetGains(double[] gains, double bass) =>
        System.Threading.Interlocked.Exchange(ref pending, new Settings(gains, bass));

    private void Apply(Settings settings)
    {
        var rate = WaveFormat.SampleRate;
        headroom = (float)Math.Pow(10, -(Math.Max(0, settings.Gains.Max()) + settings.Bass + 3) / 20);
        for (var c = 0; c < filters.Length; c++)
        {
            var active = new List<BiQuadFilter>();
            for (var i = 0; i < frequencies.Length; i++)
                if (frequencies[i] < rate * .45 && Math.Abs(settings.Gains[i]) > .01)
                    active.Add(BiQuadFilter.PeakingEQ(rate, (float)frequencies[i], i < 10 ? 1.4f : .7f, (float)settings.Gains[i]));
            if (settings.Bass > 0)
                active.Add(BiQuadFilter.PeakingEQ(rate, 80, .7f, (float)settings.Bass));
            filters[c] = active.ToArray();
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (System.Threading.Interlocked.Exchange(ref pending, null) is { } settings) Apply(settings);
        var read = source.Read(buffer, offset, count);
        for (var i = offset; i < offset + read; i++)
        {
            var sample = buffer[i] * headroom;
            foreach (var filter in filters[channel]) sample = filter.Transform(sample);
            buffer[i] = Math.Clamp(sample, -.85f, .85f);
            channel = (channel + 1) % filters.Length;
        }
        return read;
    }
}

internal sealed class TestGainProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly float gain;
    private readonly bool studio, gate, compressor;
    private readonly float threshold, attack, release;
    private readonly BiQuadFilter[] hp, eq;
    private readonly bool highPass;
    private readonly float[] envelope, gateGain;
    private int channel;
    private readonly float noiseStrength, noiseLearn, noiseSmooth;
    private readonly float[] noiseFloor, noiseGain;
    public TestGainProvider(ISampleProvider source, float gain, bool studio, bool highPass, bool gate, double thresholdDb, bool compressor, double presence, double noise)
    {
        this.source = source; this.gain = gain; this.studio = studio; this.highPass = highPass; this.gate = gate; this.compressor = compressor;
        threshold = (float)Math.Pow(10, thresholdDb / 20);
        int rate = source.WaveFormat.SampleRate, channels = source.WaveFormat.Channels;
        attack = (float)Math.Exp(-1.0 / (rate * .005)); release = (float)Math.Exp(-1.0 / (rate * .15));
        hp = new BiQuadFilter[channels]; eq = new BiQuadFilter[channels];
        envelope = new float[channels]; gateGain = new float[channels];
        noiseLearn = (float)(1 - Math.Exp(-1.0 / (rate * .18)));
        noiseSmooth = (float)(1 - Math.Exp(-1.0 / (rate * .006)));
        noiseStrength = (float)Math.Clamp(noise / 100, 0, 1);
        noiseFloor = new float[channels]; noiseGain = new float[channels];
        Array.Fill(noiseFloor, .001f); Array.Fill(noiseGain, 1f); Array.Fill(gateGain, 1f);
        for (int i = 0; i < channels; i++) {
            hp[i] = BiQuadFilter.HighPassFilter(rate, 80, .707f);
            eq[i] = BiQuadFilter.PeakingEQ(rate, Math.Min(2500, rate / 4f), 1f, (float)presence);
        }
    }
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        for (int i = offset; i < offset + read; i++) {
            float v = buffer[i];
            if (studio) {
                if (highPass) v = hp[channel].Transform(v);
                v = eq[channel].Transform(v);
                float level = Math.Abs(v);
                float coeff = level > envelope[channel] ? attack : release;
                envelope[channel] = coeff * envelope[channel] + (1-coeff)*level;
                if (noiseStrength > 0) {
                    float env = envelope[channel];
                    float floor = noiseFloor[channel];
                    // Learn quiet stationary background, and freeze during louder speech.
                    if (env < .025f) floor += noiseLearn * (env - floor);
                    noiseFloor[channel] = Math.Clamp(floor, .0001f, .025f);
                    float ratio = Math.Clamp((env - noiseFloor[channel]) / Math.Max(env, .0001f), 0, 1);
                    float targetNoise = 1 - noiseStrength * (1 - ratio * ratio);
                    noiseGain[channel] += noiseSmooth * (targetNoise - noiseGain[channel]);
                    v *= noiseGain[channel];
                }
                if (gate) {
                    float target = envelope[channel] >= threshold ? 1f : .03f;
                    float smooth = target > gateGain[channel] ? attack : release;
                    gateGain[channel] = smooth * gateGain[channel] + (1-smooth)*target;
                    v *= gateGain[channel];
                }
                if (compressor && envelope[channel] > .125f)
                    v *= (float)Math.Pow(.125f / envelope[channel], .6667);
            }
            buffer[i] = .35f * (float)Math.Tanh(v * gain);
            channel = (channel + 1) % WaveFormat.Channels;
        }
        return read;
    }
}

internal static class ChannelLayout
{
    public static int GetMask(WaveFormat format)
    {
        if (format is not WaveFormatExtensible extensible) return 0;
        using var bytes = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(bytes);
        extensible.Serialize(writer);
        // NAudio serializes a 4-byte length before WAVEFORMATEXTENSIBLE.
        return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.GetBuffer().AsSpan(24, 4));
    }
}

internal static class StereoSignal
{
    public static byte[] Create(int channel)
    {
        if (channel is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(channel));
        const int sampleRate = 48000;
        const int frames = sampleRate / 2;
        var samples = new byte[frames * 4];
        for (var frame = 0; frame < frames; frame++)
        {
            var fade = Math.Min(1, Math.Min(frame, frames - 1 - frame) / 480.0);
            var sample = (short)(Math.Sin(frame * 2 * Math.PI * 440 / sampleRate) * 1800 * fade);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(frame * 4 + channel * 2, 2), sample);
        }
        return samples;
    }
}
