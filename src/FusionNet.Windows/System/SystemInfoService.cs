using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Utilities;
using System.Security;
using FusionNet.Windows.Native;
using Microsoft.Win32;

namespace FusionNet.Windows.System;

/// <summary>
/// Reads real machine facts (Windows build, CPU, RAM, system drive, uptime) without ever
/// throwing. A missing registry value or an unavailable counter simply yields a safe fallback.
/// </summary>
public sealed class SystemInfoService : ISystemInfoService
{
    private readonly WindowsVersionInfo _windows;
    private readonly string _cpuName;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly string _appVersion;

    public SystemInfoService()
    {
        try
        {
            AppPaths.EnsureDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Data folders are created again on first save; reading facts still works.
            Debug.WriteLine("Data directory unavailable: " + ex.Message);
        }

        _windows = ReadWindowsVersion();
        _cpuName = ReadCpuName();
        _startedAtUtc = ReadProcessStartUtc();
        _appVersion = ReadAppVersion();
    }

    public bool IsElevated => ElevationService.CheckIsElevated();

    public string MachineName => Environment.MachineName;

    public string WindowsVersion => _windows.Display;

    public string AppVersion => _appVersion;

    public string DataDirectory => AppPaths.DataDirectory;

    public long TotalPhysicalMemoryBytes => (long)Win32Native.GetMemoryStatus().TotalPhysicalBytes;

    public long AvailablePhysicalMemoryBytes => (long)Win32Native.GetMemoryStatus().AvailablePhysicalBytes;

    public long TotalSystemDriveFreeBytes
    {
        get
        {
            try
            {
                var windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var root = Path.GetPathRoot(windowsFolder);
                if (string.IsNullOrWhiteSpace(root)) root = "C:\\";

                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return 0;
            }
        }
    }

    public string CpuName => _cpuName;

    public int LogicalProcessorCount => Math.Max(1, Environment.ProcessorCount);

    public bool IsWindows11 => _windows.Build >= 22000;

    public DateTimeOffset ProcessStartedAtUtc => _startedAtUtc;

    public TimeSpan Uptime
    {
        get
        {
            try
            {
                var milliseconds = Environment.TickCount64;
                return TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            }
            catch (Exception ex) when (ex is OverflowException or InvalidOperationException)
            {
                return TimeSpan.Zero;
            }
        }
    }

    private readonly record struct WindowsVersionInfo(string Display, int Major, int Minor, int Build);

    private static WindowsVersionInfo ReadWindowsVersion()
    {
        int major = 10, minor = 0, build = 0;
        if (Win32Native.TryGetWindowsVersion(out var version))
        {
            major = version.Major;
            minor = version.Minor;
            build = version.Build;
        }
        else
        {
            try
            {
                var fallback = Environment.OSVersion.Version;
                major = fallback.Major;
                minor = fallback.Minor;
                build = fallback.Build;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                build = 0;
            }
        }

        var family = build >= 22000 ? "Windows 11"
            : major >= 10 ? "Windows 10"
            : $"Windows {major}.{minor}";

        var displayVersion = ReadWindowsDisplayVersion();
        var display = string.IsNullOrWhiteSpace(displayVersion)
            ? $"{family} (build {build})"
            : $"{family} {displayVersion} (build {build})";

        return new WindowsVersionInfo(display, major, minor, build);
    }

    private static string? ReadWindowsDisplayVersion()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Default })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(keyPath);
                if (key is null) continue;

                // DisplayVersion is the friendly "23H2"/"24H2" label on Windows 10/11.
                if (key.GetValue("DisplayVersion") is string display && !string.IsNullOrWhiteSpace(display))
                    return display.Trim();

                if (key.GetValue("ReleaseId") is string release && !string.IsNullOrWhiteSpace(release))
                    return release.Trim();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                // Fall through to the next registry view / plain build string.
            }
        }

        return null;
    }

    private static string ReadCpuName()
    {
        const string keyPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("ProcessorNameString") is string name && !string.IsNullOrWhiteSpace(name))
            {
                return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            // Registry access can be restricted by policy; fall back to the environment.
        }

        var identifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        return string.IsNullOrWhiteSpace(identifier) ? "Unknown CPU" : identifier.Trim();
    }

    private static DateTimeOffset ReadProcessStartUtc()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static string ReadAppVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(SystemInfoService).Assembly;

            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            var version = assembly.GetName().Version;
            return version is null ? "3.3.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return "3.3.0";
        }
    }
}


