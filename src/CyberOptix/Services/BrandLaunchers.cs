using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CyberOptix.Services
{
    /// <summary>One app or website the dock can have an icon for. A click opens the app when this PC has it, otherwise the website in the default browser.</summary>
    /// <summary>
    /// <paramref name="StartMenuName"/> is the name the app has in the Start menu (several names can be separated by |), and <paramref name="AppIdPattern"/>
    /// matches its Start-menu app id (for a Store app whose name differs, like the Codex app that shows as "ChatGPT"). They are how an installed app is found.
    /// </summary>
    public record BrandLauncher(string Id, string Name, string ColorHex, string[] Protocols, string? Url, string? StartMenuName = null, string? AppIdPattern = null);

    public static class BrandLaunchers
    {
        public const string GrokBotId = "grokbot";

        public static readonly IReadOnlyList<BrandLauncher> All = new List<BrandLauncher>
        {
            new("chatgpt", "ChatGPT", "#FFFFFF", Array.Empty<string>(), "https://chatgpt.com", "ChatGPT", @"^OpenAI\.(ChatGPT|Codex)"),
            new("gemini", "Gemini", "#8AB4F8", Array.Empty<string>(), "https://gemini.google.com", "Gemini"),
            new("twitch", "Twitch", "#9146FF", new[] { "twitch" }, "https://www.twitch.tv", "Twitch"),
            new("instagram", "Instagram", "#E4405F", Array.Empty<string>(), "https://www.instagram.com", "Instagram"),
            new("x", "X", "#FFFFFF", Array.Empty<string>(), "https://x.com"),
            new("facebook", "Facebook", "#1877F2", Array.Empty<string>(), "https://www.facebook.com", "Facebook"),
            new("reddit", "Reddit", "#FF4500", Array.Empty<string>(), "https://www.reddit.com"),
            new("messenger", "Messenger", "#0084FF", new[] { "fb-messenger" }, "https://www.messenger.com", "Messenger"),
            new("whatsapp", "WhatsApp", "#25D366", new[] { "whatsapp" }, "https://web.whatsapp.com", "WhatsApp"),
            new("discord", "Discord", "#5865F2", new[] { "discord" }, "https://discord.com/app", "Discord"),
            new("tiktok", "TikTok", "#FFFFFF", Array.Empty<string>(), "https://www.tiktok.com", "TikTok"),
            new("bluesky", "Bluesky", "#1185FE", Array.Empty<string>(), "https://bsky.app"),
            new("telegram", "Telegram", "#26A5E4", new[] { "tg" }, "https://web.telegram.org", "Telegram|Telegram Desktop"),
            new("grok", "Grok", "#FFFFFF", Array.Empty<string>(), "https://grok.com", "Grok"),
            // Grok Bot is a program, not a site: it has no web address, so it opens only when it is installed (found in the Start menu).
            new(GrokBotId, "Grok Bot", "#FFFFFF", Array.Empty<string>(), null, "Grok Bot"),
            new("ollama", "Ollama", "#FFFFFF", Array.Empty<string>(), "https://ollama.com", "Ollama"),
            new("opencode", "OpenCode", "#FFFFFF", Array.Empty<string>(), "https://opencode.ai", "OpenCode"),
            new("codex", "Codex", "#FFFFFF", Array.Empty<string>(), "https://chatgpt.com/codex", "Codex", @"^OpenAI\.Codex"),
            new("github", "GitHub", "#FFFFFF", new[] { "x-github-client" }, "https://github.com", "GitHub Desktop"),
            new("pinokio", "Pinokio", "#FFFFFF", Array.Empty<string>(), "https://pinokio.computer", "Pinokio"),
            new("vercel", "Vercel", "#FFFFFF", Array.Empty<string>(), "https://vercel.com"),
            new("cursor", "Cursor", "#FFFFFF", Array.Empty<string>(), "https://cursor.com", "Cursor"),
            new("perplexity", "Perplexity", "#20B8CD", Array.Empty<string>(), "https://www.perplexity.ai", "Perplexity"),
            new("multiset", "MultiSet AI", "#6C47FF", Array.Empty<string>(), "https://www.multiset.ai"),
            new("claudeduet", "Claude Duet", "#F3DCC8", Array.Empty<string>(), "https://claude.ai"),
            new("claude", "Claude", "#D97757", new[] { "claude" }, "https://claude.ai", "Claude"),
            new("vscode", "VS Code", "#007ACC", new[] { "vscode" }, "https://code.visualstudio.com", "Visual Studio Code"),
        };

        public static BrandLauncher? Find(string id) => All.FirstOrDefault(l => l.Id == id);

        /// <summary>The URL-protocol this PC has registered for the app (like discord:), or null if the app is not installed.</summary>
        public static string? InstalledProtocol(BrandLauncher l)
        {
            foreach (var p in l.Protocols)
            {
                try
                {
                    using var key = Registry.ClassesRoot.OpenSubKey(p);
                    if (key?.GetValue("URL Protocol") != null) return p;
                }
                catch { }
            }
            return null;
        }

        private static string[] Names(BrandLauncher l) =>
            (l.StartMenuName ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>A shortcut in the Start menu with this name (how a program installed for one user or for everyone shows up), or null.</summary>
        public static string? StartMenuShortcut(string name)
        {
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    var hit = Directory.EnumerateFiles(Path.Combine(root, "Programs"), name + ".lnk", SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null) return hit;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// The app in the Start menu's app list (programs and Store apps) that matches this launcher by name or app id, as an address Explorer can open.
        /// Store apps such as the Codex app have no shortcut file, so this is how they are found. Null when the app is not installed.
        /// </summary>
        public static string? StartApp(BrandLauncher l)
        {
            if (l.StartMenuName == null && l.AppIdPattern == null) return null;
            try
            {
                var names = Names(l);
                foreach (var (name, id) in HardwareSoftwareService.AllStartApps())
                {
                    if (names.Any(n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase)) ||
                        (l.AppIdPattern != null && System.Text.RegularExpressions.Regex.IsMatch(id, l.AppIdPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                        return HardwareSoftwareService.Target(id);
                }
            }
            catch { }
            return null;
        }

        private static string? Shortcut(BrandLauncher l, Func<string, string?>? shortcutOf)
        {
            foreach (var n in Names(l))
            {
                var s = (shortcutOf ?? StartMenuShortcut)(n);
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>What a click opens: the app when it is installed (its Start-menu entry, or <c>discord:</c>), otherwise the website. Null when there is nothing to open.</summary>
        public static string? Target(BrandLauncher l, Func<BrandLauncher, string?>? protocolOf = null, Func<string, string?>? shortcutOf = null, Func<BrandLauncher, string?>? startAppOf = null)
        {
            var app = (startAppOf ?? StartApp)(l);
            if (app != null) return app;
            var shortcut = Shortcut(l, shortcutOf);
            if (shortcut != null) return shortcut;
            var protocol = (protocolOf ?? InstalledProtocol)(l);
            if (protocol != null) return protocol + ":";
            return l.Url;
        }

        public static bool IsInstalled(BrandLauncher l) => StartApp(l) != null || InstalledProtocol(l) != null || Shortcut(l, null) != null;

        /// <summary>Opens the app or the site. Goes through Explorer, so the app starts as you (not as administrator) and a website opens in your default browser.</summary>
        public static void Open(string id)
        {
            var l = Find(id);
            var target = l == null ? null : Target(l);
            if (target != null) DockService.Launch(target);
        }

        public static string Tooltip(BrandLauncher l)
        {
            if (IsInstalled(l)) return $"Open {l.Name}";
            return l.Url != null ? $"Open {l.Name} in your browser" : $"{l.Name} is not installed";
        }
    }
}
