using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace ParkCapture
{
    internal static class Program
    {
        internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParkCapture");
        internal static bool Demo;
        [STAThread]
        private static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { try { Native.SetProcessDPIAware(); } catch { } }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 1 && args[0] == "--verify-update-feed")
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                    {
                        var update = Updates.Check(timeout.Token, "0.0.0").GetAwaiter().GetResult();
                        if (update == null) throw new InvalidDataException("No published update is available.");
                        string stage = Updates.Prepare(update, timeout.Token, null).GetAwaiter().GetResult();
                        UpdateSupport.UpdatePackage.Extract(Path.Combine(stage, update.Manifest.asset), Path.Combine(stage, "diagnostic"), update.Manifest);
                        File.WriteAllText(args[1], "PASS Public GitHub feed, release signature, ZIP download, SHA-256 and executable versions\r\nCurrent: " + Updates.CurrentVersion + "\r\nLatest: " + update.Manifest.version + "\r\nStage: " + stage);
                    }
                    return 0;
                }
                catch (Exception e) { File.WriteAllText(args[1], "FAIL " + e); return 1; }
            }
            if (args.Length > 1 && args[0] == "--button-checks")
            {
                try { ButtonRenderingChecks.Run(args[1]); File.WriteAllText(Path.Combine(args[1], "button-checks.txt"), "PASS 100/125/150/175/200% enlarged-text rendering; icon/text separation; complete labels; 12 repeated repaint states. These simulate text scaling on the current display, not a change to Windows monitor DPI."); return 0; }
                catch (Exception e) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "button-checks.txt"), "FAIL " + e); return 1; }
            }
            if (args.Length > 0 && args[0] == "--self-test") return SelfTests.Run(args.Length > 1 ? args[1] : "self-test.txt");
            bool preview = args.Length > 1 && args[0] == "--preview";
            Demo = preview || (args.Length > 0 && args[0] == "--demo");
            bool first;
            using (var mutex = new Mutex(true, Demo ? "Local\\ParkCaptureDemo" : "Local\\ParkCapture", out first))
            {
                if (!first) { MessageBox.Show("박주임 캡처가 이미 실행 중입니다. 알림 영역의 아이콘을 눌러 주세요.", "박주임 캡처"); return 0; }
                Directory.CreateDirectory(DataDir);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Report(e.Exception); };
                try { var main = new MainForm(); if (preview) main.PreviewPath = args[1]; Application.Run(main); return 0; }
                catch (Exception e) { Report(e); return 1; }
            }
        }
        internal static void Report(Exception e)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(Path.Combine(DataDir, "error.log"), DateTime.Now + " " + e + Environment.NewLine); } catch { }
            MessageBox.Show(e.Message, "박주임 캡처", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public class Settings
    {
        public string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "박주임 캡처");
        public string Language = "ko";
        public int Hotkey = (int)Keys.D2;
        public uint Modifiers = 6;
        public bool CloseToTray = true;
        public bool OpenAfterCapture = true;
        public bool AutoUpdate = true;
        public bool StartWithWindows = false;
        public string FFmpegPath = "";
        public static Settings Load()
        {
            try { using (var f = File.OpenRead(Path.Combine(Program.DataDir, "settings.xml"))) return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(f); }
            catch { return new Settings(); }
        }
        public void Save() { Storage.WriteXml(Path.Combine(Program.DataDir, "settings.xml"), this); }
    }

    internal static class Words
    {
        internal static bool English;
        internal static string T(string ko, string en) { return English ? en : ko; }
    }

    internal static class Style
    {
        internal static readonly Color Ink = Color.FromArgb(29, 29, 31), Blue = Color.FromArgb(0, 122, 255), Paper = Color.FromArgb(242, 242, 245), Line = Color.FromArgb(229, 229, 234), Red = Color.FromArgb(255, 69, 58);
        internal static readonly Color Sidebar = Color.FromArgb(248, 248, 250), Muted = Color.FromArgb(110, 110, 115), Disabled = Color.FromArgb(174, 174, 181), Hover = Color.FromArgb(236, 236, 240), BlueWash = Color.FromArgb(231, 241, 255);
        internal static Font Font(float size, FontStyle style = FontStyle.Regular) { return new Font("Malgun Gothic", size, style); }
        internal static Button Button(string text, EventHandler click, bool primary = false)
        {
            var b = new SoftButton { Text = text, Tone = primary ? ButtonTone.Primary : ButtonTone.Normal, AccessibleName = text };
            b.Click += click; return b;
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        internal delegate bool EnumProc(IntPtr window, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT rect, int size);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);
        internal static Bitmap ScreenBitmap(Rectangle bounds)
        {
            var b = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            b.SetResolution(96, 96);
            try { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy); return b; }
            catch { b.Dispose(); throw; }
        }
    }
}
