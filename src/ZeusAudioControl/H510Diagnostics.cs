using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ZeusAudioControl;

internal static class H510Diagnostics
{
    private const int HidSuccess = 0x00110000;
    private const uint EnumeratePresentInterfaces = 0x12;
    private const int NoMoreItems = 259;
    private const int ValueCapsSize = 72;

    public static string Inspect()
    {
        var result = new StringBuilder($"H510-PRO · USB 040B:0897 · {DateTimeOffset.Now:O}\n");
        var nodes = UsbNodeCount();
        result.AppendLine(nodes is { } count ? $"Present USB PnP nodes matching VID/PID: {count}" : "USB PnP node count unavailable.");
        var found = 0;
        var advertised = false;
        foreach (var path in ReceiverPaths())
        {
            result.AppendLine($"HID collection {++found}");
            using var handle = CreateFile(path!, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
            if (!handle.IsInvalid && HidD_GetAttributes(handle, ref attributes))
                result.AppendLine($"  USB ID {attributes.Vendor:X4}:{attributes.Product:X4}, release 0x{attributes.Release:X4}");
            if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var descriptor))
            {
                result.AppendLine("  HID metadata unavailable.");
                continue;
            }
            try
            {
                if (HidP_GetCaps(descriptor, out var caps) != HidSuccess)
                {
                    result.AppendLine("  HID capabilities unavailable.");
                    continue;
                }
                result.AppendLine($"  Usage page 0x{caps.Page:X4}, usage 0x{caps.Usage:X4}");
                result.AppendLine($"  Report sizes: input {caps.InputBytes}, output {caps.OutputBytes}, feature {caps.FeatureBytes}");
                var input = BatteryUsage(descriptor, 0, caps.InputValues);
                var feature = BatteryUsage(descriptor, 2, caps.FeatureValues);
                advertised |= input == true || feature == true;
                result.AppendLine($"  Battery Strength usage 0006:0020: input {Describe(input)}, feature {Describe(feature)}");
            }
            finally { HidD_FreePreparsedData(descriptor); }
        }

        if (found == 0) result.AppendLine("No present HID collection matched VID/PID. Connect the receiver, turn the headset on, and retry.");
        if (advertised) result.AppendLine("A standard battery descriptor was advertised. A descriptor does not establish a validated battery percentage.");
        result.AppendLine("Battery percentage unavailable. Inspection opened descriptor metadata only. No HID input, output or feature report was requested or sent.");
        return result.ToString();
    }

    private static IEnumerable<string> ReceiverPaths()
    {
        HidD_GetHidGuid(out var hid);
        var devices = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, EnumeratePresentInterfaces);
        if (devices == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (uint index = 0; ; index++)
            {
                var entry = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref hid, index, ref entry))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NoMoreItems) yield break;
                    throw new System.ComponentModel.Win32Exception(error);
                }
                var path = ReadInterfacePath(devices, ref entry);
                if (IsReceiverPath(path)) yield return path!;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devices); }
    }

    public static async Task<string> Capture(CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        var token = budget.Token;
        var log = new StringBuilder($"H510-PRO · 040B:0897 · rev 002A · F100:0000\nStarted {DateTimeOffset.UtcNow:O}\n");
        foreach (var path in ReceiverPaths())
        {
            token.ThrowIfCancellationRequested();
            using var metadata = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (!MatchesPassiveCollection(metadata)) continue;

            const uint genericRead = 0x80000000;
            const uint overlapped = 0x40000000;
            using var handle = CreateFile(path, genericRead, 3, IntPtr.Zero, 3, overlapped, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Shared read-only HID access was refused.");
            if (!MatchesPassiveCollection(handle)) throw new InvalidDataException("The HID collection changed before capture.");
            using var reader = new FileStream(handle, FileAccess.Read, 13, isAsync: true);
            var data = new byte[13];
            var count = 0;
            try
            {
                while (count < 256)
                {
                    var read = await reader.ReadAsync(data.AsMemory(), token).ConfigureAwait(false);
                    if (read == 0) break;
                    if (read != data.Length)
                    {
                        log.AppendLine($"Incomplete report ({read} bytes) omitted.");
                        break;
                    }
                    count++;
                    log.AppendLine($"{DateTimeOffset.UtcNow:O}  {Convert.ToHexString(data)}");
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                log.AppendLine(cancellationToken.IsCancellationRequested ? "Cancelled." : "Time limit reached.");
            }
            catch (IOException error)
            {
                log.AppendLine($"Read stopped: {error.Message}");
            }
            log.AppendLine($"Reports: {count}. Battery percentage unavailable; bytes are uninterpreted.");
            return log.ToString();
        }
        return log.AppendLine("Matching receiver collection unavailable. No reports captured.").ToString();
    }

    private static bool MatchesPassiveCollection(SafeFileHandle handle)
    {
        var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
        if (handle.IsInvalid || !HidD_GetAttributes(handle, ref attributes) ||
            attributes.Vendor != 0x040B || attributes.Product != 0x0897 ||
            !HidD_GetPreparsedData(handle, out var descriptor)) return false;
        try
        {
            return HidP_GetCaps(descriptor, out var caps) == HidSuccess &&
                IsPassiveTarget(attributes.Release, caps.Page, caps.Usage, caps.InputBytes, caps.OutputBytes, caps.FeatureBytes);
        }
        finally { HidD_FreePreparsedData(descriptor); }
    }

    internal static bool IsPassiveTarget(int release, int page, int usage, int input, int output, int feature) =>
        release == 0x002A && page == 0xF100 && usage == 0 && input == 13 && output == 65 && feature == 0;

    internal static bool IsReceiverPath(string? path)
    {
        if (path is null) return false;
        foreach (var part in path.Split('#', '\\'))
        {
            var ids = part.Split('&');
            if (ids.Contains("vid_040b", StringComparer.OrdinalIgnoreCase) &&
                ids.Contains("pid_0897", StringComparer.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int? UsbNodeCount()
    {
        const uint flags = 0x101; // Filter USB enumerator, present nodes only.
        if (CM_Get_Device_ID_List_Size(out var chars, "USB", flags) != 0 || chars is < 1 or > 1048576)
            return null;
        var buffer = Marshal.AllocHGlobal(checked(chars * 2));
        try
        {
            if (CM_Get_Device_ID_List("USB", buffer, chars, flags) != 0) return null;
            var ids = Marshal.PtrToStringUni(buffer, chars)!;
            return ids.Split('\0', StringSplitOptions.RemoveEmptyEntries).Count(IsReceiverPath);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string Describe(bool? advertised) => advertised switch
    {
        true => "advertised", false => "not advertised", _ => "unknown"
    };

    private static bool? BatteryUsage(IntPtr descriptor, int type, ushort count)
    {
        if (count == 0) return false;
        var capabilities = Marshal.AllocHGlobal(checked(count * ValueCapsSize));
        try
        {
            var status = HidP_GetSpecificValueCaps(type, 0x06, 0, 0x20, capabilities, ref count, descriptor);
            return status switch
            {
                HidSuccess => count > 0,
                0xC0110004u => false, // HIDP_STATUS_USAGE_NOT_FOUND
                _ => null
            };
        }
        finally { Marshal.FreeHGlobal(capabilities); }
    }

    private static string? ReadInterfacePath(IntPtr devices, ref InterfaceData entry)
    {
        SetupDiGetDeviceInterfaceDetail(devices, ref entry, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        if (size is < 8 or > 65536) return null;
        var data = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(data, IntPtr.Size == 8 ? 8 : 6);
            return SetupDiGetDeviceInterfaceDetail(devices, ref entry, data, size, out _, IntPtr.Zero)
                ? Marshal.PtrToStringUni(IntPtr.Add(data, 4)) : null;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        public int Size;
        public Guid Class;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct HidCaps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort Page;
        [FieldOffset(4)] public ushort InputBytes;
        [FieldOffset(6)] public ushort OutputBytes;
        [FieldOffset(8)] public ushort FeatureBytes;
        [FieldOffset(48)] public ushort InputValues;
        [FieldOffset(60)] public ushort FeatureValues;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes { public int Size; public ushort Vendor, Product, Release; }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_Size(out int length, string filter, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List(string filter, IntPtr buffer, int length, uint flags);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HidAttributes attributes);
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
    [DllImport("hid.dll")] private static extern uint HidP_GetSpecificValueCaps(int type, ushort page, ushort collection, ushort usage,
        IntPtr capabilities, ref ushort length, IntPtr data);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string file, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr devices, IntPtr device, ref Guid guid, uint index, ref InterfaceData entry);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devices, ref InterfaceData entry, IntPtr detail, int size, out int required, IntPtr device);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
}
