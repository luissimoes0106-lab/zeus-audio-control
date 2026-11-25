namespace ZeusAudioControl;

internal sealed record GamingBinding(string Executable, string Preset, int Priority, double[]? Equalizer = null);

internal static class GamingRules
{
    public static double[] Preset(string preset) => preset switch
    {
        "fps" => [-5, -4, -3, -2, 0, 2, 4, 4, 2, 0, 0, 0, 0, 0],
        "immersive" => [3, 4, 3, 1, 0, -1, 1, 2, 2, 1, 3, 0, 0, 0],
        _ => new double[14]
    };

    public static GamingBinding? Match(IEnumerable<GamingBinding> bindings, IEnumerable<string> running)
    {
        var paths = running.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return bindings.Where(rule => paths.Contains(rule.Executable))
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Executable, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    public static double[]? RouteEq(double[]? manualGaming, double[]? detectedGaming, double[]? foregroundProfile) =>
        manualGaming ?? detectedGaming ?? foregroundProfile;
}
