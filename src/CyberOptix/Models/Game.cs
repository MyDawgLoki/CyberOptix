namespace CyberOptix.Models
{
    public class Game
    {
        public string Name { get; set; } = "";
        /// <summary>Install folder, for games found by Steam or added by hand. Empty for the built-in list.</summary>
        public string Path { get; set; } = "";
        /// <summary>"Steam" or "Folder" for games you added; empty for the built-in list.</summary>
        public string Source { get; set; } = "";
        public bool IsCustom => Source.Length > 0;
    }
}
