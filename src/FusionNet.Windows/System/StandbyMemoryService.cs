using FusionNet.Core.Abstractions;
using System.ComponentModel;
using System.Diagnostics;
using FusionNet.Windows.Native;

namespace FusionNet.Windows.System;

/// <summary>
/// Purges the Windows standby memory list after enabling the required token privilege.
/// Returns the approximate number of bytes that became available; non-admin callers get 0.
/// </summary>
public sealed class StandbyMemoryService : IStandbyMemoryService
{
    private const string PrivilegeName = "SeProfileSingleProcessPrivilege";

    public long Purge()
    {
        if (!ElevationService.CheckIsElevated()) return 0;

        try
        {
            // The privilege is referenced "held but disabled" by default; enable it first.
            if (!Win32Native.TryEnablePrivilege(PrivilegeName, out _)) return 0;
            if (!Win32Native.TryPurgeStandbyList(out var before, out var after)) return 0;

            var freed = after - before;
            return freed > 0 ? freed : 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Memory purge is a best-effort optimisation; the coordinator logs the zero result.
            Debug.WriteLine("Standby memory purge failed: " + ex.Message);
            return 0;
        }
    }
}
