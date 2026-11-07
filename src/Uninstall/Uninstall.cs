// CyberOptix uninstaller. Deliberately small and dependency-free: it is built with the .NET Framework compiler that ships with Windows,
// so it runs on any PC without installing anything. Written in plain C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

// Native libraries are only ever loaded from System32, never from the folder this program runs in.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

internal static class Uninstaller
{
    private const string Title = "Uninstall CyberOptix";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);

    [STAThread]
    private static int Main()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        Environment.CurrentDirectory = Path.GetTempPath(); // so this program never keeps its own folder busy

        // Only ever delete a real install: it must contain CyberOptix, and must not look like a source folder.
        bool hasApp = File.Exists(Path.Combine(dir, "CyberOptix.exe"));
        bool looksLikeSource = Directory.GetFiles(dir, "*.csproj").Length > 0 || Directory.GetFiles(dir, "*.sln").Length > 0
                               || Directory.Exists(Path.Combine(dir, ".git"));
        if (!hasApp || looksLikeSource)
        {
            MessageBox.Show("This folder does not look like a CyberOptix install (it needs CyberOptix.exe and must not be a source folder), so nothing was changed.\n\n" + dir,
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        if (!IsSafeToDelete(dir))
        {
            MessageBox.Show("For safety this program will not remove this folder, because it is a drive root, a Windows or personal folder, or it holds far more than a CyberOptix install:\n\n" + dir +
                            "\n\nPut CyberOptix in a folder of its own and run the uninstaller from there. Nothing was changed.",
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appData = Path.Combine(roaming, "CyberOptix");
        string legacyData = Path.Combine(roaming, "PCOptimizer"); // what earlier versions called it
        string secureData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CyberOptix"); // admin-only settings and backup

        var ask = MessageBox.Show(
            "Uninstall CyberOptix?\n\n" +
            "This will:\n" +
            "  - close CyberOptix and Glass Cursor\n" +
            "  - bring back your Windows taskbar and normal mouse pointer\n" +
            "  - move every desktop shortcut, file and folder you put in the Apps panel back to your desktop\n" +
            "  - remove the CyberOptix desktop shortcut\n" +
            "  - delete this folder:\n    " + dir + "\n\n" +
            "Your games and personal files are not touched.\n\n" +
            "You will be asked next whether to put your Windows settings back too.",
            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (ask != DialogResult.Yes) return 0;

        // CyberOptix saved how Windows was set up before it first changed anything. Offer to put that back.
        bool hasOriginals = File.Exists(Path.Combine(secureData, "originals.json")) || File.Exists(Path.Combine(appData, "originals.json"));
        // NVIDIA keeps digital vibrance and hue in its driver, so they stay after CyberOptix is gone. Offer to reset them when the driver is there.
        bool hasNvidia = File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"));
        // PawnIO is the sensor driver CyberOptix can install to read the CPU temperature. It is shared by the whole PC, so removing it is an opt-in choice.
        bool hasPawn = IsPawnIoInstalled();
        string restoreList = (hasOriginals || hasNvidia || hasPawn) ? AskRestore(hasOriginals, hasNvidia, hasPawn) : null;   // null = leave Windows settings alone
        bool removePawn;
        restoreList = SplitPawn(restoreList, out removePawn);   // "pawnio" is not a Windows setting: it is handled on its own below
        bool restoreWindows = restoreList != null;

        bool deleteSettings = (Directory.Exists(appData) || Directory.Exists(legacyData) || Directory.Exists(secureData)) && MessageBox.Show(
            "Also delete your saved CyberOptix settings and Glass Cursor settings?\n\n" + appData + "\n" + secureData + "\n\n" +
            "Choose No to keep them in case you install CyberOptix again.",
            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        // 1. Close CyberOptix (politely first, so it puts the taskbar back itself). Only programs running from THIS folder.
        foreach (Process p in ProcessesIn(dir))
        {
            try
            {
                p.CloseMainWindow();
                if (!p.WaitForExit(6000)) p.Kill();
            }
            catch { }
        }

        // 1b. Stop CyberOptix starting with Windows: remove its start-up task (a scheduled task named CyberOptix).
        try
        {
            ProcessStartInfo task = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Delete /TN \"CyberOptix\" /F");
            task.UseShellExecute = false;
            task.CreateNoWindow = true;
            using (Process t = Process.Start(task)) { if (t != null) t.WaitForExit(15000); }
        }
        catch { }

        // 1c. Remove the items CyberOptix added to the desktop right-click menu (Unlock Widgets, Restore Widgets Placing) and the script they run.
        try
        {
            using (RegistryKey shell = Registry.CurrentUser.OpenSubKey(@"Software\Classes\DesktopBackground\Shell", true))
            {
                if (shell != null)
                {
                    shell.DeleteSubKeyTree("CyberOptixWidgets", false);
                    shell.DeleteSubKeyTree("CyberOptixWidgetsRestore", false);
                }
            }
        }
        catch { }
        try
        {
            string widgetScript = Path.Combine(secureData, "widget-command.vbs");
            if (File.Exists(widgetScript)) File.Delete(widgetScript);
        }
        catch { }

        // 2. If the taskbar was left hidden, show it again.
        try
        {
            ShowWindow(FindWindow("Shell_TrayWnd", null), 5);
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) ShowWindow(h, 5);
        }
        catch { }

        // 3. Close Glass Cursor and give the normal pointer back.
        string glassFolder = Path.Combine(appData, "GlassCursor");
        string glassExe = Path.Combine(glassFolder, "GlassCursor.exe");
        string legacyGlassFolder = Path.Combine(legacyData, "GlassCursor");
        foreach (string gf in new string[] { glassFolder, legacyGlassFolder })
        {
            foreach (Process p in ProcessesIn(gf))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }
        try
        {
            if (File.Exists(glassExe))
            {
                var restore = new ProcessStartInfo(glassExe, "--restore");
                restore.UseShellExecute = false;
                restore.CreateNoWindow = true;
                using (Process r = Process.Start(restore)) { if (r != null) r.WaitForExit(6000); }
            }
        }
        catch { }

        // 3a. Put Windows back the way it was. CyberOptix does this itself (it knows what it changed), with no window.
        if (restoreWindows)
        {
            try
            {
                var back = new ProcessStartInfo(Path.Combine(dir, "CyberOptix.exe"), "--restore-originals=" + restoreList);
                back.UseShellExecute = false;
                back.CreateNoWindow = true;
                back.WorkingDirectory = dir;
                using (Process r = Process.Start(back)) { if (r != null) r.WaitForExit(180000); }
            }
            catch { }
        }

        // 3a2. Remove the PawnIO sensor driver, if it was ticked. CyberOptix is closed by now, so nothing is holding the driver.
        if (removePawn) RemovePawnIo();

        // 3b. Everything the user moved into the Apps panel (shortcuts, files, folders) goes back, so nothing is lost with the app.
        // CyberOptix does this itself first, because it remembers where each thing came from (your desktop or the shared one).
        try
        {
            ProcessStartInfo putBack = new ProcessStartInfo(Path.Combine(dir, "CyberOptix.exe"), "--restore-desktop");
            putBack.UseShellExecute = false;
            putBack.CreateNoWindow = true;
            putBack.WorkingDirectory = dir;
            using (Process r = Process.Start(putBack)) { if (r != null) r.WaitForExit(120000); }
        }
        catch { }

        // Anything that is still there (CyberOptix could not run, or its list was lost) goes to the desktop.
        try
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            // Files and folders are kept in a hidden folder inside the Desktop.
            string itemsFolder = Path.Combine(desktopDir, ".CyberOptix panel");
            if (Directory.Exists(itemsFolder))
            {
                foreach (string entry in Directory.GetFileSystemEntries(itemsFolder))
                {
                    bool isDir = Directory.Exists(entry);
                    string name = Path.GetFileName(entry);
                    string target = Path.Combine(desktopDir, name);
                    int n = 2;
                    while (File.Exists(target) || Directory.Exists(target))
                    {
                        target = Path.Combine(desktopDir, (isDir ? name : Path.GetFileNameWithoutExtension(name)) + " (" + n + ")" + (isDir ? "" : Path.GetExtension(name)));
                        n++;
                    }
                    try { if (isDir) Directory.Move(entry, target); else File.Move(entry, target); } catch { }
                }
                try { Directory.Delete(itemsFolder); } catch { }   // only works once it is empty
            }

            string movedFolder = Path.Combine(appData, "DesktopShortcuts");
            if (Directory.Exists(movedFolder))
            {
                foreach (string f in Directory.GetFiles(movedFolder))
                {
                    string target = Path.Combine(desktopDir, Path.GetFileName(f));
                    int n = 2;
                    while (File.Exists(target))
                    {
                        target = Path.Combine(desktopDir, Path.GetFileNameWithoutExtension(f) + " (" + n + ")" + Path.GetExtension(f));
                        n++;
                    }
                    try { File.Move(f, target); } catch { }
                }
                foreach (string d in Directory.GetDirectories(movedFolder))
                {
                    string target = Path.Combine(desktopDir, Path.GetFileName(d));
                    int n = 2;
                    while (Directory.Exists(target) || File.Exists(target))
                    {
                        target = Path.Combine(desktopDir, Path.GetFileName(d) + " (" + n + ")");
                        n++;
                    }
                    try { Directory.Move(d, target); } catch { }
                }
                try { File.Delete(Path.Combine(appData, "moved-shortcuts.json")); } catch { }
                try { File.Delete(Path.Combine(secureData, "moved-shortcuts.json")); } catch { }
            }
        }
        catch { }

        // 4. Desktop shortcuts that point at this install
        foreach (Environment.SpecialFolder desktop in new Environment.SpecialFolder[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
        {
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(desktop), "CyberOptix.lnk");
                if (File.Exists(lnk) && ShortcutTarget(lnk).StartsWith(dir, StringComparison.OrdinalIgnoreCase)) File.Delete(lnk);
            }
            catch { }
        }

        // 5. Saved settings, if asked
        if (deleteSettings)
        {
            try { if (Directory.Exists(appData)) Directory.Delete(appData, true); } catch { }
            try { if (Directory.Exists(legacyData)) Directory.Delete(legacyData, true); } catch { }
            try { if (Directory.Exists(secureData)) Directory.Delete(secureData, true); } catch { }
        }

        // 6. Files a single-file build unpacks into Temp
        try
        {
            string unpacked = Path.Combine(Path.GetTempPath(), ".net", "CyberOptix");
            if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true);
        }
        catch { }

        // 7. This folder, including this program. It cannot delete itself while running, so a hidden helper waits a few seconds and then removes it.
        try
        {
            // Keeps trying for up to about a minute, because this program's own file cannot be deleted until it has closed.
            var cleanup = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c for /l %i in (1,1,60) do @(if exist \"" + dir + "\" (rmdir /s /q \"" + dir + "\" 2>nul & ping 127.0.0.1 -n 2 >nul))");
            cleanup.CreateNoWindow = true;
            cleanup.UseShellExecute = false;
            cleanup.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(cleanup);
        }
        catch { }

        MessageBox.Show("CyberOptix has been uninstalled.\n\n" +
                        "Any shortcuts, files and folders from the Apps panel are back on your desktop. " +
                        (restoreWindows ? "The Windows settings you chose are back to how they were before CyberOptix (key remaps finish after restarting your PC)." : "Your Windows settings were left as they are.") +
                        (deleteSettings ? "" : "\n\nYour CyberOptix settings were kept in:\n" + appData),
                        Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    /// <summary>Refuses folders that must never be deleted wholesale: a drive root, Windows, Program Files itself, or the user's own folders.</summary>
    private static bool IsSafeToDelete(string dir)
    {
        string full;
        try { full = Path.GetFullPath(dir).TrimEnd('\\'); } catch { return false; }
        if (full.Length < 4) return false;
        string root = Path.GetPathRoot(full + "\\");
        if (root != null && root.TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase)) return false;

        Environment.SpecialFolder[] never =
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos,
        };
        foreach (Environment.SpecialFolder f in never)
        {
            string p = Environment.GetFolderPath(f);
            if (p.Length > 0 && p.TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase)) return false;
        }

        // Folders directly under the user profile such as Downloads
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        if (profile.Length > 0 && full.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase) && full.Substring(profile.Length + 1).IndexOf('\\') < 0)
        {
            string leaf = full.Substring(profile.Length + 1);
            string[] personal = { "Downloads", "Documents", "Desktop", "Pictures", "Music", "Videos", "OneDrive", "Favorites", "Links", "Saved Games", "Contacts", "Searches", "3D Objects", "AppData" };
            foreach (string name in personal)
                if (leaf.Equals(name, StringComparison.OrdinalIgnoreCase)) return false;
        }

        // A real install is a few hundred files at most; a folder far bigger than that is somebody's data.
        try { if (Directory.GetFileSystemEntries(full).Length > 2000) return false; } catch { return false; }
        return true;
    }

    /// <summary>Asks which groups of Windows settings to put back. Returns the choice as "tweaks,services,..." or null for none.</summary>
    private static string AskRestore(bool hasOriginals, bool hasNvidia, bool hasPawn)
    {
        string[] allKeys = { "tweaks", "services", "colors", "rotation", "remaps", "pointer", "scrolling", "cursors", "nvidia", "pawnio" };
        string[] allNames = { "Windows tweaks", "Background services", "Display colors", "Display rotation", "Key remaps (finish after restarting your PC)", "Pointer speed and precision", "Scrolling", "Cursor theme and size", "NVIDIA digital vibrance and hue", "PawnIO sensor driver" };
        string[] allDetails = {
            "The tweaks you switched on, plus the performance profile.",
            "Services the tweaks stopped, and services stopped by Free up resources.",
            "Brightness, contrast, gamma, digital vibrance and hue, back to how they were before CyberOptix.",
            "Each screen goes back to its original orientation.",
            "Removes your key remaps.",
            "Mouse speed and Enhance pointer precision.",
            "How many lines one wheel notch scrolls.",
            "Your cursor theme and cursor size.",
            "Reset to NVIDIA's defaults on every display, even if you set them in NVIDIA Control Panel. The NVIDIA driver saves these, so they stay after CyberOptix is gone.",
            "Uninstalls the PawnIO driver (through Windows' package manager, winget). Other programs that read sensors through PawnIO will lose those readings." };

        // Only what applies: the saved-originals items need that file, the NVIDIA item needs the NVIDIA driver.
        List<int> shown = new List<int>();
        for (int i = 0; i < allKeys.Length; i++)
        {
            bool isNvidia = allKeys[i] == "nvidia";
            bool isPawn = allKeys[i] == "pawnio";
            if (isPawn ? hasPawn : (isNvidia ? hasNvidia : hasOriginals)) shown.Add(i);
        }
        string[] keys = new string[shown.Count];
        string[] names = new string[shown.Count];
        string[] details = new string[shown.Count];
        for (int i = 0; i < shown.Count; i++) { keys[i] = allKeys[shown[i]]; names[i] = allNames[shown[i]]; details[i] = allDetails[shown[i]]; }

        using (Form f = new Form())
        {
            f.Text = Title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.MaximizeBox = false; f.MinimizeBox = false;
            int extra = hasPawn ? 18 : 0;   // room for one more line of explanation
            f.ClientSize = new System.Drawing.Size(520, 144 + extra + keys.Length * 44 + 56);
            f.Font = new System.Drawing.Font("Segoe UI", 9f);

            Label intro = new Label();
            intro.Left = 16; intro.Top = 14; intro.Width = 488; intro.Height = 108 + extra;
            intro.Text = "Put your Windows settings back to how they were before CyberOptix?\r\n\r\n" +
                         "Tick what you want undone. Anything you leave unticked stays as it is now. Items CyberOptix never changed are skipped automatically." +
                         (hasNvidia ? "\r\nThe NVIDIA item is the exception: it is off by default, and resets those two settings whoever changed them." : "") +
                         (hasPawn ? "\r\nThe PawnIO item is off by default too: other programs may use that driver." : "");
            f.Controls.Add(intro);

            CheckBox[] boxes = new CheckBox[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                CheckBox c = new CheckBox();
                c.Left = 24; c.Top = 128 + extra + i * 44; c.Width = 480; c.Height = 40;
                c.Checked = keys[i] != "nvidia" && keys[i] != "pawnio";   // these two affect things outside CyberOptix (NVIDIA settings, a shared driver), so they are never ticked for you
                c.Text = names[i] + " - " + details[i];
                boxes[i] = c;
                f.Controls.Add(c);
            }

            Button ok = new Button();
            ok.Text = "Restore selected"; ok.Width = 130; ok.Height = 30;
            ok.Left = 520 - 16 - 130 - 8 - 190; ok.Top = f.ClientSize.Height - 44;
            ok.DialogResult = DialogResult.OK;
            Button skip = new Button();
            skip.Text = "Leave my settings as they are"; skip.Width = 190; skip.Height = 30;
            skip.Left = 520 - 16 - 190; skip.Top = ok.Top;
            skip.DialogResult = DialogResult.Cancel;
            f.Controls.Add(ok); f.Controls.Add(skip);
            f.AcceptButton = ok; f.CancelButton = skip;

            if (f.ShowDialog() != DialogResult.OK) return null;
            List<string> chosen = new List<string>();
            for (int i = 0; i < keys.Length; i++) if (boxes[i].Checked) chosen.Add(keys[i]);
            return chosen.Count == 0 ? null : string.Join(",", chosen.ToArray());
        }
    }

    /// <summary>True when the PawnIO sensor driver is installed on this PC.</summary>
    private static bool IsPawnIoInstalled()
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"))
                return key != null;
        }
        catch { return false; }
    }

    /// <summary>Takes "pawnio" out of the chosen list (it is not a Windows setting). Returns the rest, or null when nothing else is left.</summary>
    private static string SplitPawn(string list, out bool removePawn)
    {
        removePawn = false;
        if (list == null) return null;
        List<string> rest = new List<string>();
        foreach (string item in list.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (item == "pawnio") removePawn = true; else rest.Add(item);
        }
        return rest.Count == 0 ? null : string.Join(",", rest.ToArray());
    }

    /// <summary>Uninstalls PawnIO with winget (the same way CyberOptix installs it). Says so if that did not work.</summary>
    private static void RemovePawnIo()
    {
        string problem = null;
        try
        {
            // the exact winget file in the user's WindowsApps folder (a bare name would also be looked for next to this program first)
            string wingetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            if (!File.Exists(wingetPath)) throw new System.ComponentModel.Win32Exception();   // handled below as "winget is not available"
            ProcessStartInfo psi = new ProcessStartInfo(wingetPath, "uninstall --id namazso.PawnIO --exact --source winget --silent --disable-interactivity");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                if (p == null) problem = "Windows' package manager (winget) could not be started.";
                else if (!p.WaitForExit(180000)) { try { p.Kill(); } catch { } problem = "Removing it took too long and was stopped."; }
                else if (IsPawnIoInstalled()) problem = "winget finished (code " + p.ExitCode + ") but the driver is still there. A restart of Windows may be needed.";
            }
        }
        catch (System.ComponentModel.Win32Exception) { problem = "Windows' package manager (winget) is not available on this PC."; }
        catch (Exception ex) { problem = ex.Message; }

        if (problem != null)
            MessageBox.Show("The PawnIO sensor driver was not removed.\n\n" + problem + "\n\nYou can remove it yourself in Windows Settings, Installed apps (PawnIO).",
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Programs whose exe lives inside a folder.</summary>
    private static List<Process> ProcessesIn(string folder)
    {
        var result = new List<Process>();
        int me = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == me) continue;
                string path = p.MainModule.FileName;
                if (path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)) result.Add(p);
            }
            catch { /* system processes can't be inspected; they are not ours */ }
        }
        return result;
    }

    private static string ShortcutTarget(string lnk)
    {
        try
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            return (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
        }
        catch
        {
            return "";
        }
    }
}
