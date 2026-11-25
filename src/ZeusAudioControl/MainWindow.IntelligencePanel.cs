using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private void Intelligence_Click(object sender, RoutedEventArgs e)
    {
        var (window, body) = BrandWindow("ZEUS Intelligence Pack");
        var panel = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var gaming = new Button { Content = "ZEUS Gaming Mode" };
        gaming.Click += Gaming_Click;
        panel.Children.Add(gaming);
        var hid = new Button { Content = L("Diagnóstico H510-PRO USB/HID", "H510-PRO USB/HID diagnostics") };
        hid.Click += H510Diagnostics_Click;
        panel.Children.Add(hid);
        var mixer = new Button { Content = "ZEUS Audio Mixer" };
        mixer.Click += Mixer_Click;
        panel.Children.Add(mixer);
        var doctor = new Button { Content = "ZEUS Audio Doctor" };
        doctor.Click += AudioDoctor_Click;
        panel.Children.Add(doctor);
        var calibration = new Button { Content = L("Calibrar microfone", "Calibrate microphone") };
        calibration.Click += Calibration_Click;
        panel.Children.Add(calibration);

        var battery = new TextBlock { Text = L("Bateria · selecione um dispositivo Bluetooth LE pareado.", "Battery · select a paired Bluetooth LE device."), TextWrapping = TextWrapping.Wrap };
        var devices = new ComboBox { DisplayMemberPath = "Name" };
        var batteryScan = new Button { Content = L("Listar dispositivos BLE", "List BLE devices") };
        var batteryRead = new Button { Content = L("Ler bateria", "Read battery") };
        using var cancel = new CancellationTokenSource();
        batteryScan.Click += async (_, _) =>
        {
            batteryScan.IsEnabled = false;
            try
            {
                var entries = await PairedBatteryDevices(cancel.Token);
                if (cancel.IsCancellationRequested) return;
                devices.ItemsSource = entries;
                devices.SelectedItem = entries.FirstOrDefault(device => device.Id == _intelligence.BatteryId);
                if (entries.Length == 0) battery.Text = L("Nenhum dispositivo BLE pareado disponível.", "No paired BLE devices available.");
            }
            catch (Exception error) when (error is not OutOfMemoryException) { if (window.IsVisible) battery.Text = error.Message; }
            finally { batteryScan.IsEnabled = true; }
        };
        batteryRead.Click += async (_, _) =>
        {
            if (devices.SelectedItem is BatteryDevice selected)
            {
                _intelligence.BatteryId = selected.Id;
                SaveIntelligence();
            }
            batteryRead.IsEnabled = false;
            await ShowBatteryStatus(battery, cancel.Token);
            batteryRead.IsEnabled = true;
        };
        panel.Children.Add(battery);
        panel.Children.Add(devices);
        panel.Children.Add(new WrapPanel { Children = { batteryScan, batteryRead } });

        var enabled = new CheckBox
        {
            Content = L("Autorizo detectar o aplicativo em primeiro plano quando uma rota de saída estiver ativa.",
                "Allow detection of the foreground app while a playback route is active."), IsChecked = _intelligence.Automation
        };
        enabled.Click += (_, _) =>
        {
            _intelligence.Automation = enabled.IsChecked == true;
            ResetAutomaticProfile();
            PollForeground();
            SaveIntelligence();
        };
        panel.Children.Add(enabled);
        var rules = new ListBox
        {
            MaxHeight = 100,
            Background = System.Windows.Media.Brushes.Black,
            Foreground = System.Windows.Media.Brushes.White
        };
        void ShowRules() => rules.ItemsSource = _intelligence.Profiles.Select(rule => $"{Path.GetFileName(rule.Executable)} → {Path.GetFileName(rule.File)} · {rule.Priority}").ToArray();
        ShowRules();
        var priority = new TextBox { Text = "0", Width = 60, MaxLength = 4, ToolTip = L("Prioridade", "Priority") };
        var add = new Button { Content = L("Associar aplicativo a perfil EQ", "Associate app with EQ profile") };
        add.Click += (_, _) =>
        {
            try
            {
                if (!int.TryParse(priority.Text, out var weight) || weight is < -100 or > 100)
                    throw new InvalidDataException(L("Prioridade de −100 a 100.", "Priority from −100 to 100."));
                if (_intelligence.Profiles.Count >= 50) throw new InvalidOperationException(L("Limite de associações atingido.", "Association limit reached."));
                var executable = new OpenFileDialog { Filter = "Windows executable|*.exe" };
                if (executable.ShowDialog(window) != true) return;
                var file = new OpenFileDialog { Filter = "ZEUS profile|*.json" };
                if (file.ShowDialog(window) != true) return;
                if (ExportedProfile.Load(file.FileName).Equalizer is null)
                    throw new InvalidDataException(L("Escolha um perfil exportado que contenha EQ.", "Choose an exported profile that includes EQ."));
                _intelligence.Profiles.Add(new AppProfile(Path.GetFullPath(executable.FileName), file.FileName, weight));
                ResetAutomaticProfile();
                SaveIntelligence();
                ShowRules();
            }
            catch (Exception error) { MessageBox.Show(window, error.Message, window.Title); }
        };
        var remove = new Button { Content = L("Remover associação", "Remove association") };
        remove.Click += (_, _) =>
        {
            if (rules.SelectedIndex < 0) return;
            _intelligence.Profiles.RemoveAt(rules.SelectedIndex);
            ResetAutomaticProfile();
            PollForeground();
            SaveIntelligence();
            ShowRules();
        };
        panel.Children.Add(rules);
        panel.Children.Add(new TextBlock { Text = L("Prioridade (maior primeiro)", "Priority (highest first)") });
        panel.Children.Add(new WrapPanel { Children = { priority, add, remove } });

        var hearing = new CheckBox { Content = L("Proteção auditiva ativa", "Hearing protection enabled"), IsChecked = _intelligence.HearingLimit is not null };
        var limit = new Slider { Minimum = 10, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = _intelligence.HearingLimit ?? 75 };
        var exposure = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void ShowExposure() => exposure.Text = L($"Limite de {limit.Value:0}% · tempo em nível elevado nesta sessão: {_elevated:hh\\:mm\\:ss}. A escala é digital, sem estimativa acústica.",
            $"Limit {limit.Value:0}% · time at elevated level this session: {_elevated:hh\\:mm\\:ss}. Digital scale, no acoustic estimate.");
        void SaveHearing()
        {
            _intelligence.HearingLimit = hearing.IsChecked == true ? limit.Value : null;
            SaveIntelligence();
            SendHearingLimit();
            ShowExposure();
        }
        hearing.Click += (_, _) => SaveHearing();
        limit.ValueChanged += (_, _) => { ShowExposure(); _intelligence.HearingLimit = hearing.IsChecked == true ? limit.Value : null; _intelligenceDirty = true; SendHearingLimit(); };
        var exposureTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        exposureTimer.Tick += (_, _) => ShowExposure();
        exposureTimer.Start();
        ShowExposure();
        panel.Children.Add(hearing);
        panel.Children.Add(limit);
        panel.Children.Add(exposure);

        var shortcuts = new CheckBox { Content = L("Atalhos globais ativos", "Global shortcuts enabled"), IsChecked = _intelligence.EnableShortcuts };
        var floating = new CheckBox { Content = L("Quick Control flutuante", "Floating Quick Control"), IsChecked = _intelligence.FloatingPanel };
        var shortcutLabels = new[] { L("Aumentar volume", "Raise volume"), L("Diminuir volume", "Lower volume"), L("Mudo da saída", "Mute output"), L("Mudo do microfone", "Mute microphone") };
        panel.Children.Add(shortcuts);
        var bindings = shortcutLabels.Select((label, i) =>
        {
            panel.Children.Add(new TextBlock { Text = label });
            var entry = new TextBox { Text = _intelligence.Shortcuts.ElementAtOrDefault(i) ?? "", MaxLength = 64 };
            panel.Children.Add(entry);
            return entry;
        }).ToArray();
        var shortcutStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = _shortcutStatus };
        var apply = new Button { Content = L("Aplicar atalhos", "Apply shortcuts") };
        apply.Click += (_, _) =>
        {
            _intelligence.EnableShortcuts = shortcuts.IsChecked == true;
            _intelligence.Shortcuts = bindings.Select(input => input.Text.Trim()).ToArray();
            RegisterShortcuts();
            if (_shortcutStatus.Length != 0) _intelligence.EnableShortcuts = false;
            shortcuts.IsChecked = _intelligence.EnableShortcuts;
            SaveIntelligence();
            shortcutStatus.Text = _shortcutStatus;
        };
        floating.Click += (_, _) => { _intelligence.FloatingPanel = floating.IsChecked == true; SaveIntelligence(); };
        panel.Children.Add(apply);
        panel.Children.Add(shortcutStatus);
        panel.Children.Add(floating);

        window.Closed += (_, _) => { cancel.Cancel(); exposureTimer.Stop(); };
        window.ShowDialog();
    }
}
