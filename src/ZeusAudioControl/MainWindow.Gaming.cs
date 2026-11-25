using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private static readonly StudioProfile GamingVoiceProfile =
        new(3, true, true, true, -43, true, 3, 80, 4, 80, 160, -17, 3, true, true);
    private double[]? _gamingManualEq, _gamingDetectedEq;
    private string? _gamingDetectedName, _gamingDetectedPreset;
    private bool _gamingPollBusy;
    private int _gamingRevision;

    private void Gaming_Click(object sender, RoutedEventArgs e)
    {
        var (window, body) = BrandWindow("ZEUS Gaming Mode");
        if (sender is FrameworkElement element && Window.GetWindow(element) is { } owner) window.Owner = owner;
        var panel = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var choice = new ComboBox { SelectedIndex = 0, ItemsSource = new[] { "Gaming FPS", L("Gaming Imersivo", "Gaming Immersive"), L("EQ atual", "Current EQ") } };
        var manual = new Button { Content = L("Ativar manualmente", "Activate manually") };
        var off = new Button { Content = L("Desativar Gaming Mode", "Disable Gaming Mode") };
        var voice = new Button { Content = "Gaming Voice" };
        var mixer = new Button { Content = "ZEUS Audio Mixer" };
        var low = new CheckBox { Content = L("Solicitar baixa latência na próxima rota", "Request low latency for the next route"), IsChecked = _intelligence.GamingLowLatency };
        var automatic = new CheckBox
        {
            Content = L("Autorizo detectar apenas os executáveis associados abaixo.", "Allow detection of the executable paths listed below."),
            IsChecked = _intelligence.GamingAuto
        };
        var rules = new ListBox { MaxHeight = 120, Background = System.Windows.Media.Brushes.Black, Foreground = System.Windows.Media.Brushes.White };
        var priority = new TextBox { Text = "0", MaxLength = 4, Width = 54, ToolTip = L("Prioridade de −100 a 100", "Priority from −100 to 100") };
        var add = new Button { Content = L("Associar jogo ao preset", "Associate game with preset") };
        var remove = new Button { Content = L("Remover associação", "Remove association") };
        var communication = new Button { Content = L("Identificar aplicativo de comunicação", "Identify a communications app") };
        var preferences = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(summary);
        panel.Children.Add(choice);
        panel.Children.Add(new WrapPanel { Children = { manual, off, voice, mixer } });
        panel.Children.Add(low);
        panel.Children.Add(automatic);
        panel.Children.Add(rules);
        panel.Children.Add(new WrapPanel { Children = { priority, add, remove } });
        panel.Children.Add(communication);
        panel.Children.Add(preferences);
        panel.Children.Add(new TextBlock
        {
            Text = L("Os presets de jogo atuam na rota de reprodução. Use o Mixer para controlar sessões distintas do jogo e da comunicação. A baixa latência é uma solicitação ao WASAPI.",
                "Gaming presets affect the playback route. Use the Mixer to control distinct game and communications sessions. Low latency is a WASAPI request."),
            TextWrapping = TextWrapping.Wrap
        });
        string Key() => choice.SelectedIndex switch { 1 => "immersive", 2 => "custom", _ => "fps" };
        void UpdateStatus()
        {
            var text = _gamingManualEq is not null ? L("Preset manual ativo", "Manual preset active")
                : _gamingDetectedName is { } detected ? L($"Jogo detectado: {detected}", $"Game detected: {detected}")
                : L("Gaming Mode inativo", "Gaming Mode inactive");
            if (!_routes.TryGetValue(RouteKind.Playback, out var playback) || !playback.Running)
                text += L(" · sem rota de reprodução.", " · no playback route.");
            summary.Text = text;
            preferences.Text = _intelligence.CommunicationsApp is { } app
                ? $"{L("Comunicação", "Communications")} · {Path.GetFileName(app)}"
                : L("Selecione opcionalmente o executável de comunicação.", "Optionally select the communications executable.");
        }
        void ShowRules()
        {
            rules.ItemsSource = _intelligence.Games.Select(rule =>
                $"{Path.GetFileName(rule.Executable)} → {rule.Preset} · {rule.Priority}").ToArray();
        }
        manual.Click += (_, _) =>
        {
            _gamingManualEq = Key() == "custom" ? CurrentEq() : GamingRules.Preset(Key());
            UpdatePlaybackEq();
            UpdateDashboard();
            UpdateStatus();
        };
        off.Click += (_, _) =>
        {
            _gamingManualEq = null;
            _intelligence.GamingAuto = false;
            automatic.IsChecked = false;
            ResetDetectedGame();
            UpdatePlaybackEq();
            UpdateDashboard();
            SaveIntelligence();
            UpdateStatus();
        };
        voice.Click += (_, _) =>
        {
            _voiceProfile = "gaming";
            ApplyStudio(GamingVoiceProfile);
            PersistIfChanged();
            summary.Text = L("Gaming Voice salvo. A automação Smart Voice tem prioridade quando ativa.",
                "Gaming Voice saved. Smart Voice automation takes priority when active.");
            UpdateDashboard();
        };
        mixer.Click += Mixer_Click;
        low.Click += (_, _) =>
        {
            _intelligence.GamingLowLatency = low.IsChecked == true;
            if (SelectedRouteKind == RouteKind.Playback && SelectedRoute is null) RouteLowLatency.IsChecked = _intelligence.GamingLowLatency;
            SaveIntelligence();
        };
        automatic.Click += (_, _) =>
        {
            _intelligence.GamingAuto = automatic.IsChecked == true;
            ResetDetectedGame();
            SaveIntelligence();
            PollGaming();
            UpdateStatus();
        };
        add.Click += (_, _) =>
        {
            if (!int.TryParse(priority.Text, out var rank) || rank is < -100 or > 100)
            {
                MessageBox.Show(window, L("A prioridade deve estar entre −100 e 100.", "Priority must be between −100 and 100."));
                return;
            }
            var executable = new OpenFileDialog { Filter = "Windows executable|*.exe" };
            if (executable.ShowDialog(window) != true) return;
            var file = Path.GetFullPath(executable.FileName);
            var existing = _intelligence.Games.FindIndex(rule => string.Equals(rule.Executable, file, StringComparison.OrdinalIgnoreCase));
            if (existing < 0 && _intelligence.Games.Count >= 25) return;
            var rule = new GamingBinding(file, Key(), rank, Key() == "custom" ? CurrentEq() : null);
            if (existing >= 0) _intelligence.Games[existing] = rule;
            else _intelligence.Games.Add(rule);
            ResetDetectedGame();
            SaveIntelligence();
            ShowRules();
            PollGaming();
        };
        remove.Click += (_, _) =>
        {
            if (rules.SelectedIndex < 0) return;
            _intelligence.Games.RemoveAt(rules.SelectedIndex);
            ResetDetectedGame();
            SaveIntelligence();
            ShowRules();
            PollGaming();
        };
        communication.Click += (_, _) =>
        {
            var executable = new OpenFileDialog { Filter = "Windows executable|*.exe" };
            if (executable.ShowDialog(window) != true) return;
            _intelligence.CommunicationsApp = executable.FileName;
            SaveIntelligence();
            UpdateStatus();
        };
        var refresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        refresh.Tick += (_, _) => UpdateStatus();
        window.Closed += (_, _) => refresh.Stop();
        UpdateStatus();
        ShowRules();
        refresh.Start();
        window.ShowDialog();
    }

    private async void PollGaming()
    {
        if (_closing || _gamingPollBusy || !_intelligence.GamingAuto || _intelligence.Games.Count == 0) return;
        _gamingPollBusy = true;
        var revision = _gamingRevision;
        var rules = _intelligence.Games.ToArray();
        try
        {
            var match = await Task.Run(() => GamingRules.Match(rules, RunningExecutables(rules.Select(rule => rule.Executable))));
            if (_closing || revision != _gamingRevision) return;
            var name = Path.GetFileName(match?.Executable);
            if (_gamingDetectedName == name && _gamingDetectedPreset == match?.Preset) return;
            _gamingDetectedName = name;
            _gamingDetectedPreset = match?.Preset;
            _gamingDetectedEq = match is null ? null :
                ExportedProfile.ValidEq(match.Equalizer) ? match.Equalizer : GamingRules.Preset(match.Preset);
            UpdatePlaybackEq();
            UpdateDashboard();
        }
        catch (Exception error) { if (!_closing) Footer.Text = error.Message; }
        finally { _gamingPollBusy = false; }
    }

    private void ResetDetectedGame()
    {
        _gamingRevision++;
        _gamingDetectedEq = null;
        _gamingDetectedName = _gamingDetectedPreset = null;
        UpdatePlaybackEq();
    }

    private static IEnumerable<string> RunningExecutables(IEnumerable<string> executables)
    {
        foreach (var name in executables.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    string? path = null;
                    try { path = GameExecutable(process.Id); }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                    if (!string.IsNullOrEmpty(path)) yield return path;
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    private static string? GameExecutable(int processId)
    {
        const uint queryLimitedInformation = 0x1000;
        using var process = OpenProcess(queryLimitedInformation, false, processId);
        if (process.IsInvalid) return null;
        var capacity = 32768;
        var path = new StringBuilder(capacity);
        return QueryFullProcessImageName(process, 0, path, ref capacity) ? path.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder path, ref int length);

    private void H510Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var (window, body) = BrandWindow("H510-PRO · USB/HID");
        if (sender is FrameworkElement element && Window.GetWindow(element) is { } owner) window.Owner = owner;
        var output = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 100, MaxHeight = 260, Text = L("Inspecionando descritores HID…", "Inspecting HID descriptors…")
        };
        var save = new Button { Content = L("Salvar diagnóstico local", "Save local diagnostics"), IsEnabled = false };
        var consent = new CheckBox { Content = L("Autorizo leitura passiva da coleção HID F100 deste receptor.", "Allow passive reads from this receiver’s F100 HID collection.") };
        var start = new Button { Content = L("Capturar por até 20 s", "Capture for up to 20 s"), IsEnabled = false };
        var stop = new Button { Content = L("Cancelar captura", "Cancel capture"), IsEnabled = false };
        CancellationTokenSource? capture = null;
        body.Children.Add(output);
        body.Children.Add(consent);
        body.Children.Add(new WrapPanel { Children = { start, stop, save } });
        consent.Click += (_, _) => start.IsEnabled = consent.IsChecked == true && capture is null;
        stop.Click += (_, _) => capture?.Cancel();
        start.Click += async (_, _) =>
        {
            if (consent.IsChecked != true || capture is not null) return;
            using var running = new CancellationTokenSource();
            capture = running;
            start.IsEnabled = save.IsEnabled = false;
            stop.IsEnabled = true;
            output.Text = L("Aguardando relatórios espontâneos…", "Waiting for spontaneous reports…");
            try
            {
                var report = await Task.Run(() => H510Diagnostics.Capture(running.Token));
                if (!window.IsVisible) return;
                output.Text = report;
                save.IsEnabled = true;
            }
            catch (Exception error) { if (window.IsVisible) output.Text = error.Message; }
            finally
            {
                capture = null;
                stop.IsEnabled = false;
                start.IsEnabled = consent.IsChecked == true;
            }
        };
        window.Closed += (_, _) => capture?.Cancel();
        window.Loaded += async (_, _) =>
        {
            try
            {
                var report = await Task.Run(H510Diagnostics.Inspect);
                if (!window.IsVisible || capture is not null) return;
                output.Text = report;
                save.IsEnabled = true;
            }
            catch (Exception error) { if (window.IsVisible) output.Text = error.Message; }
        };
        save.Click += async (_, _) =>
        {
            var file = new SaveFileDialog { FileName = "ZEUS-H510-PRO-HID.txt", Filter = "Text|*.txt" };
            try { if (file.ShowDialog(window) == true) await File.WriteAllTextAsync(file.FileName, output.Text); }
            catch (Exception error) { MessageBox.Show(window, error.Message, window.Title); }
        };
        window.ShowDialog();
    }
}
