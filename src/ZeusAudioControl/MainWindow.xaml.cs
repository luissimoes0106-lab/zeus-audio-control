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
    private const string Model = "H510-PRO";

    public MainWindow()
    {
        InitializeComponent();
        RefreshDevices();
        SystemEvents.SessionEnding += OnSessionEnding;
        Closed += (_, _) =>
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
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
            OutputDevices.ItemsSource = null;
            InputDevices.ItemsSource = null;
            ReleaseDevices();
            var render = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            var capture = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
            _ownedDevices.AddRange(render);
            _ownedDevices.AddRange(capture);
            OutputDevices.ItemsSource = render;
            InputDevices.ItemsSource = capture;
            OutputDevices.SelectedItem = render.FirstOrDefault(IsHeadset);
            InputDevices.SelectedItem = capture.FirstOrDefault(IsHeadset);
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
            MicSlider.IsEnabled = _input is not null;
            if (_input is null) { MicLabel.Text = "Selecione um microfone"; return; }
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
}