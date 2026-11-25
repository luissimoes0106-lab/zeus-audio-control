namespace ZeusAudioControl;

internal sealed record VoiceAssociation(string Executable, string Preset, int Priority);
internal readonly record struct VoiceMeasurement(double Rms, double Peak);

internal static class VoiceRules
{
    public static VoiceAssociation? Match(IEnumerable<VoiceAssociation> bindings, IEnumerable<string> running)
    {
        var paths = running.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return bindings.Where(binding => paths.Contains(binding.Executable))
            .OrderByDescending(binding => binding.Priority)
            .ThenBy(binding => binding.Executable, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    public static StudioProfile Preset(string preset, StudioProfile basis)
    {
        var voice = preset switch
        {
            "whatsapp" => basis with { Gain = 4, Gate = true, Threshold = -47, Presence = 1.5, Compressor = true, Ratio = 2.5 },
            "meet" => basis with { Gain = 5, Gate = true, Threshold = -43, Presence = 2, Compressor = true, Ratio = 3 },
            "obs" => basis with { Gain = 2, Gate = false, Presence = 1, Compressor = true, Ratio = 2 },
            "discord" => basis with { Gain = 4, Gate = true, Threshold = -45, Presence = 2.5, Compressor = true, Ratio = 3 },
            "gaming" => basis with { Gain = 3, Gate = true, Threshold = -43, Presence = 3, Compressor = true, Ratio = 3 },
            _ => basis
        };
        return preset is "whatsapp" or "meet" or "obs" or "discord" or "gaming"
            ? voice with { Enabled = true, HighPass = true, AutomaticGain = preset != "obs" }
            : voice;
    }

    public static StudioProfile Calibrate(StudioProfile basis, IReadOnlyList<VoiceMeasurement> samples)
    {
        if (samples.Count != 4 || samples.Any(s => !double.IsFinite(s.Rms) || !double.IsFinite(s.Peak) ||
            s.Rms > s.Peak || s.Peak > 0))
            throw new InvalidOperationException("Four valid digital measurements are required.");
        var noise = samples[0];
        var quiet = samples[1];
        var normal = samples[2];
        var loud = samples[3];
        if (samples.Skip(1).Any(s => s.Peak >= -.2))
            throw new InvalidOperationException("Speech clipped. Lower the input level and repeat.");
        if (quiet.Rms < noise.Rms + 6 || normal.Rms < quiet.Rms || loud.Rms < normal.Rms)
            throw new InvalidOperationException("Repeat the test with less background noise and distinct speech levels.");
        var peak = samples.Skip(1).Max(s => s.Peak);
        var gain = Math.Clamp(Math.Min(-24 - quiet.Rms, -6 - peak), 0, 24);
        var gate = Math.Clamp(Math.Min(noise.Rms + 8, quiet.Rms - 4), -65, -20);
        var threshold = Math.Clamp(normal.Rms + Math.Min(5, (loud.Rms - normal.Rms) / 2), -48, -3);
        return basis with
        {
            Enabled = true, Gain = gain, Gate = true, Threshold = gate, Compressor = true,
            CompressorThreshold = threshold, Ratio = 3, AutomaticGain = false,
            Limiter = peak > -3 ? .30 : .35
        };
    }
}
