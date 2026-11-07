using System;
using System.IO;
using Microsoft.Win32;
using CyberOptix.Models;
using CyberOptix.Widgets;

namespace CyberOptix.Services
{
    /// <summary>
    /// Adds "Unlock Widgets" (it reads "Lock Widgets" while they are unlocked) and "Restore Widgets Placing" to the menu you get by right-clicking
    /// the desktop wallpaper. Windows 11 shows them under "Show more options". They are written for the current user only (HKCU), and the
    /// uninstaller removes them. Each runs a small script that tells the running CyberOptix what was chosen.
    /// </summary>
    public static class DesktopMenuService
    {
        private const string Shell = @"Software\Classes\DesktopBackground\Shell";
        public const string LockKey = "CyberOptixWidgets";
        public const string RestoreKey = "CyberOptixWidgetsRestore";

        private static string ScriptPath => AppPaths.Secure("widget-command.vbs");

        // The script passes the one word it is given (only four are accepted) to CyberOptix's pipe, through a hidden cmd, and does nothing if CyberOptix is not running.
        private const string Script =
            "On Error Resume Next\r\n" +
            "Dim word\r\n" +
            "word = WScript.Arguments(0)\r\n" +
            "If word <> \"toggle\" And word <> \"unlock\" And word <> \"lock\" And word <> \"restore\" Then WScript.Quit\r\n" +
            "CreateObject(\"WScript.Shell\").Run \"cmd.exe /c echo \" & word & \">\\\\.\\pipe\\" + WidgetCommandPipe.PipeName + "\", 0, True\r\n";

        /// <summary>Adds or removes the menu items to match the setting. Safe to call whenever it changes, and at every launch.</summary>
        public static void Sync()
        {
            if (AppSettings.Current.WidgetDesktopMenu) Register(); else Unregister();
        }

        /// <summary>Updates the first item's name (Unlock Widgets / Lock Widgets) after the lock changed.</summary>
        public static void Refresh()
        {
            if (!AppSettings.Current.WidgetDesktopMenu) return;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Shell + @"\" + LockKey, true);
                key?.SetValue("MUIVerb", WidgetLock.IsLocked ? "Unlock Widgets" : "Lock Widgets");
            }
            catch { }
        }

        private static void Register()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
                if (!File.Exists(ScriptPath) || File.ReadAllText(ScriptPath) != Script) File.WriteAllText(ScriptPath, Script);

                string wscript = Path.Combine(Environment.SystemDirectory, "wscript.exe");
                string exe = Environment.ProcessPath ?? "";
                Write(LockKey, WidgetLock.IsLocked ? "Unlock Widgets" : "Lock Widgets", wscript, "toggle", exe);
                Write(RestoreKey, "Restore Widgets Placing", wscript, "restore", exe);
            }
            catch { }
        }

        private static void Write(string name, string label, string wscript, string word, string exe)
        {
            using var key = Registry.CurrentUser.CreateSubKey(Shell + @"\" + name);
            key.SetValue("MUIVerb", label);
            if (exe.Length > 0) key.SetValue("Icon", exe + ",0");
            using var command = key.CreateSubKey("command");
            command.SetValue("", "\"" + wscript + "\" //B //Nologo \"" + ScriptPath + "\" " + word);
        }

        /// <summary>Removes both items and the script.</summary>
        public static void Unregister()
        {
            try
            {
                using var shell = Registry.CurrentUser.OpenSubKey(Shell, true);
                shell?.DeleteSubKeyTree(LockKey, false);
                shell?.DeleteSubKeyTree(RestoreKey, false);
            }
            catch { }
            try { if (File.Exists(ScriptPath)) File.Delete(ScriptPath); } catch { }
        }
    }
}
