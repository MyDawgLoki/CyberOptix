using System.Collections.Generic;

namespace CyberOptix.Models
{
    public class SettingsProfile
    {
        public string Mode { get; set; } = "Gaming";
        public string? SelectedGame { get; set; }
        public bool UltimatePerformance { get; set; } = true;
        public bool DisableGameDVR { get; set; } = true;
        public bool DisableFullscreenOptimizations { get; set; } = true;
        public bool HardwareAcceleratedGPUScheduling { get; set; } = true;
        public bool DisableVisualEffects { get; set; } = true;
        public bool DisableSysMain { get; set; } = false;
        public bool DisableNagle { get; set; } = true;
        public int DigitalVibrance { get; set; } = 70;
        public double Gamma { get; set; } = 1.05;
        public int Brightness { get; set; } = 50;
        public int Contrast { get; set; } = 50;
        public int Hue { get; set; } = 0;
        public string LowLatencyMode { get; set; } = "Ultra";
        public string PowerManagementMode { get; set; } = "Prefer Maximum Performance";
        public int MaxFrameRate { get; set; } = 0;

        /// <summary>Catalog tweaks the user switched on or off. A tweak that is not listed uses its default for this tab.</summary>
        public SortedDictionary<string, bool> Tweaks { get; set; } = new();

        public bool IsOn(Services.Tweak t) => Tweaks.TryGetValue(t.Id, out var on) ? on : Mode == "Gaming" && t.GamingDefault;

        public static SettingsProfile DefaultGaming() => new() { Mode = "Gaming" };

        /// <summary>Everyday settings: close to Windows defaults, nothing aggressive.</summary>
        public static SettingsProfile DefaultGeneral() => new()
        {
            Mode = "General",
            UltimatePerformance = false,
            DisableGameDVR = false,
            DisableFullscreenOptimizations = false,
            HardwareAcceleratedGPUScheduling = true,
            DisableVisualEffects = false,
            DisableSysMain = false,
            DisableNagle = false,
            DigitalVibrance = 50,
            Gamma = 1.0,
            LowLatencyMode = "Off",
            PowerManagementMode = "Adaptive",
        };

        public SettingsProfile Clone()
        {
            var copy = (SettingsProfile)MemberwiseClone();
            copy.Tweaks = new SortedDictionary<string, bool>(Tweaks);
            return copy;
        }

        /// <summary>True when every setting matches; used to detect unsaved changes.</summary>
        public bool SameAs(SettingsProfile o) => Normalized().Equals(o.Normalized());

        // Spell out every catalog tweak so "not listed" and "listed with its default" compare equal.
        private string Normalized()
        {
            var c = Clone();
            foreach (var t in Services.TweakCatalog.All) c.Tweaks[t.Id] = IsOn(t);
            return System.Text.Json.JsonSerializer.Serialize(c);
        }
    }
}
