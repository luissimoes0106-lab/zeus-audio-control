using System.Windows;

namespace ZeusAudioControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!e.Args.Contains("--ui-smoke", StringComparer.Ordinal))
            new SplashScreen("ZEUS-Logo.png").Show(autoClose: true, topMost: false);
        if (e.Args.Contains("--ui-smoke", StringComparer.Ordinal))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
            {
                try
                {
                    if (MainWindow is not ZeusAudioControl.MainWindow window)
                        throw new InvalidOperationException("Main window is unavailable.");
                    window.VerifyLayout();
                    Shutdown(0);
                }
                catch (Exception error)
                {
                    var profile = Environment.GetEnvironmentVariable("ZEUS_UI_SMOKE_PROFILE");
                    if (profile is not null)
                    {
                        System.IO.Directory.CreateDirectory(profile);
                        System.IO.File.WriteAllText(System.IO.Path.Combine(profile, "failure.txt"), error.ToString());
                    }
                    Shutdown(1);
                }
            });
    }
}
