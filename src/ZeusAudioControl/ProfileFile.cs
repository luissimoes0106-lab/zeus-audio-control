using System.IO;
using System.Text.Json;

namespace ZeusAudioControl;

internal static class ProfileFile
{
    private const int MaximumBytes = 65536;

    public static T Read<T>(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = new byte[MaximumBytes + 1];
        var count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count > MaximumBytes)
            throw new InvalidDataException("Profile exceeds 64 KiB.");
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count))
            ?? throw new InvalidDataException("Profile is empty.");
    }

    public static void Write<T>(string path, T value)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("Profile exceeds 64 KiB.");

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}


internal sealed record StudioProfile(double Gain, bool Enabled, bool HighPass, bool Gate,
    double Threshold, bool Compressor, double Presence, double Noise = 0,
    double Attack = 5, double Hold = 0, double Release = 150,
    double CompressorThreshold = -18.06179974, double Ratio = 3, bool Shield = false, bool AutomaticGain = false, double Limiter = .35);

internal sealed record ExportedProfile(int Version, StudioProfile Voice, double[]? Equalizer)
{
    public static ExportedProfile Load(string path)
    {
        var document = ProfileFile.Read<JsonElement>(path);
        if (document.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected a ZEUS profile object.");

        if (!document.TryGetProperty(nameof(Version), out _))
            return new ExportedProfile(0, ReadVoice(document), null);

        if (!document.TryGetProperty(nameof(Version), out var number) || number.ValueKind != JsonValueKind.Number ||
            !number.TryGetInt32(out var version) || version != 1 ||
            !document.TryGetProperty(nameof(Voice), out var voice) ||
            !document.TryGetProperty(nameof(Equalizer), out var equalizer) ||
            equalizer.ValueKind != JsonValueKind.Array ||
            equalizer.GetArrayLength() is not (11 or 14))
            throw new InvalidDataException("Unsupported or invalid ZEUS profile.");

        var values = equalizer.Deserialize<double[]>();
        if (!ValidEq(values)) throw new InvalidDataException("Invalid equalizer values.");
        return new ExportedProfile(version, ReadVoice(voice), values);
    }

    public static bool ValidEq(double[]? values) =>
        values is { Length: 11 or 14 } && values.All(double.IsFinite);

    private static StudioProfile ReadVoice(JsonElement voice)
    {
        if (voice.ValueKind != JsonValueKind.Object ||
            !voice.TryGetProperty(nameof(StudioProfile.Gain), out _) ||
            !voice.TryGetProperty(nameof(StudioProfile.Threshold), out _) ||
            !voice.TryGetProperty(nameof(StudioProfile.Presence), out _))
            throw new InvalidDataException("Invalid voice profile.");

        var profile = voice.Deserialize<StudioProfile>()!;
        if (new[] { profile.Gain, profile.Threshold, profile.Presence, profile.Noise, profile.Attack, profile.Hold, profile.Release, profile.CompressorThreshold, profile.Ratio, profile.Limiter }.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException("Invalid voice profile values.");
        return profile;
    }
}
