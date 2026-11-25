using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private sealed class IntelligenceSettings
    {
        public bool Automation { get; set; }
        public bool VoiceAutomation { get; set; }
        public List<VoiceAssociation> VoiceAssociations { get; set; } = [];
        public bool GamingAuto { get; set; }
        public bool GamingLowLatency { get; set; }
        public List<GamingBinding> Games { get; set; } = [];
        public string? CommunicationsApp { get; set; }
        public List<AppProfile> Profiles { get; set; } = [];
        public Dictionary<string, EndpointPreference> Endpoints { get; set; } = [];
        public Dictionary<string, float> Sessions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string[] Shortcuts { get; set; } = ["Ctrl+Alt+Up", "Ctrl+Alt+Down", "Ctrl+Alt+M", "Ctrl+Alt+I"];
        public bool EnableShortcuts { get; set; }
        public bool FloatingPanel { get; set; }
        public double? HearingLimit { get; set; }
        public string? BatteryId { get; set; }
        public StudioProfile? CalibrationBackup { get; set; }
        public List<string> Disconnects { get; set; } = [];
    }

    private IntelligenceSettings _intelligence = new();
    private string IntelligencePath => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "intelligence.json");
    private EndpointNotifications? _endpointNotifications;
    private bool _endpointsChanged = true;
    private readonly HashSet<string> _previousEndpoints = new(StringComparer.Ordinal);
    private readonly HashSet<int> _registeredHotkeys = [];
    private HwndSource? _hotkeySource;
    private DateTime _lastExposure = DateTime.UtcNow;
    private TimeSpan _elevated;
    private string? _foregroundExecutable;
    private double[]? _automaticEq;
    private IntPtr _foregroundHook;
    private ForegroundEvent? _foregroundCallback;
    private System.Windows.Threading.DispatcherTimer? _foregroundDebounce;
    private Window? _quickControl;

    private void StartIntelligence()
    {
        try { if (File.Exists(IntelligencePath)) _intelligence = ProfileFile.Read<IntelligenceSettings>(IntelligencePath); }
        catch (Exception error) { Footer.Text = error.Message; }
        try
        {
            _endpointNotifications = new EndpointNotifications(() => Dispatcher.BeginInvoke(() => _endpointsChanged = true));
            _enumerator.RegisterEndpointNotificationCallback(_endpointNotifications);
        }
        catch (Exception error) { _endpointNotifications = null; Footer.Text = error.Message; }
        SourceInitialized += (_, _) =>
        {
            _hotkeySource = PresentationSource.FromVisual(this) as HwndSource;
            _hotkeySource?.AddHook(HandleHotkey);
            RegisterShortcuts();
        };
    }

    private void StopIntelligence()
    {
        ClearShortcuts();
        if (_hotkeySource is { IsDisposed: false }) _hotkeySource.RemoveHook(HandleHotkey);
        StopForeground();
        if (_endpointNotifications is { } client)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(client); }
            catch (COMException) { }
        }
    }

    private void SaveIntelligence()
    {
        try { ProfileFile.Write(IntelligencePath, _intelligence); }
        catch (Exception error) { Footer.Text = error.Message; }
    }

    private void RememberEndpoint(MMDevice device)
    {
        var volume = device.AudioEndpointVolume;
        if (!_intelligence.Endpoints.ContainsKey(device.ID) && _intelligence.Endpoints.Count >= 64) return;
        _intelligence.Endpoints[device.ID] = new EndpointPreference(volume.MasterVolumeLevelScalar * 100, volume.Mute);
        _intelligenceDirty = true;
    }

    private bool _intelligenceDirty;
    private void RestoreEndpoint(MMDevice? device, bool output)
    {
        if (device is null || !_intelligence.Endpoints.TryGetValue(device.ID, out var preference)) return;
        try
        {
            var keepMuted = !output && device.AudioEndpointVolume.Mute;
            device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(IntelligenceRules.Limit(preference.Volume,
                output ? _intelligence.HearingLimit ?? 100 : 100) / 100);
            device.AudioEndpointVolume.Mute = preference.Mute || keepMuted;
        }
        catch (COMException error) { Footer.Text = error.Message; }
    }

    private void RecordEndpointChanges(IEnumerable<MMDevice> devices)
    {
        var active = devices.Select(device => device.ID).ToHashSet(StringComparer.Ordinal);
        var disconnected = _previousEndpoints.Except(active).ToArray();
        foreach (var id in disconnected)
        {
            var label = _aliases.GetValueOrDefault(id, id);
            _intelligence.Disconnects.Add($"{DateTimeOffset.Now:O} · {label}");
        }
        if (disconnected.Length != 0)
        {
            if (_intelligence.Disconnects.Count > 40)
                _intelligence.Disconnects.RemoveRange(0, _intelligence.Disconnects.Count - 40);
            _intelligenceDirty = true;
            _tray?.ShowBalloonTip(2500, "ZEUS Audio Control",
                L($"{disconnected.Length} dispositivo(s) desconectado(s).", $"{disconnected.Length} device(s) disconnected."),
                System.Windows.Forms.ToolTipIcon.Info);
        }
        foreach (var device in devices.Where(device => !_previousEndpoints.Contains(device.ID) &&
                     (device.ID == _preferredOutput || device.ID == _preferredInput)))
            RestoreEndpoint(device, device.DataFlow == DataFlow.Render);
        _previousEndpoints.Clear();
        _previousEndpoints.UnionWith(active);
    }

    private void SendHearingLimit()
    {
        if (_routes.TryGetValue(RouteKind.Playback, out var playback))
            playback.Audio.SetProtection(_intelligence.HearingLimit);
    }

    private void PollIntelligence()
    {
        if (_intelligenceDirty) { _intelligenceDirty = false; SaveIntelligence(); }
        var now = DateTime.UtcNow;
        var interval = now - _lastExposure;
        _lastExposure = now;
        if (_output is not null && _intelligence.HearingLimit is { } limit)
        {
            var endpoint = _output.AudioEndpointVolume;
            var level = endpoint.MasterVolumeLevelScalar * 100;
            if (!endpoint.Mute && level >= Math.Min(limit, 80) && interval.TotalSeconds is > 0 and < 5)
                _elevated += interval;
            if (level > limit + .1)
            {
                endpoint.MasterVolumeLevelScalar = (float)(limit / 100);
                Footer.Text = L("Volume reduzido ao limite configurado.", "Volume reduced to the configured limit.");
            }
        }
        PollForeground();
        PollGaming();
        PollSmartVoice();
    }

    private void PollForeground()
    {
        if (!_intelligence.Automation || !_routes.TryGetValue(RouteKind.Playback, out var playback) || !playback.Running)
        {
            StopForeground();
            ResetAutomaticProfile();
            return;
        }
        if (_foregroundHook != IntPtr.Zero) return;
        _foregroundCallback ??= (_, _, _, _, _, _, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_closing || _foregroundHook == IntPtr.Zero) return;
            _foregroundDebounce?.Stop();
            _foregroundDebounce?.Start();
        });
        _foregroundDebounce ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _foregroundDebounce.Tick -= ForegroundElapsed;
        _foregroundDebounce.Tick += ForegroundElapsed;
        _foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, _foregroundCallback, 0, 0, 0);
        ApplyForeground();
    }

    private void ForegroundElapsed(object? sender, EventArgs e)
    {
        _foregroundDebounce?.Stop();
        ApplyForeground();
    }

    private void StopForeground()
    {
        _foregroundDebounce?.Stop();
        if (_foregroundHook == IntPtr.Zero) return;
        UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
    }

    private async void ApplyForeground()
    {
        if (!_intelligence.Automation || !_routes.TryGetValue(RouteKind.Playback, out var playback) || !playback.Running) return;
        var executable = ForegroundExecutable();
        if (string.IsNullOrEmpty(executable)) { ResetAutomaticProfile(); return; }
        if (string.Equals(executable, _foregroundExecutable, StringComparison.OrdinalIgnoreCase)) return;
        _foregroundExecutable = executable;
        try
        {
            var rule = IntelligenceRules.ForegroundProfile(executable, _intelligence.Profiles);
            var eq = rule is null ? null : await Task.Run(() => ExportedProfile.Load(rule.File).Equalizer);
            if (_closing || !string.Equals(executable, _foregroundExecutable, StringComparison.OrdinalIgnoreCase)) return;
            _automaticEq = eq;
            UpdatePlaybackEq();
            if (rule is not null && _automaticEq is not null)
                Footer.Text = L($"Perfil automático: {Path.GetFileName(rule.File)}", $"Automatic profile: {Path.GetFileName(rule.File)}");
        }
        catch (Exception error)
        {
            if (_closing || !string.Equals(executable, _foregroundExecutable, StringComparison.OrdinalIgnoreCase)) return;
            ResetAutomaticProfile();
            Footer.Text = error.Message;
        }
    }

    private void ResetAutomaticProfile()
    {
        _foregroundExecutable = null;
        if (_automaticEq is null) return;
        _automaticEq = null;
        UpdatePlaybackEq();
    }

    private static string? ForegroundExecutable()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var pid);
        try
        {
            if (pid == Environment.ProcessId) return null;
            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    private void ClearShortcuts()
    {
        if (_hotkeySource is { IsDisposed: false })
            foreach (var id in _registeredHotkeys) UnregisterHotKey(_hotkeySource.Handle, id);
        _registeredHotkeys.Clear();
    }

    private void RegisterShortcuts()
    {
        ClearShortcuts();
        if (_hotkeySource is null || !_intelligence.EnableShortcuts) return;
        string? invalid = null;
        for (var index = 0; index < Math.Min(4, _intelligence.Shortcuts.Length); index++)
        {
            var shortcut = _intelligence.Shortcuts[index];
            if (string.IsNullOrWhiteSpace(shortcut)) continue;
            try
            {
                if (new KeyGestureConverter().ConvertFromInvariantString(shortcut) is not KeyGesture gesture ||
                    (gesture.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Windows)) == 0)
                    throw new FormatException();
                uint modifiers = 0x4000;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 2;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
                if (!RegisterHotKey(_hotkeySource.Handle, 701 + index, modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))
                    throw new InvalidOperationException();
                _registeredHotkeys.Add(701 + index);
            }
            catch (Exception error) when (error is FormatException or InvalidOperationException or NotSupportedException)
            {
                invalid = shortcut;
                break;
            }
        }
        _shortcutStatus = invalid is null ? "" : L($"Atalho indisponível ou inválido: {invalid}", $"Shortcut unavailable or invalid: {invalid}");
        if (invalid is not null)
        {
            ClearShortcuts();
            Footer.Text = _shortcutStatus;
        }
    }

    private string _shortcutStatus = "";
    private IntPtr HandleHotkey(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0312 || !_registeredHotkeys.Contains(wParam.ToInt32())) return IntPtr.Zero;
        handled = true;
        switch (wParam.ToInt32())
        {
            case 701 when VolumeSlider.IsEnabled: VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5); break;
            case 702 when VolumeSlider.IsEnabled: VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5); break;
            case 703 when MuteToggle.IsEnabled: MuteToggle.IsChecked = MuteToggle.IsChecked != true; break;
            case 704 when MicMute.IsEnabled: MicMute.IsChecked = MicMute.IsChecked != true; break;
        }
        return IntPtr.Zero;
    }

    private sealed class EndpointNotifications(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string id, DeviceState state) => changed();
        public void OnDeviceAdded(string id) => changed();
        public void OnDeviceRemoved(string id) => changed();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) => changed();
        public void OnPropertyValueChanged(string id, PropertyKey key) { }
    }

    private delegate void ForegroundEvent(IntPtr hook, uint type, IntPtr window, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module,
        ForegroundEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
