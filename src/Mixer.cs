using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ClassicMixer
{
    internal class DeviceInfo
    {
        public string Id;
        public string FriendlyName;
        public override string ToString() { return FriendlyName ?? Id; }
    }

    /// <summary>One render endpoint (e.g. Speakers) plus its application sessions.</summary>
    internal class AudioDevice : IDisposable
    {
        public readonly string Id;
        public readonly string ShortName;      // "Speakers"
        public readonly string FriendlyName;   // "Speakers (Realtek(R) Audio)"
        public readonly string IconPath;

        private IMMDevice device;
        private IAudioEndpointVolume endpoint;
        private IAudioMeterInformation meter;
        private IAudioSessionManager2 sessions;
        private Guid ctx = Guid.Empty;

        private readonly Dictionary<string, AppGroup> groups = new Dictionary<string, AppGroup>();
        public readonly List<AppGroup> Groups = new List<AppGroup>();

        public AudioDevice(IMMDevice dev)
        {
            device = dev;
            string id; dev.GetId(out id); Id = id;
            FriendlyName = AudioConst.ReadStringProperty(dev, AudioConst.PKEY_Device_FriendlyName) ?? "Audio device";
            ShortName = AudioConst.ReadStringProperty(dev, AudioConst.PKEY_Device_DeviceDesc) ?? FriendlyName;
            IconPath = AudioConst.ReadStringProperty(dev, AudioConst.PKEY_DeviceClass_IconPath);

            object o;
            Guid iid = AudioConst.IID_IAudioEndpointVolume;
            if (dev.Activate(ref iid, AudioConst.CLSCTX_ALL, IntPtr.Zero, out o) == 0) endpoint = o as IAudioEndpointVolume;
            iid = AudioConst.IID_IAudioMeterInformation;
            if (dev.Activate(ref iid, AudioConst.CLSCTX_ALL, IntPtr.Zero, out o) == 0) meter = o as IAudioMeterInformation;
            iid = AudioConst.IID_IAudioSessionManager2;
            if (dev.Activate(ref iid, AudioConst.CLSCTX_ALL, IntPtr.Zero, out o) == 0) sessions = o as IAudioSessionManager2;
        }

        public float Volume
        {
            get { float v = 0; if (endpoint != null) endpoint.GetMasterVolumeLevelScalar(out v); return v; }
            set { if (endpoint != null) endpoint.SetMasterVolumeLevelScalar(Clamp(value), ref ctx); }
        }

        public bool Muted
        {
            get { bool m = false; if (endpoint != null) endpoint.GetMute(out m); return m; }
            set { if (endpoint != null) endpoint.SetMute(value, ref ctx); }
        }

        public float Peak
        {
            get { float p = 0; if (meter != null) meter.GetPeakValue(out p); return p; }
        }

        /// <summary>Re-reads the session list. Returns true if the set of applications changed.</summary>
        public bool RefreshSessions()
        {
            if (sessions == null) return false;
            IAudioSessionEnumerator en;
            if (sessions.GetSessionEnumerator(out en) != 0 || en == null) return false;

            var seen = new HashSet<string>();
            var fresh = new Dictionary<string, List<IAudioSessionControl2>>();
            try
            {
                int count; en.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 s;
                    if (en.GetSession(i, out s) != 0 || s == null) continue;
                    int state; s.GetState(out state);
                    if (state == AudioConst.SessionStateExpired) continue;

                    bool isSystem = s.IsSystemSoundsSession() == 0;
                    uint pid; s.GetProcessId(out pid);
                    string exe = null;
                    if (!isSystem)
                    {
                        exe = ProcessInfo.GetImagePath(pid);
                        if (exe == null && !ProcessInfo.IsAlive(pid)) continue; // stale session
                    }
                    string key = isSystem ? "#system" : (exe != null ? exe.ToLowerInvariant() : "pid:" + pid);

                    List<IAudioSessionControl2> list;
                    if (!fresh.TryGetValue(key, out list)) { list = new List<IAudioSessionControl2>(); fresh[key] = list; }
                    list.Add(s);

                    if (!groups.ContainsKey(key))
                        groups[key] = AppGroup.Create(key, s, isSystem, pid, exe);
                    seen.Add(key);
                }
            }
            finally { Marshal.ReleaseComObject(en); }

            bool changed = false;
            foreach (var key in new List<string>(groups.Keys))
            {
                if (!seen.Contains(key)) { groups[key].Dispose(); groups.Remove(key); changed = true; }
            }
            foreach (var kv in fresh)
                if (groups[kv.Key].SetSessions(kv.Value)) changed = true;

            // Ordering: System Sounds first, then by name (stable and predictable).
            var ordered = new List<AppGroup>(groups.Values);
            ordered.Sort(delegate(AppGroup a, AppGroup b)
            {
                if (a.IsSystem != b.IsSystem) return a.IsSystem ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            if (ordered.Count != Groups.Count) changed = true;
            else for (int i = 0; i < ordered.Count; i++) if (ordered[i] != Groups[i]) { changed = true; break; }
            Groups.Clear(); Groups.AddRange(ordered);
            return changed;
        }

        public Bitmap GetIcon(int size)
        {
            var bmp = IconLoader.FromResourcePath(IconPath, size);
            return bmp ?? IconLoader.Fallback(size, false);
        }

        private static float Clamp(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        public void Dispose()
        {
            foreach (var g in groups.Values) g.Dispose();
            groups.Clear(); Groups.Clear();
            Release(ref endpoint); Release(ref meter); Release(ref sessions); Release(ref device);
        }

        private static void Release<T>(ref T o) where T : class
        {
            if (o != null) { try { Marshal.ReleaseComObject(o); } catch { } o = null; }
        }

        // ---- static helpers ----

        public static IMMDeviceEnumerator CreateEnumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        public static List<DeviceInfo> ListRenderDevices(IMMDeviceEnumerator en)
        {
            var result = new List<DeviceInfo>();
            IMMDeviceCollection col;
            if (en.EnumAudioEndpoints(EDataFlow.eRender, AudioConst.DEVICE_STATE_ACTIVE, out col) != 0 || col == null) return result;
            try
            {
                int n; col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (col.Item(i, out d) != 0 || d == null) continue;
                    string id; d.GetId(out id);
                    result.Add(new DeviceInfo
                    {
                        Id = id,
                        FriendlyName = AudioConst.ReadStringProperty(d, AudioConst.PKEY_Device_FriendlyName) ?? id
                    });
                    Marshal.ReleaseComObject(d);
                }
            }
            finally { Marshal.ReleaseComObject(col); }
            return result;
        }

        public static string GetDefaultId(IMMDeviceEnumerator en)
        {
            IMMDevice d;
            if (en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out d) != 0 || d == null) return null;
            string id; d.GetId(out id);
            Marshal.ReleaseComObject(d);
            return id;
        }

        public static AudioDevice Open(IMMDeviceEnumerator en, string id)
        {
            IMMDevice d = null;
            if (id != null) en.GetDevice(id, out d);
            if (d == null) en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out d);
            return d == null ? null : new AudioDevice(d);
        }
    }

    /// <summary>All sessions belonging to one application, shown as one slider.</summary>
    internal class AppGroup : IDisposable
    {
        public string Key;
        public string Name;
        public bool IsSystem;
        public string ExePath;
        public string IconPath;
        private readonly List<ISimpleAudioVolume> volumes = new List<ISimpleAudioVolume>();
        private readonly List<IAudioMeterInformation> meters = new List<IAudioMeterInformation>();
        private Guid ctx = Guid.Empty;

        public static AppGroup Create(string key, IAudioSessionControl2 s, bool isSystem, uint pid, string exe)
        {
            var g = new AppGroup { Key = key, IsSystem = isSystem, ExePath = exe };
            string display = null, icon = null;
            s.GetDisplayName(out display);
            s.GetIconPath(out icon);
            g.IconPath = icon;

            display = ResolveIndirect(display);
            if (isSystem)
                g.Name = string.IsNullOrEmpty(display) ? "System Sounds" : display;
            else if (!string.IsNullOrEmpty(display))
                g.Name = display;
            else
                g.Name = ProcessInfo.GetFriendlyName(exe, pid);
            return g;
        }

        /// <summary>Returns true if the number of sessions changed.</summary>
        public bool SetSessions(List<IAudioSessionControl2> sessions)
        {
            bool changed = sessions.Count != volumes.Count;
            volumes.Clear(); meters.Clear();
            foreach (var s in sessions)
            {
                var v = s as ISimpleAudioVolume;
                if (v != null) volumes.Add(v);
                var m = s as IAudioMeterInformation;
                if (m != null) meters.Add(m);
            }
            return changed;
        }

        public float Volume
        {
            get { float v = 0; if (volumes.Count > 0) volumes[0].GetMasterVolume(out v); return v; }
            set
            {
                float c = value < 0 ? 0 : (value > 1 ? 1 : value);
                foreach (var v in volumes) v.SetMasterVolume(c, ref ctx);
            }
        }

        public bool Muted
        {
            get { bool m = false; if (volumes.Count > 0) volumes[0].GetMute(out m); return m; }
            set { foreach (var v in volumes) v.SetMute(value, ref ctx); }
        }

        public float Peak
        {
            get
            {
                float max = 0;
                foreach (var m in meters) { float p; if (m.GetPeakValue(out p) == 0 && p > max) max = p; }
                return max;
            }
        }

        public Bitmap GetIcon(int size)
        {
            Bitmap bmp = null;
            if (!string.IsNullOrEmpty(IconPath)) bmp = IconLoader.FromResourcePath(IconPath, size);
            if (bmp == null && ExePath != null) bmp = IconLoader.FromFile(ExePath, 0, size);
            return bmp ?? IconLoader.Fallback(size, IsSystem);
        }

        public void Dispose() { volumes.Clear(); meters.Clear(); }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string source, StringBuilder outBuf, int cch, IntPtr reserved);

        internal static string ResolveIndirect(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '@') return s;
            var sb = new StringBuilder(512);
            try
            {
                if (SHLoadIndirectString(Environment.ExpandEnvironmentVariables(s), sb, sb.Capacity, IntPtr.Zero) == 0)
                    return sb.ToString();
            }
            catch { }
            return null;
        }
    }

    internal static class ProcessInfo
    {
        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        private static extern bool GetExitCodeProcess(IntPtr h, out int code);

        public static string GetImagePath(uint pid)
        {
            if (pid == 0) return null;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public static bool IsAlive(uint pid)
        {
            if (pid == 0) return false;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return true; // access denied (elevated / other user) - assume alive
            try { int code; return GetExitCodeProcess(h, out code) && code == 259; } // STILL_ACTIVE
            finally { CloseHandle(h); }
        }

        public static string GetFriendlyName(string exe, uint pid)
        {
            if (exe != null)
            {
                try
                {
                    var fvi = FileVersionInfo.GetVersionInfo(exe);
                    if (!string.IsNullOrWhiteSpace(fvi.FileDescription)) return fvi.FileDescription.Trim();
                    if (!string.IsNullOrWhiteSpace(fvi.ProductName)) return fvi.ProductName.Trim();
                }
                catch { }
                return Path.GetFileNameWithoutExtension(exe);
            }
            try { return Process.GetProcessById((int)pid).ProcessName; } catch { }
            return "Process " + pid;
        }
    }

    internal static class IconLoader
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr large, out IntPtr small, uint sizes);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr h);

        /// <summary>Parses "@%SystemRoot%\System32\AudioSrv.Dll,-203" style paths.</summary>
        public static Bitmap FromResourcePath(string path, int size)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Trim().TrimStart('@');
            int index = 0;
            int comma = path.LastIndexOf(',');
            if (comma > 0)
            {
                int.TryParse(path.Substring(comma + 1).Trim(), out index);
                path = path.Substring(0, comma);
            }
            return FromFile(Environment.ExpandEnvironmentVariables(path.Trim('"')), index, size);
        }

        public static Bitmap FromFile(string file, int index, int size)
        {
            try
            {
                IntPtr large, small;
                uint sizes = (uint)((16 << 16) | (size & 0xFFFF));
                if (SHDefExtractIcon(file, index, 0, out large, out small, sizes) != 0) return null;
                if (small != IntPtr.Zero) DestroyIcon(small);
                if (large == IntPtr.Zero) return null;
                try { using (var ic = Icon.FromHandle(large)) return ic.ToBitmap(); }
                finally { DestroyIcon(large); }
            }
            catch { return null; }
        }

        /// <summary>Drawn fallback: a simple speaker (device/system) or app window glyph.</summary>
        public static Bitmap Fallback(int size, bool speaker)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float u = size / 32f;
                using (var b = new SolidBrush(Color.FromArgb(0, 103, 192)))
                {
                    if (speaker)
                    {
                        g.FillPolygon(b, new[] {
                            new PointF(4*u,12*u), new PointF(10*u,12*u), new PointF(17*u,5*u),
                            new PointF(17*u,27*u), new PointF(10*u,20*u), new PointF(4*u,20*u) });
                        using (var p = new Pen(b, 2.4f * u))
                        {
                            g.DrawArc(p, 13 * u, 10 * u, 12 * u, 12 * u, -50, 100);
                            g.DrawArc(p, 9 * u, 5 * u, 22 * u, 22 * u, -50, 100);
                        }
                    }
                    else
                    {
                        g.FillRectangle(b, 3 * u, 5 * u, 26 * u, 22 * u);
                        g.FillRectangle(Brushes.White, 5 * u, 10 * u, 22 * u, 15 * u);
                    }
                }
            }
            return bmp;
        }
    }
}
