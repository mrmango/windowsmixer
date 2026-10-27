using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClassicMixer
{
    internal class Theme
    {
        public bool Dark;
        public Color Back, Text, SubText, GroupBorder, Separator, Wedge, Track, Tick,
                     ThumbTop, ThumbBottom, ThumbBorder, ThumbFocusTop, ThumbFocusBottom, ThumbFocusBorder,
                     MeterLow, MeterHigh, MeterMuted, ButtonHover, Link;

        public static Theme Light()
        {
            return new Theme
            {
                Dark = false,
                Back = Color.White, Text = Color.FromArgb(30, 30, 30), SubText = Color.FromArgb(100, 100, 100),
                GroupBorder = Color.FromArgb(213, 223, 229), Separator = Color.FromArgb(225, 230, 235),
                Wedge = Color.FromArgb(226, 232, 238), Track = Color.FromArgb(204, 210, 216), Tick = Color.FromArgb(200, 206, 212),
                ThumbTop = Color.FromArgb(252, 252, 252), ThumbBottom = Color.FromArgb(222, 222, 222), ThumbBorder = Color.FromArgb(128, 128, 128),
                ThumbFocusTop = Color.FromArgb(234, 250, 255), ThumbFocusBottom = Color.FromArgb(120, 205, 245), ThumbFocusBorder = Color.FromArgb(40, 120, 175),
                MeterLow = Color.FromArgb(70, 200, 70), MeterHigh = Color.FromArgb(150, 230, 90), MeterMuted = Color.FromArgb(185, 190, 195),
                ButtonHover = Color.FromArgb(229, 241, 251), Link = Color.FromArgb(0, 102, 204)
            };
        }

        public static Theme DarkTheme()
        {
            return new Theme
            {
                Dark = true,
                Back = Color.FromArgb(32, 32, 32), Text = Color.FromArgb(240, 240, 240), SubText = Color.FromArgb(170, 170, 170),
                GroupBorder = Color.FromArgb(70, 70, 70), Separator = Color.FromArgb(58, 58, 58),
                Wedge = Color.FromArgb(52, 54, 58), Track = Color.FromArgb(85, 88, 92), Tick = Color.FromArgb(80, 82, 86),
                ThumbTop = Color.FromArgb(120, 120, 120), ThumbBottom = Color.FromArgb(78, 78, 78), ThumbBorder = Color.FromArgb(160, 160, 160),
                ThumbFocusTop = Color.FromArgb(150, 215, 255), ThumbFocusBottom = Color.FromArgb(40, 140, 220), ThumbFocusBorder = Color.FromArgb(150, 210, 255),
                MeterLow = Color.FromArgb(50, 170, 60), MeterHigh = Color.FromArgb(120, 210, 80), MeterMuted = Color.FromArgb(90, 92, 96),
                ButtonHover = Color.FromArgb(55, 55, 55), Link = Color.FromArgb(110, 180, 255)
            };
        }

        public static bool SystemUsesDarkApps()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }
    }

    /// <summary>Vertical volume slider in the style of the Windows 7 mixer: grey wedge,
    /// live green level meter inside the wedge, and an arrow-shaped thumb.</summary>
    internal class VolumeSlider : Control
    {
        private float value;
        private float peak, shownPeak;
        private bool dragging, hover;
        private int dragOffset;
        public bool Muted;
        public Theme Theme = Theme.Light();
        public float UiScale = 1f;
        public event EventHandler ValueChanged;
        private readonly ToolTip tip = new ToolTip();

        public VolumeSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable |
                     ControlStyles.SupportsTransparentBackColor, true);
            TabStop = true;
        }

        public bool IsDragging { get { return dragging; } }

        public float Value
        {
            get { return value; }
            set { float v = Math.Max(0f, Math.Min(1f, value)); if (Math.Abs(v - this.value) > 0.0005f) { this.value = v; Invalidate(); } }
        }

        public float Peak
        {
            set
            {
                peak = Math.Max(0f, Math.Min(1f, value));
                // fast attack, gentle decay - like the original
                shownPeak = peak > shownPeak ? peak : Math.Max(peak, shownPeak - 0.04f);
                Invalidate(MeterRect());
            }
        }

        private int S(float v) { return (int)Math.Round(v * UiScale); }
        private int TrackTop { get { return S(10); } }
        private int TrackBottom { get { return Height - S(10); } }
        private int TrackX { get { return Width / 2 - S(9); } }
        private int ThumbH { get { return S(15); } }
        private int ThumbW { get { return S(26); } }

        private int ValueToY(float v) { return TrackBottom - (int)Math.Round(v * (TrackBottom - TrackTop)); }
        private float YToValue(int y) { return (TrackBottom - y) / (float)(TrackBottom - TrackTop); }

        private Rectangle ThumbRect()
        {
            int y = ValueToY(value);
            return new Rectangle(TrackX - S(4), y - ThumbH / 2, ThumbW, ThumbH);
        }

        private Rectangle MeterRect()
        {
            return new Rectangle(TrackX, TrackTop - 1, S(26), TrackBottom - TrackTop + 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Back);

            int top = TrackTop, bottom = TrackBottom, x = TrackX;
            int wedgeL = x + S(5), wedgeTopR = x + S(5) + S(18), wedgeBotR = x + S(5) + S(3);
            var wedge = new[] { new Point(wedgeL, top), new Point(wedgeTopR, top), new Point(wedgeBotR, bottom), new Point(wedgeL, bottom) };
            using (var b = new SolidBrush(Theme.Wedge)) g.FillPolygon(b, wedge);

            // Level meter clipped to the wedge
            if (shownPeak > 0.005f)
            {
                int my = ValueToY(shownPeak);
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(wedge);
                    var oldClip = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    var r = new Rectangle(wedgeL, my, wedgeTopR - wedgeL + 1, bottom - my + 1);
                    if (Muted)
                        using (var b = new SolidBrush(Theme.MeterMuted)) g.FillRectangle(b, r);
                    else
                        using (var b = new LinearGradientBrush(new Rectangle(wedgeL, top, 10, bottom - top + 1), Theme.MeterHigh, Theme.MeterLow, LinearGradientMode.Vertical))
                            g.FillRectangle(b, r);
                    g.Clip = oldClip;
                }
            }

            // Track
            using (var b = new SolidBrush(Theme.Track)) g.FillRectangle(b, x, top - S(2), S(3), bottom - top + S(4));

            // Ticks (0 / 25 / 50 / 75 / 100 %)
            using (var p = new Pen(Theme.Tick, 1))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int ty = ValueToY(i / 4f);
                    int len = (i % 2 == 0) ? S(14) : S(7);
                    g.DrawLine(p, wedgeTopR + S(3), ty, wedgeTopR + S(3) + len, ty);
                }
            }

            // Thumb (arrow pointing right)
            var t = ThumbRect();
            bool active = Focused || dragging || hover;
            using (var path = new GraphicsPath())
            {
                int tip = t.Right, bodyR = t.Right - S(7), r = S(2);
                path.AddLine(t.Left + r, t.Top, bodyR, t.Top);
                path.AddLine(bodyR, t.Top, tip, t.Top + t.Height / 2);
                path.AddLine(tip, t.Top + t.Height / 2, bodyR, t.Bottom);
                path.AddLine(bodyR, t.Bottom, t.Left + r, t.Bottom);
                path.AddArc(t.Left, t.Bottom - 2 * r, 2 * r, 2 * r, 90, 90);
                path.AddArc(t.Left, t.Top, 2 * r, 2 * r, 180, 90);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new Rectangle(t.X, t.Y - 1, t.Width, t.Height + 2),
                           active ? Theme.ThumbFocusTop : Theme.ThumbTop, active ? Theme.ThumbFocusBottom : Theme.ThumbBottom, LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                using (var p = new Pen(active ? Theme.ThumbFocusBorder : Theme.ThumbBorder, 1))
                    g.DrawPath(p, path);
            }

            if (Focused && ShowFocusCues)
            {
                var fr = new Rectangle(1, 1, Width - 3, Height - 3);
                using (var p = new Pen(Theme.SubText) { DashStyle = DashStyle.Dot }) { g.SmoothingMode = SmoothingMode.None; g.DrawRectangle(p, fr); }
            }
        }

        private void SetFromUser(float v)
        {
            float old = value;
            Value = v;
            if (Math.Abs(old - value) > 0.0001f && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            tip.Show(Percent + "%", this, Width - S(4), ValueToY(value) - S(10), 900);
        }

        public int Percent { get { return (int)Math.Round(value * 100); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left) return;
            var t = ThumbRect();
            t.Inflate(S(4), S(3));
            if (t.Contains(e.Location)) dragOffset = e.Y - ValueToY(value);
            else { dragOffset = 0; SetFromUser(YToValue(e.Y)); }
            dragging = true;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromUser(YToValue(e.Y - dragOffset));
            bool h = ThumbRect().Contains(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false; Capture = false; Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            SetFromUser(value + (e.Delta > 0 ? 0.02f : -0.02f));
            var hme = e as HandledMouseEventArgs;
            if (hme != null) hme.Handled = true;
        }

        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true;
            }
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up: case Keys.Right: SetFromUser(value + 0.01f); break;
                case Keys.Down: case Keys.Left: SetFromUser(value - 0.01f); break;
                case Keys.PageUp: SetFromUser(value + 0.10f); break;
                case Keys.PageDown: SetFromUser(value - 0.10f); break;
                case Keys.Home: SetFromUser(1f); break;
                case Keys.End: SetFromUser(0f); break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Blue speaker button; shows a red "no" sign when muted.</summary>
    internal class MuteButton : Control
    {
        private bool muted, hover, down;
        public Theme Theme = Theme.Light();

        public MuteButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public bool Muted { get { return muted; } set { if (muted != value) { muted = value; Invalidate(); } } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hover || down || Focused)
            {
                using (var b = new SolidBrush(Theme.ButtonHover))
                using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 4))
                    g.FillPath(b, path);
            }
            float s = Math.Min(Width, Height) / 32f;
            float ox = (Width - 32 * s) / 2f, oy = (Height - 32 * s) / 2f;
            Func<float, float, PointF> P = (x, y) => new PointF(ox + x * s, oy + y * s);

            var blue = Theme.Dark ? Color.FromArgb(90, 170, 255) : Color.FromArgb(0, 92, 200);
            using (var b = new SolidBrush(blue))
                g.FillPolygon(b, new[] { P(6, 12.5f), P(11, 12.5f), P(17, 7), P(17, 25), P(11, 19.5f), P(6, 19.5f) });
            using (var hl = new SolidBrush(Color.FromArgb(Theme.Dark ? 70 : 110, 255, 255, 255)))
                g.FillPolygon(hl, new[] { P(7, 13.5f), P(11, 13.5f), P(16, 9), P(16, 13), P(7, 16) });

            if (!muted)
            {
                using (var p = new Pen(blue, 1.8f * s))
                {
                    g.DrawArc(p, ox + 14 * s, oy + 12 * s, 8 * s, 8 * s, -55, 110);
                    g.DrawArc(p, ox + 13 * s, oy + 8.5f * s, 15 * s, 15 * s, -55, 110);
                }
            }
            else
            {
                var red = Color.FromArgb(220, 40, 40);
                var c = P(23.5f, 19.5f); float r = 6.2f * s;
                using (var b = new SolidBrush(Theme.Back)) g.FillEllipse(b, c.X - r - s, c.Y - r - s, 2 * r + 2 * s, 2 * r + 2 * s);
                using (var p = new Pen(red, 2.1f * s))
                {
                    g.DrawEllipse(p, c.X - r, c.Y - r, 2 * r, 2 * r);
                    float d = r * 0.7071f;
                    g.DrawLine(p, c.X - d, c.Y - d, c.X + d, c.Y + d);
                }
            }
        }

        internal static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return (k & Keys.KeyCode) == Keys.Space || base.IsInputKey(k); }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyUp(e);
        }
    }

    /// <summary>One column: icon, name, slider, percentage and mute button.</summary>
    internal class ChannelColumn : Control
    {
        public readonly VolumeSlider Slider = new VolumeSlider();
        public readonly MuteButton Mute = new MuteButton();
        private Bitmap icon;
        private string title;
        private Theme theme = Theme.Light();
        private readonly float scale;
        private readonly ToolTip tip = new ToolTip();
        public bool ShowSeparator;

        public static int ColumnWidth(float scale) { return (int)(118 * scale); }
        public static int ColumnHeight(float scale) { return (int)(388 * scale); }

        public ChannelColumn(float scale)
        {
            this.scale = scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(ColumnWidth(scale), ColumnHeight(scale));
            Slider.UiScale = scale;
            Slider.Bounds = new Rectangle((Width - S(56)) / 2, S(118), S(56), S(206));
            Mute.Bounds = new Rectangle((Width - S(34)) / 2, S(348), S(34), S(30));
            Controls.Add(Slider);
            Controls.Add(Mute);
        }

        private int S(float v) { return (int)Math.Round(v * scale); }

        public void Setup(string name, Bitmap image, string tooltip)
        {
            title = name;
            if (icon != null) icon.Dispose();
            icon = image;
            tip.SetToolTip(this, tooltip);
            tip.SetToolTip(Mute, "Mute " + name);
            Mute.AccessibleName = "Mute " + name;
            Slider.AccessibleName = name + " volume";
            Invalidate();
        }

        public string Title { get { return title; } }

        public void ApplyTheme(Theme t)
        {
            theme = t; Slider.Theme = t; Mute.Theme = t; BackColor = t.Back;
            Slider.Invalidate(); Mute.Invalidate(); Invalidate();
        }

        public void SetPercentInvalidate() { Invalidate(new Rectangle(0, S(326), Width, S(20))); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(theme.Back);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int isz = S(32);
            if (icon != null) g.DrawImage(icon, new Rectangle((Width - isz) / 2, S(10), isz, isz));

            var nameRect = new Rectangle(S(4), S(52), Width - S(8), S(58));
            TextRenderer.DrawText(g, title ?? "", Font, nameRect, theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var pr = new Rectangle(0, S(326), Width, S(20));
            string pct = Slider.Muted ? "Muted" : Slider.Percent + "%";
            TextRenderer.DrawText(g, pct, Font, pr, theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            if (ShowSeparator)
                using (var p = new Pen(theme.Separator)) g.DrawLine(p, Width - 1, S(14), Width - 1, Height - S(10));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tip.Dispose(); if (icon != null) icon.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Etched group frame with a caption, like the "Device" / "Applications" boxes.</summary>
    internal class GroupFrame : Panel
    {
        public string Caption = "";
        public Theme Theme = Theme.Light();
        public float UiScale = 1f;

        public GroupFrame()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var sz = TextRenderer.MeasureText(Caption, Font);
            int capY = sz.Height / 2;
            var r = new Rectangle(0, capY, Width - 1, Height - capY - 1);
            using (var p = new Pen(Theme.GroupBorder))
            using (var path = MuteButton.Rounded(r, (int)(5 * UiScale)))
                g.DrawPath(p, path);
            var capRect = new Rectangle((int)(12 * UiScale), 0, sz.Width + (int)(4 * UiScale), sz.Height);
            using (var b = new SolidBrush(Theme.Back)) g.FillRectangle(b, capRect);
            TextRenderer.DrawText(g, Caption, Font, capRect, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }
    }
}
