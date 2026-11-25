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
        if (!IsVisible) RestoreWindow();
        var (window, body) = BrandWindow("ZEUS Quick Control");
        AddVolume(L("Saída", "Output"), VolumeSlider, MuteToggle);
        AddVolume(L("Microfone", "Microphone"), MicSlider, MicMute);
        window.ShowDialog();

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
        body.Children.Add(new TextBlock { Text = "ZEUS Audio Control · Beta 0.7", FontWeight = FontWeights.SemiBold });
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
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        window.Loaded += async (_, _) =>
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ZEUS-Audio-Control/0.7");
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
                status.Text = newer.Tag is not null
                    ? L($"Versão disponível: {newer.Tag}", $"Available version: {newer.Tag}")
                    : L("Nenhuma versão pública mais recente encontrada.", "No newer public version found.");
            }
            catch (Exception error) when (error is System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
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
