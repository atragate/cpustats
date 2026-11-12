using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class SetupWindow : Form
{
    private static readonly string[] Conflicts =
        { "CPUSTATS", "CPUSTATS-v1.0.0-windows-x64", "L280Studio", "PC Monitor" };
    private readonly string _package;
    private readonly Label _title = new Label();
    private readonly Label _status = new Label();
    private readonly SetupProgress _progress = new SetupProgress();
    private readonly CheckBox _details = new CheckBox { Text = "Show details", AutoSize = true };
    private readonly SetupLog _log = new SetupLog { Visible = false };
    private readonly Button _primary = new Button();
    private readonly Button _secondary = new Button();
    private readonly Button _close = new Button { Text = "Close" };
    private readonly Panel _header = new Panel { Dock = DockStyle.Top, Height = 75 };
    private readonly Label _brand = new Label();
    private readonly Label _release = new Label();
    private readonly FlowLayoutPanel _buttons = new FlowLayoutPanel
    {
        Dock = DockStyle.Bottom, Height = 53, Padding = new Padding(15, 8, 17, 6),
        FlowDirection = FlowDirection.RightToLeft
    };
    private Action _primaryAction;
    private Action _secondaryAction;
    private bool _busy, _filesInUse, _finished;
    private volatile bool _cancel;
    private int _total, _position, _increment, _direction = 1;
    public int ExitCode { get; private set; }

    public SetupWindow(string package, string version)
    {
        _package = package;
        ExitCode = 1602;
        Text = "CPUSTATS Setup";
        Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(570, 440);
        MinimumSize = new Size(540, 410);
        Font = new Font("Segoe UI", 10);
        var mark = new PictureBox { Location = new Point(22, 14), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Image = Icon.ToBitmap() };
        _brand.Text = "CPUSTATS Setup";
        _brand.Location = new Point(84, 12);
        _brand.AutoSize = true;
        _brand.Font = new Font(Font, FontStyle.Bold);
        _release.Text = "Version " + new Version(version).ToString(3);
        _release.Location = new Point(84, 36);
        _release.AutoSize = true;
        _header.Controls.AddRange(new Control[] { mark, _brand, _release });

        _title.SetBounds(24, 94, 510, 31);
        _title.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        _status.SetBounds(25, 133, 518, 58);
        _progress.SetBounds(25, 197, 520, 12);
        _details.SetBounds(25, 219, 160, 25);
        _log.SetBounds(25, 250, 520, 126);
        foreach (Control control in new Control[] { _title, _status, _progress, _log })
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _log.Anchor |= AnchorStyles.Bottom;
        _details.CheckedChanged += delegate { _log.Visible = _details.Checked; };

        foreach (Button button in new[] { _close, _primary, _secondary })
        {
            button.AutoSize = true;
            button.MinimumSize = new Size(85, 30);
            button.FlatStyle = FlatStyle.Flat;
            _buttons.Controls.Add(button);
        }
        _primary.Click += delegate { if (_primaryAction != null) _primaryAction(); };
        _secondary.Click += delegate { if (_secondaryAction != null) _secondaryAction(); };
        _close.Click += delegate { Close(); };
        CancelButton = _close;
        Controls.AddRange(new Control[] { _header, _title, _status, _progress, _details, _log, _buttons });
        ApplyTheme();
        ShowWelcome();
    }

    private void ApplyTheme()
    {
        var theme = new SetupTheme();
        BackColor = theme.Window;
        ForeColor = theme.Text;
        _header.BackColor = theme.Header;
        _brand.ForeColor = theme.HeaderText;
        _release.ForeColor = theme.HeaderAccent;
        _buttons.BackColor = theme.Panel;
        _details.FlatStyle = FlatStyle.Flat;
        _details.FlatAppearance.CheckedBackColor = theme.Track;
        foreach (Button button in new[] { _close, _primary, _secondary })
        {
            bool primary = button == _primary;
            button.BackColor = primary ? theme.Accent : theme.Window;
            button.ForeColor = primary ? theme.OnAccent : theme.Text;
            button.FlatAppearance.BorderColor = primary ? theme.Accent : theme.Track;
        }
        _progress.ApplyTheme(theme);
        _log.ApplyTheme(theme);
        if (IsHandleCreated) theme.ApplyTitleBar(Handle);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        base.OnHandleDestroyed(e);
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            try { BeginInvoke((Action)ApplyTheme); }
            catch (InvalidOperationException) { }
        }
    }

    private void ShowWelcome()
    {
        _progress.Visible = false;
        _details.Visible = false;
        bool installed = WindowsInstaller.Installed;
        bool upgrade = !installed && WindowsInstaller.HasRelatedProduct;
        _title.Text = installed ? "Maintain CPUSTATS" : upgrade ? "Update CPUSTATS" : "Install CPUSTATS";
        _status.Text = installed
            ? "Repair the installed app or remove it from this computer."
            : "Setup installs CPUSTATS and the sensor driver needed for supported hardware readings.";
        SetActions(installed ? "Repair" : upgrade ? "Update" : "Install",
            () => CheckApps(installed ? "REINSTALL=ALL REINSTALLMODE=vomus" : "", false),
            installed ? "Uninstall" : null, () => CheckApps("REMOVE=ALL", true));
    }

    private static IEnumerable<Process> RunningApps()
    {
        return Conflicts.SelectMany(Process.GetProcessesByName);
    }

    private void CheckApps(string operation, bool remove)
    {
        var names = new List<string>();
        foreach (Process process in RunningApps())
            using (process) names.Add(process.ProcessName);

        if (names.Count == 0)
        {
            Run(operation, remove);
            return;
        }

        _title.Text = "Close running apps";
        _status.Text = string.Join(", ", names.Distinct()) + " must be closed before Setup can continue.";
        SetActions("Retry", () => CheckApps(operation, remove), "End listed apps", () =>
        {
            try
            {
                foreach (Process process in RunningApps())
                    using (process)
                    {
                        if (!names.Contains(process.ProcessName) || process.HasExited) continue;
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                CheckApps(operation, remove);
            }
            catch (Exception error) { _status.Text = "Close the listed apps and retry. " + error.Message; }
        });
    }

    private async void Run(string operation, bool remove)
    {
        _cancel = _filesInUse = false;
        _busy = true;
        _title.Text = remove ? "Removing CPUSTATS" : "Applying changes";
        _status.Text = "Preparing Windows Installer…";
        _progress.Value = 0;
        _progress.Marquee = true;
        _progress.Visible = _details.Visible = true;
        _log.Clear();
        AppendLog("Starting Windows Installer.");
        _close.Text = "Cancel";
        SetActions(null, null, null, null);
        int result;
        try { result = await Task.Run(() => WindowsInstaller.Run(_package, operation, OnInstallerMessage)); }
        catch (Exception error) { AppendLog(error.ToString()); result = 1603; }
        _busy = false;
        _close.Enabled = true;
        _close.Text = "Close";
        _progress.Marquee = false;
        ExitCode = result;

        if (result == 0 || result == 3010)
        {
            _finished = true;
            _progress.Value = 100;
            _title.Text = remove ? "CPUSTATS removed" : "Setup complete";
            _status.Text = result == 3010 ? "Restart Windows to finish applying the changes."
                : remove ? "CPUSTATS has been removed from this computer." : "CPUSTATS is ready to open.";
            AppendLog(_status.Text);
            SetActions(remove ? null : "Finish and start", () =>
            {
                try { WindowsInstaller.LaunchDesktop(); Close(); }
                catch (Exception error) { _status.Text = error.Message; }
            }, null, null);
        }
        else
        {
            _title.Text = _filesInUse ? "Files are still in use" : _cancel ? "Setup canceled" : "Setup could not finish";
            _status.Text = _filesInUse ? "Close the apps using CPUSTATS files, then select Retry."
                : _cancel ? "You can retry the operation or close Setup."
                : "Windows Installer returned code " + result + ". See the details below.";
            _details.Checked = true;
            SetActions("Retry", () => CheckApps(operation, remove), null, null);
        }
    }

    private int OnInstallerMessage(IntPtr context, uint messageType, string message)
    {
        uint kind = messageType >> 24;
        if (kind == 5 || kind == 25)
        {
            _filesInUse = true;
            BeginInvoke((Action)(() => AppendLog("Files in use: " + message)));
            return 2;
        }
        if (_cancel) return 2;
        if (kind == 10)
        {
            UpdateProgress(message);
            return 1;
        }
        if ((kind <= 3 || kind == 8 || kind == 9) && !string.IsNullOrWhiteSpace(message))
        {
            if (kind == 9 && _increment != 0)
            {
                _position += _direction * _increment;
                ShowProgress();
            }
            string text = kind == 8 ? FormatAction(message) : message.Trim();
            if (text.Length == 0) return 1;
            BeginInvoke((Action)(() =>
            {
                AppendLog(text);
                if (kind == 8) _status.Text = text;
            }));
        }
        if (kind == 6 || kind == 7) return 2;
        return 1;
    }

    private void UpdateProgress(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        var fields = Regex.Matches(message, @"\d+:\s*(-?\d+)").Cast<Match>()
            .Select(match => int.Parse(match.Groups[1].Value)).ToArray();
        if (fields.Length < 2) return;
        switch (fields[0])
        {
            case 0:
                _total = fields[1];
                _direction = fields.Length > 2 && fields[2] == 1 ? -1 : 1;
                _position = _direction == 1 ? 0 : _total;
                _increment = 0;
                break;
            case 1: _increment = fields.Length > 2 && fields[2] == 1 ? fields[1] : 0; break;
            case 2: _position += _direction * fields[1]; break;
            case 3: _total += fields[1]; break;
        }
        ShowProgress();
    }

    private void ShowProgress()
    {
        if (_total <= 0) return;
        int value = (int)Math.Max(0, Math.Min(100, 100L * _position / _total));
        BeginInvoke((Action)(() =>
        {
            _progress.Marquee = false;
            _progress.Value = value;
        }));
    }

    private void AppendLog(string text)
    {
        _log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + "  " + text.Trim());
    }

    private static string FormatAction(string message)
    {
        var action = Regex.Match(message.Trim(), @"^Action\s+\d{1,2}:\d{2}:\d{2}:\s*(?<name>[^.]+)\.\s*(?<label>.*)$");
        if (!action.Success) return message.Trim();
        string name = action.Groups["name"].Value;
        string label = Regex.Replace(action.Groups["label"].Value, @"\s+\p{Lu}[\w ]*:\s*\[\d+\]", "").Trim();
        if (label.Length != 0) return label;
        if (name.StartsWith("Wix4", StringComparison.Ordinal)) return "";
        return Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " ");
    }

    private void SetActions(string primary, Action primaryAction, string secondary, Action secondaryAction)
    {
        _primaryAction = primaryAction;
        _secondaryAction = secondaryAction;
        _primary.Text = primary;
        _primary.Visible = primary != null;
        _secondary.Text = secondary;
        _secondary.Visible = secondary != null;
        AcceptButton = primary == null ? null : _primary;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            _cancel = true;
            _close.Enabled = false;
            _status.Text = "Canceling and rolling back changes…";
        }
        else if (!_finished && ExitCode == 0) ExitCode = 1602;
        base.OnFormClosing(e);
    }
}
