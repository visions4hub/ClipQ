using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClipQ
{
    public class Item
    {
        public string Text { get; set; }
        public bool Pinned { get; set; }
    }

    public class Snippet
    {
        public string Name { get; set; }
        public string Text { get; set; }
        public int Keys { get; set; }
    }

    // Placeholders a snippet can hold; they are filled in at the moment it is pasted.
    static class Vars
    {
        static readonly string[] Days = { "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت" };

        public static string Expand(string text)
        {
            if (text.IndexOf('{') < 0) return text;
            var now = DateTime.Now;
            var inv = System.Globalization.CultureInfo.InvariantCulture; // English numerals
            return text
                .Replace("{date}", now.ToString("d/M/yyyy", inv))
                .Replace("{time}", now.ToString("h:mm", inv) + (now.Hour < 12 ? L.T(" ص", " AM") : L.T(" م", " PM")))
                .Replace("{day}", L.Rtl ? Days[(int)now.DayOfWeek] : now.DayOfWeek.ToString());
        }
    }

    public class Settings
    {
        public int PopupKeys { get; set; }
        public List<Snippet> Snippets { get; set; }

        public static Settings Default()
        {
            var s = new Settings();
            s.PopupKeys = (int)(Keys.Control | Keys.Alt | Keys.V);
            s.Snippets = new List<Snippet>();
            return s;
        }
    }

    // DropQR's palette: light or dark, following the Windows app mode. Refresh() re-reads it before a window opens.
    static class Theme
    {
        public static bool Dark;
        public static Color Bg, Surface, Surface2, Border, Text, Muted, Accent, AccentHover, AccentSoft, OnAccent, Danger;

        static Theme() { Refresh(); }

        public static void Refresh()
        {
            Dark = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    Dark = k != null && Equals(k.GetValue("AppsUseLightTheme"), 0);
            }
            catch { }
            if (Dark)
            {
                Bg = Hex(0x0a0d17); Surface = Hex(0x121626); Surface2 = Hex(0x1a1f33); Border = Hex(0x262c44);
                Text = Hex(0xeef1fb); Muted = Hex(0x9aa3bd); Accent = Hex(0x818cf8); AccentHover = Hex(0xa5b4fc);
                AccentSoft = Hex(0x1e2250); OnAccent = Hex(0x0b1020); Danger = Hex(0xf87171);
            }
            else
            {
                Bg = Hex(0xf6f7fb); Surface = Hex(0xffffff); Surface2 = Hex(0xf1f3f9); Border = Hex(0xe3e6ef);
                Text = Hex(0x0b1020); Muted = Hex(0x5b6478); Accent = Hex(0x4f46e5); AccentHover = Hex(0x6366f1);
                AccentSoft = Hex(0xeef0ff); OnAccent = Color.White; Danger = Hex(0xdc2626);
            }
        }

        static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Win11 frame: rounded corners, and caption, caption text and border in the palette's colours.
        public static void StyleFrame(IntPtr hwnd, Color caption)
        {
            int v = Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, 20, ref v, 4);           // dark title bar
            v = 2; Native.DwmSetWindowAttribute(hwnd, 33, ref v, 4);    // round corners
            v = ColorRef(Border); Native.DwmSetWindowAttribute(hwnd, 34, ref v, 4);
            v = ColorRef(caption); Native.DwmSetWindowAttribute(hwnd, 35, ref v, 4);
            v = ColorRef(Text); Native.DwmSetWindowAttribute(hwnd, 36, ref v, 4);
        }

        // Dark scrollbars for list and text boxes when Windows is dark.
        public static void StyleScrollbars(Control c)
        {
            if (Dark) c.HandleCreated += delegate { Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null); };
        }

        static float dpi;
        public static int S(int v)
        {
            if (dpi == 0) using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f;
            return (int)Math.Round(v * dpi);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static string KeyText(int k)
        {
            if (k == 0) return "—";
            var keys = (Keys)k;
            var sb = new StringBuilder();
            if ((keys & Keys.Control) != 0) sb.Append("Ctrl+");
            if ((keys & Keys.Alt) != 0) sb.Append("Alt+");
            if ((keys & Keys.Shift) != 0) sb.Append("Shift+");
            var code = keys & Keys.KeyCode;
            if (code >= Keys.D0 && code <= Keys.D9) sb.Append((char)('0' + (code - Keys.D0)));
            else if (code >= Keys.NumPad0 && code <= Keys.NumPad9) sb.Append("Num" + (code - Keys.NumPad0));
            else sb.Append(code);
            return sb.ToString();
        }
    }

    static class Store
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipQ");
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static T Load<T>(string name) where T : class
        {
            try
            {
                var path = Path.Combine(Dir, name);
                if (File.Exists(path)) return Json.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch { }
            return null;
        }

        // Write to a temp file first so a crash never leaves a half-written file.
        public static void Save(string name, object data)
        {
            Directory.CreateDirectory(Dir);
            var path = Path.Combine(Dir, name);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(data), Encoding.UTF8);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, string l);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public int type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }

        static INPUT Key(ushort vk, bool up)
        {
            var i = new INPUT { type = 1 };
            i.u.ki.vk = vk;
            i.u.ki.flags = up ? 2u : 0u;
            return i;
        }

        static void Send(params INPUT[] inputs)
        {
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SendCtrlV()
        {
            Send(Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true));
        }

        // A hotkey swallows its key, so releasing Alt/Win afterwards looks like a lone tap
        // and opens the app's menu bar or the Start menu. An unassigned key in between prevents that.
        public static void SuppressMenuKey()
        {
            Send(Key(0xFF, false), Key(0xFF, true));
        }

        // Windows blocks a background app from taking focus; sharing input with the focused thread lifts that for the popup.
        public static void ForceForeground(IntPtr hwnd)
        {
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
            bool attach = fg != 0 && fg != me && AttachThreadInput(me, fg, true);
            SetForegroundWindow(hwnd);
            if (attach) AttachThreadInput(me, fg, false);
        }

        public static bool ModifiersDown()
        {
            foreach (var vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
            return false;
        }
    }

    // Message-only window: receives clipboard notifications and hotkeys, never shown.
    class MsgWindow : NativeWindow
    {
        public event Action ClipboardChanged;
        public event Action<int> Hotkey;

        public MsgWindow()
        {
            CreateHandle(new CreateParams { Parent = (IntPtr)(-3) });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x031D && ClipboardChanged != null) ClipboardChanged();
            else if (m.Msg == 0x0312 && Hotkey != null) Hotkey(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    class App : ApplicationContext
    {
        const int PopupId = 1, SnippetBase = 100, MaxHistory = 200;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static Icon AppIcon;
        public Settings Settings;
        public List<Item> History;

        readonly MsgWindow msg = new MsgWindow();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly List<int> registered = new List<int>();
        readonly Popup popup;
        string ignoreText;
        IntPtr target;
        bool settingsOpen;

        public App()
        {
            Settings = Store.Load<Settings>("settings.json");
            bool firstRun = Settings == null;
            if (firstRun) Settings = Settings.Default();
            if (Settings.Snippets == null) Settings.Snippets = new List<Snippet>();
            History = Store.Load<List<Item>>("history.json") ?? new List<Item>();
            History.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Text));

            AppIcon = MakeIcon();
            popup = new Popup(this);

            msg.ClipboardChanged += OnClipboard;
            msg.Hotkey += OnHotkey;
            Native.AddClipboardFormatListener(msg.Handle);

            var menu = new ContextMenuStrip { RightToLeft = L.Dir };
            menu.Items.Add(L.T("الاختصارات", "Shortcuts"), null, delegate { OpenSettings(); });
            menu.Items.Add(L.T("سجل النسخ", "Copy history"), null, delegate { ShowPopup(IntPtr.Zero); });
            menu.Items.Add(L.T("مسح السجل", "Clear history"), null, delegate { ClearHistory(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("خروج", "Exit"), null, delegate { ExitThread(); });
            tray.Icon = AppIcon;
            tray.Text = "ClipQ";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };
            tray.Visible = true;

            var failed = RegisterAll();
            if (firstRun)
            {
                try { Store.Save("settings.json", Settings); } catch { }
                var timer = new System.Windows.Forms.Timer { Interval = 300 };
                timer.Tick += delegate { timer.Dispose(); OpenSettings(); };
                timer.Start();
            }
            else if (failed.Count > 0)
                tray.ShowBalloonTip(6000, "ClipQ", L.T("هذي الاختصارات محجوزة لبرنامج ثاني، غيّرها من شاشة الاختصارات: ",
                    "These shortcuts are taken by another program. Change them in the Shortcuts window: ") + string.Join(L.T("، ", ", "), failed), ToolTipIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            tray.Visible = false;
            UnregisterAll();
            Native.RemoveClipboardFormatListener(msg.Handle);
            base.ExitThreadCore();
        }

        // ---------- hotkeys ----------

        List<string> RegisterAll()
        {
            UnregisterAll();
            var failed = new List<string>();
            if (!Register(PopupId, Settings.PopupKeys)) failed.Add(Theme.KeyText(Settings.PopupKeys));
            for (int i = 0; i < Settings.Snippets.Count; i++)
                if (!Register(SnippetBase + i, Settings.Snippets[i].Keys)) failed.Add(Theme.KeyText(Settings.Snippets[i].Keys));
            return failed;
        }

        bool Register(int id, int keys)
        {
            if (keys == 0) return true;
            var k = (Keys)keys;
            uint mods = 0x4000; // MOD_NOREPEAT
            if ((k & Keys.Alt) != 0) mods |= 1;
            if ((k & Keys.Control) != 0) mods |= 2;
            if ((k & Keys.Shift) != 0) mods |= 4;
            if (!Native.RegisterHotKey(msg.Handle, id, mods, (uint)(k & Keys.KeyCode))) return false;
            registered.Add(id);
            return true;
        }

        void UnregisterAll()
        {
            foreach (var id in registered) Native.UnregisterHotKey(msg.Handle, id);
            registered.Clear();
        }

        void OnHotkey(int id)
        {
            Native.SuppressMenuKey();
            if (id == PopupId)
            {
                ShowPopup(Native.GetForegroundWindow());
                return;
            }
            int i = id - SnippetBase;
            if (i >= 0 && i < Settings.Snippets.Count) Paste(Vars.Expand(Settings.Snippets[i].Text ?? ""), false);
        }

        // ---------- clipboard ----------

        void OnClipboard()
        {
            string text = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var data = Clipboard.GetDataObject();
                    if (data == null || !data.GetDataPresent(DataFormats.UnicodeText)) return;
                    // Password managers mark secrets with these formats so history tools skip them.
                    if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return;
                    text = data.GetData(DataFormats.UnicodeText) as string;
                    break;
                }
                catch (ExternalException) { Thread.Sleep(25); } // another app still holds the clipboard
            }
            if (text == null) return;
            // One copy can raise several notifications, so keep ignoring the pasted text until something else is copied.
            if (text == ignoreText) return;
            ignoreText = null;
            AddHistory(text);
        }

        void AddHistory(string text)
        {
            if (text.Trim().Length == 0 || text.Length > 100000) return;
            bool pinned = false;
            int idx = History.FindIndex(x => x.Text == text);
            if (idx >= 0) { pinned = History[idx].Pinned; History.RemoveAt(idx); }
            History.Insert(0, new Item { Text = text, Pinned = pinned });
            int unpinned = 0;
            for (int i = 0; i < History.Count; i++)
                if (!History[i].Pinned && ++unpinned > MaxHistory) History.RemoveAt(i--);
            SaveHistory();
        }

        public void SaveHistory()
        {
            try { Store.Save("history.json", History); } catch { }
            if (popup.Visible) popup.Reload();
        }

        public void ClearHistory()
        {
            if (MessageBox.Show(L.T("مسح كل السجل؟ العناصر المثبتة تبقى.", "Clear the whole history? Pinned items are kept."), "ClipQ",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, L.MsgOptions) != DialogResult.Yes) return;
            History.RemoveAll(x => !x.Pinned);
            SaveHistory();
        }

        static bool SetClipboard(string text)
        {
            try { Clipboard.SetDataObject(text, true, 10, 30); return true; }
            catch (ExternalException) { return false; }
        }

        // Puts text on the clipboard, then presses Ctrl+V once the user lets go of the hotkey's modifiers.
        void Paste(string text, bool record)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!record) ignoreText = text;
            if (!SetClipboard(text)) { ignoreText = null; return; }
            var timer = new System.Windows.Forms.Timer { Interval = 15 };
            int waited = 0;
            timer.Tick += delegate
            {
                waited += timer.Interval;
                if (Native.ModifiersDown() && waited < 2000) return;
                timer.Dispose();
                Native.SendCtrlV();
            };
            timer.Start();
        }

        // ---------- popup ----------

        void ShowPopup(IntPtr window)
        {
            if (popup.Visible) { popup.Hide(); return; }
            target = window;
            popup.ShowAtCursor();
        }

        public void Choose(Item item)
        {
            if (target == IntPtr.Zero) { popup.Hide(); SetClipboard(item.Text); return; } // opened from the tray: copy only
            // Hand focus back before hiding; hiding first lets Windows focus some other window.
            Native.ForceForeground(target);
            popup.Hide();
            Paste(item.Text, true);
        }

        // ---------- settings ----------

        public void OpenSettings()
        {
            if (settingsOpen) return;
            settingsOpen = true;
            popup.Hide();
            UnregisterAll(); // so the hotkey boxes can capture combos that are currently registered
            Theme.Refresh();
            using (var form = new SettingsForm(Settings, AutoStart))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    Settings = form.Result;
                    try { Store.Save("settings.json", Settings); } catch { }
                    try { AutoStart = form.AutoStartChecked; } catch { }
                }
            }
            var failed = RegisterAll();
            settingsOpen = false;
            if (failed.Count > 0)
                MessageBox.Show(L.T("هذي الاختصارات محجوزة لبرنامج ثاني، اختار غيرها:\n", "These shortcuts are taken by another program. Please choose different ones:\n")
                    + string.Join("\n", failed), "ClipQ", MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, L.MsgOptions);
        }

        static bool AutoStart
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("ClipQ") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("ClipQ", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("ClipQ", false);
                }
            }
        }

        static Icon MakeIcon()
        {
            int size = Theme.S(32);
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp)) Ui.DrawLogo(g, new Rectangle(0, 0, size - 1, size - 1));
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (new Mutex(true, "ClipQ.SingleInstance", out first))
            {
                if (!first) return;
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
            }
        }
    }
}
