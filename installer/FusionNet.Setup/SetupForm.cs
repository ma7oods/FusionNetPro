using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FusionNet.Setup;

internal sealed class SetupForm : Form
{
    private static readonly Color PageBack = Color.FromArgb(10, 18, 32);
    private static readonly Color HeaderBack = Color.FromArgb(13, 26, 44);
    private static readonly Color FooterBack = Color.FromArgb(13, 26, 44);
    private static readonly Color Accent = Color.FromArgb(56, 203, 255);
    private static readonly Color AccentHover = Color.FromArgb(118, 222, 255);
    private static readonly Color ButtonText = Color.FromArgb(6, 24, 36);
    private static readonly Color TextMain = Color.FromArgb(233, 241, 250);
    private static readonly Color TextMuted = Color.FromArgb(148, 170, 194);
    private static readonly Color BorderColor = Color.FromArgb(38, 56, 82);

    private readonly TextBox _pathBox;
    private readonly Button _browseButton;
    private readonly CheckBox _desktopCheck;
    private readonly CheckBox _launchCheck;
    private readonly ProgressBar _progress;
    private readonly Label _statusLabel;
    private readonly Button _installButton;
    private readonly Button _cancelButton;
    private bool _working;

    public int ExitCode { get; private set; }

    public SetupForm(SetupOptions options)
    {
        Text = "FusionNet Pro 3.3.3 — معالج التثبيت";
        ClientSize = new Size(680, 552);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = PageBack;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 10.5F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = PageBack, Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        Controls.Add(root);

        // ---- header ----
        var header = new Panel { Dock = DockStyle.Fill, BackColor = HeaderBack, Padding = new Padding(30, 22, 30, 16) };
        root.Controls.Add(header, 0, 0);

        var title = new Label
        {
            Text = "FusionNet Pro",
            Font = new Font("Segoe UI", 24F, FontStyle.Bold),
            ForeColor = TextMain,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(30, 14)
        };
        var subtitle = new Label
        {
            Text = "معالج التثبيت — الإصدار 3.3.3  •  ويندوز 10/11 (64-بت)",
            Font = new Font("Segoe UI", 10F),
            ForeColor = TextMuted,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(32, 68)
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        // ---- content ----
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = PageBack,
            Padding = new Padding(30, 20, 30, 8),
            RightToLeft = RightToLeft.Yes
        };
        root.Controls.Add(content, 0, 1);

        var intro = new Label
        {
            Text = "سيتم تثبيت التطبيق على حسابك الحالي دون الحاجة إلى صلاحيات المسؤول.\r\n" +
                   "البرنامج نفسه يطلب الصلاحيات فقط عند تفعيل وضع Fusion أو تعديل إعدادات النظام.",
            Font = new Font("Segoe UI", 9.75F),
            ForeColor = TextMuted,
            AutoSize = false,
            Size = new Size(600, 44),
            Margin = new Padding(0, 0, 0, 12)
        };
        content.Controls.Add(intro);

        var pathCaption = new Label
        {
            Text = "مجلد التثبيت:",
            Font = new Font("Segoe UI", 9.75F, FontStyle.Bold),
            ForeColor = TextMain,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        content.Controls.Add(pathCaption);

        var pathRow = new TableLayoutPanel
        {
            Width = 600,
            Height = 34,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 14),
            BackColor = PageBack
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        content.Controls.Add(pathRow);

        _pathBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(18, 30, 48),
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 10F),
            Margin = new Padding(0, 0, 8, 0),
            Text = InstallEngine.ResolveInstallDirectory(options)
        };
        pathRow.Controls.Add(_pathBox, 0, 0);

        _browseButton = new Button
        {
            Text = "استعراض…",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(24, 38, 60),
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 9.75F),
            Margin = new Padding(0)
        };
        _browseButton.FlatAppearance.BorderColor = BorderColor;
        _browseButton.Click += OnBrowse;
        pathRow.Controls.Add(_browseButton, 1, 0);

        _desktopCheck = new CheckBox
        {
            Text = "إنشاء اختصار على سطح المكتب",
            Checked = options.CreateDesktopShortcut,
            AutoSize = true,
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 9.75F),
            Margin = new Padding(0, 0, 0, 4)
        };
        content.Controls.Add(_desktopCheck);

        _launchCheck = new CheckBox
        {
            Text = "تشغيل FusionNet Pro بعد انتهاء التثبيت",
            Checked = options.LaunchAfterInstall,
            AutoSize = true,
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 9.75F),
            Margin = new Padding(0, 0, 0, 10)
        };
        content.Controls.Add(_launchCheck);

        _statusLabel = new Label
        {
            Text = "جاهز للتثبيت.",
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = TextMuted,
            AutoSize = false,
            Size = new Size(600, 22),
            Margin = new Padding(0, 0, 0, 6)
        };
        content.Controls.Add(_statusLabel);

        _progress = new ProgressBar
        {
            Width = 600,
            Height = 14,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Margin = new Padding(0, 0, 0, 10)
        };
        content.Controls.Add(_progress);

        var hint = new Label
        {
            Text = "لا حاجة لصلاحيات المسؤول أثناء التثبيت. يمكنك إزالة البرنامج لاحقاً من «التطبيقات والميزات» في إعدادات ويندوز.",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(120, 142, 168),
            AutoSize = false,
            Size = new Size(600, 34),
            Margin = new Padding(0)
        };
        content.Controls.Add(hint);

        // ---- footer ----
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = FooterBack };
        root.Controls.Add(footer, 0, 2);

        _installButton = new Button
        {
            Text = "تثبيت الآن",
            Location = new Point(30, 22),
            Size = new Size(176, 42),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = ButtonText,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.FlatAppearance.MouseOverBackColor = AccentHover;
        _installButton.Click += OnInstall;
        footer.Controls.Add(_installButton);

        _cancelButton = new Button
        {
            Text = "إلغاء",
            Location = new Point(218, 22),
            Size = new Size(120, 42),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(24, 38, 60),
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 10F),
            Cursor = Cursors.Hand
        };
        _cancelButton.FlatAppearance.BorderColor = BorderColor;
        _cancelButton.Click += (_, _) => Close();
        footer.Controls.Add(_cancelButton);

        AcceptButton = _installButton;
        CancelButton = _cancelButton;
        FormClosing += OnFormClosing;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        if (_working) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "اختر مجلد تثبيت FusionNet Pro",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = SafeDirectory(_pathBox.Text)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _pathBox.Text = dialog.SelectedPath;
    }

    private void OnInstall(object? sender, EventArgs e)
    {
        if (_working) return;

        string directory;
        try
        {
            directory = SetupPaths.ValidateInstallDirectory(_pathBox.Text);
        }
        catch (Exception ex)
        {
            ShowError("تعذّر استخدام مجلد التثبيت:\r\n\r\n" + ex.Message);
            return;
        }

        var installOptions = new SetupOptions
        {
            Silent = false,
            Uninstall = false,
            CreateDesktopShortcut = _desktopCheck.Checked,
            LaunchAfterInstall = _launchCheck.Checked,
            InstallDirectory = directory
        };

        SetWorking(true);
        _statusLabel.Text = "جارٍ التثبيت…";
        _progress.Value = 0;

        var progress = new Progress<InstallProgress>(item =>
        {
            _progress.Value = Math.Clamp(item.Percent, 0, 100);
            _statusLabel.Text = item.Message;
        });

        var thread = new Thread(() =>
        {
            try
            {
                InstallEngine.RunInstall(installOptions, progress);
                BeginInvoke(new Action(() => FinishSuccess(installOptions, directory)));
            }
            catch (Exception ex)
            {
                SetupPaths.Log("GUI install failed: " + ex);
                BeginInvoke(new Action(() => FinishFailure(ex, installOptions)));
            }
        })
        {
            IsBackground = true,
            Name = "FusionNetProSetup"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void FinishSuccess(SetupOptions options, string directory)
    {
        _working = false;
        _progress.Value = 100;
        _statusLabel.Text = "تم التثبيت بنجاح.";
        MessageBox.Show(
            "تم تثبيت FusionNet Pro بنجاح!" + Environment.NewLine + Environment.NewLine +
            "مجلد التثبيت: " + directory + Environment.NewLine +
            "يمكنك تشغيله من قائمة ابدأ أو من الاختصار على سطح المكتب.",
            "FusionNet Pro",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        if (options.LaunchAfterInstall) InstallEngine.Launch(options);
        ExitCode = 0;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void FinishFailure(Exception ex, SetupOptions options)
    {
        SetWorking(false);
        _progress.Value = 0;
        if (ex is UnauthorizedAccessException && !SetupElevation.IsElevated)
        {
            _statusLabel.Text = "رفض ويندوز الوصول. اختر مجلداً آخر أو أعد المحاولة بصلاحية المسؤول.";
            var answer = MessageBox.Show(this,
                // InstallEngine.StopRunningApp wraps "app runs elevated" in a Win32Exception; show its own reason.
                (ex.InnerException is System.ComponentModel.Win32Exception ? ex.Message : "منع ويندوز الكتابة أثناء التثبيت.") +
                "\r\n\r\nالمجلد: " + options.InstallDirectory +
                "\r\n\r\nهل تريد إعادة فتح المثبّت بصلاحية المسؤول؟ سيظهر طلب موافقة ويندوز، ثم تراجع الخيارات وتضغط تثبيت الآن مجدداً." +
                "\r\nاستخدم حسابك نفسه. لا حاجة لتغيير صلاحيات المجلد. اختر لا للعودة واختيار مجلد آخر من استعراض.",
                "صلاحية التثبيت", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (answer == DialogResult.Yes)
            {
                if (SetupElevation.TryRestart(options, out var error)) { Close(); return; }
                ShowError(error ?? "تعذر بدء المثبّت. يمكنك اختيار مجلد آخر.");
            }
            ExitCode = 1;
            return;
        }
        _statusLabel.Text = "فشل التثبيت. حاول مرة أخرى.";
        ShowError("تعذّر إكمال التثبيت." + Environment.NewLine + Environment.NewLine + ex.Message +
                  Environment.NewLine + Environment.NewLine + "سجل التفاصيل: %LOCALAPPDATA%\\FusionNetPro\\install.log");
        ExitCode = 1;
    }

    private void SetWorking(bool working)
    {
        _working = working;
        _installButton.Enabled = !working;
        _browseButton.Enabled = !working;
        _pathBox.Enabled = !working;
        _desktopCheck.Enabled = !working;
        _launchCheck.Enabled = !working;
        _cancelButton.Enabled = !working;
        _installButton.Text = working ? "جارٍ التثبيت…" : "تثبيت الآن";
        UseWaitCursor = working;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_working) return;
        e.Cancel = true;
        MessageBox.Show(
            "يرجى الانتظار حتى يكتمل التثبيت.",
            "FusionNet Pro",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }

    private void ShowError(string message)
    {
        MessageBox.Show(
            message,
            "FusionNet Pro",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }

    private static string SafeDirectory(string text)
    {
        try
        {
            var path = Path.GetFullPath(text.Trim());
            return Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

