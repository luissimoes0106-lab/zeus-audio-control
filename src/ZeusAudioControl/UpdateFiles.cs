using System.IO;
using System.Security.Cryptography;

namespace ZeusAudioControl;

internal static class UpdateFiles
{
    public const long MaximumPackage = 256L * 1024 * 1024;
    public static readonly string[] Settings = ["devices.json", "studio.json", "eq.json", "aliases.json", "preferences.json"];

    public static string HashFor(string manifest, string name)
    {
        var matching = manifest.Split('\n').Select(line => line.TrimEnd('\r').Split(' ', 2))
            .Where(fields => fields.Length == 2 && fields[1].TrimStart(' ', '*') == name).ToArray();
        if (matching.Length != 1 || matching[0][0].Length != 64 || !matching[0][0].All(Uri.IsHexDigit))
            throw new InvalidDataException("Missing, duplicate or invalid SHA-256 entry.");
        return matching[0][0];
    }

    public static async Task<string> CopyVerified(Stream stream, string file, string hash, long maximum, CancellationToken token)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                long total = 0;
                int count;
                while ((count = await stream.ReadAsync(buffer, token)) != 0)
                {
                    total += count;
                    if (total > maximum) throw new InvalidDataException("Package exceeds the size limit.");
                    digest.AppendData(buffer, 0, count);
                    await destination.WriteAsync(buffer.AsMemory(0, count), token);
                }
            }
            if (!string.Equals(Convert.ToHexString(digest.GetHashAndReset()), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 mismatch.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, file);
            return file;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task Backup(string executable, string settingsPath, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var name in Settings.Prepend("ZeusAudioControl.exe"))
        {
            var source = name == "ZeusAudioControl.exe" ? executable : Path.Combine(settingsPath, name);
            if (!File.Exists(source)) continue;
            await using var data = File.OpenRead(source);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(data, token));
            data.Position = 0;
            await CopyVerified(data, Path.Combine(destination, name), hash, MaximumPackage, token);
            await File.AppendAllTextAsync(Path.Combine(destination, "SHA256SUMS.txt"), $"{hash}  {name}\n", token);
        }
    }

    public static async Task Recover(string backup, string destination, CancellationToken token)
    {
        var manifest = await File.ReadAllTextAsync(Path.Combine(backup, "SHA256SUMS.txt"), token);
        var staging = destination + ".partial";
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var name in Settings.Prepend("ZeusAudioControl.exe"))
            {
                var file = Path.Combine(backup, name);
                if (name != "ZeusAudioControl.exe" && !File.Exists(file) &&
                    !manifest.Split('\n').Any(line => line.TrimEnd('\r').EndsWith("  " + name, StringComparison.Ordinal)))
                    continue;
                await using var data = File.OpenRead(file);
                await CopyVerified(data, Path.Combine(staging, name), HashFor(manifest, name), MaximumPackage, token);
            }
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
