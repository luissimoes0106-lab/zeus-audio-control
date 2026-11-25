using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace ZeusAudioControl;

public partial class MainWindow
{
    private sealed record BatteryDevice(string Id, string Name);
    private async Task<BatteryDevice[]> PairedBatteryDevices(CancellationToken token)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return [];
        var entries = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(token);
        return entries.Select(device => new BatteryDevice(device.Id, device.Name)).ToArray();
    }

    private async Task<string> BatteryStatus(CancellationToken token)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) || string.IsNullOrEmpty(_intelligence.BatteryId))
            return L("Bateria indisponível · selecione um dispositivo BLE.", "Battery unavailable · select a BLE device.");
        using var device = await BluetoothLEDevice.FromIdAsync(_intelligence.BatteryId).AsTask(token);
        if (device is null) return L("Dispositivo Bluetooth indisponível.", "Bluetooth device unavailable.");
        var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask();
        try
        {
            if (services.Status == GattCommunicationStatus.Success)
            {
                foreach (var service in services.Services)
                {
                    token.ThrowIfCancellationRequested();
                    var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask();
                    if (characteristics.Status != GattCommunicationStatus.Success) continue;
                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        token.ThrowIfCancellationRequested();
                        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask();
                        if (result.Status != GattCommunicationStatus.Success || result.Value.Length != 1) continue;
                        using var reader = DataReader.FromBuffer(result.Value);
                        if (IntelligenceRules.BatteryPercent(reader.ReadByte()) is { } percent)
                            return L($"{device.Name} · bateria {percent}% (Bluetooth LE)", $"{device.Name} · battery {percent}% (Bluetooth LE)");
                    }
                }
            }
            return L($"{device.Name} · bateria indisponível no hardware.", $"{device.Name} · hardware battery unavailable.");
        }
        finally { foreach (var service in services.Services) service.Dispose(); }
    }

    private async Task ShowBatteryStatus(TextBlock status, CancellationToken token)
    {
        try
        {
            var reading = await BatteryStatus(token);
            if (!token.IsCancellationRequested) status.Text = reading;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!token.IsCancellationRequested) status.Text = L("Bateria indisponível.", "Battery unavailable.");
        }
    }

    private static string Transport(string endpoint)
    {
        try
        {
            if (CM_Locate_DevNode(out var node, @"SWD\MMDEVAPI\" + endpoint, 0) != 0) return "—";
            for (var depth = 0; depth < 12; depth++)
            {
                var id = new StringBuilder(512);
                if (CM_Get_Device_ID(node, id, id.Capacity, 0) != 0) break;
                var value = id.ToString();
                if (value.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) return "Bluetooth";
                if (value.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) return "USB";
                if (value.StartsWith("HDAUDIO\\", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("INTELAUDIO\\", StringComparison.OrdinalIgnoreCase)) return "HD Audio";
                if (CM_Get_Parent(out node, node, 0) != 0) break;
            }
        }
        catch (ExternalException) { }
        return "—";
    }

    private void AudioDoctor_Click(object sender, RoutedEventArgs e)
    {
        var (window, body) = BrandWindow("ZEUS Audio Doctor");
        if (sender is FrameworkElement control && Window.GetWindow(control) is { } owner) window.Owner = owner;
        var report = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 125, MaxHeight = 275,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = L("Coletando diagnóstico…", "Collecting diagnostics…") };
        var save = new Button { Content = L("Salvar relatório local", "Save local report"), IsEnabled = false };
        body.Children.Add(report);
        body.Children.Add(save);
        var outId = _output?.ID;
        var inId = _input?.ID;
        var preferredOut = _preferredOutput;
        var preferredIn = _preferredInput;
        var measures = PeakDb.Text;
        var routes = string.Join(Environment.NewLine, _routes.Values.Select(active =>
            $"{active.Endpoints.Kind}: {active.Endpoints.Input} -> {active.Endpoints.Output}; " +
            $"state={(active.Running ? "running" : "starting")}, underruns={active.Audio.Buffer?.Underflows}, " +
            $"drops={active.Audio.Buffer?.Overflows}, discontinuities={Interlocked.Read(ref active.Audio.Discontinuities)}"));
        var history = string.Join(Environment.NewLine, _intelligence.Disconnects.TakeLast(10));
        window.Loaded += async (_, _) =>
        {
            var diagnostics = await Task.Run(() => DescribeAudio(outId, inId, preferredOut, preferredIn));
            if (!window.IsVisible) return;
            report.Text = $"{diagnostics}\n{measures}\n{routes}\n{history}";
            save.IsEnabled = true;
        };
        save.Click += async (_, _) =>
        {
            var file = new Microsoft.Win32.SaveFileDialog { FileName = "ZEUS-Audio-Doctor.txt", Filter = "Text|*.txt" };
            try { if (file.ShowDialog(window) == true) await File.WriteAllTextAsync(file.FileName, report.Text); }
            catch (Exception error) { Footer.Text = error.Message; }
        };
        window.ShowDialog();
    }

    private static string DescribeAudio(string? output, string? input, string? preferredOutput, string? preferredInput)
    {
        var text = new StringBuilder($"ZEUS Audio Doctor · {DateTimeOffset.Now:O}\n");
        var state = AudioServiceState();
        text.AppendLine($"Windows Audio (Audiosrv): {(state == 4 ? "Running" : state == 0 ? "Unavailable" : $"SCM state {state}")}");
        if (state != 4) text.AppendLine("Verify Windows Audio in services.msc.");
        try
        {
            using var devices = new MMDeviceEnumerator();
            foreach (var (selected, preferred, label) in new[] { (output, preferredOutput, "Output"), (input, preferredInput, "Input") })
            {
                if (preferred is not null && preferred != selected)
                    text.AppendLine($"{label}: preferred endpoint disconnected; verify the physical connection.");
                if (selected is null) { text.AppendLine($"{label}: unavailable; select a Windows endpoint."); continue; }
                using var endpoint = devices.GetDevice(selected);
                text.AppendLine($"{label}: {endpoint.FriendlyName} · {endpoint.State} · {Transport(endpoint.ID)}");
                if (endpoint.State != DeviceState.Active) continue;
                using var audio = endpoint.AudioClient;
                var mix = audio.MixFormat;
                text.AppendLine($"  {mix.SampleRate} Hz · {mix.Channels} ch · {mix.BitsPerSample} bit · mask 0x{ChannelLayout.GetMask(mix):X}");
                if (endpoint.AudioEndpointVolume.Mute) text.AppendLine("  Endpoint muted; check its mute control.");
            }
        }
        catch (Exception error) { text.AppendLine(error.Message); }
        text.AppendLine("For virtual routes, verify distinct cable endpoints and use standard buffering if interruptions increase.");
        return text.ToString();
    }

    private static uint AudioServiceState()
    {
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) return 0;
        try
        {
            var service = OpenService(manager, "Audiosrv", 4);
            if (service == IntPtr.Zero) return 0;
            try { return QueryServiceStatus(service, out var status) ? status.State : 0; }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus
    { public uint Type, State, Accepted, Exit, ServiceExit, Checkpoint, WaitHint; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll")] private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNode(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID(uint node, StringBuilder id, int size, uint flags);
}
