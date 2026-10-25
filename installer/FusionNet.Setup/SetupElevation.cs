using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace FusionNet.Setup;

internal static class SetupElevation
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static ProcessStartInfo BuildStartInfo(string executable, SetupOptions options)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        start.ArgumentList.Add("/D=" + options.InstallDirectory);
        if (!options.CreateDesktopShortcut) start.ArgumentList.Add("/nodesktop");
        if (!options.LaunchAfterInstall) start.ArgumentList.Add("/nolaunch");
        return start;
    }

    public static bool TryRestart(SetupOptions options, out string? error)
    {
        error = null;
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("تعذر تحديد ملف المثبّت.");
            using var process = Process.Start(BuildStartInfo(executable, options));
            return process is not null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "أُلغي طلب صلاحية المسؤول؛ لم تُعدّل صلاحيات المجلد. يمكنك اختيار مجلد آخر من استعراض.";
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            error = "تعذر إعادة تشغيل المثبّت: " + ex.Message;
            return false;
        }
    }
}
