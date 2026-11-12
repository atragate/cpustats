using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class SetupLog : UserControl
{
    private const int FirstVisibleLine = 0x00CE;
    private const int LineCount = 0x00BA;
    private const int LineScroll = 0x00B6;

    private readonly LogText _text = new LogText();
    private readonly ScrollTrack _track = new ScrollTrack();
    private int _first, _maximum, _visibleLines;

    public SetupLog()
    {
        AccessibleName = "Installation log";
        BackColor = ColorTranslator.FromHtml("#EDF3F1");
        Padding = new Padding(12, 8, 6, 8);
        _text.BorderStyle = BorderStyle.None;
        _text.ScrollBars = RichTextBoxScrollBars.None;
        _text.ReadOnly = true;
        _text.WordWrap = true;
        _text.DetectUrls = false;
        _text.BackColor = BackColor;
        _text.ForeColor = ColorTranslator.FromHtml("#24433D");
        _text.Font = new Font("Consolas", 9);
        _text.Dock = DockStyle.Fill;
        _text.AccessibleName = AccessibleName;
        _text.Wheel = delta =>
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            ScrollTo(_first - Math.Sign(delta) * (lines < 0 ? _visibleLines : lines));
        };
        _text.VScroll += delegate { RefreshTrack(); };
        _text.SelectionChanged += delegate { RefreshTrack(); };
        _text.SizeChanged += delegate { RefreshTrack(); };
        _track.Dock = DockStyle.Right;
        _track.Width = 14;
        _track.ScrollTo = ScrollTo;
        Controls.Add(_text);
        Controls.Add(_track);
    }

    public void Clear()
    {
        _text.Clear();
        RefreshTrack();
    }

    public void ApplyTheme(SetupTheme theme)
    {
        BackColor = _text.BackColor = theme.Panel;
        _text.ForeColor = theme.Text;
        _track.ApplyTheme(theme);
    }

    public void AppendLine(string line)
    {
        bool atEnd = _first >= _maximum;
        int first = _first;
        _text.AppendText((_text.TextLength == 0 ? "" : Environment.NewLine) + line);
        ScrollTo(atEnd ? int.MaxValue : first);
    }

    private void RefreshTrack()
    {
        if (!_text.IsHandleCreated) return;
        _visibleLines = Math.Max(1, _text.ClientSize.Height / _text.Font.Height);
        _first = (int)SendMessage(_text.Handle, FirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
        _maximum = Math.Max(0, (int)SendMessage(_text.Handle, LineCount, IntPtr.Zero, IntPtr.Zero) - _visibleLines);
        _track.UpdateRange(_first, _maximum, _visibleLines);
    }

    private void ScrollTo(int line)
    {
        RefreshTrack();
        int delta = Math.Max(0, Math.Min(_maximum, line)) - _first;
        if (delta != 0)
            SendMessage(_text.Handle, LineScroll, IntPtr.Zero, (IntPtr)delta);
        RefreshTrack();
    }

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private sealed class LogText : RichTextBox
    {
        public Action<int> Wheel;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Wheel != null) Wheel(e.Delta);
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }
    }

    private sealed class ScrollTrack : Control
    {
        private int _position, _maximum, _page, _dragOffset;
        private bool _hover;
        private SetupTheme _theme = new SetupTheme();
        public Action<int> ScrollTo;

        public ScrollTrack()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            TabStop = false;
        }

        private int ThumbHeight
        {
            get { return Math.Min(Height, Math.Max(16, (int)(Height * ((double)_page / Math.Max(1, _page + _maximum))))); }
        }

        private int ThumbTop
        {
            get { return _maximum == 0 ? 0 : (int)((Height - ThumbHeight) * ((double)_position / _maximum)); }
        }

        public void UpdateRange(int position, int maximum, int page)
        {
            _position = Math.Min(position, maximum);
            _maximum = maximum;
            _page = page;
            Invalidate();
        }

        public void ApplyTheme(SetupTheme theme)
        {
            _theme = theme;
            BackColor = theme.Panel;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_maximum == 0 || Height <= 0) return;
            using (var rail = new SolidBrush(_theme.Track))
            using (var thumb = new SolidBrush(_hover || Capture ? _theme.Accent : _theme.Thumb))
            {
                int x = Math.Max(0, (Width - 5) / 2);
                e.Graphics.FillRectangle(rail, x, 0, 5, Height);
                e.Graphics.FillRectangle(thumb, x, ThumbTop, 5, ThumbHeight);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_maximum == 0 || e.Button != MouseButtons.Left) return;
            int top = ThumbTop;
            _dragOffset = e.Y >= top && e.Y < top + ThumbHeight ? e.Y - top : ThumbHeight / 2;
            Capture = true;
            MoveThumb(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture) MoveThumb(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e) { Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        private void MoveThumb(int y)
        {
            int travel = Height - ThumbHeight;
            if (travel > 0 && ScrollTo != null)
                ScrollTo((int)Math.Round((y - _dragOffset) * ((double)_maximum / travel)));
        }
    }
}
