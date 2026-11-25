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
        SetupEq();
        SetupTray();
        LoadAliases();
        RefreshDevices();
        RestoreSelection();
        RestoreStudio();
        RestoreStartup();
        _timer.Tick += (_, _) => PollAudio();
        _timer.Start();
        SystemEvents.SessionEnding += OnSessionEnding;
        Closed += (_, _) =>
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _timer.Stop();
            SaveLocal();
            _tray?.ContextMenuStrip?.Dispose();
            _tray?.Dispose();
            _trayIcon?.Dispose();
            StopTest();
            StopEq();
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
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && TrayToggle.IsChecked == true) Hide(); };
    }
    private void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
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
            OutputDevices.SelectedItem = render.FirstOrDefault(d => d.ID == previousOutput) ?? render.FirstOrDefault(IsHeadset) ?? render.FirstOrDefault();
            InputDevices.SelectedItem = capture.FirstOrDefault(d => d.ID == previousInput) ?? capture.FirstOrDefault(IsHeadset) ?? capture.FirstOrDefault();
            var found = render.Any(IsHeadset) || capture.Any(IsHeadset);
            DeviceStatus.Text = found
                ? "H510 Pro detectado nas interfaces de áudio do Windows."
                : "Modo universal: selecione qualquer dispositivo de áudio ativo.";
            Footer.Text = found
                ? "Dispositivo detectado. Os controles abaixo atuam nos endpoints selecionados, sem mudar o driver."
                : "Modo universal. Confira os dispositivos selecionados antes de ajustar.";
        }
        catch (Exception ex)
        {
            DeviceStatus.Text = "Não foi possível enumerar os dispositivos de áudio.";
            Footer.Text = ex.Message;
        }
        finally { _updating = false; }
        SyncOutput();
        SyncInput();
        ShowAliases();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) { if (_testCapture is null) RefreshDevices(); }

    private void OutputDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _output = OutputDevices.SelectedItem as MMDevice;
        SyncOutput();
        ShowAliases();
    }

    private void InputDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _input = InputDevices.SelectedItem as MMDevice;
        SyncInput();
        ShowAliases();
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
            var ch = audio.Channels;
            BalanceSlider.IsEnabled = ch.Count == 2;
            if (ch.Count == 2) {
                var l = ch[0].VolumeLevelScalar; var r = ch[1].VolumeLevelScalar;
                BalanceSlider.Value = Math.Max(l,r) > 0 ? (r-l)/Math.Max(l,r)*100 : 0;
                BalanceStatus.Text = Math.Abs(BalanceSlider.Value)<1 ? "Centro" : $"{BalanceSlider.Value:0}%";
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

    private void Balance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _output is null) return;
        try {
            var channels = _output.AudioEndpointVolume.Channels;
            if (channels.Count != 2) { BalanceStatus.Text = "Disponível apenas em saídas estéreo."; return; }
            var master = Math.Max(channels[0].VolumeLevelScalar, channels[1].VolumeLevelScalar);
            var b = (float)(BalanceSlider.Value / 100);
            channels[0].VolumeLevelScalar = master * (1 - Math.Max(0, b));
            channels[1].VolumeLevelScalar = master * (1 + Math.Min(0, b));
            BalanceStatus.Text = b == 0 ? "Centro" : $"{Math.Abs(b)*100:0}% para {(b < 0 ? "esquerda" : "direita")}";
        } catch (Exception ex) { BalanceStatus.Text = ex.Message; }
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
            using (var process = System.Diagnostics.Process.GetCurrentProcess()) Performance.Text = $"Memória: {process.WorkingSet64 / 1048576.0:0} MB";
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active).ToList();
            string signature;
            try { signature = string.Join("|", devices.Select(d => d.ID).OrderBy(id => id)); }
            finally { foreach (var d in devices) d.Dispose(); }
            if (signature != _signature) RefreshDevices();
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
    private sealed record StudioProfile(double Gain, bool Enabled, bool HighPass, bool Gate, double Threshold, bool Compressor, double Presence, double Noise = 0);
    private string StudioPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_settingsPath)!, "studio.json");
    private StudioProfile CurrentStudio() => new(TestGain.Value, StudioEnabled.IsChecked == true, HighPass.IsChecked == true, GateEnabled.IsChecked == true, GateThreshold.Value, CompressorEnabled.IsChecked == true, Presence.Value, NoiseReduction.Value);
    private void ApplyStudio(StudioProfile p)
    {
        TestGain.Value = Math.Clamp(p.Gain, 0, 24); StudioEnabled.IsChecked = p.Enabled;
        HighPass.IsChecked = p.HighPass; GateEnabled.IsChecked = p.Gate;
        GateThreshold.Value = Math.Clamp(p.Threshold, -65, -20); CompressorEnabled.IsChecked = p.Compressor;
        Presence.Value = Math.Clamp(p.Presence, -6, 6); NoiseReduction.Value = Math.Clamp(p.Noise, 0, 100);
    }
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag as string;
        ApplyStudio(tag switch {
            "discord" => new StudioProfile(6, true, true, true, -38, true, 3),
            "record" => new StudioProfile(3, true, true, false, -50, true, 1),
            _ => new StudioProfile(6, true, true, true, -45, true, 2)
        });
        StudioStatus.Text = "Perfil aplicado ao teste local. Ajuste conforme sua voz e salve se desejar.";
    }
    private void SaveStudio_Click(object sender, RoutedEventArgs e)
    {
        try {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StudioPath)!);
            System.IO.File.WriteAllText(StudioPath, System.Text.Json.JsonSerializer.Serialize(CurrentStudio()));
            StudioStatus.Text = "Perfil personalizado salvo.";
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
            if (RawPlayback.IsChecked == true) { _testReader.Volume = .35f; _testPlayer.Init(_testReader); }
            else _testPlayer.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(new TestGainProvider(_testReader, (float)Math.Pow(10, TestGain.Value / 20), StudioEnabled.IsChecked == true, HighPass.IsChecked == true, GateEnabled.IsChecked == true, GateThreshold.Value, CompressorEnabled.IsChecked == true, Presence.Value, NoiseReduction.Value)));
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


    private Dictionary<string,string> _aliases = new();
    private string AliasPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_settingsPath)!, "aliases.json");
    private void LoadAliases() { try { if(System.IO.File.Exists(AliasPath)) _aliases=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(System.IO.File.ReadAllText(AliasPath)) ?? new(); } catch { } }
    private void ShowAliases() { OutputAlias.Text=_output is null?"":_aliases.GetValueOrDefault(_output.ID,""); InputAlias.Text=_input is null?"":_aliases.GetValueOrDefault(_input.ID,""); }
    private void Alias_Click(object sender,RoutedEventArgs e) { try { bool output=(sender as Button)?.Tag as string=="out"; var device=output?_output:_input; if(device is null)return; var alias=(output?OutputAlias.Text:InputAlias.Text).Trim(); if(alias.Length>80) { Footer.Text="Use até 80 caracteres.";return; } if(alias.Length==0)_aliases.Remove(device.ID);else _aliases[device.ID]=alias; System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AliasPath)!); System.IO.File.WriteAllText(AliasPath,System.Text.Json.JsonSerializer.Serialize(_aliases)); Footer.Text=alias.Length==0?"Apelido removido.":$"Apelido salvo: {alias}"; }catch(Exception ex){Footer.Text=ex.Message;} }
    private void SaveLocal() { try { SaveEq(); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StudioPath)!); System.IO.File.WriteAllText(StudioPath,System.Text.Json.JsonSerializer.Serialize(CurrentStudio())); System.IO.File.WriteAllText(_settingsPath,System.Text.Json.JsonSerializer.Serialize(new string?[]{_output?.ID,_input?.ID})); } catch { } }
    private void RestoreStartup() { try { using var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); AutoStart.IsChecked=k?.GetValue("ZeusAudioControl") is string; } catch { } }
    private void AutoStart_Click(object sender,RoutedEventArgs e) { try { using var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); if(AutoStart.IsChecked==true) k.SetValue("ZeusAudioControl","\""+Environment.ProcessPath+"\""); else k.DeleteValue("ZeusAudioControl",false); } catch(Exception ex){Footer.Text=ex.Message;RestoreStartup();} }
    private void Export_Click(object sender,RoutedEventArgs e) { try { var d=new Microsoft.Win32.SaveFileDialog{Filter="Perfil ZEUS (*.json)|*.json",FileName="zeus-voice.json"}; if(d.ShowDialog()==true) System.IO.File.WriteAllText(d.FileName,System.Text.Json.JsonSerializer.Serialize(CurrentStudio())); } catch(Exception ex){Footer.Text=ex.Message;} }
    private void Import_Click(object sender,RoutedEventArgs e) { try { var d=new Microsoft.Win32.OpenFileDialog{Filter="Perfil ZEUS (*.json)|*.json"}; if(d.ShowDialog()==true && new System.IO.FileInfo(d.FileName).Length<65536 && System.Text.Json.JsonSerializer.Deserialize<StudioProfile>(System.IO.File.ReadAllText(d.FileName)) is StudioProfile p){ApplyStudio(p);SaveLocal();} } catch(Exception ex){Footer.Text=ex.Message;} }
    private void Reset_Click(object sender,RoutedEventArgs e) { ApplyStudio(new StudioProfile(6,true,true,true,-45,true,2));SaveLocal(); }
    private async void Stereo_Click(object sender,RoutedEventArgs e) { if(_output is null)return; try { int side=(sender as Button)?.Tag as string=="1"?1:0; var samples=new byte[48000*4/2]; for(int i=0;i<24000;i++){short v=(short)(Math.Sin(i*2*Math.PI*440/48000)*1800*Math.Min(1,Math.Min(i,23999-i)/480.0)); int j=i*4+side*2;samples[j]=(byte)v;samples[j+1]=(byte)(v>>8);} using var stream=new System.IO.MemoryStream(samples); using var wave=new NAudio.Wave.RawSourceWaveStream(stream,new NAudio.Wave.WaveFormat(48000,16,2)); using var player=new NAudio.Wave.WasapiOut(_output,AudioClientShareMode.Shared,false,100); player.Init(wave);player.Play();await System.Threading.Tasks.Task.Delay(800);player.Stop(); }catch(Exception ex){Footer.Text=ex.Message;} }

    private readonly double[] _freq={31,62,125,250,500,1000,2000,4000,8000,16000};
    private readonly List<Slider> _bands=new();
    private NAudio.Wave.AudioFileReader? _eqReader;
    private NAudio.Wave.WaveOutEvent? _eqPlayer;
    private string EqPath=>System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_settingsPath)!,"eq.json");
    private void SetupEq(){
        foreach(var f in _freq){var panel=new StackPanel();var slider=new Slider{Orientation=System.Windows.Controls.Orientation.Vertical,Minimum=-12,Maximum=12,Height=120,Width=24,Value=0};_bands.Add(slider);panel.Children.Add(slider);panel.Children.Add(new TextBlock{Text=f>=1000?$"{f/1000:0}k":$"{f:0}",FontSize=10,HorizontalAlignment=HorizontalAlignment.Center});EqGrid.Children.Add(panel);}
        try{if(System.IO.File.Exists(EqPath)){var v=System.Text.Json.JsonSerializer.Deserialize<double[]>(System.IO.File.ReadAllText(EqPath));if(v?.Length==11){for(int i=0;i<10;i++)_bands[i].Value=Math.Clamp(v[i],-12,12);BassBoost.Value=Math.Clamp(v[10],0,9);}}}catch{}
    }
    private void SaveEq(){System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(EqPath)!);System.IO.File.WriteAllText(EqPath,System.Text.Json.JsonSerializer.Serialize(_bands.Select(b=>b.Value).Append(BassBoost.Value).ToArray()));}
    private void EqPreset_Click(object sender,RoutedEventArgs e){double[] v=((sender as Button)?.Tag as string) switch{"game"=>new double[]{-3,-2,-1,0,1,2,4,4,2,0},"music"=>new double[]{3,3,2,0,-1,0,1,2,3,2},"movie"=>new double[]{4,3,2,0,0,2,2,1,0,0},_=>new double[10]};for(int i=0;i<10;i++)_bands[i].Value=v[i];BassBoost.Value=0;}
    private void StopEq(){_eqPlayer?.Dispose();_eqPlayer=null;_eqReader?.Dispose();_eqReader=null;}
    private void EqStop_Click(object sender,RoutedEventArgs e){StopEq();EqStatus.Text="Reprodução parada.";}
    private void EqPlay_Click(object sender,RoutedEventArgs e){try{var d=new Microsoft.Win32.OpenFileDialog{Filter="Áudio|*.wav;*.mp3;*.aiff;*.wma"};if(d.ShowDialog()!=true)return;StopEq();_eqReader=new NAudio.Wave.AudioFileReader(d.FileName);_eqPlayer=new NAudio.Wave.WaveOutEvent();_eqPlayer.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(new LocalEq(_eqReader,_freq,_bands.Select(b=>b.Value).ToArray(),BassBoost.Value)));_eqPlayer.Play();EqStatus.Text="Reproduzindo na saída padrão. Para aplicar novos ajustes, reabra o arquivo.";}catch(Exception ex){StopEq();EqStatus.Text=ex.Message;}}
    private sealed class LocalEq:NAudio.Wave.ISampleProvider{
        private readonly NAudio.Wave.ISampleProvider source;private readonly NAudio.Dsp.BiQuadFilter[][] filters;private int channel;private readonly float headroom;
        public LocalEq(NAudio.Wave.ISampleProvider source,double[] freq,double[] gains,double bass){this.source=source;filters=new NAudio.Dsp.BiQuadFilter[source.WaveFormat.Channels][];int rate=source.WaveFormat.SampleRate;headroom=(float)Math.Pow(10,-(Math.Max(0,gains.Max())+bass+3)/20);for(int c=0;c<filters.Length;c++){var list=new List<NAudio.Dsp.BiQuadFilter>();for(int i=0;i<freq.Length;i++)if(freq[i]<rate*.45 && Math.Abs(gains[i])>.01)list.Add(NAudio.Dsp.BiQuadFilter.PeakingEQ(rate,(float)freq[i],1.4f,(float)gains[i]));if(bass>0)list.Add(NAudio.Dsp.BiQuadFilter.PeakingEQ(rate,80,.7f,(float)bass));filters[c]=list.ToArray();}}
        public NAudio.Wave.WaveFormat WaveFormat=>source.WaveFormat;
        public int Read(float[] buffer,int offset,int count){int n=source.Read(buffer,offset,count);for(int i=offset;i<offset+n;i++){float v=buffer[i]*headroom;foreach(var f in filters[channel])v=f.Transform(v);buffer[i]=Math.Clamp(v,-.85f,.85f);channel=(channel+1)%filters.Length;}return n;}
    }
    private sealed class TestGainProvider : NAudio.Wave.ISampleProvider
    {
        private readonly NAudio.Wave.ISampleProvider source;
        private readonly float gain;
        private readonly bool studio, gate, compressor;
        private readonly float threshold, attack, release;
        private readonly NAudio.Dsp.BiQuadFilter[] hp, eq;
        private readonly bool highPass;
        private readonly float[] envelope, gateGain;
        private int channel;
        private readonly float noiseStrength;
        private readonly float[] noiseFloor, noiseGain;
        public TestGainProvider(NAudio.Wave.ISampleProvider source, float gain, bool studio, bool highPass, bool gate, double thresholdDb, bool compressor, double presence, double noise)
        {
            this.source = source; this.gain = gain; this.studio = studio; this.highPass = highPass; this.gate = gate; this.compressor = compressor;
            threshold = (float)Math.Pow(10, thresholdDb / 20);
            int rate = source.WaveFormat.SampleRate, channels = source.WaveFormat.Channels;
            attack = (float)Math.Exp(-1.0 / (rate * .005)); release = (float)Math.Exp(-1.0 / (rate * .15));
            hp = new NAudio.Dsp.BiQuadFilter[channels]; eq = new NAudio.Dsp.BiQuadFilter[channels];
            envelope = new float[channels]; gateGain = new float[channels];
            noiseStrength = (float)Math.Clamp(noise / 100, 0, 1);
            noiseFloor = new float[channels]; noiseGain = new float[channels];
            Array.Fill(noiseFloor, .001f); Array.Fill(noiseGain, 1f); Array.Fill(gateGain, 1f);
            for (int i = 0; i < channels; i++) {
                hp[i] = NAudio.Dsp.BiQuadFilter.HighPassFilter(rate, 80, .707f);
                eq[i] = NAudio.Dsp.BiQuadFilter.PeakingEQ(rate, Math.Min(2500, rate / 4f), 1f, (float)presence);
            }
        }
        public NAudio.Wave.WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            for (int i = offset; i < offset + read; i++) {
                float v = buffer[i];
                if (studio) {
                    if (highPass) v = hp[channel].Transform(v);
                    v = eq[channel].Transform(v);
                    float level = Math.Abs(v);
                    float coeff = level > envelope[channel] ? attack : release;
                    envelope[channel] = coeff * envelope[channel] + (1-coeff)*level;
                    if (noiseStrength > 0) {
                        float env = envelope[channel];
                        float floor = noiseFloor[channel];
                        if (env < floor * 2 || env < .008f) floor += .0002f * (env - floor);
                        noiseFloor[channel] = Math.Clamp(floor, .0001f, .02f);
                        float ratio = Math.Clamp((env - noiseFloor[channel]) / Math.Max(env, .0001f), 0, 1);
                        float targetNoise = 1 - noiseStrength * (1 - ratio * ratio);
                        noiseGain[channel] = .995f * noiseGain[channel] + .005f * targetNoise;
                        v *= noiseGain[channel];
                    }
                    if (gate) {
                        float target = envelope[channel] >= threshold ? 1f : .03f;
                        float smooth = target > gateGain[channel] ? attack : release;
                        gateGain[channel] = smooth * gateGain[channel] + (1-smooth)*target;
                        v *= gateGain[channel];
                    }
                    if (compressor && envelope[channel] > .125f)
                        v *= (float)Math.Pow(.125f / envelope[channel], .6667);
                }
                buffer[i] = .35f * (float)Math.Tanh(v * gain);
                channel = (channel + 1) % WaveFormat.Channels;
            }
            return read;
        }
    }
}
