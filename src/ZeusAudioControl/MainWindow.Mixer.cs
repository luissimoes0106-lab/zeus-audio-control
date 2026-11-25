using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private sealed record MixerEntry(string Id, string Name, string? Path, float Level, bool Mute);

    private void Mixer_Click(object sender, RoutedEventArgs e)
    {
        var (window, body) = BrandWindow("ZEUS Audio Mixer");
        if (sender is FrameworkElement control && Window.GetWindow(control) is { } owner) window.Owner = owner;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var rows = new StackPanel();
        var scroll = new ScrollViewer { Content = rows, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(status);
        body.Children.Add(scroll);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var seen = new HashSet<string>();
        string? displayed = null;
        bool busy = false;
        async Task Refresh()
        {
            if (busy) return;
            if (_output?.ID is not { } outputId)
            {
                rows.Children.Clear();
                displayed = null;
                status.Text = L("Selecione uma saída do Windows.", "Select a Windows output.");
                return;
            }
            busy = true;
            try
            {
                var preferences = new Dictionary<string, float>(_intelligence.Sessions, StringComparer.OrdinalIgnoreCase);
                var previous = seen.ToHashSet();
                var snapshot = await Task.Run(() => ReadSessions(outputId, preferences, previous));
                if (!window.IsVisible) return;
                seen.UnionWith(snapshot.Select(session => outputId + session.Id));
                if (seen.Count > 512)
                {
                    seen.Clear();
                    seen.UnionWith(snapshot.Select(session => outputId + session.Id));
                }
                var signature = outputId + string.Join("\n", snapshot.Select(session => $"{session.Id}:{session.Level:0.000}:{session.Mute}"));
                if (signature == displayed || rows.IsMouseCaptureWithin) return;
                displayed = signature;
                status.Text = snapshot.Length == 0
                    ? L("Nenhum aplicativo com sessão ativa nesta saída.", "No apps have an active session on this output.")
                    : $"{_output?.FriendlyName} · {snapshot.Length}";
                rows.Children.Clear();
                foreach (var session in snapshot) rows.Children.Add(MixerRow(session, outputId));
            }
            catch (Exception error) { if (window.IsVisible) status.Text = error.Message; }
            finally { busy = false; }
        }
        timer.Tick += async (_, _) => await Refresh();
        window.Loaded += async (_, _) => { timer.Start(); await Refresh(); };
        window.Closed += (_, _) => timer.Stop();
        window.ShowDialog();
    }

    private FrameworkElement MixerRow(MixerEntry entry, string endpoint)
    {
        var row = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        var header = new DockPanel();
        if (entry.Path is { } executable)
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executable);
                if (icon is not null)
                    header.Children.Add(new Image
                    {
                        Source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(20, 20)),
                        Width = 20, Height = 20, Margin = new Thickness(0, 0, 7, 0), Stretch = Stretch.Uniform
                    });
            }
            catch (Exception error) when (error is IOException or ArgumentException or System.ComponentModel.Win32Exception) { }
        }
        var role = entry.Path is not { } path ? "" :
            string.Equals(path, _intelligence.CommunicationsApp, StringComparison.OrdinalIgnoreCase)
                ? L(" · Comunicação", " · Communications")
                : _intelligence.Games.Any(rule => string.Equals(rule.Executable, path, StringComparison.OrdinalIgnoreCase))
                    ? L(" · Jogo", " · Game") : "";
        header.Children.Add(new TextBlock { Text = entry.Name + role, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(header);
        var level = new Slider { Minimum = 0, Maximum = 100, Value = entry.Level * 100, TabIndex = 0 };
        System.Windows.Automation.AutomationProperties.SetName(level, entry.Name);
        var mute = new CheckBox { Content = L("Silenciar", "Mute"), IsChecked = entry.Mute };
        row.Children.Add(level);
        row.Children.Add(mute);
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        debounce.Tick += async (_, _) =>
        {
            debounce.Stop();
            var volume = (float)(level.Value / 100);
            try
            {
                await Task.Run(() => ChangeSession(endpoint, entry.Id, volume, null));
                if (entry.Path is { } file && (_intelligence.Sessions.ContainsKey(file) || _intelligence.Sessions.Count < 80))
                {
                    _intelligence.Sessions[file] = volume;
                    _intelligenceDirty = true;
                }
            }
            catch (Exception error) { Footer.Text = error.Message; }
        };
        level.ValueChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        row.Unloaded += (_, _) => debounce.Stop();
        mute.Click += async (_, _) =>
        {
            var isMuted = mute.IsChecked == true;
            try { await Task.Run(() => ChangeSession(endpoint, entry.Id, null, isMuted)); }
            catch (Exception error) { Footer.Text = error.Message; }
        };
        return row;
    }

    private static MixerEntry[] ReadSessions(string endpoint, Dictionary<string, float> preferences, HashSet<string> seen)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(endpoint);
        var sessions = device.AudioSessionManager.Sessions;
        var result = new List<MixerEntry>();
        for (var i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            try
            {
                if (session.State != AudioSessionState.AudioSessionStateActive) continue;
                string? path = null;
                var name = session.DisplayName;
                if (session.IsSystemSoundsSession) name = "Windows";
                else
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)session.GetProcessID);
                        name = process.ProcessName;
                        path = process.MainModule?.FileName;
                    }
                    catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
                var id = session.GetSessionInstanceIdentifier;
                var audio = session.SimpleAudioVolume;
                if (path is not null && !seen.Contains(endpoint + id) && preferences.TryGetValue(path, out var saved) && float.IsFinite(saved))
                    audio.Volume = Math.Clamp(saved, 0, 1);
                result.Add(new MixerEntry(id, string.IsNullOrWhiteSpace(name) ? $"PID {session.GetProcessID}" : name, path, audio.Volume, audio.Mute));
            }
            catch (System.Runtime.InteropServices.COMException) { }
        }
        return result.OrderBy(session => session.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static void ChangeSession(string endpoint, string id, float? volume, bool? mute)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(endpoint);
        var sessions = device.AudioSessionManager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            if (session.GetSessionInstanceIdentifier != id) continue;
            if (volume is { } level) session.SimpleAudioVolume.Volume = Math.Clamp(level, 0, 1);
            if (mute is { } muted) session.SimpleAudioVolume.Mute = muted;
            return;
        }
        throw new InvalidOperationException("Audio session has ended.");
    }
}
