using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FusionNet.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            SetupPaths.Log("FATAL unhandled exception: " + e.ExceptionObject);

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            SetupPaths.Log("FATAL: " + ex);
            try
            {
                MessageBox.Show(
                    "تعذّر تشغيل معالج التثبيت:" + Environment.NewLine + Environment.NewLine + ex.Message,
                    "FusionNet Pro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // no UI available; the log has the details
            }
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        // Declaring comctl32 v6 in app.manifest makes visual styles work without a temp activation
        // context. Keep the call best-effort so a locked-down temp folder can never crash setup.
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        }
        catch (Exception ex)
        {
            SetupPaths.Log("EnableVisualStyles failed (continuing): " + ex.Message);
        }

        var options = SetupOptions.Parse(args);

        if (options.ShowHelp)
        {
            MessageBox.Show(SetupOptions.HelpText, "FusionNet Pro Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // QA helper: render the wizard to a PNG and exit (never shown to end users).
        if (options.PreviewPath is { Length: > 0 } previewPath)
        {
            try
            {
                using var form = new SetupForm(options);
                form.Show();
                Application.DoEvents();
                Thread.Sleep(600);
                Application.DoEvents();

                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                var full = Path.GetFullPath(previewPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                bitmap.Save(full, ImageFormat.Png);
                form.Close();
                return 0;
            }
            catch (Exception ex)
            {
                SetupPaths.Log("Preview failed: " + ex);
                return 2;
            }
        }

        if (options.Uninstall)
            return InstallEngine.Uninstall(options);

        if (options.Silent)
            return InstallEngine.InstallSilent(options);

        using var wizard = new SetupForm(options);
        Application.Run(wizard);
        return wizard.ExitCode;
    }
}
