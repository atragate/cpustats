using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class SetupTheme
{
    public readonly bool Dark;
    public readonly Color Window, Panel, Header, HeaderText, HeaderAccent, Text, Accent, OnAccent, Track, Thumb;

    public SetupTheme()
    {
        if (SystemInformation.HighContrast)
        {
            Window = Panel = Header = SystemColors.Window;
            HeaderText = HeaderAccent = Text = SystemColors.WindowText;
            Accent = Thumb = SystemColors.Highlight;
            OnAccent = SystemColors.HighlightText;
            Track = SystemColors.ControlDark;
            return;
        }

        Dark = object.Equals(Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1), 0);
        Window = ColorOf(Dark ? "#171D20" : "#FAFCFB");
        Panel = ColorOf(Dark ? "#20282C" : "#EDF3F1");
        Header = ColorOf("#171D20");
        HeaderText = ColorOf("#F2F6F4");
        HeaderAccent = ColorOf("#85E6C3");
        Text = ColorOf(Dark ? "#F2F6F4" : "#152522");
        Accent = ColorOf(Dark ? "#85E6C3" : "#0B7056");
        OnAccent = ColorOf(Dark ? "#152522" : "#FFFFFF");
        Track = ColorOf(Dark ? "#39474D" : "#DCE7E5");
        Thumb = ColorOf(Dark ? "#719B90" : "#62877E");
    }

    private static Color ColorOf(string value) { return ColorTranslator.FromHtml(value); }

    public void ApplyTitleBar(IntPtr handle)
    {
        int enabled = Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class SetupProgress : Control
{
    private readonly Timer _animation = new Timer { Interval = 35 };
    private int _phase, _value;
    private bool _marquee;
    private SetupTheme _theme = new SetupTheme();

    public SetupProgress()
    {
        AccessibleName = "Setup progress";
        AccessibleRole = AccessibleRole.ProgressBar;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        _animation.Tick += delegate { _phase = (_phase + 4) % 100; Invalidate(); };
    }

    public bool Marquee
    {
        get { return _marquee; }
        set { _marquee = value; _animation.Enabled = value; Invalidate(); }
    }

    public int Value
    {
        get { return _value; }
        set
        {
            _value = value;
            AccessibleDescription = value + "% complete";
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            Invalidate();
        }
    }

    public void ApplyTheme(SetupTheme theme) { _theme = theme; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var track = new SolidBrush(_theme.Track))
        using (var fill = new SolidBrush(_theme.Accent))
        {
            e.Graphics.FillRectangle(track, ClientRectangle);
            if (_marquee)
            {
                int segment = Width / 4;
                e.Graphics.FillRectangle(fill, (Width + segment) * _phase / 100 - segment, 0, segment, Height);
            }
            else e.Graphics.FillRectangle(fill, 0, 0, Width * _value / 100, Height);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
