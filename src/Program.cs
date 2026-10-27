using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Classic Volume Mixer")]
[assembly: AssemblyDescription("Windows 7 style per-application volume mixer for the system tray")]
[assembly: AssemblyProduct("Classic Volume Mixer")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ClassicMixer
{
    internal static class Program
    {
        public const string AppName = "Classic Volume Mixer";
        private const string MutexName = "Local\\ClassicVolumeMixer.SingleInstance";
        internal const string ShowEventName = "Local\\ClassicVolumeMixer.Show";

        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        public static Icon AppIcon { get { return LoadIcon("app.ico", 32); } }

        [STAThread]
        private static void Main(string[] args)
        {
            bool startHidden = false;
            foreach (var a in args)
            {
                string s = a.Trim().ToLowerInvariant();
                if (s == "/tray" || s == "-tray" || s == "--tray" || s == "/minimized") startHidden = true;
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Already running: ask the existing copy to show itself
                    try { using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set(); } catch { }
                    return;
                }

                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext(startHidden));
                GC.KeepAlive(mutex);
            }
        }

        public static Icon LoadIcon(string name, int size)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (res.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                        using (var st = asm.GetManifestResourceStream(res)) return new Icon(st, size, size);
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        public static void Launch(string file, string args = null)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { UseShellExecute = true };
                if (args != null) psi.Arguments = args;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open " + file + ":\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>Persisted options in HKCU\Software\ClassicVolumeMixer.</summary>
    internal static class Settings
    {
        private const string KeyPath = @"Software\ClassicVolumeMixer";

        public static object Get(string name, object def)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(KeyPath)) { return k == null ? def : (k.GetValue(name) ?? def); } }
            catch { return def; }
        }

        public static void Set(string name, object value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (value == null) k.DeleteValue(name, false);
                    else if (value is bool) k.SetValue(name, (bool)value ? 1 : 0, RegistryValueKind.DWord);
                    else if (value is int) k.SetValue(name, value, RegistryValueKind.DWord);
                    else k.SetValue(name, value.ToString(), RegistryValueKind.String);
                }
            }
            catch { }
        }

        public static bool GetBool(string name, bool def) { var v = Get(name, def ? 1 : 0); return v is int ? (int)v != 0 : def; }
        public static int GetInt(string name, int def) { var v = Get(name, def); return v is int ? (int)v : def; }
        public static string GetString(string name) { var v = Get(name, null) as string; return string.IsNullOrEmpty(v) ? null : v; }
    }

    /// <summary>"Start with Windows" via HKCU\...\Run (no admin rights needed).</summary>
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ClassicVolumeMixer";
        private static string Command { get { return "\"" + Application.ExecutablePath + "\" /tray"; } }

        public static bool Enabled
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(ValueName) != null; }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue(ValueName, Command, RegistryValueKind.String);
                        else k.DeleteValue(ValueName, false);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not change the startup setting:\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>If the exe was moved, point the startup entry at the new location.</summary>
        public static void RepairPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    var cur = k == null ? null : k.GetValue(ValueName) as string;
                    if (cur != null && !string.Equals(cur, Command, StringComparison.OrdinalIgnoreCase))
                        k.SetValue(ValueName, Command, RegistryValueKind.String);
                }
            }
            catch { }
        }
    }

    /// <summary>Hidden window that receives the global hotkey (Win+Alt+V).</summary>
    internal class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312, MOD_ALT = 0x1, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000, ID = 0xB00B;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int mods, int vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler Pressed;
        public bool Registered { get; private set; }

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Enable(bool on)
        {
            if (Registered) { UnregisterHotKey(Handle, ID); Registered = false; }
            if (on) Registered = RegisterHotKey(Handle, ID, MOD_WIN | MOD_ALT | MOD_NOREPEAT, (int)Keys.V);
            return Registered || !on;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == ID && Pressed != null) Pressed(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        public void Dispose() { Enable(false); DestroyHandle(); }
    }

    internal class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly MixerForm form;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem miOpen, miStartup, miKeepOpen, miHotkey;
        private readonly HotkeyWindow hotkey = new HotkeyWindow();
        private readonly System.Windows.Forms.Timer trayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly Icon iconNormal, iconMuted;
        private readonly EventWaitHandle showEvent;
        private RegisteredWaitHandle showWait;
        private bool lastMuted;
        private Point? pinnedLocation;

        public TrayContext(bool startHidden)
        {
            float scale;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;

            var trayIconSize = SystemInformation.SmallIconSize.Width;
            iconNormal = Program.LoadIcon("app.ico", trayIconSize);
            iconMuted = Program.LoadIcon("muted.ico", trayIconSize);

            int px = Settings.GetInt("WinX", int.MinValue), py = Settings.GetInt("WinY", int.MinValue);
            if (px != int.MinValue && py != int.MinValue) pinnedLocation = new Point(px, py);

            form = new MixerForm(scale, AudioDevice.CreateEnumerator(), Settings.GetString("DeviceId"), Settings.GetBool("Pinned", false));
            form.OptionsMenu = menu;
            form.PinnedChanged += delegate
            {
                Settings.Set("Pinned", form.Pinned);
                Settings.Set("DeviceId", form.ChosenDeviceId ?? "");
                miKeepOpen.Checked = form.Pinned;
            };
            form.LocationChanged += delegate { if (form.Visible && form.Pinned) pinnedLocation = form.Location; };
            form.VisibleChanged += delegate { if (!form.Visible) SaveLocation(); };
            var h = form.Handle; // create the handle so cross-thread BeginInvoke works before first show

            // ---- tray menu
            miOpen = new ToolStripMenuItem("Open Volume Mixer", null, delegate { ShowMixer(); });
            miOpen.Font = new Font(miOpen.Font, FontStyle.Bold);
            miStartup = new ToolStripMenuItem("Start with Windows", null, delegate
            {
                Startup.Enabled = !Startup.Enabled;
                miStartup.Checked = Startup.Enabled;
            });
            miKeepOpen = new ToolStripMenuItem("Keep window open (don't auto-hide)", null, delegate
            {
                // Routed through the form's checkbox so both stay in sync
                var cb = FindKeepOpen();
                if (cb != null) cb.Checked = !cb.Checked;
            });
            miHotkey = new ToolStripMenuItem("Open with Win+Alt+V", null, delegate
            {
                bool want = !miHotkey.Checked;
                if (!hotkey.Enable(want))
                    MessageBox.Show("Win+Alt+V is already in use by another program.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                miHotkey.Checked = hotkey.Registered;
                Settings.Set("Hotkey", want);
            });
            menu.Items.AddRange(new ToolStripItem[]
            {
                miOpen,
                new ToolStripSeparator(),
                miStartup, miKeepOpen, miHotkey,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Windows Sound settings", null, delegate { Program.Launch("ms-settings:sound"); }),
                new ToolStripMenuItem("Windows app volume settings", null, delegate { Program.Launch("ms-settings:apps-volume"); }),
                new ToolStripMenuItem("Sound control panel (classic)", null, delegate { Program.Launch("control.exe", "mmsys.cpl"); }),
                new ToolStripSeparator(),
                new ToolStripMenuItem("About", null, delegate
                {
                    MessageBox.Show(Program.AppName + " 1.0\n\nLeft-click the tray icon: open / close the mixer\nMiddle-click the tray icon: mute / unmute\nWin+Alt+V: open the mixer from anywhere\n\nMouse wheel or arrow keys adjust a focused slider.",
                        Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }),
                new ToolStripMenuItem("Exit", null, delegate { ExitApp(); })
            });
            menu.Opening += delegate
            {
                miStartup.Checked = Startup.Enabled;
                miKeepOpen.Checked = form.Pinned;
                miHotkey.Checked = hotkey.Registered;
            };

            // ---- tray icon
            tray.Icon = iconNormal;
            tray.Text = Program.AppName;
            tray.ContextMenuStrip = menu;
            tray.MouseUp += Tray_MouseUp;
            tray.Visible = true;

            // ---- hotkey
            hotkey.Pressed += delegate { ToggleMixer(); };
            if (Settings.GetBool("Hotkey", true)) hotkey.Enable(true);

            // ---- second-instance "show" signal
            bool createdNew;
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName, out createdNew);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate
            {
                try { form.BeginInvoke(new Action(ShowMixer)); } catch { }
            }, null, -1, false);

            trayTimer.Tick += delegate { UpdateTray(); };
            trayTimer.Start();
            UpdateTray();
            Startup.RepairPath();

            if (!Settings.GetBool("FirstRunDone", false))
            {
                Settings.Set("FirstRunDone", true);
                tray.ShowBalloonTip(8000, Program.AppName,
                    "I'm in the system tray. Click the speaker to open the mixer, or press Win+Alt+V.\n" +
                    "Tip: right-click for 'Start with Windows'. To always show the icon, drag it out of the ^ overflow onto the taskbar.",
                    ToolTipIcon.Info);
            }

            if (!startHidden) ShowMixer();
        }

        private CheckBox FindKeepOpen()
        {
            foreach (Control c in form.Controls) { var cb = c as CheckBox; if (cb != null) return cb; }
            return null;
        }

        private void Tray_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ToggleMixer();
            else if (e.Button == MouseButtons.Middle) { form.ToggleMasterMute(); UpdateTray(); }
        }

        private void ToggleMixer()
        {
            // A tray click first deactivates the popup (which hides it); don't immediately re-open.
            if ((DateTime.Now - form.LastAutoHide).TotalMilliseconds < 400) return;
            if (form.Visible && Form.ActiveForm == form) form.HideMixer();
            else ShowMixer();
        }

        private void ShowMixer() { form.ShowMixer(pinnedLocation); }

        private void UpdateTray()
        {
            int pct = (int)Math.Round(form.MasterVolume * 100);
            bool muted = form.MasterMuted;
            string text = (muted ? "Muted" : "Volume: " + pct + "%") + " - " + form.DeviceName;
            if (text.Length > 63) text = text.Substring(0, 60) + "...";
            if (tray.Text != text) tray.Text = text;
            if (muted != lastMuted) { tray.Icon = muted ? iconMuted : iconNormal; lastMuted = muted; }
        }

        private void SaveLocation()
        {
            if (pinnedLocation.HasValue)
            {
                Settings.Set("WinX", pinnedLocation.Value.X);
                Settings.Set("WinY", pinnedLocation.Value.Y);
            }
        }

        private void ExitApp()
        {
            SaveLocation();
            trayTimer.Stop();
            tray.Visible = false;
            if (showWait != null) showWait.Unregister(null);
            hotkey.Dispose();
            form.Dispose();
            tray.Dispose();
            ExitThread();
        }
    }
}
