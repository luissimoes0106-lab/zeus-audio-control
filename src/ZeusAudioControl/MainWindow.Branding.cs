using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private static BitmapImage BrandImage(string name) =>
        new(new Uri($"pack://application:,,,/{name}", UriKind.Absolute));

    private (Window Window, StackPanel Body) BrandWindow(string title)
    {
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new Image
        {
            Source = BrandImage("ZEUS-Logo.png"), Width = 112, Height = 112, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12)
        });
        var window = new Window
        {
            Title = title, Icon = BrandImage("Zeus.ico"), Owner = this, ShowInTaskbar = false,
            Width = 410, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 18)), Foreground = Foreground,
            ResizeMode = ResizeMode.NoResize, FontFamily = FontFamily, FontSize = FontSize, Content = body
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ZeusAudioControl;component/Theme.xaml", UriKind.Absolute) });
        window.Resources[typeof(Button)] = Resources[typeof(Button)];
        return (window, body);
    }

    private void QuickControl_Click(object sender, RoutedEventArgs e)
    {
        if (_quickControl is { } existing) { existing.Activate(); return; }
        if (!IsVisible) RestoreWindow();
        var (window, body) = BrandWindow("ZEUS Quick Control");
        AddVolume(L("Saída", "Output"), VolumeSlider, MuteToggle);
        AddVolume(L("Microfone", "Microphone"), MicSlider, MicMute);
        var routing = new Button { Content = L("Abrir rotas", "Open routes"), Margin = new Thickness(0, 10, 0, 0) };
        routing.Click += (_, _) => { window.Close(); Navigate("Rotas"); };
        var stop = new Button { Content = L("Parar todas as rotas", "Stop all routes"), IsEnabled = _routes.Count > 0 };
        stop.Click += (_, _) => { foreach (var active in _routes.Values) active.Audio.Stop(); stop.IsEnabled = false; };
        body.Children.Add(routing);
        body.Children.Add(stop);
        var mixer = new Button { Content = "ZEUS Audio Mixer" };
        mixer.Click += Mixer_Click;
        body.Children.Add(mixer);
        var gaming = new Button { Content = "ZEUS Gaming Mode" };
        gaming.Click += Gaming_Click;
        body.Children.Add(gaming);
        var presets = new WrapPanel();
        foreach (var (label, key) in new[] { ("Gaming FPS", "fps"), (L("Gaming Imersivo", "Gaming Immersive"), "immersive"), (L("Música", "Music"), "music"), (L("Filmes", "Movies"), "movie"), ("Flat", "flat") })
        {
            var preset = new Button { Content = label, Tag = key };
            preset.Click += EqPreset_Click;
            presets.Children.Add(preset);
        }
        body.Children.Add(presets);
        var battery = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var cancel = new CancellationTokenSource();
        body.Children.Add(battery);
        var batteryTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        bool batteryBusy = false;
        async Task RefreshBattery()
        {
            if (batteryBusy || !window.IsVisible) return;
            batteryBusy = true;
            try { await ShowBatteryStatus(battery, cancel.Token); }
            finally { batteryBusy = false; }
        }
        batteryTimer.Tick += async (_, _) => await RefreshBattery();
        window.Loaded += async (_, _) => { batteryTimer.Start(); await RefreshBattery(); };
        _quickControl = window;
        window.Closed += (_, _) => { batteryTimer.Stop(); cancel.Cancel(); _quickControl = null; };
        window.Topmost = _intelligence.FloatingPanel;
        if (_intelligence.FloatingPanel) window.Show();
        else window.ShowDialog();

        void AddVolume(string label, Slider source, CheckBox mute)
        {
            body.Children.Add(new TextBlock { Text = label });
            var level = new Slider { Minimum = source.Minimum, Maximum = source.Maximum };
            level.SetBinding(Slider.ValueProperty, new Binding(nameof(Slider.Value)) { Source = source, Mode = BindingMode.TwoWay });
            level.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = source });
            body.Children.Add(level);
            var toggle = new CheckBox { Content = L("Silenciar", "Mute") };
            toggle.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = mute, Mode = BindingMode.TwoWay });
            toggle.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = mute });
            body.Children.Add(toggle);
        }
    }

    private void AboutZeus_Click(object sender, RoutedEventArgs e)
    {
        var (window, body) = BrandWindow(L("Sobre o ZEUS", "About ZEUS"));
        body.Children.Add(new TextBlock { Text = "ZEUS Audio Control · Beta 0.7.2", FontWeight = FontWeights.SemiBold });
        var releases = new Button
        {
            Content = L("Ver versões no GitHub", "View releases on GitHub"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0)
        };
        releases.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/luissimoes0106-lab/zeus-audio-control/releases") { UseShellExecute = true });
            }
            catch (Exception error) { MessageBox.Show(window, error.Message, window.Title); }
        };
        body.Children.Add(releases);
        var updates = new Button
        {
            Content = L("Verificar atualizações", "Check for updates"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0)
        };
        updates.Click += (_, _) => ShowUpdates(window, releases.RaiseEvent);
        body.Children.Add(updates);
        window.ShowDialog();
    }
    private void ShowUpdates(Window owner, Action<RoutedEventArgs> openReleases)
    {
        var (window, body) = BrandWindow(L("Atualizações do ZEUS", "ZEUS updates"));
        window.Owner = owner;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = L("Consultando versões públicas…", "Checking public releases…") };
        body.Children.Add(status);
        var open = new Button { Content = L("Abrir GitHub Releases", "Open GitHub Releases"), Margin = new Thickness(0, 10, 0, 0) };
        open.Click += (_, _) => openReleases(new RoutedEventArgs(Button.ClickEvent));
        body.Children.Add(open);
        var download = new Button { IsEnabled = false, Content = L("Baixar e verificar instalador", "Download and verify installer") };
        var recover = new Button { Content = L("Recuperar cópia anterior", "Recover previous copy") };
        body.Children.Add(download);
        body.Children.Add(recover);
        string? releaseTag = null, installerName = null;
        var updateRoot = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "Updates");
        var cancellation = new CancellationTokenSource();
        download.Click += async (_, _) =>
        {
            if (releaseTag is null || installerName is null) return;
            download.IsEnabled = false;
            recover.IsEnabled = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(4));
                var token = timeout.Token;
                var baseUrl = $"https://github.com/luissimoes0106-lab/zeus-audio-control/releases/download/{releaseTag}/";
                using var client = new System.Net.Http.HttpClient();
                using var checksum = await client.GetAsync(baseUrl + "SHA256SUMS.txt", System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
                checksum.EnsureSuccessStatusCode();
                await using var checksumData = await checksum.Content.ReadAsStreamAsync(token);
                var data = new byte[16385];
                var length = await checksumData.ReadAtLeastAsync(data, data.Length, false, token);
                if (length == data.Length) throw new InvalidDataException("Checksum manifest exceeds 16 KiB.");
                var hash = UpdateFiles.HashFor(System.Text.Encoding.UTF8.GetString(data, 0, length), installerName);
                var destination = Path.Combine(updateRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(destination);
                status.Text = L("Baixando pacote e verificando SHA-256…", "Downloading and checking SHA-256…");
                using var response = await client.GetAsync(baseUrl + installerName, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var package = await response.Content.ReadAsStreamAsync(token);
                await UpdateFiles.CopyVerified(package, Path.Combine(destination, installerName), hash, UpdateFiles.MaximumPackage, token);
                PersistIfChanged();
                status.Text = L("Criando backup do executável e dos perfis…", "Backing up the executable and profiles…");
                var backup = Path.Combine(destination, "Backup");
                await UpdateFiles.Backup(Environment.ProcessPath!, Path.GetDirectoryName(_settingsPath)!, backup, token);
                status.Text = L("Instalador verificado. Feche o ZEUS antes de executá-lo.", "Installer verified. Close ZEUS before running it.");
                Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (!cancellation.IsCancellationRequested) status.Text = error.Message;
            }
            finally { download.IsEnabled = recover.IsEnabled = true; }
        };
        recover.Click += async (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog
            {
                Title = L("Selecione a pasta Backup", "Select the Backup folder"),
                InitialDirectory = Directory.Exists(updateRoot) ? updateRoot : ""
            };
            if (picker.ShowDialog(window) != true) return;
            download.IsEnabled = recover.IsEnabled = false;
            try
            {
                var destination = Path.Combine(updateRoot, "Recovered-" + Guid.NewGuid().ToString("N"));
                await Task.Run(() => UpdateFiles.Recover(picker.FolderName, destination, cancellation.Token), cancellation.Token);
                status.Text = L("Cópia verificada recuperada em pasta separada.", "Verified copy recovered to a separate folder.");
                Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (!cancellation.IsCancellationRequested) status.Text = error.Message;
            }
            finally { download.IsEnabled = releaseTag is not null; recover.IsEnabled = true; }
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ZEUS-Audio-Control/0.7.2");
                using var response = await client.GetAsync(
                    "https://api.github.com/repos/luissimoes0106-lab/zeus-audio-control/releases?per_page=30", cancellation.Token);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
                using var document = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellation.Token);
                var versions = document.RootElement.EnumerateArray()
                    .Where(release => !release.GetProperty("draft").GetBoolean())
                    .Select(release => release.GetProperty("tag_name").GetString() ?? "")
                    .Select(tag => (Tag: tag, Match: System.Text.RegularExpressions.Regex.Match(tag, @"^v(\d+\.\d+\.\d+)(?:-beta(?:\.(\d+))?)?$")))
                    .Where(item => item.Match.Success && Version.TryParse(item.Match.Groups[1].Value, out _))
                    .OrderByDescending(item => Version.Parse(item.Match.Groups[1].Value))
                    .ThenByDescending(item => !item.Tag.Contains("-beta", StringComparison.Ordinal))
                    .ThenByDescending(item => int.TryParse(item.Match.Groups[2].Value, out var revision) ? revision : 0)
                    .ToArray();
                var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 7, 0, 0);
                var newer = versions.FirstOrDefault(item => IsNewerRelease(item.Tag, Version.Parse(item.Match.Groups[1].Value), current));
                if (newer.Tag is not null)
                {
                    var version = Version.Parse(newer.Match.Groups[1].Value);
                    releaseTag = newer.Tag;
                    installerName = $"ZEUS-Audio-Control-Beta-{(version.Build == 0 ? version.ToString(2) : version.ToString(3))}-Setup-x64.exe";
                    download.IsEnabled = true;
                }
                status.Text = newer.Tag is not null
                    ? L($"Versão disponível: {newer.Tag}", $"Available version: {newer.Tag}")
                    : L("Nenhuma versão pública mais recente encontrada.", "No newer public version found.");
            }
            catch (Exception error) when (error is System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
            {
                status.Text = L("Não foi possível consultar o GitHub. Tente novamente com acesso à internet.",
                    "Could not check GitHub. Try again with an internet connection.");
            }
        };
        window.Closed += (_, _) => cancellation.Cancel();
        window.ShowDialog();
    }

    private static bool IsNewerRelease(string tag, Version release, Version installed)
    {
        var current = new Version(installed.Major, installed.Minor, installed.Build);
        return release > current || release == current &&
            (!tag.Contains("-beta", StringComparison.Ordinal) || tag.StartsWith($"v{current}-beta.", StringComparison.Ordinal));
    }
}
