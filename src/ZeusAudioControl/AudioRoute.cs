using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZeusAudioControl;

internal sealed class AudioRoute(int outputBufferMilliseconds = 40)
{
    private readonly CancellationTokenSource stop = new();
    private int bypass;
    internal AudioTap? Before;
    internal AudioTap? After;
    internal RouteBuffer? Buffer;
    internal LocalEq? Eq;
    internal long Discontinuities;
    internal double CaptureLatencyMilliseconds;
    internal int OutputBufferMilliseconds { get; } = outputBufferMilliseconds;
    private int telemetry;
    private float protection = .85f;
    public void SetProtection(double? percentage) =>
        Volatile.Write(ref protection, percentage is null ? .85f : (float)(.85 * IntelligenceRules.Limit(percentage.Value, 100) / 100));
    public void SetTelemetry(bool enabled) => Volatile.Write(ref telemetry, enabled ? 1 : 0);

    public Task Run(string inputId, string outputId, RouteKind kind, double[] frequencies, double[] gains, double bass,
        StudioProfile studio, bool compressor, bool normalize, Action onStarted)
    {
        return Task.Run(() =>
        {
            try { Pump(inputId, outputId, kind, frequencies, gains, bass, studio, compressor, normalize, onStarted); }
            finally { stop.Dispose(); }
        });
    }

    public void Stop()
    {
        try { stop.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void SetBypass(bool original) => Volatile.Write(ref bypass, original ? 1 : 0);

    private void Pump(string inputId, string outputId, RouteKind kind, double[] frequencies, double[] gains, double bass,
        StudioProfile studio, bool compressor, bool normalize, Action onStarted)
    {
        using var deviceList = new MMDeviceEnumerator();
        using var input = deviceList.GetDevice(inputId);
        using var output = deviceList.GetDevice(outputId);
        using var client = input.AudioClient;
        using var wake = new AutoResetEvent(false);
        using var failed = new ManualResetEvent(false);
        var mix = client.MixFormat;
        var format = mix is WaveFormatExtensible extended ? extended.ToStandardWaveFormat() : mix;
        client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.NoPersist,
            0, 0, mix, Guid.Empty);
        client.SetEventHandle(wake.SafeWaitHandle.DangerousGetHandle());
        CaptureLatencyMilliseconds = client.StreamLatency / 10000.0;
        var capture = client.AudioCaptureClient;
        var bytes = new byte[checked(client.BufferSize * mix.BlockAlign)];
        var buffer = new RouteBuffer(format, OutputBufferMilliseconds == 25 ? 65 : 100);
        Volatile.Write(ref Buffer, buffer);
        var before = new AudioTap(buffer.ToSampleProvider(), () => Volatile.Read(ref telemetry) != 0);
        Volatile.Write(ref Before, before);
        ISampleProvider processed;
        if (kind == RouteKind.Playback)
        {
            var eq = new LocalEq(before, frequencies, gains, bass);
            Volatile.Write(ref Eq, eq);
            processed = new RouteDynamics(eq, compressor, normalize, .85f, () => Volatile.Read(ref protection));
        }
        else
        {
            processed = new TestGainProvider(before, (float)Math.Pow(10, studio.Gain / 20),
                studio.Enabled, studio.HighPass, studio.Gate, studio.Threshold, studio.Compressor, studio.Presence, studio.Noise,
                studio.Attack, studio.Hold, studio.Release, studio.CompressorThreshold, studio.Ratio, studio.Shield, studio.AutomaticGain);
        }
        if (kind == RouteKind.Headphones)
            processed = new RouteDynamics(new PreviewSource(before, processed, () => Volatile.Read(ref bypass) != 0), false, false, .12f);
        var after = new AudioTap(processed, () => Volatile.Read(ref telemetry) != 0);
        Volatile.Write(ref After, after);
        Exception? playbackError = null;
        using var player = new WasapiOut(output, AudioClientShareMode.Shared, true, OutputBufferMilliseconds);
        player.PlaybackStopped += (_, args) =>
        {
            playbackError = args.Exception;
            failed.Set();
        };
        player.Init(new SampleToWaveProvider(after));
        if (stop.IsCancellationRequested) return;
        client.Start();
        try
        {
            WaitHandle[] events = [stop.Token.WaitHandle, failed, wake];
            var prefill = System.Diagnostics.Stopwatch.StartNew();
            while (buffer.BufferedMilliseconds < (OutputBufferMilliseconds == 25 ? 15 : 25) && prefill.ElapsedMilliseconds < 750)
            {
                if (WaitHandle.WaitAny(events, 25) == 0) return;
                DrainCapture();
            }
            if (stop.IsCancellationRequested) return;
            player.Play();
            onStarted();
            while (!stop.IsCancellationRequested)
            {
                var signal = WaitHandle.WaitAny(events, 500);
                if (signal == 0) break;
                if (signal == 1) throw playbackError ?? new InvalidOperationException("The output stream stopped.");
                DrainCapture();
            }
        }
        finally
        {
            try { player.Stop(); }
            finally
            {
                try { client.Stop(); }
                catch (COMException) when (stop.IsCancellationRequested) { }
            }
        }

        void DrainCapture()
        {
            while (!stop.IsCancellationRequested && capture.GetNextPacketSize() > 0)
            {
                var data = capture.GetBuffer(out var frames, out var flags);
                try
                {
                    var count = checked(frames * mix.BlockAlign);
                    if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0)
                        Interlocked.Increment(ref Discontinuities);
                    if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(bytes, 0, count);
                    else Marshal.Copy(data, bytes, 0, count);
                    buffer.Write(bytes, count);
                }
                finally { capture.ReleaseBuffer(frames); }
            }
        }
    }

    private sealed class PreviewSource(AudioTap before, ISampleProvider wet, Func<bool> original) : ISampleProvider
    {
        private float[] dry = [];
        public WaveFormat WaveFormat => wet.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            if (dry.Length < count) dry = new float[count];
            before.Mirror = dry;
            var read = wet.Read(buffer, offset, count);
            if (original()) Array.Copy(dry, 0, buffer, offset, read);
            return read;
        }
    }
}

internal sealed class AudioTap(ISampleProvider source, Func<bool> telemetry) : ISampleProvider
{
    private const int Size = 2048;
    private readonly float[] history = new float[Size];
    private readonly object gate = new();
    private int index, channel;
    private float peak;
    public float[]? Mirror { get; set; }
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        if (Mirror is { } mirror) Array.Copy(buffer, offset, mirror, 0, read);
        if (!telemetry()) return read;
        var currentPeak = 0f;
        lock (gate)
        {
            for (var i = offset; i < offset + read; i++)
            {
                var sample = buffer[i];
                if (!float.IsFinite(sample)) sample = 0;
                currentPeak = Math.Max(currentPeak, Math.Abs(sample));
                if (channel == 0)
                {
                    history[index] = sample;
                    index = (index + 1) % Size;
                }
                channel = (channel + 1) % WaveFormat.Channels;
            }
            peak = Math.Max(currentPeak, peak);
        }
        return read;
    }

    public float ReadSnapshot(float[] samples)
    {
        lock (gate)
        {
            Array.Copy(history, index, samples, 0, Size - index);
            Array.Copy(history, 0, samples, Size - index, index);
            var value = peak;
            peak = 0;
            return value;
        }
    }
}
