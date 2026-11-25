using System.Windows;
using System.Windows.Controls;

namespace ZeusAudioControl;

public partial class MainWindow
{
    internal void VerifyLayout()
    {
        var sizes = new[] { new Size(630, 500), new Size(860, 620), new Size(1180, 760) };
        var pages = new (string Page, FrameworkElement Panel)[]
        {
            ("Visão geral", OverviewPanel), ("Áudio", AudioPanel), ("Microfone", MicPanel),
            ("Rotas", RoutesPanel), ("Configurações", SettingsPanel)
        };
        foreach (var size in sizes)
        {
            Width = size.Width;
            Height = size.Height;
            foreach (var (page, panel) in pages)
            {
                Navigate(page);
                UpdateLayout();
                if (!panel.IsVisible || panel.ActualWidth <= 0 || panel.ActualWidth > MainScroll.ViewportWidth)
                    throw new InvalidOperationException($"Layout of {page} is outside the viewport at {size}.");
            }
        }

        Navigate("Áudio");
        UpdateLayout();
        if (VolumeSlider.Template.FindName("PART_Track", VolumeSlider) is not System.Windows.Controls.Primitives.Track ||
            OutputDevices.Template.FindName("PART_Popup", OutputDevices) is not System.Windows.Controls.Primitives.Popup)
            throw new InvalidOperationException("The slider or selector template is missing a required part.");

        Navigate("Rotas");
        UpdateLayout();
        for (var i = 0; i < RouteMode.Items.Count; i++)
        {
            RouteMode.SelectedIndex = i;
            if (string.IsNullOrWhiteSpace(RouteHelp.Text))
                throw new InvalidOperationException($"Missing route help for mode {i}.");
        }

        EnglishToggle.IsChecked = true;
        ApplyPreferences();
        if (PageTitle.Text != "Routes" || RouteHelp.Text.Length == 0)
            throw new InvalidOperationException("Route language resources did not update.");
    }
}
