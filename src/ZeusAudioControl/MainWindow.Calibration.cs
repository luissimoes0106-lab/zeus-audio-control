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
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = L("Grave primeiro cinco segundos em silêncio.", "First record five seconds of room noise.") };
        var ambient = new Button { Content = L("Medir ambiente", "Measure background") };
        var voice = new Button { Content = L("Gravar fala normal", "Record normal speech"), IsEnabled = false };
        var raw = new Button { Content = L("Ouvir original", "Play original"), IsEnabled = false };
        var preview = new Button { Content = L("Ouvir recomendação", "Preview recommendation"), IsEnabled = false };
        var apply = new Button { Content = L("Aplicar recomendação", "Apply recommendation"), IsEnabled = false };
        var restore = new Button { Content = L("Restaurar configuração anterior", "Restore previous settings"), IsEnabled = _intelligence.CalibrationBackup is not null };
        body.Children.Add(status);
        body.Children.Add(new WrapPanel { Children = { ambient, voice, raw, preview, apply, restore } });
        double? noise = null;
        StudioProfile? recommendation = null;
        async Task Measure(bool speech)
        {
            ambient.IsEnabled = voice.IsEnabled = raw.IsEnabled = preview.IsEnabled = apply.IsEnabled = false;
            recommendation = null;
            status.Text = speech ? L("Fale no volume habitual por cinco segundos.", "Speak normally for five seconds.")
                : L("Mantenha silêncio por cinco segundos.", "Stay quiet for five seconds.");
            try
            {
                if (!await CaptureTest()) throw new InvalidOperationException(L("A gravação falhou. Verifique a entrada selecionada.", "Recording failed. Check the selected input."));
                if (!window.IsVisible) return;
                var (rms, peak) = await Task.Run(() => MeasureWave(_testPath));
                if (!window.IsVisible) return;
                if (speech)
                {
                    raw.IsEnabled = true;
                    recommendation = IntelligenceRules.Calibrate(CurrentStudio(), noise!.Value, rms, peak);
                    preview.IsEnabled = apply.IsEnabled = true;
                    status.Text = L(
                        $"Ambiente {noise:0.0} dBFS · fala {rms:0.0} dBFS · pico {peak:0.0} dBFS. Ganho +{recommendation.Gain:0.0} dB · gate {recommendation.Threshold:0.0} dBFS · compressor {recommendation.CompressorThreshold:0.0} dBFS.",
                        $"Background {noise:0.0} dBFS · speech {rms:0.0} dBFS · peak {peak:0.0} dBFS. Gain +{recommendation.Gain:0.0} dB · gate {recommendation.Threshold:0.0} dBFS · compressor {recommendation.CompressorThreshold:0.0} dBFS.");
                }
                else
                {
                    noise = rms;
                    status.Text = L($"Ambiente {rms:0.0} dBFS. Grave a fala.", $"Background {rms:0.0} dBFS. Record speech.");
                }
            }
            catch (Exception error) { if (window.IsVisible) status.Text = error.Message; }
            finally { ambient.IsEnabled = true; voice.IsEnabled = noise is not null; }
        }
        ambient.Click += async (_, _) => await Measure(false);
        voice.Click += async (_, _) => await Measure(true);
        raw.Click += (_, _) => PlayTest(true);
        preview.Click += (_, _) => { if (recommendation is not null) PlayTest(false, recommendation); };
        apply.Click += (_, _) =>
        {
            if (recommendation is null) return;
            _intelligence.CalibrationBackup = CurrentStudio();
            ApplyStudio(recommendation);
            PersistIfChanged();
            SaveIntelligence();
            restore.IsEnabled = true;
            apply.IsEnabled = false;
            status.Text = L("Aplicado. Reinicie a rota de microfone para usar os ajustes.", "Applied. Restart the microphone route to use the new settings.");
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
        window.Closed += (_, _) => StopMicrophonePlayback();
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
