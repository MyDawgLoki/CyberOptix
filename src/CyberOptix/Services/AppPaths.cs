using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CyberOptix.Services
{
    /// <summary>
    /// Where CyberOptix keeps its data: %AppData%\Roaming\CyberOptix. Earlier versions used a folder called PCOptimizer, so on first use that
    /// folder (settings, Glass Cursor, caches) is moved across and nothing is lost.
    /// </summary>
    public static class AppPaths
    {
        private static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string Legacy = Path.Combine(Roaming, "PCOptimizer");

        public static readonly string Folder = Path.Combine(Roaming, "CyberOptix");

        /// <summary>
        /// %ProgramData%\CyberOptix, owned by Administrators and SYSTEM. Ordinary users and programs running as you can read it but not change it.
        /// It holds everything the elevated app trusts to decide what to change: settings, the original-Windows-settings backup, the stopped-services
        /// list and the moved-shortcuts list. Glass Cursor runs inside CyberOptix, so its settings file is kept there too.
        /// </summary>
        public static readonly string SecureFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CyberOptix");

        private static readonly string[] SecureFiles = { "settings.json", "originals.json", "ram-state.json", "moved-shortcuts.json", @"GlassCursor\settings.json" };

        static AppPaths()
        {
            Migrate();
            EnsureSecureFolder();
            foreach (var name in SecureFiles) MoveIntoSecure(name);
            RemoveOldGlassCursorProgram();
        }

        public static string Secure(string name) => Path.Combine(SecureFolder, name);

        private static void EnsureSecureFolder()
        {
            try
            {
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

                var dir = new DirectoryInfo(SecureFolder);
                if (dir.Exists)
                {
                    // Someone else may have made this folder first so they could keep write access to it. If it isn't ours, or it is a link, start over.
                    var owner = dir.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                    bool ours = owner != null && (owner.Equals(admins) || owner.Equals(system));
                    bool link = (dir.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (link) dir.Delete(false);
                    else if (!ours) dir.Delete(true);
                }

                var sec = new DirectorySecurity();
                sec.SetOwner(admins);
                sec.SetAccessRuleProtection(true, false);   // no rules inherited from C:\ProgramData
                var flags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, flags, PropagationFlags.None, AccessControlType.Allow));

                if (!Directory.Exists(SecureFolder)) dir.Create(sec);
                else dir.SetAccessControl(sec);
            }
            catch { }
        }

        /// <summary>Glass Cursor used to be unpacked here as its own program. It is part of CyberOptix now, so the old copy is removed.</summary>
        private static void RemoveOldGlassCursorProgram()
        {
            try
            {
                var old = Path.Combine(Folder, "GlassCursor");
                if (Directory.Exists(old) && File.Exists(Path.Combine(old, "GlassCursor.exe"))) Directory.Delete(old, true);
            }
            catch { }
        }

        /// <summary>One-time move of a data file from the old user-writable folder into the protected one.</summary>
        private static void MoveIntoSecure(string name)
        {
            try
            {
                var old = Path.Combine(Folder, name);
                var target = Secure(name);
                if (!File.Exists(old)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target)) File.Copy(old, target);
                File.Delete(old);
            }
            catch { }
        }

        public static string Combine(params string[] parts) => Path.Combine(Folder, Path.Combine(parts));

        private static void Migrate()
        {
            try
            {
                if (!Directory.Exists(Legacy)) return;

                if (!Directory.Exists(Folder))
                {
                    Directory.Move(Legacy, Folder);
                    return;
                }

                // Both exist (for example the new folder was created first): bring over anything that is missing, then tidy up.
                CopyMissing(Legacy, Folder);
                try { Directory.Delete(Legacy, true); } catch { }
            }
            catch
            {
                try { if (Directory.Exists(Legacy)) { Directory.CreateDirectory(Folder); CopyMissing(Legacy, Folder); } } catch { }
            }
        }

        private static void CopyMissing(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                var target = Path.Combine(to, Path.GetFileName(file));
                if (!File.Exists(target)) File.Copy(file, target);
            }
            foreach (var dir in Directory.GetDirectories(from))
                CopyMissing(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }
}
