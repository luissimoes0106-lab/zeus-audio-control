using NAudio.Wave;
using System.IO;
using System.Windows.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace ZeusAudioControl;

public partial class MainWindow : Window
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly List<MMDevice> _ownedDevices = new();
    private MMDevice? _output;
    private MMDevice? _input;
    private bool _updating;
    private string? _preferredOutput, _preferredInput;
    private string? _savedSettings;
    private TimeSpan _lastCpu;
    private DateTime _lastCpuAt;
    private bool _closing;
    private MMDevice? _diagnosedOutput;
    private sealed record DeviceChoice(MMDevice Device, string Label);
    private DeviceChoice Choice(MMDevice device) => new(device,
        _aliases.TryGetValue(device.ID, out var alias) ? alias + " · " + device.FriendlyName : device.FriendlyName);
    private static MMDevice? Selected(ComboBox box) => (box.SelectedItem as DeviceChoice)?.Device;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int _ticks;
    private string _signature = "";
    private const string Model = "H510-PRO";

    public MainWindow()
    {
        InitializeComponent();
        MigrateEarlierSettings();
        StartIntelligence();
        RouteLowLatency.IsChecked = _intelligence.GamingLowLatency;
        SetupEq();
        foreach (var slider in _bands.Concat([BassBoost, LowTone, MidTone, HighTone]))
            slider.ValueChanged += (_, _) =>
            {
                if (!_applyingEq) _eqProfile = "custom";
                UpdatePlaybackEq();
                PaintEqCurve();
                UpdateDashboard();
            };
        foreach (var slider in new[] { TestGain, GateThreshold, GateAttack, GateHold, GateRelease,
            CompressorThreshold, CompressorRatio, Presence, NoiseReduction })
            slider.ValueChanged += (_, _) => { if (!_applyingStudio) _voiceProfile = "custom"; UpdateDashboard(); };
        foreach (var box in new[] { StudioEnabled, GateEnabled, HighPass, CompressorEnabled })
            box.Click += (_, _) => { _voiceProfile = "custom"; UpdateDashboard(); };
        RouteInput.SelectionChanged += (_, _) => RouteConfirm.IsChecked = false;
        RouteOutput.SelectionChanged += (_, _) => RouteConfirm.IsChecked = false;
        SetupTray();
        LoadAliases();
        RefreshDevices();
        RestoreSelection();
        RestoreStudio();
        RestoreStartup();
        RestorePreferences();
        ApplyPreferences();
        _timer.Tick += (_, _) => PollAudio();
        _timer.Start();
        SystemEvents.SessionEnding += OnSessionEnding;
        Closed += (_, _) =>
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _closing = true;
            _timer.Stop();
            foreach (var active in _routes.Values) active.Audio.Stop();
            PersistIfChanged();
            if (_intelligenceDirty) SaveIntelligence();
            StopIntelligence();
            _tray?.ContextMenuStrip?.Dispose();
            _tray?.Dispose();
            _trayIcon?.Dispose();
            StopTest();
            StopEq();
            _stereoPlayer?.Dispose();
            _stereoPlayer = null;
            ReleaseDevices();
            _enumerator.Dispose();
        };
    }

    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private void SetupTray()
    {
        _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _tray = new System.Windows.Forms.NotifyIcon { Icon = _trayIcon, Text = "ZEUS Audio Control", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir ZEUS", null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add("Sair", null, (_, _) => Dispatcher.Invoke(Close));
        menu.Items.Add("ZEUS Quick Control", null, (_, _) => Dispatcher.Invoke(() => QuickControl_Click(this, new RoutedEventArgs())));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && TrayToggle.IsChecked == true) Hide(); };
    }
    private void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Dispatcher.Invoke(Close);

    private void ReleaseDevices()
    {
        _diagnosedOutput = null;
        _output = null;
        _input = null;
        foreach (var device in _ownedDevices) device.Dispose();
        _ownedDevices.Clear();
    }

    private static bool IsHeadset(MMDevice device) =>
        device.FriendlyName.Contains(Model, StringComparison.OrdinalIgnoreCase) ||
        device.FriendlyName.Contains("H510 PRO", StringComparison.OrdinalIgnoreCase) ||
        device.FriendlyName.Contains("H510-PRO USB Gaming Headset", StringComparison.OrdinalIgnoreCase);

    private void RefreshDevices()
    {
        _updating = true;
        try
        {
            var routeInput = Selected(RouteInput)?.ID;
            var routeOutput = Selected(RouteOutput)?.ID;
            RouteInput.ItemsSource = null;
            RouteOutput.ItemsSource = null;
            var previousOutput = _preferredOutput;
            var previousInput = _preferredInput;
            OutputDevices.ItemsSource = null;
            InputDevices.ItemsSource = null;
            ReleaseDevices();
            var render = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            _ownedDevices.AddRange(render);
            var capture = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
            _ownedDevices.AddRange(capture);
            RecordEndpointChanges(_ownedDevices);
            OutputDevices.ItemsSource = render.Select(Choice).ToArray();
            InputDevices.ItemsSource = capture.Select(Choice).ToArray();
            UpdateRouteChoices(capture, render, routeInput, routeOutput);
            _signature = string.Join("|", render.Concat(capture).Select(d => d.ID).OrderBy(id => id));
            OutputDevices.SelectedItem = FindChoice(OutputDevices, previousOutput, fallback: true);
            InputDevices.SelectedItem = FindChoice(InputDevices, previousInput, fallback: true);
            UpdateDeviceStatus();
        }
        catch (Exception ex)
        {
            DeviceStatus.Text = T("Não foi possível enumerar os dispositivos de áudio.");
            Footer.Text = ex.Message;
        }
        finally { _updating = false; }
        SyncOutput();
        SyncInput();
        ShowAliases();
        UpdateDashboard();
    }

    private void UpdateDeviceStatus()
    {
        var found = _ownedDevices.Any(IsHeadset);
        DeviceStatus.Text = T(found
            ? "H510 Pro detectado nas interfaces de áudio do Windows."
            : "Modo universal: selecione qualquer dispositivo de áudio ativo.");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) { if (_testCapture is null && _stereoPlayer is null) RefreshDevices(); }

    private void OutputDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _output = Selected(OutputDevices);
        _preferredOutput = _output?.ID;
        RestoreEndpoint(_output, true);
        SyncOutput();
        ShowAliases();
        UpdateDashboard();
    }

    private void InputDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _input = Selected(InputDevices);
        _preferredInput = _input?.ID;
        RestoreEndpoint(_input, false);
        SyncInput();
        ShowAliases();
        UpdateDashboard();
    }

    private void SyncOutput()
    {
        _updating = true;
        try
        {
            _output = Selected(OutputDevices);
            VolumeSlider.IsEnabled = MuteToggle.IsEnabled = _output is not null;
            BalanceSlider.IsEnabled = false;
            StereoLeft.IsEnabled = StereoRight.IsEnabled = _output is not null;
            if (_output != _diagnosedOutput || _output is null)
            {
                try { SurroundStatus.Text = DescribeOutput(); _diagnosedOutput = _output; }
                catch (Exception error) { SurroundStatus.Text = error.Message; }
            }
            if (_output is null) { VolumeLabel.Text = T("Selecione um dispositivo de saída"); return; }
            var audio = _output.AudioEndpointVolume;
            VolumeSlider.Value = audio.MasterVolumeLevelScalar * 100;
            MuteToggle.IsChecked = audio.Mute;
            VolumeLabel.Text = $"{VolumeSlider.Value:0}%";
            var ch = audio.Channels;
            BalanceSlider.IsEnabled = ch.Count == 2;
            if (ch.Count == 2) {
                var l = ch[0].VolumeLevelScalar; var r = ch[1].VolumeLevelScalar;
                BalanceSlider.Value = Math.Max(l,r) > 0 ? (r-l)/Math.Max(l,r)*100 : 0;
                BalanceStatus.Text = Math.Abs(BalanceSlider.Value)<1 ? T("Centro") : $"{BalanceSlider.Value:0}%";
            }
        }
        catch (Exception ex) { Footer.Text = $"Falha ao ler saída: {ex.Message}"; }
        finally { _updating = false; }
    }

    private void SyncInput()
    {
        _updating = true;
        try
        {
            _input = Selected(InputDevices);
            MicSlider.IsEnabled = MicMute.IsEnabled = _input is not null;
            if (_input is null) { MicLabel.Text = T("Selecione um microfone"); return; }
            MicMute.IsChecked = _input.AudioEndpointVolume.Mute;
            MicSlider.Value = _input.AudioEndpointVolume.MasterVolumeLevelScalar * 100;
            MicLabel.Text = $"{MicSlider.Value:0}%";
        }
        catch (Exception ex) { Footer.Text = $"Falha ao ler microfone: {ex.Message}"; }
        finally { _updating = false; }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _output is null) return;
        try
        {
            var safe = IntelligenceRules.Limit(VolumeSlider.Value, _intelligence.HearingLimit ?? 100);
            if (safe != VolumeSlider.Value)
            {
                _updating = true;
                try { VolumeSlider.Value = safe; }
                finally { _updating = false; }
                Footer.Text = L("Limite de volume aplicado.", "Volume limit applied.");
            }
            _output.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(safe / 100);
            RememberEndpoint(_output);
            VolumeLabel.Text = $"{VolumeSlider.Value:0}%";
        }
        catch (Exception ex) { Footer.Text = $"Falha ao ajustar volume: {ex.Message}"; }
    }

    private void Balance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _output is null) return;
        try {
            var channels = _output.AudioEndpointVolume.Channels;
            if (channels.Count != 2) { BalanceStatus.Text = T("Disponível apenas em saídas estéreo."); return; }
            var master = Math.Max(channels[0].VolumeLevelScalar, channels[1].VolumeLevelScalar);
            var b = (float)(BalanceSlider.Value / 100);
            channels[0].VolumeLevelScalar = master * (1 - Math.Max(0, b));
            channels[1].VolumeLevelScalar = master * (1 + Math.Min(0, b));
            BalanceStatus.Text = b == 0 ? T("Centro") : $"{Math.Abs(b)*100:0}% para {(b < 0 ? "esquerda" : "direita")}";
        } catch (Exception ex) { BalanceStatus.Text = ex.Message; }
    }
    private void MicSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _input is null) return;
        try
        {
            _input.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(MicSlider.Value / 100);
            RememberEndpoint(_input);
            MicLabel.Text = $"{MicSlider.Value:0}%";
        }
        catch (Exception ex) { Footer.Text = $"Falha ao ajustar microfone: {ex.Message}"; }
    }

    private void MuteToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || _output is null) return;
        try { _output.AudioEndpointVolume.Mute = MuteToggle.IsChecked == true; RememberEndpoint(_output); }
        catch (Exception ex) { Footer.Text = $"Falha ao silenciar: {ex.Message}"; }
    }

    private void MicMute_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || _input is null) return;
        try { _input.AudioEndpointVolume.Mute = MicMute.IsChecked == true; RememberEndpoint(_input); }
        catch (Exception ex) { Footer.Text = $"Falha ao silenciar microfone: {ex.Message}"; }
    }

    private void PollAudio()
    {
        PollRoute();
        if (_updating || _testCapture is not null || _stereoPlayer is not null) return;
        try
        {
            try
            {
                var peak = _input is null || !IsVisible ? 0 : _input.AudioMeterInformation.MasterPeakValue;
                var db = 20 * Math.Log10(Math.Max(0.000001, peak));
                MicMeter.Value = Math.Clamp((db + 60) / 60 * 100, 0, 100);
                PeakDb.Text = _english ? $"Peak: {db:0.0} dBFS" : $"Pico: {db:0.0} dBFS";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                MicMeter.Value = 0;
                PeakDb.Text = "— dBFS";
            }
            if (++_ticks % 14 != 0) return;
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                var now = DateTime.UtcNow;
                var cpu = process.TotalProcessorTime;
                var elapsed = (now - _lastCpuAt).TotalSeconds;
                var percent = elapsed > 0 && _lastCpuAt != default
                    ? 100 * (cpu - _lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount : 0;
                Performance.Text = $"CPU: {percent:0.0}%  ·  RAM: {process.WorkingSet64 / 1048576.0:0} MB";
                _lastCpu = cpu;
                _lastCpuAt = now;
            }
            PersistIfChanged();
            UpdateDashboard();
            if (_page == "Áudio" && _ticks % 70 == 0) _diagnosedOutput = null;
            PollIntelligence();
            var refresh = _endpointsChanged;
            _endpointsChanged = false;
            if (_endpointNotifications is null || _ticks % 140 == 0)
            {
                var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active).ToList();
                try { refresh |= string.Join("|", devices.Select(d => d.ID).OrderBy(id => id)) != _signature; }
                finally { foreach (var device in devices) device.Dispose(); }
            }
            if (refresh) RefreshDevices();
            else
            {
                if (!VolumeSlider.IsMouseCaptureWithin && !BalanceSlider.IsMouseCaptureWithin) SyncOutput();
                if (!MicSlider.IsMouseCaptureWithin) SyncInput();
            }
        }
        catch (Exception ex) { MicMeter.Value = 0; Footer.Text = $"Diagnóstico: {ex.Message}"; }
    }

    private NAudio.CoreAudioApi.WasapiCapture? _testCapture;
    private NAudio.Wave.WaveFileWriter? _testWriter;
    private NAudio.Wave.WaveOutEvent? _testPlayer;
    private NAudio.Wave.AudioFileReader? _testReader;
    private readonly string _testPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zeus-" + Guid.NewGuid().ToString("N") + ".wav");
    private readonly string _settingsPath = Path.Combine(
        Environment.GetEnvironmentVariable("ZEUS_UI_SMOKE_PROFILE") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeusAudioControl", "Beta07"),
        "devices.json");
    private void Overview_Click(object sender, RoutedEventArgs e) => Navigate("Visão geral");
    private void Audio_Click(object sender, RoutedEventArgs e) => Navigate("Áudio");
    private void Microphone_Click(object sender, RoutedEventArgs e) => Navigate("Microfone");
    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate("Configurações");
    private void Navigate(string page)
    {
        _page = page;
        PageTitle.Text = T(page);
        OverviewPanel.Visibility = page == "Visão geral" ? Visibility.Visible : Visibility.Collapsed;
        ControlsCard.Visibility = page == "Visão geral" ? Visibility.Collapsed : Visibility.Visible;
        AudioPanel.Visibility = page == "Áudio" ? Visibility.Visible : Visibility.Collapsed;
        MicPanel.Visibility = page == "Microfone" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "Configurações" ? Visibility.Visible : Visibility.Collapsed;
        RoutesPanel.Visibility = page == "Rotas" ? Visibility.Visible : Visibility.Collapsed;
        MainScroll.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(130)));
        MainScroll.ScrollToTop();
    }
    private string StudioPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_settingsPath)!, "studio.json");
    private StudioProfile CurrentStudio() => new(TestGain.Value, StudioEnabled.IsChecked == true, HighPass.IsChecked == true, GateEnabled.IsChecked == true, GateThreshold.Value, CompressorEnabled.IsChecked == true, Presence.Value, NoiseReduction.Value, GateAttack.Value, GateHold.Value, GateRelease.Value, CompressorThreshold.Value, CompressorRatio.Value, VoiceShield.IsChecked == true, GamingVoiceAgc.IsChecked == true);
    private bool _applyingStudio;
    private void ApplyStudio(StudioProfile p)
    {
        _applyingStudio = true;
        try
        {
            TestGain.Value = Math.Clamp(p.Gain, 0, 24);
            StudioEnabled.IsChecked = p.Enabled;
            HighPass.IsChecked = p.HighPass;
            GateEnabled.IsChecked = p.Gate;
            GateThreshold.Value = Math.Clamp(p.Threshold, -65, -20);
            CompressorEnabled.IsChecked = p.Compressor;
            Presence.Value = Math.Clamp(p.Presence, -6, 6);
            NoiseReduction.Value = Math.Clamp(p.Noise, 0, 100);
            GateAttack.Value = Math.Clamp(p.Attack, 1, 100);
            GateHold.Value = Math.Clamp(p.Hold, 0, 500);
            GateRelease.Value = Math.Clamp(p.Release, 10, 1000);
            CompressorThreshold.Value = Math.Clamp(p.CompressorThreshold, -48, -3);
            CompressorRatio.Value = Math.Clamp(p.Ratio, 1, 12);
            VoiceShield.IsChecked = p.Shield;
            GamingVoiceAgc.IsChecked = p.AutomaticGain;
        }
        finally { _applyingStudio = false; }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag as string ?? "studio";
        _voiceProfile = tag;
        ApplyStudio(tag switch
        {
            "communication" => new StudioProfile(6, true, true, true, -38, true, 3, 60, 4, 65, 155, -20, 4),
            "gaming" => GamingVoiceProfile,
            "record" => new StudioProfile(3, true, true, false, -50, true, 1, 25, 8, 0, 190, -15, 2.5),
            _ => new StudioProfile(6, true, true, true, -45, true, 2, 40, 5, 55, 150, -18, 3)
        });
        UpdateDashboard();
        StudioStatus.Text = L("Perfil de voz aplicado. Reinicie uma rota de voz ativa para usar esses ajustes.", "Voice profile applied. Restart any active voice route to use these settings.");
    }
    private void SaveStudio_Click(object sender, RoutedEventArgs e)
    {
        try {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StudioPath)!);
            System.IO.File.WriteAllText(StudioPath, System.Text.Json.JsonSerializer.Serialize(CurrentStudio()));
            StudioStatus.Text = T("Perfil personalizado salvo.");
        } catch (Exception ex) { StudioStatus.Text = ex.Message; }
    }
    private void RestoreStudio()
    {
        try {
            if (System.IO.File.Exists(StudioPath) && System.Text.Json.JsonSerializer.Deserialize<StudioProfile>(System.IO.File.ReadAllText(StudioPath)) is StudioProfile p) ApplyStudio(p);
        } catch (Exception ex) { StudioStatus.Text = ex.Message; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_settingsPath)!);
            ProfileFile.Write(_settingsPath, new string?[] { _preferredOutput ?? _output?.ID, _preferredInput ?? _input?.ID });
            Footer.Text = T("Seleção salva neste computador.");
        } catch (Exception ex) { Footer.Text = ex.Message; }
    }
    private static DeviceChoice? FindChoice(ComboBox box, string? id, bool fallback)
    {
        var choices = box.Items.OfType<DeviceChoice>();
        return choices.FirstOrDefault(c => c.Device.ID == id)
            ?? (fallback ? choices.FirstOrDefault(c => IsHeadset(c.Device)) ?? choices.FirstOrDefault() : null);
    }

    private void RestoreSelection()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var ids = ProfileFile.Read<string?[]>(_settingsPath);
            if (ids.Length != 2) return;
            _preferredOutput = ids[0];
            _preferredInput = ids[1];
            RefreshDevices();
        }
        catch (Exception ex) { Footer.Text = ex.Message; }
    }

    private string DescribeOutput()
    {
        if (_output is null)
        {
            UpdateChannelTests(0);
            return T("Selecione uma saída para verificar o formato.");
        }
        using var client = _output.AudioClient;
        var format = client.MixFormat;
        var mask = ChannelLayout.GetMask(format);
        UpdateChannelTests(format.Channels);
        var layout = format.Channels == 8 && mask is 0x63F or 0xFF
            ? T("Formato 7.1 anunciado pelo Windows. Confirme a saída física no teste de alto-falantes do Windows.")
            : _english ? $"{format.Channels} channel(s) in the current mix format." : $"{format.Channels} canal(is) no formato de mixagem atual.";
        return $"{layout} {format.SampleRate:N0} Hz.";
    }

    private sealed record Preferences(bool Tray, bool Advanced, bool English, string EqProfile = "custom", string VoiceProfile = "custom");
    private string PreferencesPath => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "preferences.json");
    private Preferences CurrentPreferences() => new(TrayToggle.IsChecked == true, AdvancedToggle.IsChecked == true, EnglishToggle.IsChecked == true, _eqProfile, _voiceProfile);

    private void MigrateEarlierSettings()
    {
        if (Environment.GetEnvironmentVariable("ZEUS_UI_SMOKE_PROFILE") is not null) return;
        var directory = Path.GetDirectoryName(_settingsPath)!;
        var previous = Path.GetDirectoryName(directory)!;
        foreach (var file in new[] { "devices.json", "studio.json", "eq.json", "aliases.json", "preferences.json" })
        {
            var oldPath = Path.Combine(previous, "Beta05", file);
            if (!File.Exists(oldPath)) oldPath = Path.Combine(previous, file);
            var newPath = Path.Combine(directory, file);
            try
            {
                if (!File.Exists(newPath) && File.Exists(oldPath))
                    ProfileFile.Write(newPath, ProfileFile.Read<System.Text.Json.JsonElement>(oldPath));
            }
            catch (Exception error) { Footer.Text = error.Message; }
        }
    }

    private void RestorePreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return;
            var preferences = ProfileFile.Read<Preferences>(PreferencesPath);
            _eqProfile = preferences.EqProfile;
            _voiceProfile = preferences.VoiceProfile;
            TrayToggle.IsChecked = preferences.Tray;
            AdvancedToggle.IsChecked = preferences.Advanced;
            EnglishToggle.IsChecked = preferences.English;
            ApplyPreferences();
        }
        catch (Exception ex) { Footer.Text = ex.Message; }
    }

    private void Preferences_Click(object sender, RoutedEventArgs e) => ApplyPreferences();

    private void ApplyPreferences()
    {
        var preferences = CurrentPreferences();
        AdvancedAudio.Visibility = AdvancedMic.Visibility = preferences.Advanced ? Visibility.Visible : Visibility.Collapsed;
        ApplyLanguage(preferences.English);
    }

    private void PersistIfChanged()
    {
        try
        {
            var preferences = CurrentPreferences();
            var eq = CurrentEq();
            var studio = CurrentStudio();
            string?[] ids = [_preferredOutput ?? _output?.ID, _preferredInput ?? _input?.ID];
            var snapshot = System.Text.Json.JsonSerializer.Serialize(new { preferences, eq, studio, ids });
            if (snapshot == _savedSettings) return;
            ProfileFile.Write(PreferencesPath, preferences);
            ProfileFile.Write(StudioPath, studio);
            ProfileFile.Write(EqPath, eq);
            ProfileFile.Write(_settingsPath, ids);
            _savedSettings = snapshot;
        }
        catch (Exception ex) { Footer.Text = ex.Message; }
    }

    private async void Record_Click(object sender, RoutedEventArgs e) => await CaptureTest();

    private async Task<bool> CaptureTest()
    {
        if (_input is null || _testCapture is not null) return false;
        RecordButton.IsEnabled = PlayButton.IsEnabled = false;
        StopMicrophonePlayback();
        try {
            _testCapture = new NAudio.CoreAudioApi.WasapiCapture(_input);
            _testWriter = new NAudio.Wave.WaveFileWriter(_testPath, _testCapture.WaveFormat);
            var done = new System.Threading.Tasks.TaskCompletionSource<Exception?>();
            var capture = _testCapture;
            var writer = _testWriter;
            capture.DataAvailable += (_, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
                UpdateWaveform(a.Buffer, a.BytesRecorded, capture.WaveFormat);
            };
            _testCapture.RecordingStopped += (_, a) => done.TrySetResult(a.Exception);
            _testCapture.StartRecording();
            TestStatus.Text = T("Gravando localmente por 5 segundos...");
            await System.Threading.Tasks.Task.WhenAny(done.Task, System.Threading.Tasks.Task.Delay(5000));
            if (_closing) return false;
            _testCapture?.StopRecording();
            if (await System.Threading.Tasks.Task.WhenAny(done.Task, System.Threading.Tasks.Task.Delay(3000)) != done.Task) throw new TimeoutException("Tempo limite de gravação.");
            if (_closing) return false;
            if (await done.Task is Exception error) throw error;
            _testCapture?.Dispose(); _testCapture = null;
            _testWriter?.Dispose(); _testWriter = null;
            PlayButton.IsEnabled = true;
            TestStatus.Text = T("Teste gravado. Ouça com volume baixo. Reprodução pela saída padrão do Windows.");
            return true;
        } catch (Exception ex) { StopTest(); TestStatus.Text = ex.Message; return false; }
        finally { if (!_closing) RecordButton.IsEnabled = true; }
    }
    private void UpdateWaveform(byte[] data, int length, NAudio.Wave.WaveFormat format)
    {
        if (format.Encoding != NAudio.Wave.WaveFormatEncoding.IeeeFloat &&
            format.Encoding != NAudio.Wave.WaveFormatEncoding.Pcm &&
            format.Encoding != NAudio.Wave.WaveFormatEncoding.Extensible) return;
        var samples = new float[96];
        var bytesPerSample = format.BitsPerSample / 8;
        var frames = length / format.BlockAlign;
        if (frames == 0 || bytesPerSample is < 2 or > 4) return;
        var floating = format.Encoding == NAudio.Wave.WaveFormatEncoding.IeeeFloat ||
            format is NAudio.Wave.WaveFormatExtensible ext &&
            ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        for (var i = 0; i < samples.Length; i++)
        {
            var index = i * frames / samples.Length * format.BlockAlign;
            samples[i] = bytesPerSample switch
            {
                2 => BitConverter.ToInt16(data, index) / 32768f,
                3 => ((data[index] << 8) | (data[index + 1] << 16) | (data[index + 2] << 24)) / 2147483648f,
                4 when floating => BitConverter.ToSingle(data, index),
                4 => BitConverter.ToInt32(data, index) / 2147483648f,
                _ => 0
            };
        }
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing || _testCapture is null) return;
            var points = new PointCollection(samples.Length);
            for (var i = 0; i < samples.Length; i++)
            {
                var amplitude = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1, 1) : 0;
                points.Add(new Point(i * WaveCanvas.ActualWidth / (samples.Length - 1), 32 - amplitude * 31));
            }
            WaveLine.Points = points;
        });
    }

    private void Play_Click(object sender, RoutedEventArgs e) => PlayTest(RawPlayback.IsChecked == true);

    private void PlayTest(bool original, StudioProfile? preview = null)
    {
        try {
            StopMicrophonePlayback();
            _testReader = new NAudio.Wave.AudioFileReader(_testPath);
            _testReader.Volume = 1f;
            _testPlayer = new NAudio.Wave.WaveOutEvent();
            if (original) { _testReader.Volume = .35f; _testPlayer.Init(_testReader); }
            else
            {
                var profile = preview ?? CurrentStudio();
                _testPlayer.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(new TestGainProvider(_testReader,
                    (float)Math.Pow(10, profile.Gain / 20), profile.Enabled, profile.HighPass, profile.Gate, profile.Threshold,
                    profile.Compressor, profile.Presence, profile.Noise, profile.Attack, profile.Hold, profile.Release,
                    profile.CompressorThreshold, profile.Ratio, profile.Shield, profile.AutomaticGain)));
            }
            var player = _testPlayer;
            player.PlaybackStopped += (_, args) => Dispatcher.BeginInvoke(() =>
            {
                if (_testPlayer != player) return;
                StopMicrophonePlayback();
                if (args.Exception is { } error) TestStatus.Text = error.Message;
            });
            player.Play();
        } catch (Exception ex) { StopMicrophonePlayback(); TestStatus.Text = ex.Message; }
    }
    private void StopMicrophonePlayback()
    {
        var player = _testPlayer;
        _testPlayer = null;
        try { player?.Dispose(); }
        finally { _testReader?.Dispose(); _testReader = null; }
    }

    private void StopTest()
    {
        try { _testCapture?.StopRecording(); _testCapture?.Dispose(); } catch { }
        _testCapture = null;
        _testWriter?.Dispose(); _testWriter = null;
        StopMicrophonePlayback();
        try { System.IO.File.Delete(_testPath); } catch { }
    }


    private Dictionary<string,string> _aliases = new();
    private string AliasPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_settingsPath)!, "aliases.json");
    private void LoadAliases() { try { if(System.IO.File.Exists(AliasPath)) _aliases=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(System.IO.File.ReadAllText(AliasPath)) ?? new(); } catch { } }
    private void ShowAliases() { OutputAlias.Text=_output is null?"":_aliases.GetValueOrDefault(_output.ID,""); InputAlias.Text=_input is null?"":_aliases.GetValueOrDefault(_input.ID,""); }
    private void Alias_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool output = (sender as Button)?.Tag as string == "out";
            var device = output ? _output : _input;
            if (device is null) return;
            var alias = (output ? OutputAlias.Text : InputAlias.Text).Trim();
            if (alias.Length > 80) { Footer.Text = T("Use até 80 caracteres."); return; }
            var aliases = new Dictionary<string, string>(_aliases);
            if (alias.Length == 0) aliases.Remove(device.ID);
            else aliases[device.ID] = alias;
            ProfileFile.Write(AliasPath, aliases);
            _aliases = aliases;
            var selector = output ? OutputDevices : InputDevices;
            _updating = true;
            try
            {
                selector.ItemsSource = selector.Items.Cast<DeviceChoice>().Select(c => Choice(c.Device)).ToArray();
                selector.SelectedItem = FindChoice(selector, device.ID, fallback: false);
                var routeSelector = output ? RouteOutput : RouteInput;
                var routeId = Selected(routeSelector)?.ID;
                routeSelector.ItemsSource = routeSelector.Items.Cast<DeviceChoice>().Select(c => Choice(c.Device)).ToArray();
                routeSelector.SelectedItem = FindChoice(routeSelector, routeId, fallback: false);
            }
            finally { _updating = false; }
            UpdateDashboard();
            Footer.Text = alias.Length == 0 ? T("Apelido removido.") : $"Apelido salvo: {alias}";
        }
        catch (Exception ex) { Footer.Text = ex.Message; }
    }
    private void RestoreStartup() { try { using var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); AutoStart.IsChecked=k?.GetValue("ZeusAudioControlBeta07") is string; } catch { } }
    private void AutoStart_Click(object sender,RoutedEventArgs e) { try { using var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); if(AutoStart.IsChecked==true) k.SetValue("ZeusAudioControlBeta07","\""+Environment.ProcessPath+"\""); else k.DeleteValue("ZeusAudioControlBeta07",false); } catch(Exception ex){Footer.Text=ex.Message;RestoreStartup();} }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog { Filter = "ZEUS (*.json)|*.json", FileName = "zeus-profile.json" };
            if (dialog.ShowDialog() == true)
                ProfileFile.Write(dialog.FileName, new ExportedProfile(1, CurrentStudio(), CurrentEq()));
        }
        catch (Exception ex) { Footer.Text = ex.Message; }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Filter = "ZEUS (*.json)|*.json" };
            if (dialog.ShowDialog() != true) return;
            var profile = ExportedProfile.Load(dialog.FileName);
            _voiceProfile = "custom";
            if (profile.Equalizer is not null) _eqProfile = "custom";
            ApplyStudio(profile.Voice);
            if (profile.Equalizer is { } values) ApplyEq(values);
            PersistIfChanged();
        }
        catch (Exception ex) { Footer.Text = ex.Message; }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _voiceProfile = _eqProfile = "custom";
        ApplyStudio(new StudioProfile(6, true, true, true, -45, true, 2));
        ApplyEq(new double[14]);
        PersistIfChanged();
    }
    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_testCapture is not null || _stereoPlayer is not null)
        {
            Footer.Text = _english ? "Wait for the active test to finish." : "Aguarde o teste em andamento.";
            return;
        }

        var prompt = _english
            ? "Reset local profiles, aliases, device selection and app preferences?"
            : "Restaurar perfis, apelidos, seleção de dispositivos e preferências locais?";
        if (MessageBox.Show(this, prompt, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            ProfileFile.Write(AliasPath, new Dictionary<string, string>());
            _aliases.Clear();
            using var startup = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            startup?.DeleteValue("ZeusAudioControlBeta07", throwOnMissingValue: false);
            AutoStart.IsChecked = false;
            _preferredOutput = _preferredInput = null;
            TrayToggle.IsChecked = AdvancedToggle.IsChecked = true;
            EnglishToggle.IsChecked = false;
            ApplyPreferences();
            RefreshDevices();
            Reset_Click(sender, e);
        }
        catch (Exception error) { Footer.Text = error.Message; }
    }

    private NAudio.Wave.WasapiOut? _stereoPlayer;

    private async void Stereo_Click(object sender, RoutedEventArgs e)
    {
        if (_output is null || _stereoPlayer is not null) return;
        if (!int.TryParse((sender as Button)?.Tag as string, out var side)) return;
        try
        {
            using var device = _output.AudioClient;
            var mix = device.MixFormat;
            var samples = StereoSignal.Create(side, mix.Channels, mix.SampleRate);
            var format = mix.Channels <= 2 ? new WaveFormat(mix.SampleRate, 16, mix.Channels) : mix;
            if (mix.Channels > 2 && mix.BitsPerSample == 32 && mix is WaveFormatExtensible extended &&
                extended.ToStandardWaveFormat().Encoding == WaveFormatEncoding.IeeeFloat)
            {
                var pcm = samples;
                samples = new byte[pcm.Length * 2];
                for (var i = 0; i < pcm.Length / 2; i++)
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(samples.AsSpan(i * 4, 4), BitConverter.ToInt16(pcm, i * 2) / 32768f);
            }
            else if (mix.Channels > 2 && mix.BitsPerSample != 16)
                throw new NotSupportedException(L("Formato multicanal incompatível com o teste.", "Multichannel format unsupported by the test."));
            using var stream = new MemoryStream(samples);
            using var wave = new RawSourceWaveStream(stream, format);
            var player = _stereoPlayer = new NAudio.Wave.WasapiOut(_output, AudioClientShareMode.Shared, false, 100);
            player.Init(wave);
            player.Play();
            await System.Threading.Tasks.Task.Delay(800);
            if (_stereoPlayer == player) player.Stop();
        }
        catch (Exception ex) { Footer.Text = ex.Message; }
        finally
        {
            _stereoPlayer?.Dispose();
            _stereoPlayer = null;
        }
    }

    private static readonly double[] BandFrequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    private static readonly double[] EqFrequencies = BandFrequencies.Concat([120d, 1000d, 6500d]).ToArray();
    private bool _applyingEq;
    private readonly List<Slider> _bands = new();
    private WaveStream? _eqReader;
    private LocalEq? _playbackEq;
    private NAudio.Wave.WaveOutEvent? _eqPlayer;
    private string EqPath => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "eq.json");
    private void SetupEq()
    {
        foreach (var frequency in BandFrequencies)
        {
            var panel = new StackPanel();
            var label = frequency >= 1000 ? $"{frequency / 1000:0}k" : $"{frequency:0}";
            var slider = new Slider
            {
                Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12,
                Height = 120, Width = 24, TickFrequency = 1, IsSnapToTickEnabled = true,
                ToolTip = $"{frequency:0} Hz"
            };
            System.Windows.Automation.AutomationProperties.SetName(slider, $"{frequency:0} Hz");
            _bands.Add(slider);
            panel.Children.Add(slider);
            panel.Children.Add(new TextBlock { Text = label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
            EqGrid.Children.Add(panel);
        }
        try
        {
            if (File.Exists(EqPath))
            {
                var values = ProfileFile.Read<double[]>(EqPath);
                if (ExportedProfile.ValidEq(values)) ApplyEq(values);
            }
        }
        catch (Exception error) { EqStatus.Text = error.Message; }
    }

    private double[] CurrentEq() => _bands.Select(b => b.Value)
        .Concat([BassBoost.Value, LowTone.Value, MidTone.Value, HighTone.Value]).ToArray();

    private void ApplyEq(double[] values)
    {
        _applyingEq = true;
        try
        {
            for (var i = 0; i < 10; i++) _bands[i].Value = Math.Clamp(values[i], -12, 12);
            BassBoost.Value = Math.Clamp(values[10], 0, 9);
            LowTone.Value = values.Length == 14 ? Math.Clamp(values[11], -9, 9) : 0;
            MidTone.Value = values.Length == 14 ? Math.Clamp(values[12], -9, 9) : 0;
            HighTone.Value = values.Length == 14 ? Math.Clamp(values[13], -9, 9) : 0;
        }
        finally { _applyingEq = false; }
    }
    private void EqPreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = (sender as Button)?.Tag as string;
        _eqProfile = preset ?? "custom";
        UpdateDashboard();
        if (preset == "custom")
        {
            AdvancedToggle.IsChecked = true;
            ApplyPreferences();
            return;
        }
        double[] gains = preset switch
        {
            "fps" or "immersive" => GamingRules.Preset(preset).Take(10).ToArray(),
            "game" => [-3, -2, -1, 0, 1, 2, 4, 4, 2, 0],
            "music" => [3, 3, 2, 0, -1, 0, 1, 2, 3, 2],
            "movie" => [4, 3, 2, 0, 0, 2, 2, 1, 0, 0],
            _ => new double[10]
        };
        ApplyEq(preset is "fps" or "immersive" ? GamingRules.Preset(preset) : gains.Concat(new double[4]).ToArray());
    }

    private void StopEq()
    {
        var player = _eqPlayer;
        _eqPlayer = null;
        _playbackEq = null;
        try { player?.Dispose(); }
        finally { _eqReader?.Dispose(); _eqReader = null; }
    }
    private void EqStop_Click(object sender,RoutedEventArgs e){StopEq();EqStatus.Text="Reprodução parada.";}
    private double[] PlaybackGains() =>
        _bands.Select(band => band.Value).Concat([LowTone.Value, MidTone.Value, HighTone.Value]).ToArray();

    private void UpdatePlaybackEq()
    {
        var routeEq = _routes.GetValueOrDefault(RouteKind.Playback) is { } active ? Volatile.Read(ref active.Audio.Eq) : null;
        if (_playbackEq is null && routeEq is null) return;
        var gains = PlaybackGains();
        _playbackEq?.SetGains(gains, BassBoost.Value);
        if (routeEq is not null)
        {
            if (GamingRules.RouteEq(_gamingManualEq, _gamingDetectedEq, _automaticEq) is { } automatic)
            {
                var bands = automatic.Take(10).Concat(automatic.Length == 14 ? automatic.Skip(11) : new double[3]).ToArray();
                routeEq.SetGains(bands, automatic[10]);
            }
            else routeEq.SetGains(gains, BassBoost.Value);
        }
    }

    private void EqPlay_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.aiff;*.wma" };
            if (dialog.ShowDialog() != true) return;
            StopEq();
            ISampleProvider source;
            if (VirtualSurround.IsChecked == true)
            {
                if (!string.Equals(Path.GetExtension(dialog.FileName), ".wav", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(L("A espacialização exige WAV multicanal.", "Spatialization requires multichannel WAV."));
                var input = new WaveFileReader(dialog.FileName);
                _eqReader = input;
                var channels = input.WaveFormat.Channels;
                var mask = ChannelLayout.GetMask(input.WaveFormat);
                if (!(channels == 6 && mask is 0x3F or 0x60F || channels == 8 && mask == 0x63F))
                    throw new InvalidDataException(L("O WAV precisa declarar uma máscara 5.1 ou 7.1 compatível.", "The WAV must declare a supported 5.1 or 7.1 channel mask."));
                var samples = new RawSourceWaveStream(input, input.WaveFormat.AsStandardWaveFormat()).ToSampleProvider();
                source = new StereoSpatializer(samples);
            }
            else
            {
                var reader = new AudioFileReader(dialog.FileName);
                _eqReader = reader;
                source = reader;
            }
            var player = _eqPlayer = new WaveOutEvent();
            var eq = _playbackEq = new LocalEq(source, EqFrequencies, PlaybackGains(), BassBoost.Value);
            player.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(eq));
            player.PlaybackStopped += (_, args) => Dispatcher.BeginInvoke(() =>
            {
                if (_eqPlayer != player) return;
                StopEq();
                EqStatus.Text = args.Exception?.Message ?? T("Reprodução parada.");
            });
            player.Play();
            EqStatus.Text = T("Reproduzindo na saída padrão. Ajustes aplicados em tempo real.");
        }
        catch (Exception ex) { StopEq(); EqStatus.Text = ex.Message; }
    }
}
