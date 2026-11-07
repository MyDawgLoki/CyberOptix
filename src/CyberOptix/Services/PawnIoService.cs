using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CyberOptix.Services
{
    /// <summary>
    /// PawnIO is a small, signed, freeware kernel driver (by namazso, https://pawnio.eu) that the sensor library uses to read the CPU temperature and
    /// some fan and board sensors. It is shared by the whole PC, so it is only ever installed when the user asks, after a confirmation, through
    /// Windows' own package manager (winget), which downloads it from its official source and checks its checksum.
    /// </summary>
    public static class PawnIoService
    {
        public const string PackageId = "namazso.PawnIO";
        public const string Website = "https://pawnio.eu";

        /// <summary>True when the PawnIO driver is installed on this PC.</summary>
        public static bool IsInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                return key != null;
            }
            catch { return false; }
        }

        /// <summary>The arguments for removing it again: the same one package, nothing else.</summary>
        public static string UninstallArguments =>
            $"uninstall --id {PackageId} --exact --source winget --silent --disable-interactivity";

        /// <summary>The arguments given to winget to install: this package only, from the winget source, with no prompts.</summary>
        public static string InstallArguments =>
            $"install --id {PackageId} --exact --source winget --silent --disable-interactivity --accept-package-agreements --accept-source-agreements";

        public record InstallResult(bool Installed, bool WingetMissing, string Message);

        /// <summary>Installs the driver with winget and waits for it. Never throws.</summary>
        public static Task<InstallResult> InstallAsync() => RunWingetAsync(InstallArguments, wantInstalled: true);

        /// <summary>Removes the driver with winget and waits for it. Never throws. "Installed" in the result means it is gone.</summary>
        public static async Task<InstallResult> UninstallAsync()
        {
            var r = await RunWingetAsync(UninstallArguments, wantInstalled: false);
            return r;
        }

        private static async Task<InstallResult> RunWingetAsync(string arguments, bool wantInstalled)
        {
            try
            {
                var psi = new ProcessStartInfo(SystemTools.Winget, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var process = Process.Start(psi);
                if (process == null) return new InstallResult(false, true, "Windows' package manager (winget) could not be started.");

                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (!await Task.Run(() => process.WaitForExit(5 * 60 * 1000)))
                {
                    try { process.Kill(); } catch { }
                    return new InstallResult(false, false, "The installation took too long and was stopped.");
                }
                var text = (await output + await errors).Trim();

                if (IsInstalled() == wantInstalled)
                    return new InstallResult(true, false, process.ExitCode == 3010 ? (wantInstalled ? "Installed. Restart Windows to finish." : "Removed. Restart Windows to finish.") : (wantInstalled ? "Installed." : "Removed."));
                return new InstallResult(false, false, (wantInstalled ? "The installation" : "The removal") + " did not finish (code " + process.ExitCode + "). " + Last(text));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No winget on this PC (older Windows 10): the website has the installer.
                return new InstallResult(false, true, "Windows' package manager (winget) is not available on this PC.");
            }
            catch (Exception ex)
            {
                return new InstallResult(false, false, "The installation failed: " + ex.Message);
            }
        }

        private static string Last(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "" : lines[^1].Trim();
        }

        /// <summary>Opens the PawnIO website in the default browser (for a PC without winget).</summary>
        public static void OpenWebsite() => DockService.Launch(Website);
    }
}
