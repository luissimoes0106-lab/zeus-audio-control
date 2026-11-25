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
    private AudioRoute? _route;
    private RouteKind _routeKind;
    private bool _routeRunning;
    private StudioProfile? _runningVoice;
    private bool _runningCompressor, _runningNormalizer;
    private static readonly PropertyKey EndpointContainer = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    private string? _routeInputId, _routeOutputId;
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
        ("Normalize", "Normalizar nível da saída", "Normalize output level"),
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
        if (_route is null) RouteStatus.Text = L("Rota inativa", "Route inactive");
        UpdateDashboard();
    }

    private void UpdateRouteChoices(IEnumerable<MMDevice> inputs, IEnumerable<MMDevice> outputs, string? inputId, string? outputId)
    {
        RouteInput.ItemsSource = inputs.Select(Choice).ToArray();
        RouteOutput.ItemsSource = outputs.Select(Choice).ToArray();
        RouteInput.SelectedItem = FindChoice(RouteInput, inputId, fallback: false);
        RouteOutput.SelectedItem = FindChoice(RouteOutput, outputId, fallback: false);
        if (_route is not null && (Selected(RouteInput)?.ID != _routeInputId || Selected(RouteOutput)?.ID != _routeOutputId))
            _route.Stop();
    }

    private async void StartRoute_Click(object sender, RoutedEventArgs e)
    {
        if (_route is not null) return;
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
            else
            {
                var inputContainer = ReadEndpointProperty(input, EndpointContainer);
                var outputContainer = ReadEndpointProperty(output, EndpointContainer);
                if (Guid.TryParse(inputContainer?.ToString(), out var id) && id != Guid.Empty &&
                    Guid.TryParse(outputContainer?.ToString(), out var other) && id == other)
                {
                    RouteStatus.Text = L("Os endpoints pertencem ao mesmo dispositivo. Use um dispositivo físico e um cabo virtual distintos.",
                        "Both endpoints belong to the same device. Use separate physical and virtual devices.");
                    return;
                }
            }
        }
        catch (Exception error)
        {
            RouteStatus.Text = error.Message;
            return;
        }

        var route = _route = new AudioRoute();
        _routeKind = kind;
        _runningCompressor = RouteCompressor.IsChecked == true;
        _runningNormalizer = RouteNormalizer.IsChecked == true;
        var studio = CurrentStudio();
        _runningVoice = kind == RouteKind.Playback ? null : studio;
        _routeInputId = input.ID;
        _routeOutputId = output.ID;
        SetRouteControls(true);
        RouteStatus.Text = L("Abrindo endpoints…", "Opening endpoints…");
        route.SetBypass(MonitorOriginal.IsChecked == true);
        try
        {
            await route.Run(_routeInputId, _routeOutputId, _routeKind, EqFrequencies, PlaybackGains(),
                BassBoost.Value, studio, _runningCompressor, _runningNormalizer,
                () => Dispatcher.BeginInvoke(() =>
                {
                    if (_closing || _route != route) return;
                    _routeRunning = true;
                    RouteStatus.Text = L("Rota ativa", "Route active");
                    UpdatePlaybackEq();
                    UpdateDashboard();
                }));
            if (!_closing) RouteStatus.Text = L("Rota encerrada. Confira os endpoints antes de reiniciar.", "Route stopped. Check the endpoints before restarting.");
        }
        catch (Exception error)
        {
            if (!_closing) RouteStatus.Text = L("A rota parou: ", "The route stopped: ") + error.Message;
        }
        finally
        {
            _route = null;
            _routeRunning = false;
            if (!_closing)
            {
                SetRouteControls(false);
                RouteConfirm.IsChecked = false;
                UpdateDashboard();
                RouteMetrics.Text = SaturationStatus.Text = "";
                DryWave.Points.Clear();
                WetWave.Points.Clear();
                SpectrumLine.Points.Clear();
            }
        }
    }

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
        RouteMode.IsEnabled = RouteInput.IsEnabled = RouteOutput.IsEnabled = RouteConfirm.IsEnabled =
            RouteCompressor.IsEnabled = RouteNormalizer.IsEnabled = RouteStart.IsEnabled = !active;
        RouteStop.IsEnabled = active;
    }

    private void StopRoute_Click(object sender, RoutedEventArgs e) => _route?.Stop();
    private void MonitorOriginal_Click(object sender, RoutedEventArgs e)
    {
        _route?.SetBypass(MonitorOriginal.IsChecked == true);
        UpdateDashboard();
    }

    private void UpdateDashboard()
    {
        OverviewOutput.Text = _output is null ? L("Saída desconectada", "Output disconnected") : Choice(_output).Label;
        OverviewInput.Text = _input is null ? L("Entrada desconectada", "Input disconnected") : Choice(_input).Label;
        var eq = _eqProfile switch
        {
            "game" => "Gaming", "music" => L("Música", "Music"), "movie" => L("Filmes", "Movies"),
            "flat" => "Flat", _ => L("Personalizado", "Custom")
        };
        var voice = _voiceProfile switch
        {
            "gaming" => "Gaming", "communication" => L("Comunicação", "Communication"),
            "studio" => "Studio", "record" => L("Gravação", "Recording"), _ => L("Personalizado", "Custom")
        };
        OverviewProfile.Text = $"EQ · {eq}     /     {L("Voz", "Voice")} · {voice}";
        string effects;
        if (_routeRunning && _routeKind == RouteKind.Playback)
        {
            effects = string.Join(" · ", new[]
            {
                "EQ", _runningCompressor ? "Compressor" : "",
                _runningNormalizer ? L("Normalizador", "Normalizer") : "",
                L("Limitador", "Limiter")
            }.Where(label => label.Length > 0));
        }
        else
        {
            var studio = _routeRunning && _runningVoice is not null ? _runningVoice : CurrentStudio();
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
        var scope = !_routeRunning || _routeKind == RouteKind.Headphones
            ? L("Processamento local", "Local processing")
            : L("Processamento entre aplicativos por rota", "Inter-app processing via route");
        if (_routeRunning && _routeKind == RouteKind.Headphones && MonitorOriginal.IsChecked == true)
            effects = L("Original com limite de monitoramento", "Original with monitor level cap");
        OverviewFilters.Text = $"{scope} · {effects}";
    }

    private void PollRoute()
    {
        if (!_routeRunning || _page != "Rotas" || !IsVisible || _route is not { } route) return;
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
            $"Fila {buffer.BufferedMilliseconds:0} ms · captura WASAPI {route.CaptureLatencyMilliseconds:0} ms · saída solicitada {AudioRoute.OutputBufferMilliseconds} ms · faltas {buffer.Underflows} · descartes {buffer.Overflows} · descontinuidades {Interlocked.Read(ref route.Discontinuities)}",
            $"Queue {buffer.BufferedMilliseconds:0} ms · WASAPI capture {route.CaptureLatencyMilliseconds:0} ms · requested output {AudioRoute.OutputBufferMilliseconds} ms · underruns {buffer.Underflows} · drops {buffer.Overflows} · discontinuities {Interlocked.Read(ref route.Discontinuities)}");
        DryWave.Points = WavePoints(_drySamples, RouteWave.ActualWidth, RouteWave.ActualHeight);
        WetWave.Points = WavePoints(_wetSamples, RouteWave.ActualWidth, RouteWave.ActualHeight);
        for (var i = 0; i < _spectrum.Length; i++)
        {
            _spectrum[i].X = (float)(_wetSamples[i] * FastFourierTransform.HannWindow(i, _spectrum.Length));
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
