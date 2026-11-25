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
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int _ticks;
    private string _signature = "";
    private const string Model = "H510-PRO";

    public MainWindow()
    {
        InitializeComponent();
        RefreshDevices();
        RestoreSelection();
        _timer.Tick += (_, _) => PollAudio();
        _timer.Start();
        SystemEvents.SessionEnding += OnSessionEnding;
        Closed += (_, _) =>
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _timer.Stop();
            StopTest();
            ReleaseDevices();
            _enumerator.Dispose();
        };
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Close();

    private void ReleaseDevices()
    {
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
            var previousOutput = _output?.ID;
            var previousInput = _input?.ID;
            OutputDevices.ItemsSource = null;
            InputDevices.ItemsSource = null;
            ReleaseDevices();
            var render = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            var capture = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
            _ownedDevices.AddRange(render);
            _ownedDevices.AddRange(capture);
            OutputDevices.ItemsSource = render;
            InputDevices.ItemsSource = capture;
            _signature = string.Join("|", render.Concat(capture).Select(d => d.ID).OrderBy(id => id));
            OutputDevices.SelectedItem = render.FirstOrDefault(d => d.ID == previousOutput) ?? render.FirstOrDefault(IsHeadset);
            InputDevices.SelectedItem = capture.FirstOrDefault(d => d.ID == previousInput) ?? capture.FirstOrDefault(IsHeadset);
            var found = render.Any(IsHeadset) || capture.Any(IsHeadset);
            DeviceStatus.Text = found
                ? "H510 Pro detectado nas interfaces de áudio do Windows."
                : "H510 Pro não encontrado entre os endpoints ativos. Confira a conexão USB e use Atualizar dispositivos.";
            Footer.Text = found
                ? "Dispositivo detectado. Os controles abaixo atuam nos endpoints selecionados, sem mudar o driver."
                : "Nenhum dispositivo H510 Pro detectado. Outros dispositivos não serão selecionados automaticamente.";
        }
        catch (Exception ex)
        {
            DeviceStatus.Text = "Não foi possível enumerar os dispositivos de áudio.";
            Footer.Text = ex.Message;
        }
        finally { _updating = false; }
        SyncOutput();
        SyncInput();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) { if (_testCapture is null) RefreshDevices(); }

    private void OutputDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _output = OutputDevices.SelectedItem as MMDevice;
        SyncOutput();
    }

    private void InputDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _input = InputDevices.SelectedItem as MMDevice;
        SyncInput();
    }

    private void SyncOutput()
    {
        _updating = true;
        try
        {
            _output = OutputDevices.SelectedItem as MMDevice;
            VolumeSlider.IsEnabled = MuteToggle.IsEnabled = _output is not null;
            if (_output is null) { VolumeLabel.Text = "Selecione um dispositivo de saída"; return; }
            var audio = _output.AudioEndpointVolume;
            VolumeSlider.Value = audio.MasterVolumeLevelScalar * 100;
            MuteToggle.IsChecked = audio.Mute;
            VolumeLabel.Text = $"{VolumeSlider.Value:0}%";
        }
        catch (Exception ex) { Footer.Text = $"Falha ao ler saída: {ex.Message}"; }
        finally { _updating = false; }
    }

    private void SyncInput()
    {
        _updating = true;
        try
        {
            _input = InputDevices.SelectedItem as MMDevice;
            MicSlider.IsEnabled = MicMute.IsEnabled = _input is not null;
            if (_input is null) { MicLabel.Text = "Selecione um microfone"; return; }
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
            _output.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(VolumeSlider.Value / 100);
            VolumeLabel.Text = $"{VolumeSlider.Value:0}%";
        }
        catch (Exception ex) { Footer.Text = $"Falha ao ajustar volume: {ex.Message}"; }
    }

    private void MicSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _input is null) return;
        try
        {
            _input.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(MicSlider.Value / 100);
            MicLabel.Text = $"{MicSlider.Value:0}%";
        }
        catch (Exception ex) { Footer.Text = $"Falha ao ajustar microfone: {ex.Message}"; }
    }

    private void MuteToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || _output is null) return;
        try { _output.AudioEndpointVolume.Mute = MuteToggle.IsChecked == true; }
        catch (Exception ex) { Footer.Text = $"Falha ao silenciar: {ex.Message}"; }
    }

    private void MicMute_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || _input is null) return;
        try { _input.AudioEndpointVolume.Mute = MicMute.IsChecked == true; }
        catch (Exception ex) { Footer.Text = $"Falha ao silenciar microfone: {ex.Message}"; }
    }

    private void PollAudio()
    {
        if (_updating || _testCapture is not null) return;
        try
        {
            var peak = _input is null ? 0 : _input.AudioMeterInformation.MasterPeakValue;
            var db = 20 * Math.Log10(Math.Max(0.000001, peak));
            MicMeter.Value = Math.Clamp((db + 60) / 60 * 100, 0, 100);
            PeakDb.Text = $"Pico: {db:0.0} dBFS (escala -60 a 0)";
            if (++_ticks % 14 != 0) return;
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active).ToList();
            string signature;
            try { signature = string.Join("|", devices.Select(d => d.ID).OrderBy(id => id)); }
            finally { foreach (var d in devices) d.Dispose(); }
            if (signature != _signature) RefreshDevices();
            else
            {
                if (!VolumeSlider.IsMouseCaptureWithin) SyncOutput();
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
    private readonly string _settingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeusAudioControl", "devices.json");
    private void Overview_Click(object sender, RoutedEventArgs e) => Navigate("Visão geral");
    private void Audio_Click(object sender, RoutedEventArgs e) => Navigate("Áudio");
    private void Microphone_Click(object sender, RoutedEventArgs e) => Navigate("Microfone");
    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate("Configurações");
    private void Navigate(string page)
    {
        PageTitle.Text = page;
        OverviewPanel.Visibility = page == "Visão geral" ? Visibility.Visible : Visibility.Collapsed;
        ControlsCard.Visibility = page == "Visão geral" ? Visibility.Collapsed : Visibility.Visible;
        AudioPanel.Visibility = page == "Áudio" ? Visibility.Visible : Visibility.Collapsed;
        MicPanel.Visibility = page == "Microfone" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "Configurações" ? Visibility.Visible : Visibility.Collapsed;
        MainScroll.ScrollToTop();
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_settingsPath)!);
            System.IO.File.WriteAllText(_settingsPath, System.Text.Json.JsonSerializer.Serialize(new string?[] { _output?.ID, _input?.ID }));
            Footer.Text = "Seleção salva neste computador.";
        } catch (Exception ex) { Footer.Text = ex.Message; }
    }
    private void RestoreSelection()
    {
        try {
            if (!System.IO.File.Exists(_settingsPath)) return;
            var ids = System.Text.Json.JsonSerializer.Deserialize<string?[]>(System.IO.File.ReadAllText(_settingsPath));
            if (ids is null || ids.Length != 2) return;
            var output = OutputDevices.Items.OfType<MMDevice>().FirstOrDefault(d => d.ID == ids[0]);
            var input = InputDevices.Items.OfType<MMDevice>().FirstOrDefault(d => d.ID == ids[1]);
            if (output is not null) OutputDevices.SelectedItem = output;
            if (input is not null) InputDevices.SelectedItem = input;
        } catch (Exception ex) { Footer.Text = ex.Message; }
    }
    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_input is null || _testCapture is not null) return;
        RecordButton.IsEnabled = PlayButton.IsEnabled = false;
        _testPlayer?.Dispose(); _testPlayer = null;
        _testReader?.Dispose(); _testReader = null;
        try {
            _testCapture = new NAudio.CoreAudioApi.WasapiCapture(_input);
            _testWriter = new NAudio.Wave.WaveFileWriter(_testPath, _testCapture.WaveFormat);
            var done = new System.Threading.Tasks.TaskCompletionSource<Exception?>();
            _testCapture.DataAvailable += (_, a) => _testWriter?.Write(a.Buffer, 0, a.BytesRecorded);
            _testCapture.RecordingStopped += (_, a) => done.TrySetResult(a.Exception);
            _testCapture.StartRecording();
            TestStatus.Text = "Gravando localmente por 5 segundos...";
            await System.Threading.Tasks.Task.WhenAny(done.Task, System.Threading.Tasks.Task.Delay(5000));
            _testCapture?.StopRecording();
            if (await System.Threading.Tasks.Task.WhenAny(done.Task, System.Threading.Tasks.Task.Delay(3000)) != done.Task) throw new TimeoutException("Tempo limite de gravação.");
            if (await done.Task is Exception error) throw error;
            _testCapture?.Dispose(); _testCapture = null;
            _testWriter?.Dispose(); _testWriter = null;
            PlayButton.IsEnabled = true;
            TestStatus.Text = "Teste gravado. Ouça com volume baixo. Reprodução pela saída padrão do Windows.";
        } catch (Exception ex) { StopTest(); TestStatus.Text = ex.Message; }
        finally { RecordButton.IsEnabled = true; }
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        try {
            _testPlayer?.Dispose(); _testReader?.Dispose();
            _testReader = new NAudio.Wave.AudioFileReader(_testPath);
            _testReader.Volume = 1f;
            _testPlayer = new NAudio.Wave.WaveOutEvent();
            _testPlayer.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(new TestGainProvider(_testReader, (float)Math.Pow(10, TestGain.Value / 20))));
            _testPlayer.Play();
        } catch (Exception ex) { TestStatus.Text = ex.Message; }
    }
    private void StopTest()
    {
        try { _testCapture?.StopRecording(); _testCapture?.Dispose(); } catch { }
        _testCapture = null;
        _testWriter?.Dispose(); _testWriter = null;
        _testPlayer?.Dispose(); _testPlayer = null;
        _testReader?.Dispose(); _testReader = null;
        try { System.IO.File.Delete(_testPath); } catch { }
    }
    private sealed class TestGainProvider : NAudio.Wave.ISampleProvider
    {
        private readonly NAudio.Wave.ISampleProvider source;
        private readonly float gain;
        public TestGainProvider(NAudio.Wave.ISampleProvider source, float gain) { this.source = source; this.gain = gain; }
        public NAudio.Wave.WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            for (int i = offset; i < offset + read; i++)
                buffer[i] = 0.35f * (float)Math.Tanh(buffer[i] * gain);
            return read;
        }
    }
}
