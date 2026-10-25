using System.Runtime.InteropServices;

namespace FusionNet.Windows.Monitoring;

internal static class OfflineSignature
{
    // Cache-only flags prevent certificate retrieval over the network; catalog-only signatures remain unknown.
    internal static string Verify(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || !File.Exists(path)) return "Unavailable";
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
                File = pointer, StateAction = 1, ProviderFlags = 0x1010 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try
            {
                var status = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                return status switch { 0 => "ValidCached", unchecked((int)0x800B0100) => "NotSigned",
                    unchecked((int)0x80096010) => "HashMismatch", _ => "Unknown (offline trust unavailable)" };
            }
            finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or global::System.ComponentModel.Win32Exception) { return "Unavailable"; }
        finally { Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer); }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr FileHandle, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr Policy, Sip;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData, Url;
        public uint ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }
}
