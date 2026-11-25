using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private readonly (string Name, string Id)[] _voicePresets =
    [
        ("WhatsApp — Voz equilibrada", "whatsapp"), ("Google Meet — Comunicação", "meet"),
        ("OBS — Gravação", "obs"), ("Discord — Comunicação", "discord"), ("Gaming Voice", "gaming"), ("Personalizado", "custom")
    ];
    private string? _activeVoicePreset;
    private bool _voicePollBusy;
    private int _voiceRevision;
    private TextBlock? _smartStatus, _smartMeter;
    private string ProfilesDirectory => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "Profiles");

    private void MixerPage_Click(object sender, RoutedEventArgs e) => Navigate("Audio Mixer");
    private void ProfilesPage_Click(object sender, RoutedEventArgs e) => Navigate("Meus Perfis");
    private void SmartVoice_Click(object sender, RoutedEventArgs e) => Navigate("Smart Voice");

    private void BuildManagedPages()
    {
        MixerPanel.Children.Add(BuildMixerPanel());
        BuildProfilesPage();
        BuildVoicePage();
    }

    private Button PageAction(Panel panel, string label, RoutedEventHandler action)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += action;
        panel.Children.Add(button);
        return button;
    }

    private void BuildProfilesPage()
    {
        ProfilesPanel.Children.Add(new TextBlock
        {
            Text = L("Carregue um perfil, edite os controles de Áudio e Microfone e salve sobre a seleção. Para criar, digite um novo nome.",
                "Load a profile, edit Audio and Microphone controls, and save over the selection. Enter a new name to create a profile."),
            TextWrapping = TextWrapping.Wrap
        });
        var names = new ListBox { Height = 118, Background = System.Windows.Media.Brushes.Black, Foreground = System.Windows.Media.Brushes.White };
        var name = new TextBox { MaxLength = 70 };
        var results = new TextBlock { TextWrapping = TextWrapping.Wrap };
        ProfilesPanel.Children.Add(names);
        ProfilesPanel.Children.Add(name);
        var actions = new WrapPanel();
        ProfilesPanel.Children.Add(actions);
        ProfilesPanel.Children.Add(results);
        void Refresh(string? selected = null)
        {
            names.ItemsSource = Directory.Exists(ProfilesDirectory)
                ? Directory.EnumerateFiles(ProfilesDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(value => value).ToArray()
                : [];
            names.SelectedItem = selected;
        }
        names.SelectionChanged += (_, _) => { if (names.SelectedItem is string selected) name.Text = selected; };
        PageAction(actions, L("Salvar perfil atual", "Save current profile"), (_, _) =>
        {
            try
            {
                var title = name.Text.Trim();
                if (!ValidProfileName(title)) throw new InvalidDataException(L("Escolha um nome válido para o perfil.", "Choose a valid profile name."));
                var file = Path.Combine(ProfilesDirectory, title + ".json");
                if (File.Exists(file) && MessageBox.Show(this, L("Sobrescrever este perfil?", "Overwrite this profile?"),
                        "ZEUS", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                ProfileFile.Write(file, new ExportedProfile(1, CurrentStudio(), CurrentEq()));
                Refresh(title);
                results.Text = L("Perfil salvo.", "Profile saved.");
            }
            catch (Exception error) { results.Text = error.Message; }
        });
        PageAction(actions, L("Carregar selecionado", "Load selected"), (_, _) =>
        {
            try
            {
                if (names.SelectedItem is not string selected || !ValidProfileName(selected)) return;
                var profile = ExportedProfile.Load(Path.Combine(ProfilesDirectory, selected + ".json"));
                ApplyStudio(profile.Voice);
                if (profile.Equalizer is { } eq) ApplyEq(eq);
                _voiceProfile = _eqProfile = "custom";
                UpdatePlaybackEq();
                RefreshVoiceRoutes();
                UpdateDashboard();
                PersistIfChanged();
                results.Text = L("Perfil carregado.", "Profile loaded.");
            }
            catch (Exception error) { results.Text = error.Message; }
        });
        PageAction(actions, L("Exportar atual", "Export current"), Export_Click);
        PageAction(actions, L("Importar de arquivo", "Import from file"), Import_Click);
        try { Refresh(); }
        catch (Exception error) { results.Text = error.Message; }
    }

    private static bool ValidProfileName(string name)
    {
        if (name.Length is 0 or > 70 || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        var prefix = name.Split('.')[0].Trim().ToUpperInvariant();
        return prefix is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(prefix.Length == 4 && (prefix.StartsWith("COM", StringComparison.Ordinal) ||
               prefix.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(prefix[3]));
    }

    private void BuildVoicePage()
    {
        SmartVoicePanel.Children.Add(new TextBlock
        {
            Text = L("Aplicativos que selecionam a mesma entrada virtual compartilham o processamento. A associação de maior prioridade entre os executáveis em execução controla a rota de microfone já iniciada.",
                "Apps using the same virtual input share processing. The highest-priority running association controls the microphone route you have already started."),
            TextWrapping = TextWrapping.Wrap
        });
        var names = new ComboBox();
        names.ItemsSource = _voicePresets.Select(p => p.Name).ToArray();
        names.SelectedIndex = 0;
        SmartVoicePanel.Children.Add(names);
        var actions = new WrapPanel();
        SmartVoicePanel.Children.Add(actions);
        PageAction(actions, L("Aplicar preset", "Apply preset"), (_, _) =>
        {
            if (names.SelectedIndex < 0) return;
            var key = _voicePresets[names.SelectedIndex].Id;
            ApplyStudio(VoiceRules.Preset(key, CurrentStudio()));
            _voiceProfile = key;
            RefreshVoiceRoutes();
            UpdateDashboard();
            PersistIfChanged();
        });
        PageAction(actions, L("Editar controles de voz", "Edit voice controls"), (_, _) => Navigate("Microfone"));
        PageAction(actions, L("Calibração avançada", "Advanced calibration"), Calibration_Click);
        var consent = new CheckBox
        {
            IsChecked = _intelligence.VoiceAutomation, Content = L("Autorizo detectar os executáveis associados quando a rota de voz estiver ativa.",
                "Allow detection of associated executables while the voice route is active.")
        };
        consent.Click += (_, _) =>
        {
            _intelligence.VoiceAutomation = consent.IsChecked == true;
            ResetVoiceAssociations();
        };
        SmartVoicePanel.Children.Add(consent);
        var entries = new ListBox { Height = 105, Background = System.Windows.Media.Brushes.Black, Foreground = System.Windows.Media.Brushes.White };
        SmartVoicePanel.Children.Add(entries);
        var priority = new TextBox { Width = 65, Text = "0", MaxLength = 4 };
        SmartVoicePanel.Children.Add(new TextBlock { Text = L("Prioridade (−100 a 100)", "Priority (−100 to 100)") });
        var editor = new WrapPanel { Children = { priority } };
        SmartVoicePanel.Children.Add(editor);
        void Reload() => entries.ItemsSource = _intelligence.VoiceAssociations.Select(item =>
            $"{Path.GetFileName(item.Executable)} · {item.Preset} · {item.Priority}").ToArray();
        PageAction(editor, L("Associar executável", "Associate executable"), (_, _) =>
        {
            if (names.SelectedIndex < 0 || !int.TryParse(priority.Text, out var rank) || rank is < -100 or > 100) return;
            var file = new OpenFileDialog { Filter = "Windows executable|*.exe" };
            if (file.ShowDialog(this) != true) return;
            var executable = Path.GetFullPath(file.FileName);
            var existing = _intelligence.VoiceAssociations.FindIndex(rule => string.Equals(rule.Executable, executable, StringComparison.OrdinalIgnoreCase));
            if (existing < 0 && _intelligence.VoiceAssociations.Count >= 30) return;
            var rule = new VoiceAssociation(executable, _voicePresets[names.SelectedIndex].Id, rank);
            if (existing < 0) _intelligence.VoiceAssociations.Add(rule);
            else _intelligence.VoiceAssociations[existing] = rule;
            ResetVoiceAssociations();
            Reload();
        });
        PageAction(editor, L("Remover associação", "Remove association"), (_, _) =>
        {
            if (entries.SelectedIndex < 0) return;
            _intelligence.VoiceAssociations.RemoveAt(entries.SelectedIndex);
            ResetVoiceAssociations();
            Reload();
        });
        _smartStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _smartMeter = new TextBlock { TextWrapping = TextWrapping.Wrap };
        SmartVoicePanel.Children.Add(_smartStatus);
        SmartVoicePanel.Children.Add(_smartMeter);
        Reload();
        RefreshVoiceRoutes();
    }

    private void ResetVoiceAssociations()
    {
        _voiceRevision++;
        _activeVoicePreset = null;
        RefreshVoiceRoutes();
        SaveIntelligence();
        PollSmartVoice();
    }

    private void RefreshVoiceRoutes()
    {
        foreach (var route in _routes.Values)
        {
            if (route.Endpoints.Kind == RouteKind.Playback || !route.Running) continue;
            var profile = CurrentStudio();
            if (route.Endpoints.Kind == RouteKind.Microphone && _intelligence.VoiceAutomation && _activeVoicePreset is { } key)
                profile = VoiceRules.Preset(key, profile);
            if (route.AppliedVoice == profile || Volatile.Read(ref route.Audio.Voice) is not { } voice) continue;
            voice.Set(profile);
            route.AppliedVoice = profile;
        }
        if (_smartStatus is not null)
        {
            var active = _routes.GetValueOrDefault(RouteKind.Microphone)?.Running == true;
            _smartStatus.Text = !active ? L("Rota de microfone inativa.", "Microphone route inactive.")
                : _activeVoicePreset is { } key && _intelligence.VoiceAutomation
                    ? L($"Preset automático ativo: {key}", $"Automatic preset active: {key}")
                    : L("Rota ativa · perfil manual.", "Route active · manual profile.");
        }
    }

    private async void PollSmartVoice()
    {
        if (_closing || _voicePollBusy) return;
        if (!_intelligence.VoiceAutomation || _intelligence.VoiceAssociations.Count == 0 ||
            _routes.GetValueOrDefault(RouteKind.Microphone)?.Running != true)
        {
            if (_activeVoicePreset is not null) { _activeVoicePreset = null; RefreshVoiceRoutes(); }
            return;
        }
        var revision = _voiceRevision;
        var bindings = _intelligence.VoiceAssociations.ToArray();
        _voicePollBusy = true;
        try
        {
            var match = await Task.Run(() => VoiceRules.Match(bindings, RunningExecutables(bindings.Select(rule => rule.Executable))));
            if (_closing || revision != _voiceRevision || _routes.GetValueOrDefault(RouteKind.Microphone)?.Running != true) return;
            if (_activeVoicePreset == match?.Preset) return;
            _activeVoicePreset = match?.Preset;
            RefreshVoiceRoutes();
            UpdateDashboard();
        }
        catch (Exception error) { if (!_closing) Footer.Text = error.Message; }
        finally { _voicePollBusy = false; }
    }

    private void PollSmartVoiceMeters()
    {
        if (_smartMeter is null) return;
        var route = _routes.GetValueOrDefault(RouteKind.Microphone)?.Audio;
        if (route is null || Volatile.Read(ref route.Before) is not { } before || Volatile.Read(ref route.After) is not { } after)
        {
            _smartMeter.Text = L("Picos da rota · — dBFS", "Route peaks · — dBFS");
            return;
        }
        var input = ToDb(before.ReadSnapshot(_drySamples));
        var output = ToDb(after.ReadSnapshot(_wetSamples));
        _smartMeter.Text = L($"Pico de entrada {input:0.0} dBFS · saída {output:0.0} dBFS",
            $"Input peak {input:0.0} dBFS · output {output:0.0} dBFS");
        static double ToDb(float level) => 20 * Math.Log10(Math.Max(1e-6f, level));
    }
}
