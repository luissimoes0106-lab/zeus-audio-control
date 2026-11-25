using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NAudio.CoreAudioApi;
using NAudio.Dsp;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private sealed record ActiveRoute(AudioRoute Audio, RouteEndpoints Endpoints, StudioProfile Studio, bool Compressor, bool SmartVolume)
    {
        public bool Running { get; set; }
    }

    private readonly Dictionary<RouteKind, ActiveRoute> _routes = new();
    private RouteKind SelectedRouteKind => (RouteKind)Math.Max(0, RouteMode.SelectedIndex);
    private ActiveRoute? SelectedRoute => _routes.GetValueOrDefault(SelectedRouteKind);
    private static readonly PropertyKey EndpointContainer = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    private static readonly float[] SpectrumWindow = Enumerable.Range(0, 2048)
        .Select(i => (float)FastFourierTransform.HannWindow(i, 2048)).ToArray();
    private readonly float[] _drySamples = new float[2048];
    private readonly float[] _wetSamples = new float[2048];
    private readonly Complex[] _spectrum = new Complex[2048];
    private int _curveBand = -1, _channelCount;
    private string _eqProfile = "custom", _voiceProfile = "custom";

    private static readonly (string Key, string Pt, string En)[] RouteLabels =
    [
        ("RoutesTitle", "⇄  Rotas", "⇄  Routes"),
        ("RouteAbout", "Encaminhe áudio entre endpoints do Windows. Rotas entre aplicativos exigem um cabo virtual confiável já instalado.", "Route audio between Windows endpoints. Inter-app routes require a trusted virtual cable that is already installed."),
        ("Mode", "Tipo de rota", "Route type"),
        ("PlaybackRoute", "Áudio de aplicativos", "App audio"),
        ("VoiceRoute", "Microfone para aplicativos", "Microphone to apps"),
        ("HeadphoneRoute", "Escuta local em fones", "Local headphone monitoring"),
        ("RouteSource", "Entrada da rota", "Route input"),
        ("RouteDestination", "Saída da rota", "Route output"),
        ("OutputCompressor", "Compressor de saída", "Output compressor"),
        ("Normalize", "Smart Volume", "Smart Volume"),
        ("GamingImmersiveLabel", "Gaming Imersivo", "Gaming Immersive"),
        ("GamingVoiceAgcLabel", "Ganho automático de voz", "Automatic voice gain"),
        ("LowLatency", "Baixa latência (experimental)", "Low latency (experimental)"),
        ("VoiceShield", "Voice Shield · reforço adaptativo de voz", "Voice Shield · adaptive voice protection"),
        ("StartRoute", "Iniciar rota", "Start route"),
        ("MeterTitle", "Forma de onda · original em cinza, saída em vermelho", "Waveform · original in gray, output in red"),
        ("SpectrumTitle", "Espectro da saída · primeiro canal", "Output spectrum · first channel"),
        ("SurroundLocal", "Espacialização estéreo para WAV 5.1/7.1 compatível", "Stereo spatialization for compatible 5.1/7.1 WAV files"),
        ("Attack", "Ataque do gate", "Gate attack"),
        ("Hold", "Sustentação do gate", "Gate hold"),
        ("Release", "Liberação do gate", "Gate release"),
        ("CompThreshold", "Limiar do compressor", "Compressor threshold"),
        ("Ratio", "Razão do compressor", "Compressor ratio")
    ];

    private string L(string pt, string en) => _english ? en : pt;
    private void Routes_Click(object sender, RoutedEventArgs e) => Navigate("Rotas");
    private void RouteMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RouteConfirm is null) return;
        if (SelectedRoute is { } active)
        {
            RouteInput.SelectedItem = FindChoice(RouteInput, active.Endpoints.Input, false);
            RouteOutput.SelectedItem = FindChoice(RouteOutput, active.Endpoints.Output, false);
            RouteLowLatency.IsChecked = active.Audio.OutputBufferMilliseconds == 25;
            RouteNormalizer.IsChecked = active.SmartVolume;
            RouteCompressor.IsChecked = active.Compressor;
        }
        else
        {
            RouteInput.SelectedItem = null;
            RouteOutput.SelectedItem = null;
            RouteLowLatency.IsChecked = SelectedRouteKind == RouteKind.Playback && _intelligence.GamingLowLatency;
        }
        RouteConfirm.IsChecked = false;
        UpdateRouteLabels();
    }

    private void UpdateRouteLabels()
    {
        foreach (var (key, pt, en) in RouteLabels) Resources[key] = L(pt, en);
        var kind = (RouteKind)Math.Max(0, RouteMode.SelectedIndex);
        PlaybackOptions.Visibility = kind == RouteKind.Playback ? Visibility.Visible : Visibility.Collapsed;
        MonitorOriginal.Visibility = kind == RouteKind.Headphones ? Visibility.Visible : Visibility.Collapsed;
        RouteHelp.Text = kind switch
        {
            RouteKind.Playback => L("Nos aplicativos, escolha a saída do cabo. Aqui, selecione a entrada desse cabo e seus fones ou alto-falantes.",
                "In the apps, select the cable's playback endpoint. Here, select the cable's recording endpoint and your headphones or speakers."),
            RouteKind.Microphone => L("Selecione o microfone físico e a saída do cabo. No aplicativo receptor, escolha a entrada desse cabo. Reinicie a rota para aplicar novos ajustes de voz.",
                "Select the physical mic and the cable's playback endpoint. In the receiving app, select the cable's recording endpoint. Restart the route to apply new voice settings."),
            _ => L("Use fones de ouvido. Comece com volume baixo. Reinicie a rota para aplicar novos ajustes de voz.",
                "Use headphones and start at low volume. Restart the route to apply new voice settings.")
        };
        RouteConfirm.Content = kind == RouteKind.Headphones
            ? L("Confirmei que a saída selecionada é um fone de ouvido.", "I verified that the selected output is headphones.")
            : L("Confirmei os endpoints e que a rota não fecha um ciclo de áudio.", "I verified the endpoints and that the route does not form an audio loop.");
        var selected = SelectedRoute;
        SetRouteControls(selected is not null);
        RouteStatus.Text = selected is null ? L("Rota inativa", "Route inactive")
            : selected.Running ? L("Rota ativa", "Route active") : L("Abrindo endpoints…", "Opening endpoints…");
        RouteMetrics.Text = SaturationStatus.Text = "";
        DryWave.Points = new PointCollection();
        WetWave.Points = new PointCollection();
        SpectrumLine.Points = new PointCollection();
        UpdateDashboard();
    }

    private void UpdateRouteChoices(IEnumerable<MMDevice> inputs, IEnumerable<MMDevice> outputs, string? inputId, string? outputId)
    {
        RouteInput.ItemsSource = inputs.Select(Choice).ToArray();
        RouteOutput.ItemsSource = outputs.Select(Choice).ToArray();
        RouteInput.SelectedItem = FindChoice(RouteInput, inputId, fallback: false);
        RouteOutput.SelectedItem = FindChoice(RouteOutput, outputId, fallback: false);
        foreach (var active in _routes.Values)
            if (!inputs.Any(device => device.ID == active.Endpoints.Input) ||
                !outputs.Any(device => device.ID == active.Endpoints.Output))
                active.Audio.Stop();
    }

    private async void StartRoute_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRoute is not null) return;
        var input = Selected(RouteInput);
        var output = Selected(RouteOutput);
        if (input is null || output is null || RouteConfirm.IsChecked != true)
        {
            RouteStatus.Text = L("Selecione e confirme os dois endpoints.", "Select and confirm both endpoints.");
            return;
        }

        var kind = (RouteKind)RouteMode.SelectedIndex;
        try
        {
            if (kind == RouteKind.Headphones)
            {
                var formFactor = Convert.ToInt32(ReadEndpointProperty(output, PropertyKeys.PKEY_AudioEndpoint_FormFactor));
                if (formFactor is not (3 or 5))
                {
                    RouteStatus.Text = L("A escuta exige uma saída que o Windows identifique como fones ou headset.",
                        "Monitoring requires an output that Windows identifies as headphones or a headset.");
                    return;
                }
            }
        }
        catch (Exception error)
        {
            RouteStatus.Text = error.Message;
            return;
        }

        var endpoints = new RouteEndpoints(kind, input.ID, output.ID, ContainerId(input), ContainerId(output));
        if (!RouteSafety.CanStart(endpoints, _routes.Values.Select(active => active.Endpoints)))
        {
            RouteStatus.Text = L("Rota bloqueada: endpoints duplicados, cabo compartilhado ou topologia desconhecida.",
                "Route blocked: duplicate endpoints, shared cable, or unknown topology.");
            return;
        }

        var route = new AudioRoute(RouteLowLatency.IsChecked == true ? 25 : 40);
        route.SetProtection(_intelligence.HearingLimit);
        var active = new ActiveRoute(route, endpoints, CurrentStudio(), RouteCompressor.IsChecked == true,
            RouteNormalizer.IsChecked == true);
        _routes.Add(kind, active);
        SetRouteControls(true);
        RouteStatus.Text = L("Abrindo endpoints…", "Opening endpoints…");
        route.SetBypass(MonitorOriginal.IsChecked == true);
        try
        {
            await route.Run(endpoints.Input, endpoints.Output, kind, EqFrequencies, PlaybackGains(),
                BassBoost.Value, active.Studio, active.Compressor, active.SmartVolume,
                () => Dispatcher.BeginInvoke(() =>
                {
                    if (_closing || !_routes.TryGetValue(kind, out var current) || current != active) return;
                    active.Running = true;
                    if (SelectedRouteKind == kind) RouteStatus.Text = L("Rota ativa", "Route active");
                    UpdatePlaybackEq();
                    PollForeground();
                    UpdateDashboard();
                }));
            if (!_closing && SelectedRouteKind == kind)
                RouteStatus.Text = L("Rota encerrada.", "Route stopped.");
        }
        catch (Exception error)
        {
            if (!_closing)
            {
                var message = L("A rota parou: ", "The route stopped: ") + error.Message;
                Footer.Text = message;
                if (SelectedRouteKind == kind) RouteStatus.Text = message;
            }
        }
        finally
        {
            _routes.Remove(kind);
            if (!_closing) PollForeground();
            if (!_closing)
            {
                if (SelectedRouteKind == kind)
                {
                    SetRouteControls(false);
                    RouteConfirm.IsChecked = false;
                    RouteMetrics.Text = SaturationStatus.Text = "";
                    DryWave.Points = new PointCollection();
                    WetWave.Points = new PointCollection();
                    SpectrumLine.Points = new PointCollection();
                }
                UpdateDashboard();
            }
        }
    }

    private static Guid? ContainerId(MMDevice endpoint) =>
        Guid.TryParse(ReadEndpointProperty(endpoint, EndpointContainer)?.ToString(), out var id) && id != Guid.Empty ? id : null;

    private static object? ReadEndpointProperty(MMDevice device, PropertyKey key)
    {
        try { return device.Properties.Contains(key) ? device.Properties[key].Value : null; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or NotSupportedException or NotImplementedException)
        {
            return null;
        }
    }

    private void SetRouteControls(bool active)
    {
        RouteInput.IsEnabled = RouteOutput.IsEnabled = RouteConfirm.IsEnabled = RouteLowLatency.IsEnabled =
            RouteCompressor.IsEnabled = RouteNormalizer.IsEnabled = RouteStart.IsEnabled = !active;
        RouteStop.IsEnabled = active;
    }

    private void StopRoute_Click(object sender, RoutedEventArgs e) => SelectedRoute?.Audio.Stop();
    private void MonitorOriginal_Click(object sender, RoutedEventArgs e)
    {
        _routes.GetValueOrDefault(RouteKind.Headphones)?.Audio.SetBypass(MonitorOriginal.IsChecked == true);
        UpdateDashboard();
    }

    private void UpdateDashboard()
    {
        OverviewOutput.Text = _output is null ? L("Saída desconectada", "Output disconnected") : Choice(_output).Label;
        OverviewInput.Text = _input is null ? L("Entrada desconectada", "Input disconnected") : Choice(_input).Label;
        var eq = _eqProfile switch
        {
            "fps" => "Gaming FPS", "immersive" => L("Gaming Imersivo", "Gaming Immersive"),
            "game" => "Gaming", "music" => L("Música", "Music"), "movie" => L("Filmes", "Movies"),
            "flat" => "Flat", _ => L("Personalizado", "Custom")
        };
        var voiceLabel = _voiceProfile switch
        {
            "gaming" => "Gaming", "communication" => L("Comunicação", "Communication"),
            "studio" => "Studio", "record" => L("Gravação", "Recording"), _ => L("Personalizado", "Custom")
        };
        OverviewProfile.Text = $"EQ · {eq}     /     {L("Voz", "Voice")} · {voiceLabel}";
        string effects;
        var playback = _routes.GetValueOrDefault(RouteKind.Playback);
        var voice = _routes.GetValueOrDefault(RouteKind.Microphone) ?? _routes.GetValueOrDefault(RouteKind.Headphones);
        if (playback is { Running: true })
        {
            effects = string.Join(" · ", new[]
            {
                "EQ", playback.Compressor ? "Compressor" : "",
                playback.SmartVolume ? "Smart Volume" : "",
                L("Limitador", "Limiter")
            }.Where(label => label.Length > 0));
        }
        else
        {
            var studio = voice is { Running: true } ? voice.Studio : CurrentStudio();
            effects = studio.Enabled
                ? string.Join(" · ", new[]
                {
                    studio.HighPass ? L("Passa-altas", "High-pass") : "",
                    studio.Gate ? "Gate" : "", studio.Compressor ? "Compressor" : "",
                    studio.Noise > 0 ? L("Redução de ruído", "Noise reduction") : "",
                    L("Limitador", "Limiter")
                }.Where(label => label.Length > 0))
                : L("Ganho e limitador · Studio desligado", "Gain and limiter · Studio off");
        }
        var activeCount = _routes.Values.Count(route => route.Running);
        var scope = activeCount == 0 ? L("Processamento local", "Local processing")
            : L($"Rotas ativas: {activeCount}", $"Active routes: {activeCount}");
        if (voice is { Running: true })
        {
            if (voice.Endpoints.Kind == RouteKind.Headphones && MonitorOriginal.IsChecked == true)
                effects = L("Original com limite de monitoramento", "Original with monitor level cap");
            else if (voice.Studio.Shield && voice.Studio.Enabled)
                effects += " · Voice Shield";
        }
        var gaming = _gamingManualEq is not null ? " · Gaming (manual)" : _gamingDetectedName is { } game ? $" · Gaming ({game})" : "";
        OverviewFilters.Text = $"{scope} · {effects}{gaming}";
    }

    private void PollRoute()
    {
        var selected = SelectedRoute;
        var visible = _page == "Rotas" && IsVisible;
        foreach (var active in _routes.Values) active.Audio.SetTelemetry(visible && active == selected);
        if (!visible || selected is not { Running: true }) return;
        var route = selected.Audio;
        var before = Volatile.Read(ref route.Before);
        var after = Volatile.Read(ref route.After);
        var buffer = Volatile.Read(ref route.Buffer);
        if (before is null || after is null || buffer is null) return;
        var inputPeak = before.ReadSnapshot(_drySamples);
        after.ReadSnapshot(_wetSamples);
        SaturationStatus.Text = inputPeak >= .98f
            ? L("Entrada próxima da saturação", "Input near clipping")
            : $"{L("Pico de entrada", "Input peak")} · {20 * Math.Log10(Math.Max(inputPeak, .000001f)):0.0} dBFS";
        RouteMetrics.Text = L(
            $"Fila {buffer.BufferedMilliseconds:0} ms · captura WASAPI {route.CaptureLatencyMilliseconds:0} ms · saída solicitada {route.OutputBufferMilliseconds} ms · faltas {buffer.Underflows} · descartes {buffer.Overflows} · descontinuidades {Interlocked.Read(ref route.Discontinuities)}",
            $"Queue {buffer.BufferedMilliseconds:0} ms · WASAPI capture {route.CaptureLatencyMilliseconds:0} ms · requested output {route.OutputBufferMilliseconds} ms · underruns {buffer.Underflows} · drops {buffer.Overflows} · discontinuities {Interlocked.Read(ref route.Discontinuities)}");
        if (route.OutputBufferMilliseconds == 25 && (buffer.Underflows > 3 || buffer.Overflows > 3))
            RouteMetrics.Text += L(" · Se as interrupções persistirem, use o modo padrão.", " · Use standard mode if interruptions persist.");
        DryWave.Points = WavePoints(_drySamples, RouteWave.ActualWidth, RouteWave.ActualHeight);
        WetWave.Points = WavePoints(_wetSamples, RouteWave.ActualWidth, RouteWave.ActualHeight);
        for (var i = 0; i < _spectrum.Length; i++)
        {
            _spectrum[i].X = _wetSamples[i] * SpectrumWindow[i];
            _spectrum[i].Y = 0;
        }
        FastFourierTransform.FFT(true, 11, _spectrum);
        var points = new PointCollection(65);
        for (var i = 0; i <= 64; i++)
        {
            var index = (int)Math.Pow(1023, i / 64.0);
            var bin = _spectrum[index];
            var db = 20 * Math.Log10(Math.Max(1e-8, Math.Sqrt(bin.X * bin.X + bin.Y * bin.Y)));
            points.Add(new Point(i * SpectrumCanvas.ActualWidth / 64, SpectrumCanvas.ActualHeight * (1 - Math.Clamp((db + 80) / 80, 0, 1))));
        }
        SpectrumLine.Points = points;
    }

    private static PointCollection WavePoints(float[] source, double width, double height)
    {
        var points = new PointCollection(96);
        for (var i = 0; i < 96; i++)
            points.Add(new Point(i * width / 95, height * (.5 - .48 * Math.Clamp(source[i * source.Length / 96], -1, 1))));
        return points;
    }

    private void EqCurve_SizeChanged(object sender, SizeChangedEventArgs e) => PaintEqCurve();

    private void PaintEqCurve()
    {
        if (_bands.Count != 10 || EqCurve.ActualWidth <= 0) return;
        EqCenter.X2 = EqCurve.ActualWidth;
        EqCenter.Y1 = EqCenter.Y2 = EqCurve.ActualHeight / 2;
        var points = new PointCollection(_bands.Count);
        for (var i = 0; i < _bands.Count; i++)
            points.Add(new Point((i + .5) * EqCurve.ActualWidth / _bands.Count, EqCurve.ActualHeight * (12 - _bands[i].Value) / 24));
        EqLine.Points = points;
    }

    private void EqCurve_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (EqCurve.ActualWidth <= 0 || _bands.Count != 10) return;
        _curveBand = Math.Clamp((int)(e.GetPosition(EqCurve).X * _bands.Count / EqCurve.ActualWidth), 0, _bands.Count - 1);
        EqCurve.CaptureMouse();
        SetCurveGain(e.GetPosition(EqCurve).Y);
    }

    private void EqCurve_MouseMove(object sender, MouseEventArgs e)
    {
        if (_curveBand >= 0 && e.LeftButton == MouseButtonState.Pressed) SetCurveGain(e.GetPosition(EqCurve).Y);
        else if (_curveBand >= 0) EqCurve_MouseUp(sender, null!);
    }

    private void EqCurve_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _curveBand = -1;
        EqCurve.ReleaseMouseCapture();
    }

    private void SetCurveGain(double y) => _bands[_curveBand].Value = Math.Round(Math.Clamp(12 - 24 * y / EqCurve.ActualHeight, -12, 12));

    private void UpdateChannelTests(int channels)
    {
        if (_channelCount == channels) return;
        _channelCount = channels;
        while (ChannelTests.Children.Count > 2) ChannelTests.Children.RemoveAt(2);
        StereoLeft.Visibility = StereoRight.Visibility = channels == 2 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < Math.Min(channels, 32) && channels != 2; i++)
        {
            var button = new Button { Content = $"{L("Canal", "Channel")} {i + 1}", Tag = i.ToString() };
            button.Click += Stereo_Click;
            ChannelTests.Children.Add(button);
        }
    }
}
