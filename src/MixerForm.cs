using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClassicMixer
{
    internal class MixerForm : Form
    {
        private readonly float scale;
        private readonly IMMDeviceEnumerator enumerator;
        private AudioDevice device;
        private string chosenDeviceId; // null = follow the Windows default device

        private readonly GroupFrame deviceFrame = new GroupFrame { Caption = "Device" };
        private readonly GroupFrame appsFrame = new GroupFrame { Caption = "Applications" };
        private readonly Panel appsScroller = new Panel();
        private readonly ComboBox deviceCombo = new ComboBox();
        private ChannelColumn deviceColumn;
        private readonly List<ChannelColumn> appColumns = new List<ChannelColumn>();
        private readonly Label emptyLabel = new Label();
        private readonly CheckBox keepOpen = new CheckBox();
        private readonly LinkLabel soundSettings = new LinkLabel();
        private readonly LinkLabel classicPanel = new LinkLabel();
        private readonly LinkLabel optionsLink = new LinkLabel();
        private readonly Timer meterTimer = new Timer { Interval = 40 };
        private readonly Timer sessionTimer = new Timer { Interval = 1500 };
        private Theme theme = Theme.Light();
        private bool suppressCombo;

        public DateTime LastAutoHide = DateTime.MinValue;
        public ContextMenuStrip OptionsMenu;
        public event EventHandler PinnedChanged;

        public MixerForm(float scale, IMMDeviceEnumerator enumerator, string preferredDeviceId, bool pinned)
        {
            this.scale = scale;
            this.enumerator = enumerator;
            this.chosenDeviceId = string.IsNullOrEmpty(preferredDeviceId) ? null : preferredDeviceId;

            Text = "Volume Mixer";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            try { Icon = Program.AppIcon; } catch { }

            deviceFrame.UiScale = appsFrame.UiScale = scale;
            deviceFrame.Font = appsFrame.Font = new Font("Segoe UI", 10.5f);
            Controls.Add(deviceFrame);
            Controls.Add(appsFrame);

            deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            deviceCombo.FlatStyle = FlatStyle.Flat;
            deviceCombo.DropDownWidth = S(320);
            deviceCombo.SelectedIndexChanged += DeviceCombo_Changed;
            deviceFrame.Controls.Add(deviceCombo);

            appsScroller.AutoScroll = true;
            appsFrame.Controls.Add(appsScroller);
            emptyLabel.Text = "No applications are using this device.";
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            emptyLabel.Visible = false;
            appsScroller.Controls.Add(emptyLabel);

            keepOpen.Text = "Keep open";
            keepOpen.AutoSize = true;
            keepOpen.Checked = pinned;
            keepOpen.CheckedChanged += delegate { ApplyPinned(); if (PinnedChanged != null) PinnedChanged(this, EventArgs.Empty); };
            Controls.Add(keepOpen);

            SetupLink(soundSettings, "Sound settings", delegate { Program.Launch("ms-settings:sound"); });
            SetupLink(classicPanel, "Sound control panel", delegate { Program.Launch("control.exe", "mmsys.cpl"); });
            SetupLink(optionsLink, "Options ▾", delegate
            {
                if (OptionsMenu != null) OptionsMenu.Show(optionsLink, new Point(0, optionsLink.Height));
            });

            meterTimer.Tick += delegate { UpdateMeters(); };
            sessionTimer.Tick += delegate { RefreshAll(false); };

            ApplyPinned();
            BindDevice();
            ApplyTheme();
        }

        public bool Pinned { get { return keepOpen.Checked; } }
        public string ChosenDeviceId { get { return chosenDeviceId; } }
        private int S(float v) { return (int)Math.Round(v * scale); }

        private void SetupLink(LinkLabel l, string text, Action onClick)
        {
            l.Text = text; l.AutoSize = true;
            l.LinkBehavior = LinkBehavior.HoverUnderline;
            l.LinkClicked += delegate { onClick(); };
            Controls.Add(l);
        }

        private bool switchingMode;
        private void ApplyPinned()
        {
            switchingMode = true;
            try { ApplyPinnedCore(); } finally { switchingMode = false; }
        }

        private void ApplyPinnedCore()
        {
            // Popup mode floats above other windows and hides when you click away.
            // "Keep open" turns it into a normal window that appears in Alt+Tab.
            TopMost = !keepOpen.Checked;
            ShowInTaskbar = keepOpen.Checked;
        }

        // ------------------------------------------------------------ device binding

        private void BindDevice()
        {
            if (device != null) { device.Dispose(); device = null; }
            try { device = AudioDevice.Open(enumerator, chosenDeviceId); }
            catch { device = null; }
            if (device == null && chosenDeviceId != null)
            {
                chosenDeviceId = null;
                try { device = AudioDevice.Open(enumerator, null); } catch { device = null; }
            }

            if (deviceColumn == null)
            {
                deviceColumn = new ChannelColumn(scale);
                deviceColumn.Slider.ValueChanged += delegate { if (device != null) { device.Volume = deviceColumn.Slider.Value; deviceColumn.SetPercentInvalidate(); } };
                deviceColumn.Mute.Click += delegate { if (device != null) { device.Muted = !device.Muted; UpdateValues(); } };
                deviceFrame.Controls.Add(deviceColumn);
            }

            if (device != null)
            {
                deviceColumn.Setup(device.ShortName, device.GetIcon(S(32)), device.FriendlyName);
                Text = "Volume Mixer - " + device.FriendlyName;
            }
            else
            {
                deviceColumn.Setup("No device", IconLoader.Fallback(S(32), true), "No audio output device found");
                Text = "Volume Mixer";
            }
            FillDeviceCombo();
            RebuildApps();
        }

        private void FillDeviceCombo()
        {
            suppressCombo = true;
            try
            {
                deviceCombo.Items.Clear();
                deviceCombo.Items.Add(new DeviceInfo { Id = null, FriendlyName = "Default device" });
                foreach (var d in AudioDevice.ListRenderDevices(enumerator)) deviceCombo.Items.Add(d);
                int sel = 0;
                for (int i = 1; i < deviceCombo.Items.Count; i++)
                    if (chosenDeviceId != null && ((DeviceInfo)deviceCombo.Items[i]).Id == chosenDeviceId) sel = i;
                deviceCombo.SelectedIndex = sel;
            }
            catch { }
            finally { suppressCombo = false; }
        }

        private void DeviceCombo_Changed(object sender, EventArgs e)
        {
            if (suppressCombo) return;
            var d = deviceCombo.SelectedItem as DeviceInfo;
            chosenDeviceId = d == null ? null : d.Id;
            BindDevice();
            ApplyTheme();
            if (PinnedChanged != null) PinnedChanged(this, EventArgs.Empty); // persist device choice too
        }

        // ------------------------------------------------------------ app columns

        private void RebuildApps()
        {
            if (device != null) { try { device.RefreshSessions(); } catch { } }
            var groups = device != null ? device.Groups : new List<AppGroup>();

            appsScroller.SuspendLayout();
            appsScroller.AutoScrollPosition = new Point(0, 0);
            foreach (var c in appColumns) { appsScroller.Controls.Remove(c); c.Dispose(); }
            appColumns.Clear();

            int x = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                var grp = groups[i];
                var col = new ChannelColumn(scale);
                col.Tag = grp;
                string tipText = grp.ExePath != null ? grp.Name + "\n" + grp.ExePath : grp.Name;
                col.Setup(grp.Name, grp.GetIcon(S(32)), tipText);
                col.ShowSeparator = i < groups.Count - 1;
                col.Location = new Point(x, 0);
                x += col.Width;
                var g = grp; var cc = col;
                col.Slider.ValueChanged += delegate { try { g.Volume = cc.Slider.Value; } catch { } cc.SetPercentInvalidate(); };
                col.Mute.Click += delegate { try { g.Muted = !g.Muted; } catch { } UpdateValues(); };
                col.ApplyTheme(theme);
                appColumns.Add(col);
                appsScroller.Controls.Add(col);
            }
            emptyLabel.Visible = groups.Count == 0;
            appsScroller.ResumeLayout();
            DoLayout();
            UpdateValues();
        }

        private void DoLayout()
        {
            int pad = S(12), colW = ChannelColumn.ColumnWidth(scale), colH = ChannelColumn.ColumnHeight(scale);
            int capH = TextRenderer.MeasureText("Ag", deviceFrame.Font).Height;
            int comboH = deviceCombo.PreferredHeight;
            int innerTop = capH + S(6);

            // Device frame
            int devFrameW = colW + S(20);
            deviceCombo.SetBounds(S(10), innerTop, devFrameW - S(20), comboH);
            deviceColumn.Location = new Point(S(10), innerTop + comboH + S(2));
            int frameH = innerTop + comboH + S(2) + colH + S(8);
            deviceFrame.SetBounds(pad, pad, devFrameW, frameH);

            // Applications frame: grow with the number of apps, scroll past ~90% of the screen
            var wa = Screen.FromControl(this).WorkingArea;
            int maxAppsW = Math.Max(colW * 2, (int)(wa.Width * 0.9) - devFrameW - pad * 3 - S(40));
            int contentW = Math.Max(colW * 2, appColumns.Count * colW);
            int visibleW = Math.Min(contentW, maxAppsW);
            bool needScroll = contentW > visibleW;
            int scrollH = needScroll ? SystemInformation.HorizontalScrollBarHeight : 0;
            int scrollerH = colH + scrollH;
            appsScroller.SetBounds(S(10), innerTop + comboH + S(2), visibleW, scrollerH);
            emptyLabel.SetBounds(0, 0, visibleW, colH);
            int appsFrameW = visibleW + S(20);
            int appsFrameH = Math.Max(frameH, appsScroller.Bottom + S(8));
            appsFrame.SetBounds(deviceFrame.Right + pad, pad, appsFrameW, appsFrameH);
            deviceFrame.Height = appsFrameH;

            // Footer
            int footY = appsFrame.Bottom + S(10);
            keepOpen.Location = new Point(pad + S(2), footY);
            int right = appsFrame.Right;
            optionsLink.Location = new Point(right - optionsLink.Width, footY + S(2));
            classicPanel.Location = new Point(optionsLink.Left - S(18) - classicPanel.Width, footY + S(2));
            soundSettings.Location = new Point(classicPanel.Left - S(18) - soundSettings.Width, footY + S(2));

            ClientSize = new Size(appsFrame.Right + pad, footY + Math.Max(keepOpen.Height, optionsLink.Height) + S(12));
            if (Visible)
            {
                // Keep the window fully on screen when it grows/shrinks as apps come and go
                var area = Screen.FromControl(this).WorkingArea;
                int nx = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
                int ny = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
                if (nx != Left || ny != Top) Location = new Point(nx, ny);
            }
            Invalidate(true);
        }

        // ------------------------------------------------------------ live updates

        private void RefreshAll(bool force)
        {
            try
            {
                // Follow the Windows default device if the user hasn't picked one
                if (chosenDeviceId == null && device != null)
                {
                    string def = AudioDevice.GetDefaultId(enumerator);
                    if (def != null && def != device.Id) { BindDevice(); ApplyTheme(); return; }
                }
                if (device == null) { BindDevice(); ApplyTheme(); return; }
                bool changed = device.RefreshSessions();
                if (changed || force) RebuildApps();
                if (deviceCombo.Items.Count - 1 != AudioDevice.ListRenderDevices(enumerator).Count) FillDeviceCombo();
            }
            catch (COMException)
            {
                // Device removed or invalidated - rebind to whatever is default now
                chosenDeviceId = null;
                BindDevice(); ApplyTheme();
            }
        }

        private void UpdateValues()
        {
            if (device == null) return;
            try
            {
                if (!deviceColumn.Slider.IsDragging) deviceColumn.Slider.Value = device.Volume;
                bool dm = device.Muted;
                deviceColumn.Mute.Muted = dm; deviceColumn.Slider.Muted = dm;
                deviceColumn.SetPercentInvalidate();
                foreach (var c in appColumns)
                {
                    var g = (AppGroup)c.Tag;
                    if (!c.Slider.IsDragging) c.Slider.Value = g.Volume;
                    bool m = g.Muted;
                    c.Mute.Muted = m; c.Slider.Muted = m || dm;
                    c.SetPercentInvalidate();
                }
            }
            catch (COMException) { }
        }

        private int tick;
        private void UpdateMeters()
        {
            if (device == null) return;
            try
            {
                deviceColumn.Slider.Peak = device.Peak;
                foreach (var c in appColumns) c.Slider.Peak = ((AppGroup)c.Tag).Peak;
                // Pick up changes made elsewhere (keyboard volume keys, other apps) ~4x a second
                if (++tick % 6 == 0) UpdateValues();
            }
            catch (COMException) { }
        }

        // ------------------------------------------------------------ theme

        public void ApplyTheme()
        {
            theme = Theme.SystemUsesDarkApps() ? Theme.DarkTheme() : Theme.Light();
            BackColor = theme.Back; ForeColor = theme.Text;
            deviceFrame.Theme = appsFrame.Theme = theme;
            appsScroller.BackColor = theme.Back;
            emptyLabel.ForeColor = theme.SubText; emptyLabel.BackColor = theme.Back;
            keepOpen.ForeColor = theme.Text; keepOpen.BackColor = theme.Back;
            foreach (var l in new[] { soundSettings, classicPanel, optionsLink })
            {
                l.LinkColor = theme.Link; l.ActiveLinkColor = theme.Link; l.VisitedLinkColor = theme.Link; l.BackColor = theme.Back;
            }
            deviceCombo.BackColor = theme.Dark ? Color.FromArgb(45, 45, 45) : Color.White;
            deviceCombo.ForeColor = theme.Text;
            if (deviceColumn != null) deviceColumn.ApplyTheme(theme);
            foreach (var c in appColumns) c.ApplyTheme(theme);
            if (IsHandleCreated) SetDarkTitleBar(theme.Dark);
            Invalidate(true);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private void SetDarkTitleBar(bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref v, 4);
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetDarkTitleBar(theme.Dark);
        }

        // ------------------------------------------------------------ show / hide behaviour

        /// <summary>Show the mixer; in popup mode it appears next to the tray / cursor.</summary>
        public void ShowMixer(Point? savedLocation)
        {
            ApplyTheme();
            RefreshAll(true);
            if (Pinned && savedLocation.HasValue && IsOnScreen(savedLocation.Value)) Location = savedLocation.Value;
            else if (!Visible || !Pinned) PositionNearTray();
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            if (deviceColumn != null) deviceColumn.Slider.Focus();
            meterTimer.Start();
            sessionTimer.Start();
        }

        private static bool IsOnScreen(Point p)
        {
            foreach (var s in Screen.AllScreens) if (s.WorkingArea.Contains(new Point(p.X + 40, p.Y + 10))) return true;
            return false;
        }

        private void PositionNearTray()
        {
            var cur = Cursor.Position;
            var screen = Screen.FromPoint(cur);
            var wa = screen.WorkingArea;
            var b = screen.Bounds;
            int m = S(12);
            int x, y;
            // Taskbar edge = whichever side the working area is inset from
            if (wa.Bottom < b.Bottom) { x = cur.X - Width / 2; y = wa.Bottom - Height - m; }
            else if (wa.Top > b.Top) { x = cur.X - Width / 2; y = wa.Top + m; }
            else if (wa.Left > b.Left) { x = wa.Left + m; y = cur.Y - Height / 2; }
            else if (wa.Right < b.Right) { x = wa.Right - Width - m; y = cur.Y - Height / 2; }
            else { x = wa.Right - Width - m; y = wa.Bottom - Height - m; } // auto-hide taskbar
            x = Math.Max(wa.Left + m, Math.Min(x, wa.Right - Width - m));
            y = Math.Max(wa.Top + m, Math.Min(y, wa.Bottom - Height - m));
            Location = new Point(x, y);
        }

        public void HideMixer()
        {
            meterTimer.Stop();
            sessionTimer.Stop();
            Hide();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!Pinned && !switchingMode && Visible && (OptionsMenu == null || !OptionsMenu.Visible))
            {
                LastAutoHide = DateTime.Now;
                HideMixer();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideMixer(); return; }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { HideMixer(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        // Master volume / mute for the tray icon (works while the window is hidden)
        public float MasterVolume { get { try { return device != null ? device.Volume : 0; } catch { return 0; } } }
        public bool MasterMuted { get { try { return device != null && device.Muted; } catch { return false; } } }
        public void ToggleMasterMute() { try { if (device != null) device.Muted = !device.Muted; UpdateValues(); } catch { } }
        public string DeviceName { get { return device != null ? device.FriendlyName : "No audio device"; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                meterTimer.Dispose(); sessionTimer.Dispose();
                if (device != null) device.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
