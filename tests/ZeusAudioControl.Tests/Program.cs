using NAudio.Wave;
using ZeusAudioControl;

var tests = new (string, Action)[]
{
    ("Route buffer bounds latency, counts drops and fills underflow with silence", () =>
    {
        var buffer = new RouteBuffer(WaveFormat.CreateIeeeFloatWaveFormat(1000, 1), 40);
        var data = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        buffer.Write(data, 120);
        buffer.Write(data, 120);
        Check(buffer.Overflows == 1 && buffer.BufferedMilliseconds <= 40);
        var destination = new byte[160];
        Check(buffer.Read(destination, 0, 160) == 160);
        Check(buffer.Read(destination, 0, 160) == 160);
        Check(buffer.Underflows > 0 && destination.All(value => value == 0));
    }),
    ("Bounded buffer survives repeated capture and render drift", () =>
    {
        var buffer = new RouteBuffer(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), 100);
        var packet = new byte[480 * 8];
        for (var i = 0; i < 10000; i++)
        {
            buffer.Write(packet, packet.Length);
            if (i % 37 != 0) buffer.Read(packet, 0, packet.Length);
            Check(buffer.BufferedMilliseconds <= 100);
        }
        Check(buffer.Overflows > 0);
    }),
    ("Playback dynamics bounds overload and normalizes a quiet stable signal", () =>
    {
        var limiter = new RouteDynamics(new Signal(1, _ => 5f), false, false, .85f);
        var data = new float[48000];
        limiter.Read(data, 0, data.Length);
        Check(data.All(sample => sample == .85f));
        var normalizer = new RouteDynamics(new Signal(1, _ => .04f), false, true, .85f);
        normalizer.Read(data, 0, data.Length);
        Check(data[^1] > .06f && data[^1] <= .08f);
    }),
    ("Spatialization delays the contralateral surround signal", () =>
    {
        var spatial = new StereoSpatializer(new Signal(6, i => i == 4 ? 1f : 0f));
        var data = new float[500];
        Check(spatial.Read(data, 0, data.Length) == data.Length);
        Check(data[0] > 0 && data[1] == 0 && data[2 * 192 + 1] > 0);
    }),
    ("Channel test puts a tone only in the addressed channel", () =>
    {
        var bytes = StereoSignal.Create(5, 8, 48000);
        Check(bytes.Length == 24000 * 16);
        Check(BitConverter.ToInt16(bytes, 16 * 80 + 10) != 0);
        Check(Enumerable.Range(0, 8).Where(channel => channel != 5)
            .All(channel => BitConverter.ToInt16(bytes, 16 * 80 + channel * 2) == 0));
    }),

    ("EQ attenuates peaks without leaking between channels", () =>
    {
        var eq = new LocalEq(new Signal(2, i => i % 2 == 0 ? 4f : 0f),
            [1000], [9], 3);
        var buffer = new float[2049];
        Check(eq.Read(buffer, 1, 2048) == 2048);
        for (var i = 1; i < buffer.Length; i += 2)
            Check(Math.Abs(buffer[i]) <= .85f && buffer[i + 1] == 0);
    }),
    ("Microphone high-pass attenuates rumble", () =>
    {
        float Rms(int hz)
        {
            var voice = new TestGainProvider(new Signal(1, i => .1f * MathF.Sin(i * 2 * MathF.PI * hz / 48000)),
                1, true, true, false, -45, false, 0, 0);
            var data = new float[48000];
            voice.Read(data, 0, data.Length);
            return MathF.Sqrt(data.Skip(24000).Select(v => v * v).Average());
        }
        Check(Rms(20) < Rms(1000) / 8);
    }),
    ("EQ audibly boosts the configured band relative to unrelated frequencies", () =>
    {
        float Rms(int hz, double gain)
        {
            var eq = new LocalEq(new Signal(1, i => .1f * MathF.Sin(i * 2 * MathF.PI * hz / 48000)),
                [1000], [gain], 0);
            var data = new float[48000];
            eq.Read(data, 0, data.Length);
            return MathF.Sqrt(data.Skip(24000).Select(v => v * v).Average());
        }
        Check(Rms(1000, 9) > 2 * Rms(4000, 9));
    }),
    ("EQ changes apply on the next audio read without reopening the source", () =>
    {
        var input = new Signal(1, i => .15f * MathF.Sin(i * 2 * MathF.PI * 1000 / 48000));
        var eq = new LocalEq(input, [1000], [0], 0);
        var data = new float[48000];
        eq.Read(data, 0, data.Length);
        var before = data.Skip(24000).Max(Math.Abs);
        eq.SetGains([-12], 0);
        eq.Read(data, 0, data.Length);
        Check(data.Skip(24000).Max(Math.Abs) < before / 3);
    }),
    ("Voice gate attenuates constant low signals and limiter caps peaks", () =>
    {
        float Finish(float sample, bool gate)
        {
            var provider = new TestGainProvider(new Signal(1, _ => sample), 8,
                true, false, gate, -40, true, 0, 0);
            var data = new float[48000];
            provider.Read(data, 0, data.Length);
            return data[^1];
        }
        Check(Finish(.001f, true) < Finish(.001f, false) / 10);
        Check(Finish(1, false) <= .35f);
    }),
    ("Gate hold preserves quiet tails after a loud burst", () =>
    {
        float Tail(int hold)
        {
            var voice = new TestGainProvider(new Signal(1, i => i < 2400 ? .2f : .001f), 1,
                true, false, true, -26, false, 0, 0, 1, hold, 10);
            var data = new float[7200];
            voice.Read(data, 0, data.Length);
            return data[^1];
        }
        Check(Tail(200) > 10 * Tail(0));
    }),
    ("Noise reduction attenuates a steady low-frequency tone without removing a louder voice-like segment", () =>
    {
        float[] Process(double strength)
        {
            var signal = new Signal(1, i => .014f * MathF.Sin(i * 2 * MathF.PI * 150 / 48000)
                + (i < 48000 ? 0 : .18f * MathF.Sin(i * 2 * MathF.PI * 1000 / 48000)));
            var voice = new TestGainProvider(signal, 1, true, false, false, -45, false, 0, strength);
            var data = new float[72000];
            voice.Read(data, 0, data.Length);
            return data;
        }
        static double Energy(float[] data, int offset) =>
            data.Skip(offset).Take(8000).Average(value => value * value);
        var original = Process(0);
        var filtered = Process(100);
        Check(Energy(filtered, 30000) < Energy(original, 30000) * .1);
        Check(Energy(filtered, 64000) > Energy(original, 64000) * .65);
    }),
    ("Stereo test signal respects channel selection and fades boundaries", () =>
    {
        for (var side = 0; side <= 1; side++)
        {
            var pcm = StereoSignal.Create(side);
            var audible = false;
            for (var i = 0; i < pcm.Length; i += 4)
            {
                Check(BitConverter.ToInt16(pcm, i + (1 - side) * 2) == 0);
                audible |= BitConverter.ToInt16(pcm, i + side * 2) != 0;
            }
            Check(audible && BitConverter.ToInt32(pcm, 0) == 0 && BitConverter.ToInt32(pcm, pcm.Length - 4) == 0);
        }
    }),
    ("Plain and extensible channel masks are decoded", () =>
    {
        Check(ChannelLayout.GetMask(new WaveFormat(48000, 16, 2)) == 0);
        Check(ChannelLayout.GetMask(new WaveFormatExtensible(48000, 32, 8)) == 0xFF);
    }),
    ("Versioned and legacy profile import round-trip and reject malformed JSON", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var voice = new StudioProfile(6, true, true, true, -45, true, 2);
            var gains = new double[14];
            gains[3] = -5;
            ProfileFile.Write(path, new ExportedProfile(1, voice, gains));
            var imported = ExportedProfile.Load(path);
            Check(imported.Voice == voice && imported.Equalizer!.SequenceEqual(gains));
            ProfileFile.Write(path, voice);
            Check(ExportedProfile.Load(path) == new ExportedProfile(0, voice, null));
            File.WriteAllText(path, """{"Gain":6,"Enabled":true,"HighPass":true,"Gate":true,"Threshold":-45,"Compressor":true,"Presence":2}""");
            var legacy = ExportedProfile.Load(path).Voice;
            Check(legacy.Attack == 5 && legacy.Hold == 0 && legacy.Release == 150 && legacy.Ratio == 3);
            File.WriteAllText(path, "{");
            try { ExportedProfile.Load(path); }
            catch (System.Text.Json.JsonException) { return; }
            throw new Exception("Malformed profile accepted.");
        }
        finally { File.Delete(path); }
    }),
    ("Profile overwrite, import and malformed data", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            ProfileFile.Write(path, new[] { 2d, 3d });
            ProfileFile.Write(path, new[] { 1d });
            Check(ProfileFile.Read<double[]>(path).SequenceEqual(new[] { 1d }));
            File.WriteAllText(path, new string(' ', 65537));
            try { ProfileFile.Read<double[]>(path); }
            catch (InvalidDataException) { return; }
            throw new Exception("Oversized profile accepted.");
        }
        finally { File.Delete(path); }
    })
};
var failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void Check(bool condition)
{
    if (!condition) throw new Exception("Assertion failed.");
}

sealed class Signal(int channels, Func<int, float> sample) : ISampleProvider
{
    private int position;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = offset; i < offset + count; i++) buffer[i] = sample(position++);
        return count;
    }
}
