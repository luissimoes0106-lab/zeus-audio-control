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
        _timer.Tick += (_, _) => PollAudio();
        _timer.Start();
        SystemEvents.SessionEnding += OnSessionEnding;
        Closed += (_, _) =>
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _timer.Stop();
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

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();

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
        if (_updating) return;
        try
        {
            MicMeter.Value = _input is null ? 0 : _input.AudioMeterInformation.MasterPeakValue * 100;
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
}
