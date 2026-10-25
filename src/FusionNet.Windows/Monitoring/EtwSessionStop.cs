using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FusionNet.Windows.Monitoring;

internal static class EtwSessionStop
{
    // x64 EVENT_TRACE_PROPERTIES, including its 48-byte WNODE_HEADER.
    [StructLayout(LayoutKind.Explicit, Size = 120)]
    internal struct Properties
    {
        [FieldOffset(0)] public uint BufferSize;
        [FieldOffset(44)] public uint Flags;
        [FieldOffset(88)] public uint EventsLost;
        [FieldOffset(96)] public uint LogBuffersLost;
        [FieldOffset(100)] public uint RealTimeBuffersLost;
        [FieldOffset(112)] public uint LogFileNameOffset;
        [FieldOffset(116)] public uint LoggerNameOffset;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint ControlTraceW(ulong handle, string name, IntPtr properties, uint code);

    internal static int StopAndReadLost(string name)
    {
        const string prefix = "FusionNetPro.Net.";
        if (IntPtr.Size != 8 || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[prefix.Length..], "N", out _)) throw new ArgumentException("Invalid owned ETW session.");
        const int size = 120 + 2 * 1024 * 2;
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, memory, size);
            Marshal.StructureToPtr(new Properties { BufferSize = size, Flags = 0x20000, LoggerNameOffset = 120, LogFileNameOffset = 120 + 2048 }, memory, false);
            // STOP returns final loss statistics; a pre-stop QUERY misses drops during draining.
            var error = ControlTraceW(0, name, memory, 1);
            if (error != 0) throw new Win32Exception((int)error);
            var result = Marshal.PtrToStructure<Properties>(memory);
            return (int)Math.Min(int.MaxValue, (long)result.EventsLost + result.LogBuffersLost + result.RealTimeBuffersLost);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
