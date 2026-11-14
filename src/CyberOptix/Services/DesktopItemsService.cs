using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace CyberOptix.Services
{
    /// <summary>One tile in the Apps panel. <paramref name="Moved"/> is true for shortcuts that were moved off the desktop into the panel.</summary>
    public record DesktopItem(string Name, string Path, bool IsFolder, bool IsApp, bool Moved);

    /// <summary>
    /// What the Apps panel shows: the user's desktop (their own and the shared one) plus everything they moved into the panel.
    /// Moved shortcuts live in %AppData%\Roaming\CyberOptix\DesktopShortcuts. Moved files and folders stay on the desktop's own drive, in a
    /// hidden folder inside the Desktop folder, so the move is instant and OneDrive (which often holds the Desktop) keeps backing them up.
    /// A small list remembers where each thing came from so it can go back.
    /// </summary>
    public static class DesktopItemsService
    {
        private static readonly string[] ShortcutExtensions = { ".lnk", ".url", ".appref-ms" };

        /// <summary>For tests only: when set, a desktop, a shared desktop, a panel folder and a list file are used inside this folder instead of the real ones.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static string? TestRoot;

        public static string ShortcutsFolder => TestRoot != null ? System.IO.Path.Combine(TestRoot, "Panel") : AppPaths.Combine("DesktopShortcuts");
        private static string ManifestPath => TestRoot != null ? System.IO.Path.Combine(TestRoot, "moved.json") : AppPaths.Secure("moved-shortcuts.json");

        private static string UserDesktop => TestRoot != null ? System.IO.Path.Combine(TestRoot, "Desktop") : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        private static string PublicDesktop => TestRoot != null ? System.IO.Path.Combine(TestRoot, "Public") : Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        /// <summary>Where moved files and folders are kept: a hidden folder inside the user's Desktop, so it is on the same drive (and in the same OneDrive).</summary>
        private static string ItemsFolder => System.IO.Path.Combine(UserDesktop, ".CyberOptix panel");

        /// <summary>Where a second copy of a shortcut is tucked away. The panel never lists these, but they can all go back.</summary>
        private static string DuplicatesFolder => System.IO.Path.Combine(ShortcutsFolder, "Duplicates");

        private static IEnumerable<string> Folders()
        {
            foreach (var path in new[] { ShortcutsFolder, ItemsFolder, UserDesktop, TestRoot != null ? "" : PublicDesktop })
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) yield return path;
            }
        }

        /// <summary>The same place written two ways ("C:\a\..\b" and "C:\b") is still one place.</summary>
        private static string Full(string path)
        {
            try { return System.IO.Path.GetFullPath(path).TrimEnd('\\'); }
            catch { return path; }
        }

        private static bool SamePath(string a, string b) => string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

        private static bool IsMovedFolder(string folder) => SamePath(folder, ShortcutsFolder) || SamePath(folder, ItemsFolder);

        private static bool IsShortcut(FileSystemInfo e) =>
            e is FileInfo && ShortcutExtensions.Contains(e.Extension, StringComparer.OrdinalIgnoreCase);

        public static List<DesktopItem> List()
        {
            var items = new List<DesktopItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // the panel's copy wins over the same shortcut on a desktop
            foreach (var folder in Folders().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool moved = IsMovedFolder(folder);
                try
                {
                    foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                    {
                        if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        if (entry.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        if (SamePath(entry.FullName, DuplicatesFolder)) continue;   // the tucked-away copies are not tiles

                        if (!seen.Add(KeyOf(entry))) continue;                 // already listed (same program and arguments)

                        bool isFolder = entry is DirectoryInfo;
                        string ext = entry.Extension.ToLowerInvariant();
                        bool isProgram = ext == ".exe" || IsShortcut(entry);
                        string name = !isFolder && isProgram ? System.IO.Path.GetFileNameWithoutExtension(entry.Name) : entry.Name;
                        items.Add(new DesktopItem(name, entry.FullName, isFolder, isProgram, moved));
                    }
                }
                catch
                {
                    // An unreadable folder is skipped.
                }
            }

            // Apps first, then folders, then files, each in name order, like a tidy desktop.
            return items
                .OrderBy(i => i.IsApp ? 0 : i.IsFolder ? 1 : 2)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ---------- moving shortcuts into the panel ----------

        private class MovedEntry
        {
            public string Current { get; set; } = "";
            public string Original { get; set; } = "";
        }

        private static List<MovedEntry> ReadManifest()
        {
            try
            {
                if (File.Exists(ManifestPath))
                    return JsonSerializer.Deserialize<List<MovedEntry>>(File.ReadAllText(ManifestPath)) ?? new();
            }
            catch { }
            return new();
        }

        private static void WriteManifest(List<MovedEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Folder);
                File.WriteAllText(ManifestPath, JsonSerializer.Serialize(entries));
            }
            catch { }
        }

        /// <summary>
        /// A shortcut can point into a folder that was moved into the panel (like Desktop\Launchers). This gives the path where that
        /// file is now, or the path unchanged if nothing moved. Used to still find its icon.
        /// </summary>
        public static string Remap(string path)
        {
            try
            {
                foreach (var e in ReadManifest())
                {
                    var from = e.Original.TrimEnd('\\') + "\\";
                    if (path.StartsWith(from, StringComparison.OrdinalIgnoreCase))
                    {
                        var now = System.IO.Path.Combine(e.Current, path.Substring(from.Length));
                        if (Exists(now)) return now;
                    }
                }
                // A shortcut written before a folder was tidied into another folder on the desktop (Desktop\Launchers, now
                // Desktop\Desktop Folders\Launchers): look for the same ending inside each folder that was moved.
                var desktop = UserDesktop.TrimEnd('\\') + "\\";
                if (!Exists(path) && path.StartsWith(desktop, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = path.Substring(desktop.Length);
                    foreach (var e in ReadManifest())
                    {
                        if (!Directory.Exists(e.Current)) continue;
                        var guess = System.IO.Path.Combine(e.Current, rest);
                        if (Exists(guess)) return guess;
                    }
                }
            }
            catch { }
            return path;
        }

        // ---------- telling shortcuts apart ----------

        private static readonly Dictionary<string, (DateTime Stamp, string Key)> KeyCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// What makes two shortcuts "the same": the program they start plus its arguments (a web link for .url files). Two shortcuts to the same
        /// program with different arguments, such as two browser profiles, are different. Anything that is not a shortcut is identified by its path.
        /// </summary>
        private static string KeyOf(FileSystemInfo e)
        {
            if (!IsShortcut(e)) return "path:" + e.FullName;
            lock (KeyCache)
            {
                if (KeyCache.TryGetValue(e.FullName, out var hit) && hit.Stamp == e.LastWriteTimeUtc) return hit.Key;
            }

            string key = "name:" + e.Name;
            try
            {
                var ext = e.Extension.ToLowerInvariant();
                if (ext == ".url")
                {
                    var url = File.ReadLines(e.FullName).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(url)) key = "url:" + url.Substring(4).Trim();
                }
                else if (ext == ".lnk")
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic shell = Activator.CreateInstance(shellType)!;
                        var link = shell.CreateShortcut(e.FullName);
                        string target = link.TargetPath;
                        string args = link.Arguments;
                        if (!string.IsNullOrWhiteSpace(target)) key = "lnk:" + target.Trim() + "|" + (args ?? "").Trim();
                    }
                }
            }
            catch { }

            lock (KeyCache) KeyCache[e.FullName] = (e.LastWriteTimeUtc, key);
            return key;
        }

        private static HashSet<string> KeysIn(string folder)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(folder))
                    foreach (var e in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                        if (IsShortcut(e)) keys.Add(KeyOf(e));
            }
            catch { }
            return keys;
        }

        // ---------- what would move ----------

        private static bool IsVisible(FileSystemInfo e) =>
            (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !e.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

        /// <summary>True for something on the shared desktop, which every Windows account on this PC sees.</summary>
        private static bool IsShared(string path)
        {
            try { return !string.IsNullOrEmpty(PublicDesktop) && Full(path).StartsWith(Full(PublicDesktop) + "\\", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>CyberOptix itself, or the folder it runs from, must never be moved while it is running.</summary>
        private static bool HoldsThisApp(string path)
        {
            try
            {
                var me = Environment.ProcessPath ?? "";
                if (string.Equals(path, me, StringComparison.OrdinalIgnoreCase)) return true;
                return me.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>What "Move Desktop Items Here" would do right now.</summary>
        public record MovePlan(List<string> Shortcuts, List<string> Duplicates, List<string> Files, List<string> Folders)
        {
            public int Total => Shortcuts.Count + Duplicates.Count + Files.Count + Folders.Count;
            /// <summary>How many of the shortcuts are on the shared desktop.</summary>
            public int Shared => Shortcuts.Concat(Duplicates).Count(IsShared);
        }

        /// <summary>
        /// Everything on the desktop that can move into the panel: the shortcuts on your desktop and on the shared desktop, and the files and
        /// folders on your desktop. A shortcut the panel already has, or a second copy of one, is listed as a duplicate: it is tucked away,
        /// not shown twice. Hidden and system entries (desktop.ini) are left alone.
        /// </summary>
        public static MovePlan PlanMove()
        {
            var inPanel = KeysIn(ShortcutsFolder);
            var candidates = new List<FileInfo>();
            var files = new List<string>();
            var folders = new List<string>();

            foreach (var folder in new[] { UserDesktop, PublicDesktop })
            {
                if (string.IsNullOrEmpty(folder)) continue;
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    bool own = string.Equals(folder, UserDesktop, StringComparison.OrdinalIgnoreCase);
                    foreach (var e in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                    {
                        if (!IsVisible(e)) continue;
                        if (IsShortcut(e)) { candidates.Add((FileInfo)e); continue; }
                        if (!own || HoldsThisApp(e.FullName)) continue;   // other people's files on the shared desktop stay put
                        if (e is DirectoryInfo) folders.Add(e.FullName); else files.Add(e.FullName);
                    }
                }
                catch { }
            }

            var shortcuts = new List<string>();
            var duplicates = new List<string>();
            foreach (var f in candidates
                         .OrderBy(f => f.Name.Length)                          // of two identical shortcuts, keep the one with the plainer name
                         .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(f => IsShared(f.FullName) ? 1 : 0))           // and your own over the shared one
                (inPanel.Add(KeyOf(f)) ? shortcuts : duplicates).Add(f.FullName);

            files.Sort(StringComparer.OrdinalIgnoreCase);
            folders.Sort(StringComparer.OrdinalIgnoreCase);
            return new MovePlan(shortcuts, duplicates, files, folders);
        }

        // ---------- keeping other records in step ----------

        private static readonly string[] RunKeys =
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
        };

        /// <summary>
        /// Something that pointed at a shortcut's old location now points at its new one: this user's start-up (Run and RunOnce) entries and the
        /// dock's pinned list. Only an exact match on the old path is changed. Returns how many records were updated.
        /// </summary>
        private static int UpdateReferences(string oldPath, string newPath)
        {
            int changed = 0;

            foreach (var keyPath in RunKeys)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
                    if (key == null) continue;
                    foreach (var name in key.GetValueNames())
                    {
                        if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string data) continue;
                        var kind = key.GetValueKind(name);
                        if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) continue;

                        // The path is either wrapped in quotes, or it is the start of the text and runs to the end or to the first space-separated argument.
                        string rest;
                        bool quoted;
                        int close = data.Length > 1 && data[0] == '"' ? data.IndexOf('"', 1) : -1;
                        if (close > 0)
                        {
                            quoted = true;
                            if (!string.Equals(data.Substring(1, close - 1), oldPath, StringComparison.OrdinalIgnoreCase)) continue;
                            rest = data.Substring(close + 1);
                        }
                        else
                        {
                            quoted = false;
                            bool starts = data.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase)
                                          && (data.Length == oldPath.Length || data[oldPath.Length] == ' ');
                            if (!starts) continue;
                            rest = data.Substring(oldPath.Length);
                        }

                        bool needQuotes = quoted || newPath.Contains(' ');
                        key.SetValue(name, (needQuotes ? "\"" + newPath + "\"" : newPath) + rest, kind);
                        changed++;
                    }
                }
                catch { }
            }

            try
            {
                var pins = Models.AppSettings.Current.DockPinned;
                for (int i = 0; i < pins.Count; i++)
                {
                    if (!string.Equals(pins[i], oldPath, StringComparison.OrdinalIgnoreCase)) continue;
                    pins[i] = newPath;
                    changed++;
                }
                if (changed > 0) Models.AppSettings.Save();
            }
            catch { }

            return changed;
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        /// <summary>Everything this panel holds that can be moved back: what the list says it moved, plus any shortcut found loose in the panel folder.</summary>
        private static List<string> HeldPaths()
        {
            var held = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // by full path, so one thing is never counted twice
            foreach (var e in ReadManifest())
                if (Exists(e.Current) && seen.Add(Full(e.Current))) held.Add(e.Current);
            try
            {
                if (Directory.Exists(ShortcutsFolder))
                    foreach (var f in new DirectoryInfo(ShortcutsFolder).EnumerateFiles().Where(f => IsShortcut(f)))
                        if (seen.Add(Full(f.FullName))) held.Add(f.FullName);
            }
            catch { }
            return held;
        }

        /// <summary>True when the Apps panel has been used on this PC before (there is a record of what it moved).</summary>
        public static bool HasBeenUsed => File.Exists(ManifestPath);

        /// <summary>How many things are in the panel, so there is something to move back.</summary>
        public static int MovedCount => HeldPaths().Count;

        /// <summary>A name that is not taken yet in a folder: "name (2).ext" for a file, "name (2)" for a folder.</summary>
        private static string FreeName(string folder, string name, bool isFolder)
        {
            var target = System.IO.Path.Combine(folder, name);
            if (!Exists(target)) return target;
            var stem = isFolder ? name : System.IO.Path.GetFileNameWithoutExtension(name);
            var ext = isFolder ? "" : System.IO.Path.GetExtension(name);
            for (int i = 2; ; i++)
            {
                target = System.IO.Path.Combine(folder, $"{stem} ({i}){ext}");
                if (!Exists(target)) return target;
            }
        }

        private static void RemoveIfEmpty(string folder)
        {
            try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch { }
        }

        /// <summary>What "Move Desktop Items Here" did.</summary>
        public record MoveResult(int Shortcuts, int Files, int Folders, int Duplicates, int Shared, int Failed, int Updated)
        {
            public int Moved => Shortcuts + Files + Folders + Duplicates;
        }

        /// <summary>
        /// Moves everything in <see cref="PlanMove"/> off the desktop: shortcuts into the panel's folder, copies of shortcuts into its Duplicates folder,
        /// and files and folders into the hidden folder inside the Desktop. Nothing is deleted or copied, and each thing's old place is remembered.
        /// Start-up and pinned records that pointed at something that moved are updated to its new location.
        /// </summary>
        public static MoveResult MoveDesktopItemsHere()
        {
            var plan = PlanMove();
            var manifest = ReadManifest();
            int shortcuts = 0, files = 0, folders = 0, duplicates = 0, shared = 0, failed = 0, updated = 0;

            bool Move(string path, string toFolder)
            {
                try
                {
                    Directory.CreateDirectory(toFolder);
                    bool isFolder = Directory.Exists(path);
                    var target = FreeName(toFolder, System.IO.Path.GetFileName(path), isFolder);
                    if (isFolder) Directory.Move(path, target); else File.Move(path, target);
                    manifest.Add(new MovedEntry { Current = target, Original = path });
                    updated += UpdateReferences(path, target);
                    if (IsShared(path)) shared++;
                    return true;
                }
                catch
                {
                    failed++;   // in use, or no permission: it stays where it is
                    return false;
                }
            }

            foreach (var p in plan.Shortcuts) if (Move(p, ShortcutsFolder)) shortcuts++;
            foreach (var p in plan.Duplicates) if (Move(p, DuplicatesFolder)) duplicates++;

            if (plan.Files.Count + plan.Folders.Count > 0)
            {
                try
                {
                    Directory.CreateDirectory(ItemsFolder);
                    File.SetAttributes(ItemsFolder, File.GetAttributes(ItemsFolder) | FileAttributes.Hidden);   // out of sight on the desktop
                }
                catch { }
            }
            foreach (var p in plan.Folders) if (Move(p, ItemsFolder)) folders++;
            foreach (var p in plan.Files) if (Move(p, ItemsFolder)) files++;

            WriteManifest(manifest);
            RemoveIfEmpty(ItemsFolder);
            return new MoveResult(shortcuts, files, folders, duplicates, shared, failed, updated);
        }

        /// <summary>What "Move To Desktop" did.</summary>
        public record BackResult(int Moved, int Merged, int Updated, int Failed);

        /// <summary>Puts one shortcut, file or folder from the panel back on the desktop, in the place it came from.</summary>
        public static bool MoveBack(string currentPath)
        {
            bool ok = MoveBackCore(currentPath, out _, out _);
            RemoveIfEmpty(ItemsFolder);
            RemoveIfEmpty(DuplicatesFolder);
            return ok;
        }

        private static bool MoveBackCore(string currentPath, out bool merged, out int updated)
        {
            merged = false;
            updated = 0;
            var manifest = ReadManifest();
            var entry = manifest.FirstOrDefault(e => SamePath(e.Current, currentPath));
            var folder = entry != null ? System.IO.Path.GetDirectoryName(entry.Original) ?? UserDesktop : UserDesktop;
            try
            {
                if (!Directory.Exists(folder)) folder = UserDesktop;
                bool isFolder = Directory.Exists(currentPath);
                FileSystemInfo info = isFolder ? new DirectoryInfo(currentPath) : new FileInfo(currentPath);

                // The same shortcut is already at the destination (made again, or put back by hand): do not leave two. The panel's copy is the
                // identical one, so it is the one that goes, and records that pointed at it point at the copy that stays.
                if (IsShortcut(info))
                {
                    var key = KeyOf(info);
                    var twin = new DirectoryInfo(folder).EnumerateFileSystemInfos().FirstOrDefault(e => IsShortcut(e) && KeyOf(e) == key);
                    if (twin != null)
                    {
                        updated = UpdateReferences(currentPath, twin.FullName);
                        File.Delete(currentPath);
                        merged = true;
                        if (entry != null) manifest.Remove(entry);
                        WriteManifest(manifest);
                        return true;
                    }
                }

                var target = FreeName(folder, System.IO.Path.GetFileName(entry?.Original ?? currentPath), isFolder);
                if (isFolder) Directory.Move(currentPath, target); else File.Move(currentPath, target);
                updated = UpdateReferences(currentPath, target);
                if (entry != null) manifest.Remove(entry);
                WriteManifest(manifest);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Puts everything the panel holds back on the desktop: shortcuts, the copies tucked away, files and folders. Ones this app moved go back
        /// where they came from (your desktop or the shared one); any others go to your desktop. Start-up and pinned records follow each one back,
        /// and a shortcut the desktop already has is not duplicated.
        /// </summary>
        public static BackResult MoveAllBack()
        {
            int moved = 0, merged = 0, updated = 0, failed = 0;
            var files = HeldPaths();

            foreach (var file in files)
            {
                if (MoveBackCore(file, out var wasMerged, out var n))
                {
                    if (wasMerged) merged++; else moved++;
                    updated += n;
                }
                else
                {
                    failed++;
                }
            }
            RemoveIfEmpty(ItemsFolder);
            RemoveIfEmpty(DuplicatesFolder);
            return new BackResult(moved, merged, updated, failed);
        }

        // ---------- watching ----------

        /// <summary>Calls back (once, after things settle) whenever something is added, removed or renamed on the desktop or in the panel.</summary>
        public static IDisposable Watch(Action changed)
        {
            try { Directory.CreateDirectory(ShortcutsFolder); } catch { }
            var watchers = new List<FileSystemWatcher>();
            var timer = new System.Timers.Timer(500) { AutoReset = false };
            timer.Elapsed += (_, _) => changed();

            foreach (var folder in Folders().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var w = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName, EnableRaisingEvents = true };
                    FileSystemEventHandler on = (_, _) => { timer.Stop(); timer.Start(); };
                    w.Created += on; w.Deleted += on;
                    w.Renamed += (_, _) => { timer.Stop(); timer.Start(); };
                    watchers.Add(w);
                }
                catch { }
            }
            return new WatchHandle(watchers, timer);
        }

        private sealed class WatchHandle : IDisposable
        {
            private readonly List<FileSystemWatcher> _watchers;
            private readonly System.Timers.Timer _timer;
            public WatchHandle(List<FileSystemWatcher> watchers, System.Timers.Timer timer) { _watchers = watchers; _timer = timer; }
            public void Dispose()
            {
                _timer.Stop(); _timer.Dispose();
                foreach (var w in _watchers) w.Dispose();
            }
        }
    }
}
