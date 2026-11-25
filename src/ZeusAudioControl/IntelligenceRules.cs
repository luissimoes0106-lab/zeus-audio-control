namespace ZeusAudioControl;

internal sealed record AppProfile(string Executable, string File, int Priority);
internal sealed record EndpointPreference(double Volume, bool Mute);

internal static class IntelligenceRules
{
    public static int? BatteryPercent(int? reading) => reading is >= 0 and <= 100 ? reading : null;

    public static AppProfile? ForegroundProfile(string executable, IEnumerable<AppProfile> profiles) =>
        profiles.Where(profile => string.Equals(profile.Executable, executable, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(profile => profile.Priority).FirstOrDefault();

    public static double Limit(double value, double maximum) =>
        !double.IsFinite(value) || !double.IsFinite(maximum) ? 0 : Math.Clamp(value, 0, Math.Clamp(maximum, 0, 100));

    public static StudioProfile Calibrate(StudioProfile current, double noise, double speech, double peak)
    {
        if (!double.IsFinite(noise) || !double.IsFinite(speech) || !double.IsFinite(peak) || speech < noise + 6 || peak >= -.2)
            throw new InvalidOperationException("Capture a quieter background and repeat speech without clipping.");
        var boost = Math.Clamp(Math.Min(-20 - speech, -6 - peak), 0, 24);
        var gate = Math.Clamp(Math.Min(noise + 8, speech - 5), -65, -20);
        return current with
        {
            Gain = boost, Threshold = gate, Enabled = true, Gate = true, Compressor = true,
            CompressorThreshold = Math.Clamp(speech + 5, -48, -3), Ratio = 3
        };
    }
}
