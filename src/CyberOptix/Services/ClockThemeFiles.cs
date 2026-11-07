using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyberOptix.Services
{
    /// <summary>
    /// The clock themes you add yourself. A theme is a small JSON file (data only: fonts, sizes and how the date is written, the same settings the
    /// built-in themes use). CyberOptix checks every value and keeps its own clean copy in an administrator-only "ClockThemes" folder, so nothing in
    /// a file you pick can run anything.
    /// </summary>
    public static class ClockThemeFiles
    {
        private const long MaxBytes = 8 * 1024;

        public static string Folder => AppPaths.Secure("ClockThemes");

        /// <summary>What a theme file holds. Every field except Name is optional: a missing one takes the Classic theme's value.</summary>
        private sealed class Dto
        {
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? Font { get; set; }
            public double? TimeSize { get; set; }
            public bool? TimeBold { get; set; }
            public bool? TimeLight { get; set; }
            public double? DateSize { get; set; }
            public bool? DateItalic { get; set; }
            public string? DateFormat { get; set; }
            public bool? DateUpper { get; set; }
            public bool? Stacked { get; set; }
            public bool? Digital { get; set; }
            public double? GlowStrength { get; set; }
        }

        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>The themes saved in the folder (each one checked again as it is read).</summary>
        public static List<ClockTheme> LoadAll(IEnumerable<string> takenNames)
        {
            var list = new List<ClockTheme>();
            var taken = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (var file in Directory.EnumerateFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(100))
                {
                    var theme = TryParse(file, out _);
                    if (theme == null || !taken.Add(theme.Name)) continue;
                    list.Add(theme);
                }
            }
            catch { }
            return list;
        }

        /// <summary>Reads and checks one file. Null (with the reason) when it is not a usable theme.</summary>
        public static ClockTheme? TryParse(string path, out string error)
        {
            error = "";
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) { error = "That file could not be read."; return null; }
                if (info.Length > MaxBytes) { error = "That file is too big for a clock theme (the limit is 8 KB)."; return null; }
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), ReadOptions);
                if (dto == null) { error = "That file is empty."; return null; }
                return Clean(dto, out error);
            }
            catch (JsonException) { error = "That file is not a valid clock theme (it must be JSON)."; return null; }
            catch (Exception ex) { error = "That file could not be read: " + ex.Message; return null; }
        }

        private static string Letters(string? s, int max, string allowed)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").Trim())
            {
                if (char.IsLetterOrDigit(c) || allowed.Contains(c)) sb.Append(c);
                if (sb.Length >= max) break;
            }
            return sb.ToString().Trim();
        }

        private static ClockTheme? Clean(Dto d, out string error)
        {
            error = "";
            var basis = DesktopClockThemes.Builtin[0];
            string name = Letters(d.Name, 24, " -_.'");
            if (name.Length == 0) { error = "The theme needs a \"Name\"."; return null; }
            string font = Letters(d.Font, 80, " ,-");
            if (font.Length == 0) font = basis.Font;
            string format = d.DateFormat ?? basis.DateFormat;
            if (format.Length > 40) format = format.Substring(0, 40);
            try { DateTime.Now.ToString(format, System.Globalization.CultureInfo.CurrentCulture); }
            catch { error = "The \"DateFormat\" is not a valid date pattern."; return null; }
            string desc = Letters(d.Description, 90, " ,.-'");
            return new ClockTheme(name, desc.Length == 0 ? "A theme you added." : desc, font,
                Math.Clamp(d.TimeSize ?? basis.TimeSize, 30, 250), d.TimeBold ?? false, d.TimeLight ?? false,
                Math.Clamp(d.DateSize ?? basis.DateSize, 8, 60), d.DateItalic ?? false, format, d.DateUpper ?? false,
                d.Stacked ?? false, d.Digital ?? false, Math.Clamp(d.GlowStrength ?? 1, 0, 4));
        }

        /// <summary>Adds the theme in a file to the folder (under a name that is not taken yet). Returns it, or null with the reason.</summary>
        public static ClockTheme? Import(string path, out string error)
        {
            var theme = TryParse(path, out error);
            if (theme == null) return null;
            try
            {
                var names = new HashSet<string>(DesktopClockThemes.All.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
                string name = theme.Name; int n = 2;
                while (names.Contains(name)) name = theme.Name + " " + n++;
                theme = theme with { Name = name };
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FileFor(name), JsonSerializer.Serialize(ToDto(theme), WriteOptions), new UTF8Encoding(false));
                DesktopClockThemes.Reload();
                return theme;
            }
            catch (Exception ex) { error = "The theme could not be saved: " + ex.Message; return null; }
        }

        public static void Remove(string name)
        {
            try { var f = FileFor(name); if (File.Exists(f)) File.Delete(f); } catch { }
            DesktopClockThemes.Reload();
        }

        private static string FileFor(string name) => Path.Combine(Folder, Regex.Replace(name, @"[^A-Za-z0-9 _.\-]", "_") + ".json");

        private static Dto ToDto(ClockTheme t) => new()
        {
            Name = t.Name, Description = t.Description, Font = t.Font, TimeSize = t.TimeSize, TimeBold = t.TimeBold, TimeLight = t.TimeLight,
            DateSize = t.DateSize, DateItalic = t.DateItalic, DateFormat = t.DateFormat, DateUpper = t.DateUpper, Stacked = t.Stacked, Digital = t.Digital, GlowStrength = t.GlowStrength,
        };

        /// <summary>An example theme file, for the user to save, edit and add back with the + tile.</summary>
        public static string Template() => JsonSerializer.Serialize(ToDto(new ClockTheme("My Theme", "My own look for the clock.", "Segoe UI Light, Segoe UI", 96, false, true, 20, false, "dddd, MMMM d", false, false, false, 1.2)), WriteOptions);

        public static bool IsCustom(string name) => DesktopClockThemes.Custom.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
