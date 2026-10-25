using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FusionNet.Windows.Native;

/// <summary>
/// Every Win32/NT call FusionNet needs in one audited place. All public helpers are
/// non-throwing: they translate NTSTATUS / Win32 errors into booleans and error codes so
/// callers can decide how to report the failure.
/// </summary>
public static class Win32Native
{
    // ---------------------------------------------------------------- process access rights
    public const uint ProcessSuspendResume = 0x0800;
    public const uint ProcessSetInformation = 0x0200;
    public const uint ProcessQueryInformation = 0x0400;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint ProcessSetQuota = 0x0100;

    // ---------------------------------------------------------------- priority classes
    public const uint IdlePriorityClass = 0x00000040;
    public const uint BelowNormalPriorityClass = 0x00004000;
    public const uint NormalPriorityClass = 0x00000020;
    public const uint AboveNormalPriorityClass = 0x00008000;
    public const uint HighPriorityClass = 0x00000080;
    public const uint RealTimePriorityClass = 0x00000100;

    // ---------------------------------------------------------------- IO priority values
    public const int IoPriorityVeryLow = 0;
    public const int IoPriorityLow = 1;
    public const int IoPriorityNormal = 2;
    public const int IoPriorityHigh = 3;
    public const int IoPriorityCritical = 4;

    // ---------------------------------------------------------------- NT information classes
    public const int ProcessIoPriorityClass = 0x21;   // ProcessIoPriority
    public const int SystemMemoryListInformation = 80;
    public const int MemoryPurgeStandbyList = 4;
    public const int MemoryReclaimLowPriority = 5;

    // ---------------------------------------------------------------- token privileges
    public const uint TokenAdjustPrivileges = 0x0020;
    public const uint TokenQuery = 0x0008;
    public const uint SePrivilegeEnabled = 0x00000002;

    // ---------------------------------------------------------------- common win32 errors
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidParameter = 87;
    public const int ErrorNotAllAssigned = 1300;
    public const int ErrorProcessAborted = 299;
    public const int ErrorNotFound = 1168;

    /// <summary>Result of <see cref="GetMemoryStatus"/>.</summary>
    public readonly record struct MemoryStatus(
        uint MemoryLoad,
        ulong TotalPhysicalBytes,
        ulong AvailablePhysicalBytes,
        ulong TotalPageFileBytes,
        ulong AvailablePageFileBytes);

    /// <summary>Result of <see cref="GetVersion"/>.</summary>
    public readonly record struct WindowsVersion(int Major, int Minor, int Build, int PlatformId);

    // ================================================================ kernal32

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool inheritHandle, int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilter(uint message, uint flag);

    /// <summary>UIPI drops Explorer's TaskbarCreated broadcast to an elevated process, so the tray
    /// icon would vanish after Explorer restarts. MSGFLT_ADD (1) lets it through.</summary>
    public static bool TryAllowTaskbarCreated() => ChangeWindowMessageFilter(RegisterWindowMessage("TaskbarCreated"), 1);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags,
        [Out] char[] buffer, ref int size);

    // ================================================================ ntdll

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass,
        ref int processInformation, int processInformationLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref int processInformation, int processInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass,
        ref int systemInformation, int systemInformationLength);

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    private static extern int RtlGetVersion(ref OsVersionInfoExW versionInfo);

    // ================================================================ advapi32

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    // ================================================================ dnsapi

    [DllImport("dnsapi.dll")]
    private static extern uint DnsFlushResolverCache();

    // ================================================================ structs

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfoExW
    {
        public uint Size;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public uint PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ServicePack;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    // ================================================================ process helpers

    /// <summary>Opens a process with the requested access; never throws.</summary>
    public static bool TryOpenProcess(uint access, int processId, out IntPtr handle, out int errorCode)
    {
        handle = IntPtr.Zero;
        errorCode = 0;
        if (processId <= 0)
        {
            errorCode = ErrorInvalidParameter;
            return false;
        }

        try
        {
            handle = OpenProcess(access, false, processId);
            if (handle != IntPtr.Zero) return true;
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            errorCode = -1;
            return false;
        }
    }

    /// <summary>Suspends every thread in a process. Returns the NTSTATUS success flag.</summary>
    public static bool SuspendProcess(int processId, out int errorCode)
        => InvokeOnProcess(processId, ProcessSuspendResume, NtSuspendProcess, out errorCode);

    /// <summary>Resumes every thread in a process.</summary>
    public static bool ResumeProcess(int processId, out int errorCode)
        => InvokeOnProcess(processId, ProcessSuspendResume, NtResumeProcess, out errorCode);

    private static bool InvokeOnProcess(int processId, uint access, Func<IntPtr, int> call, out int errorCode)
    {
        errorCode = 0;
        if (!TryOpenProcess(access, processId, out var handle, out errorCode)) return false;

        try
        {
            var status = call(handle);
            if (status == 0) return true;
            errorCode = status;
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            errorCode = -1;
            return false;
        }
        finally
        {
            CloseHandleSafe(handle);
        }
    }

    /// <summary>Sets the CPU priority class. <paramref name="priorityClass"/> is a Win32 *_PRIORITY_CLASS value.</summary>
    public static bool TrySetPriorityClass(int processId, uint priorityClass, out int errorCode)
    {
        errorCode = 0;
        if (!TryOpenProcess(ProcessSetInformation, processId, out var handle, out errorCode)) return false;

        try
        {
            if (SetPriorityClass(handle, priorityClass)) return true;
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }
        finally
        {
            CloseHandleSafe(handle);
        }
    }

    /// <summary>Reads the CPU priority class. Returns 0 when the process is not accessible.</summary>
    public static uint GetPriorityClassValue(int processId, out int errorCode)
    {
        errorCode = 0;
        if (!TryOpenProcess(ProcessQueryLimitedInformation, processId, out var handle, out errorCode))
        {
            if (!TryOpenProcess(ProcessQueryInformation, processId, out handle, out errorCode)) return 0;
        }

        try
        {
            var value = GetPriorityClass(handle);
            if (value == 0) errorCode = Marshal.GetLastWin32Error();
            return value;
        }
        finally
        {
            CloseHandleSafe(handle);
        }
    }

    /// <summary>Reads the IO priority (0-4) of a process, or <paramref name="fallback"/> when unavailable.</summary>
    public static int GetIoPriority(int processId, int fallback = IoPriorityNormal)
    {
        if (!TryOpenProcess(ProcessQueryInformation | ProcessQueryLimitedInformation, processId, out var handle, out _))
            return fallback;
        if (handle == IntPtr.Zero && !TryOpenProcess(ProcessQueryLimitedInformation, processId, out handle, out _))
            return fallback;

        try
        {
            var value = fallback;
            return NtQueryInformationProcess(handle, ProcessIoPriorityClass, ref value, sizeof(int), out _) >= 0
                ? value
                : fallback;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return fallback;
        }
        finally
        {
            CloseHandleSafe(handle);
        }
    }

    /// <summary>Sets the IO priority (0-4) of a process. Requires PROCESS_SET_INFORMATION.</summary>
    public static bool TrySetIoPriority(int processId, int ioPriority, out int errorCode)
    {
        errorCode = 0;
        if (!TryOpenProcess(ProcessSetInformation, processId, out var handle, out errorCode)) return false;

        try
        {
            var value = ioPriority;
            var status = NtSetInformationProcess(handle, ProcessIoPriorityClass, ref value, sizeof(int));
            if (status >= 0) return true;
            errorCode = status;
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            errorCode = -1;
            return false;
        }
        finally
        {
            CloseHandleSafe(handle);
        }
    }

    /// <summary>Resolves the full executable path of a process without throwing on access denied.</summary>
    public static bool TryQueryProcessPath(int processId, out string path, out int errorCode)
    {
        path = string.Empty;
        errorCode = 0;
        if (!TryOpenProcess(ProcessQueryLimitedInformation, processId, out var handle, out errorCode)) return false;

        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size))
            {
                errorCode = Marshal.GetLastWin32Error();
                return false;
            }

            path = new string(buffer, 0, Math.Clamp(size, 0, buffer.Length));
            return !string.IsNullOrWhiteSpace(path);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            errorCode = -1;
            return false;
        }
        finally
        {
            CloseHandleSafe(handle);
        }
    }

    // ================================================================ memory

    /// <summary>Reads physical memory counters through GlobalMemoryStatusEx.</summary>
    public static MemoryStatus GetMemoryStatus()
    {
        var buffer = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        try
        {
            if (!GlobalMemoryStatusEx(ref buffer))
                return new MemoryStatus(0, 0, 0, 0, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new MemoryStatus(0, 0, 0, 0, 0);
        }

        return new MemoryStatus(
            buffer.MemoryLoad,
            buffer.TotalPhys,
            buffer.AvailPhys,
            buffer.TotalPageFile,
            buffer.AvailPageFile);
    }

    /// <summary>
    /// Purges the standby memory list (requires SeProfileSingleProcessPrivilege) and reclaims
    /// low-priority pages. Returns false when the kernel rejects the request.
    /// </summary>
    public static bool TryPurgeStandbyList(out long availableBeforeBytes, out long availableAfterBytes)
    {
        availableBeforeBytes = (long)GetMemoryStatus().AvailablePhysicalBytes;
        availableAfterBytes = availableBeforeBytes;

        try
        {
            var purge = MemoryPurgeStandbyList;
            var status = NtSetSystemInformation(SystemMemoryListInformation, ref purge, sizeof(int));
            var reclaim = MemoryReclaimLowPriority;
            NtSetSystemInformation(SystemMemoryListInformation, ref reclaim, sizeof(int));
            availableAfterBytes = (long)GetMemoryStatus().AvailablePhysicalBytes;
            return status >= 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            availableAfterBytes = (long)GetMemoryStatus().AvailablePhysicalBytes;
            return false;
        }
    }

    /// <summary>Enables a named token privilege for the current process. Never throws.</summary>
    public static bool TryEnablePrivilege(string privilegeName, out int errorCode)
    {
        errorCode = 0;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            {
                errorCode = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                if (!LookupPrivilegeValueW(null, privilegeName, out var luid))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    return false;
                }

                var state = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                };

                // Win32 does not clear the thread's last error on success, so a stale value from
                // an earlier call would make the ERROR_NOT_ALL_ASSIGNED test below unreliable.
                Marshal.SetLastPInvokeError(0);
                if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    return false;
                }

                // AdjustTokenPrivileges reports success even when the privilege is not held;
                // ERROR_NOT_ALL_ASSIGNED (1300) reveals that case.
                errorCode = Marshal.GetLastWin32Error();
                return errorCode != ErrorNotAllAssigned;
            }
            finally
            {
                CloseHandleSafe(token);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            errorCode = -1;
            return false;
        }
    }

    /// <summary>Flushes the DNS resolver cache; returns the raw dnsapi result (0 on unavailable).</summary>
    public static uint FlushDnsCache()
    {
        try { return DnsFlushResolverCache(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    // ================================================================ version

    /// <summary>Real Windows version through RtlGetVersion (GetVersionEx lies on Win10+ manifests).</summary>
    public static bool TryGetWindowsVersion(out WindowsVersion version)
    {
        version = default;
        try
        {
            var info = new OsVersionInfoExW
            {
                Size = (uint)Marshal.SizeOf<OsVersionInfoExW>(),
                ServicePack = string.Empty
            };

            if (RtlGetVersion(ref info) != 0) return false;
            version = new WindowsVersion((int)info.MajorVersion, (int)info.MinorVersion, (int)info.BuildNumber, (int)info.PlatformId);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Win32 message for an error code so activity log entries stay readable.</summary>
    public static string DescribeError(int errorCode) =>
        errorCode switch
        {
            ErrorAccessDenied => "access denied",
            ErrorInvalidParameter => "invalid parameter",
            ErrorNotFound => "not found",
            ErrorNotAllAssigned => "privilege not held",
            _ when errorCode < 0 => "native call unavailable",
            _ when errorCode == 0 => "success",
            _ => new Win32Exception(errorCode).Message
        };

    private static void CloseHandleSafe(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
        try { CloseHandle(handle); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing sane to do: the handle simply leaks for the process lifetime.
        }
    }
}
