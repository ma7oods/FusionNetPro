using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FusionNet.Windows.Network;

/// <summary>
/// Small, allocation-free Win32 interop used by the network services. Everything here is
/// read-only except <see cref="TryFlushResolverCache"/> and every call is wrapped so that a
/// missing export (older Windows) degrades to "unknown" instead of an exception.
/// </summary>
internal static class NetworkInterop
{
    /// <summary>8.8.8.8 in network byte order — symmetric, so endianness does not matter here.</summary>
    private const uint PublicProbeAddress = 0x08080808;

    [DllImport("iphlpapi.dll", EntryPoint = "GetBestInterface")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetBestInterface(uint destination, out uint bestInterfaceIndex);

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint DnsFlushResolverCache();

    [DllImport("kernel32.dll", EntryPoint = "GetOEMCP")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NativeGetOemCodePage();

    /// <summary>
    /// True when the current process token is the elevated (unfiltered) administrator token.
    /// A standard user or a UAC-filtered administrator both report false.
    /// </summary>
    public static bool IsElevated { get; } = DetectElevation();

    /// <summary>
    /// Index of the interface Windows would use to reach the public internet, or null when the
    /// machine has no default route. Used for default-route detection without a full route table scan.
    /// </summary>
    public static uint? GetDefaultInterfaceIndex()
    {
        try
        {
            return GetBestInterface(PublicProbeAddress, out var index) == 0 ? index : null;
        }
        catch (Exception)
        {
            // iphlpapi missing or call rejected — callers fall back to gateway heuristics.
            return null;
        }
    }

    /// <summary>Flushes the DNS resolver cache through dnsapi.dll. Returns false when the export is unavailable.</summary>
    public static bool TryFlushResolverCache()
    {
        try
        {
            DnsFlushResolverCache();
            return true;
        }
        catch (Exception)
        {
            // Entry point/DLL missing (can happen on stripped-down Windows images) — caller falls back to ipconfig.
            return false;
        }
    }

    /// <summary>Console output code page used by native tools such as netsh, defaulting to 437.</summary>
    public static int GetOemCodePage()
    {
        try
        {
            var codePage = NativeGetOemCodePage();
            return codePage > 0 ? codePage : 437;
        }
        catch (Exception)
        {
            // Extremely unlikely; 437 is the historical default for English Windows.
            return 437;
        }
    }

    private static bool DetectElevation()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            // Security APIs are unavailable — treat as a standard user, which is the safe answer.
            return false;
        }
    }
}
