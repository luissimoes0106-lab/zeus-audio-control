using System.IO;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private void Calibration_Click(object sender, RoutedEventArgs e)
    {
        var (window, body) = BrandWindow("ZEUS Mic Calibration");
        if (sender is FrameworkElement control && Window.GetWindow(control) is { } owner) window.Owner = owner;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = L("Capture quatro amostras de cinco segundos.", "Capture four five-second samples.") };
        var measurements = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var labels = new[]
        {
            L("Ambiente", "Background"), L("Fala baixa", "Quiet speech"),
            L("Fala normal", "Normal speech"), L("Fala alta", "Loud speech")
        };
        var raw = new Button { Content = L("Ouvir original", "Play original"), IsEnabled = false };
        var preview = new Button { Content = L("Ouvir recomendação", "Preview recommendation"), IsEnabled = false };
        var apply = new Button { Content = L("Aplicar recomendação", "Apply recommendation"), IsEnabled = false };
        var restore = new Button { Content = L("Restaurar configuração anterior", "Restore previous settings"), IsEnabled = _intelligence.CalibrationBackup is not null };
        var samples = new VoiceMeasurement?[4];
        var steps = new WrapPanel();
        var folder = Directory.CreateTempSubdirectory("zeus-calibration-").FullName;
        string Recording(int step) => Path.Combine(folder, $"step-{step}.wav");
        var listen = new ComboBox { ItemsSource = labels, SelectedIndex = 2, IsEnabled = false };
        var busy = false;
        var closed = false;
        StudioProfile? recommendation = null;
        body.Children.Add(status);
        body.Children.Add(steps);
        body.Children.Add(measurements);
        body.Children.Add(listen);
        body.Children.Add(new WrapPanel { Children = { raw, preview, apply, restore } });
        var buttons = labels.Select(label => new Button { Content = label }).ToArray();
        void Controls()
        {
            for (var i = 0; i < buttons.Length; i++)
                buttons[i].IsEnabled = !busy && (i == 0 || samples.Take(i).All(sample => sample.HasValue));
            listen.IsEnabled = !busy && samples.Any(sample => sample.HasValue);
            raw.IsEnabled = !busy && listen.SelectedIndex >= 0 && samples[listen.SelectedIndex].HasValue;
            preview.IsEnabled = raw.IsEnabled && recommendation is not null;
            apply.IsEnabled = !busy && recommendation is not null;
        }
        void Clean()
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        async Task Measure(int step)
        {
            if (busy) return;
            busy = true;
            recommendation = null;
            samples[step] = null;
            Controls();
            status.Text = L($"Gravando por cinco segundos: {labels[step]}.", $"Recording for five seconds: {labels[step]}.");
            try
            {
                if (!await CaptureTest(Recording(step)))
                    throw new InvalidOperationException(L("A gravação falhou. Verifique a entrada selecionada.", "Recording failed. Check the selected input."));
                if (closed) return;
                var (rms, peak) = await Task.Run(() => MeasureWave(Recording(step)));
                if (closed) return;
                samples[step] = new VoiceMeasurement(rms, peak);
                listen.SelectedIndex = step;
                measurements.Text = string.Join(Environment.NewLine, samples.Select((sample, index) =>
                    sample is { } value ? $"{labels[index]} · RMS {value.Rms:0.0} dBFS · {L("pico", "peak")} {value.Peak:0.0} dBFS" : $"{labels[index]} · —"));
                if (samples.All(sample => sample.HasValue))
                {
                    recommendation = VoiceRules.Calibrate(CurrentStudio(), samples.Select(sample => sample!.Value).ToArray());
                    var profile = recommendation;
                    status.Text = L(
                        $"Ganho +{profile.Gain:0.0} dB · gate {profile.Threshold:0.0} dBFS · compressor {profile.CompressorThreshold:0.0} dBFS · teto {20 * Math.Log10(profile.Limiter):0.0} dBFS.",
                        $"Gain +{profile.Gain:0.0} dB · gate {profile.Threshold:0.0} dBFS · compressor {profile.CompressorThreshold:0.0} dBFS · ceiling {20 * Math.Log10(profile.Limiter):0.0} dBFS.");
                }
                else status.Text = L("Amostra salva para este teste. Continue com a próxima etapa.", "Sample kept for this test. Continue with the next step.");
            }
            catch (Exception error) { if (!closed) status.Text = error.Message; }
            finally
            {
                busy = false;
                if (closed) Clean();
                else Controls();
            }
        }
        for (var i = 0; i < buttons.Length; i++)
        {
            var step = i;
            steps.Children.Add(buttons[i]);
            buttons[i].Click += async (_, _) => await Measure(step);
        }
        listen.SelectionChanged += (_, _) => Controls();
        raw.Click += (_, _) => PlayTest(true, recording: Recording(listen.SelectedIndex));
        preview.Click += (_, _) => { if (recommendation is not null) PlayTest(false, recommendation, Recording(listen.SelectedIndex)); };
        Controls();
        apply.Click += (_, _) =>
        {
            if (recommendation is null) return;
            _intelligence.CalibrationBackup = CurrentStudio();
            ApplyStudio(recommendation);
            PersistIfChanged();
            SaveIntelligence();
            restore.IsEnabled = true;
            apply.IsEnabled = false;
            status.Text = L("Aplicado ao perfil manual. A automação Smart Voice ativa continua tendo prioridade.", "Applied to the manual profile. Active Smart Voice automation still takes priority.");
        };
        restore.Click += (_, _) =>
        {
            if (_intelligence.CalibrationBackup is not { } backup) return;
            ApplyStudio(backup);
            _intelligence.CalibrationBackup = null;
            PersistIfChanged();
            SaveIntelligence();
            restore.IsEnabled = false;
            status.Text = L("Configuração anterior restaurada.", "Previous settings restored.");
        };
        window.Closed += (_, _) =>
        {
            closed = true;
            StopMicrophonePlayback();
            if (!busy) Clean();
        };
        window.ShowDialog();
    }

    private static (double Rms, double Peak) MeasureWave(string file)
    {
        using var wave = new WaveFileReader(file);
        using var raw = new RawSourceWaveStream(wave, wave.WaveFormat.AsStandardWaveFormat());
        var samples = raw.ToSampleProvider();
        var block = new float[4096];
        double energy = 0, peak = 0;
        long total = 0;
        int read;
        while ((read = samples.Read(block, 0, block.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var value = float.IsFinite(block[i]) ? Math.Abs(block[i]) : 0;
                energy += value * (double)value;
                peak = Math.Max(peak, value);
            }
            total += read;
        }
        if (total == 0) throw new InvalidOperationException("No samples captured.");
        return (10 * Math.Log10(Math.Max(1e-12, energy / total)), 20 * Math.Log10(Math.Max(1e-6, peak)));
    }
}
