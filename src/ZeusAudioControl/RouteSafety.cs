namespace ZeusAudioControl;

internal enum RouteKind { Playback, Microphone, Headphones }

internal sealed record RouteEndpoints(RouteKind Kind, string Input, string Output, Guid? InputDevice, Guid? OutputDevice);

internal static class RouteSafety
{
    public static bool CanStart(RouteEndpoints proposed, IEnumerable<RouteEndpoints> existing)
    {
        if (proposed.Input == proposed.Output ||
            proposed.Kind != RouteKind.Headphones && proposed.InputDevice is { } input && input == proposed.OutputDevice)
            return false;

        foreach (var other in existing)
        {
            if (proposed.Kind == other.Kind || proposed.Kind == RouteKind.Headphones || other.Kind == RouteKind.Headphones ||
                proposed.Input == other.Input || proposed.Output == other.Output ||
                proposed.InputDevice is null || proposed.OutputDevice is null || other.InputDevice is null || other.OutputDevice is null)
                return false;

            var playback = proposed.Kind == RouteKind.Playback ? proposed : other;
            var voice = proposed.Kind == RouteKind.Microphone ? proposed : other;
            // The playback cable's recording and microphone cable's render ports must be on distinct cables.
            if (playback.InputDevice == voice.OutputDevice ||
                playback.InputDevice == voice.InputDevice || playback.OutputDevice == voice.OutputDevice)
                return false;
        }
        return true;
    }
}
