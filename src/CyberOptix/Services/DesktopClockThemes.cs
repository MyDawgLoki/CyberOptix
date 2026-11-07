using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace CyberOptix.Services
{
    /// <summary>One look for the desktop clock: the fonts, sizes and how the time and date are written.</summary>
    public record ClockTheme(
        string Name, string Description, string Font, double TimeSize, bool TimeBold, bool TimeLight,
        double DateSize, bool DateItalic, string DateFormat, bool DateUpper, bool Stacked, bool Digital, double GlowStrength);

    /// <summary>The desktop clock's themes and the text they produce for a given moment and set of options.</summary>
    public static class DesktopClockThemes
    {
        private const string Sans = "Segoe UI Variable Display, Segoe UI";

        public static readonly ClockTheme[] Builtin =
        {
            new("Classic", "The time, with the day and date under it.", Sans, 76, true, false, 22, false, "dddd, MMMM d", false, false, false, 1),
            new("Minimal", "Thin numbers and a quiet capital-letter date.", "Segoe UI Light, Segoe UI", 104, false, true, 18, false, "ddd  ·  MMM d", true, false, false, 0.8),
            new("Bold", "Heavy numbers, with the full date.", "Segoe UI Black, Segoe UI", 112, true, false, 24, false, "dddd, MMMM d, yyyy", false, false, false, 1),
            new("Digital", "Monospaced, like a digital display.", "Consolas", 84, false, false, 20, false, "yyyy-MM-dd  ddd", true, false, true, 1),
            new("Stacked", "Hours over minutes, a big tall clock.", "Segoe UI Light, Segoe UI", 118, false, true, 22, false, "dddd, MMM d", false, true, false, 1),
            new("Elegant", "Serif letters with an italic date.", "Georgia", 84, false, false, 26, true, "dddd, d MMMM", false, false, false, 1),
            new("Neon", "Glowing numbers in your accent color.", "Segoe UI Semibold, Segoe UI", 92, true, false, 20, false, "dddd  ·  MMMM d", true, false, false, 2.4),
        };

        private static List<ClockTheme>? _custom;

        /// <summary>The themes the user added (JSON files, see <see cref="ClockThemeFiles"/>).</summary>
        public static IReadOnlyList<ClockTheme> Custom => _custom ??= ClockThemeFiles.LoadAll(Builtin.Select(b => b.Name));

        /// <summary>Reads the themes folder again (after one was added or removed).</summary>
        public static void Reload() => _custom = null;

        /// <summary>Every theme: the built-in ones, then the ones the user added.</summary>
        public static IReadOnlyList<ClockTheme> All => Builtin.Concat(Custom).ToList();

        public static ClockTheme Find(string? name)
        {
            foreach (var t in All) if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return Builtin[0];
        }

        /// <summary>The time as pieces: the main digits (one or two lines), the seconds and the AM/PM, depending on the options.</summary>
        public static (string Main, string Seconds, string AmPm) Time(ClockTheme theme, DateTime now, bool h24, bool seconds, bool ampm)
        {
            var ci = CultureInfo.CurrentCulture;
            string hours = h24 ? now.ToString(theme.Stacked || theme.Digital ? "HH" : "%H", ci) : now.ToString(theme.Stacked || theme.Digital ? "hh" : "%h", ci);
            string minutes = now.ToString("mm", ci);
            string main = theme.Stacked ? hours + "\n" + minutes : hours + ":" + minutes;
            string sec = seconds ? (theme.Digital ? ":" : "") + now.ToString("ss", ci) : "";
            string suffix = !h24 && ampm ? now.ToString("tt", ci) : "";
            return (main, sec, suffix);
        }

        public static string Date(ClockTheme theme, DateTime now)
        {
            var text = now.ToString(theme.DateFormat, CultureInfo.CurrentCulture);
            return theme.DateUpper ? text.ToUpperInvariant() : text;
        }

        public static readonly (string Key, string Name)[] Anchors =
        {
            ("TopLeft", "Top left"), ("TopCenter", "Top center"), ("TopRight", "Top right"),
            ("MiddleLeft", "Middle left"), ("Center", "Center"), ("MiddleRight", "Middle right"),
            ("BottomLeft", "Bottom left"), ("BottomCenter", "Bottom center"), ("BottomRight", "Bottom right"),
        };
    }
}
