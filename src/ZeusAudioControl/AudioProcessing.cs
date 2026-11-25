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
    private readonly float compressorThreshold, compressorExponent;
    private readonly int holdFrames;
    private readonly int[] gateHold;
    private readonly BiQuadFilter[] hp, eq;
    private readonly bool highPass;
    private readonly float[] envelope, gateGain;
    private int channel;
    private readonly float noiseStrength, noiseLearn, noiseSmooth;
    private readonly float[] noiseFloor, noiseGain;
    public TestGainProvider(ISampleProvider source, float gain, bool studio, bool highPass, bool gate, double thresholdDb, bool compressor, double presence, double noise,
        double attackMs = 5, double holdMs = 0, double releaseMs = 150, double compressorDb = -18.06179974, double ratio = 3)
    {
        this.source = source; this.gain = gain; this.studio = studio; this.highPass = highPass; this.gate = gate; this.compressor = compressor;
        threshold = (float)Math.Pow(10, thresholdDb / 20);
        int rate = source.WaveFormat.SampleRate, channels = source.WaveFormat.Channels;
        attack = (float)Math.Exp(-1000.0 / (rate * Math.Clamp(attackMs, 1, 100)));
        release = (float)Math.Exp(-1000.0 / (rate * Math.Clamp(releaseMs, 10, 1000)));
        holdFrames = (int)(rate * Math.Clamp(holdMs, 0, 500) / 1000);
        gateHold = new int[channels];
        compressorThreshold = (float)Math.Pow(10, Math.Clamp(compressorDb, -48, -3) / 20);
        compressorExponent = (float)(1 - 1 / Math.Clamp(ratio, 1, 12));
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
                    if (envelope[channel] >= threshold) gateHold[channel] = holdFrames;
                    else if (gateHold[channel] > 0) gateHold[channel]--;
                    float target = envelope[channel] >= threshold || gateHold[channel] > 0 ? 1f : .03f;
                    float smooth = target > gateGain[channel] ? attack : release;
                    gateGain[channel] = smooth * gateGain[channel] + (1-smooth)*target;
                    v *= gateGain[channel];
                }
                if (compressor && envelope[channel] > compressorThreshold)
                    v *= (float)Math.Pow(compressorThreshold / envelope[channel], compressorExponent);
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
    public static byte[] Create(int channel, int channels = 2, int sampleRate = 48000)
    {
        if (channels is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(channels));
        if (channel < 0 || channel >= channels) throw new ArgumentOutOfRangeException(nameof(channel));
        if (sampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var frames = sampleRate / 2;
        var samples = new byte[frames * channels * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            var fade = Math.Min(1, Math.Min(frame, frames - 1 - frame) / (sampleRate * .01));
            var sample = (short)(Math.Sin(frame * 2 * Math.PI * 440 / sampleRate) * 1800 * fade);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan((frame * channels + channel) * 2, 2), sample);
        }
        return samples;
    }
}

internal sealed class RouteBuffer : IWaveProvider
{
    private readonly BufferedWaveProvider buffer;
    private readonly object gate = new();
    private long overflows, underflows;

    public RouteBuffer(WaveFormat format, int milliseconds)
    {
        var frames = Math.Max(1, format.SampleRate * milliseconds / 1000);
        buffer = new BufferedWaveProvider(format) { BufferLength = frames * format.BlockAlign };
    }

    public WaveFormat WaveFormat => buffer.WaveFormat;
    public long Overflows => System.Threading.Interlocked.Read(ref overflows);
    public long Underflows => System.Threading.Interlocked.Read(ref underflows);
    public double BufferedMilliseconds
    {
        get { lock (gate) return buffer.BufferedDuration.TotalMilliseconds; }
    }

    public void Write(byte[] data, int count)
    {
        lock (gate)
        {
            if (count == 0) return;
            if (count + buffer.BufferedBytes > buffer.BufferLength)
            {
                buffer.ClearBuffer();
                System.Threading.Interlocked.Increment(ref overflows);
            }
            var keep = Math.Min(count, buffer.BufferLength);
            buffer.AddSamples(data, count - keep, keep);
        }
    }

    public int Read(byte[] destination, int offset, int count)
    {
        lock (gate)
        {
            if (buffer.BufferedBytes < count)
                System.Threading.Interlocked.Increment(ref underflows);
            return buffer.Read(destination, offset, count);
        }
    }
}

internal sealed class RouteDynamics(ISampleProvider source, bool compressor, bool normalize, float cap) : ISampleProvider
{
    private float peak;
    private float power;
    private readonly float peakRelease = (float)Math.Exp(-1.0 / (source.WaveFormat.SampleRate * source.WaveFormat.Channels * .12));
    private readonly float smoothing = (float)(1 - Math.Exp(-1.0 / (source.WaveFormat.SampleRate * source.WaveFormat.Channels * .5)));

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        for (var i = offset; i < offset + read; i++)
        {
            var sample = float.IsFinite(buffer[i]) ? buffer[i] : 0;
            if (compressor) peak = Math.Max(Math.Abs(sample), peak * peakRelease);
            if (normalize) power += smoothing * (sample * sample - power);
            var gain = normalize ? Math.Clamp(.14f / MathF.Sqrt(Math.Max(.0001f, power)), .25f, 2f) : 1f;
            if (compressor && peak * gain > .22f)
                gain *= MathF.Pow(.22f / (peak * gain), .75f);
            buffer[i] = Math.Clamp(sample * gain, -cap, cap);
        }
        return read;
    }
}

/// <summary>Stereo spatialization of canonical WAVE channel order using crossfeed and short delays.</summary>
internal sealed class StereoSpatializer : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly float[] leftDelay;
    private readonly float[] rightDelay;
    private float[] work = [];
    private int cursor;

    public StereoSpatializer(ISampleProvider source)
    {
        if (source.WaveFormat.Channels is not (6 or 8)) throw new ArgumentException("Expected 5.1 or 7.1 PCM.");
        this.source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        leftDelay = new float[Math.Max(1, WaveFormat.SampleRate * 4 / 1000)];
        rightDelay = new float[leftDelay.Length];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = source.WaveFormat.Channels;
        var requested = count / 2 * channels;
        if (work.Length < requested) work = new float[requested];
        var frames = source.Read(work, 0, requested) / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var center = work[i + 2] * .707f + work[i + 3] * .20f;
            var rearLeft = work[i + 4] * .60f + (channels == 8 ? work[i + 6] * .50f : 0);
            var rearRight = work[i + 5] * .60f + (channels == 8 ? work[i + 7] * .50f : 0);
            buffer[offset + frame * 2] = .38f * (work[i] + center + rearLeft + .30f * rightDelay[cursor]);
            buffer[offset + frame * 2 + 1] = .38f * (work[i + 1] + center + rearRight + .30f * leftDelay[cursor]);
            leftDelay[cursor] = rearLeft;
            rightDelay[cursor] = rearRight;
            cursor = (cursor + 1) % leftDelay.Length;
        }
        return frames * 2;
    }
}
