using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using L = ClipQ.L;

// ClipQ-Setup.exe: installs ClipQ for the current user (no admin), and doubles as its uninstaller (/uninstall).
// ClipQ.exe is embedded in this file as a resource by build.bat, in the same language as the installer.
static class Setup
{
    const string Title = "ClipQ";
    const string Version = "1.3.1";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipQ";

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ClipQ");
    static readonly string Exe = Path.Combine(Dir, "ClipQ.exe");
    static readonly string Uninstaller = Path.Combine(Dir, "Uninstall.exe");
    static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ClipQ.lnk");
    static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipQ");

    static bool quiet;

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        bool uninstall = false;
        foreach (var a in args)
        {
            if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
            if (a.Equals("/quiet", StringComparison.OrdinalIgnoreCase)) quiet = true;
        }
        try
        {
            if (uninstall) Uninstall(); else Install();
            return 0;
        }
        catch (Exception ex)
        {
            Say(L.T("صار خطأ:\n", "Something went wrong:\n") + ex.Message, MessageBoxIcon.Error);
            return 1;
        }
    }

    static void Install()
    {
        if (!Ask(L.T("تثبيت ClipQ على هذا الحاسوب؟", "Install ClipQ on this computer?"))) return;

        StopRunning();
        Directory.CreateDirectory(Dir);
        using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClipQ.exe"))
        using (var dst = File.Create(Exe))
            src.CopyTo(dst);
        if (!string.Equals(Application.ExecutablePath, Uninstaller, StringComparison.OrdinalIgnoreCase))
            File.Copy(Application.ExecutablePath, Uninstaller, true);

        CreateShortcut();

        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            k.SetValue("ClipQ", Quote(Exe));

        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", "ClipQ");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "Hussam Khalil");
            k.SetValue("DisplayIcon", Exe);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", Quote(Uninstaller) + " /uninstall");
            k.SetValue("QuietUninstallString", Quote(Uninstaller) + " /uninstall /quiet");
            k.SetValue("EstimatedSize", (int)((new FileInfo(Exe).Length + new FileInfo(Uninstaller).Length) / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        if (quiet) return;
        Process.Start(Exe);
        Say(L.T("تم تثبيت ClipQ ✓\n\nأيقونته جنب الساعة، ويشتغل تلقائياً ويه Windows.\nضيف اختصاراتك من شاشة «الاختصارات».",
                "ClipQ is installed ✓\n\nIts icon is next to the clock, and it starts automatically with Windows.\nAdd your snippets in the Shortcuts window."),
            MessageBoxIcon.Information);
    }

    static void Uninstall()
    {
        if (!Ask(L.T("إزالة ClipQ من هذا الحاسوب؟", "Remove ClipQ from this computer?"))) return;

        StopRunning();
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            var v = k == null ? null : k.GetValue("ClipQ") as string;
            if (v != null && v.IndexOf(Dir, StringComparison.OrdinalIgnoreCase) >= 0) k.DeleteValue("ClipQ", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        if (File.Exists(Shortcut)) File.Delete(Shortcut);

        if (Directory.Exists(Data) && !quiet && Ask(L.T("تحذف الاختصارات وسجل النسخ المحفوظة هم؟", "Also delete your saved snippets and copy history?"), MessageBoxDefaultButton.Button2))
            Directory.Delete(Data, true);

        if (File.Exists(Exe)) File.Delete(Exe);
        // This file may be the one running, so the folder is removed a moment after it exits.
        Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q " + Quote(Dir))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        });

        Say(L.T("تمت إزالة ClipQ.", "ClipQ has been removed."), MessageBoxIcon.Information);
    }

    // Closes the installed copy only, so it can be replaced or removed.
    static void StopRunning()
    {
        foreach (var p in Process.GetProcessesByName("ClipQ"))
        {
            try
            {
                if (string.Equals(p.MainModule.FileName, Exe, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    static void CreateShortcut()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(type);
        try
        {
            object lnk = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Shortcut });
            var t = lnk.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { Exe });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Dir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { Exe + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { L.T("ClipQ — سجل النسخ والاختصارات", "ClipQ — clipboard history and snippets") });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            Marshal.ReleaseComObject(lnk);
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    static string Quote(string path) { return "\"" + path + "\""; }

    static bool Ask(string question, MessageBoxDefaultButton def = MessageBoxDefaultButton.Button1)
    {
        if (quiet) return true;
        return MessageBox.Show(question, Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, def,
            L.MsgOptions) == DialogResult.Yes;
    }

    static void Say(string message, MessageBoxIcon icon)
    {
        if (quiet) return;
        MessageBox.Show(message, Title, MessageBoxButtons.OK, icon, MessageBoxDefaultButton.Button1,
            L.MsgOptions);
    }
}
