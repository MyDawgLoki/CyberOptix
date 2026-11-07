namespace CyberOptix.Models
{
    /// <summary>Brightness, contrast and gamma for one color channel, using NVIDIA Control Panel's scales.</summary>
    public class ChannelColor
    {
        public double Brightness { get; set; } = 50;   // 0 to 100, 50 is neutral
        public double Contrast { get; set; } = 50;     // 0 to 100, 50 is neutral
        public double Gamma { get; set; } = 1.0;       // 0.30 to 2.80, 1.00 is neutral

        public bool IsNeutral => Brightness == 50 && Contrast == 50 && Gamma == 1.0;
    }

    /// <summary>A display's saved color adjustments. "Touched" flags make sure we only ever apply what the user actually changed.</summary>
    public class DisplayColor
    {
        public ChannelColor All { get; set; } = new();
        public ChannelColor Red { get; set; } = new();
        public ChannelColor Green { get; set; } = new();
        public ChannelColor Blue { get; set; } = new();
        public bool RampTouched { get; set; }

        public int Vibrance { get; set; } = 50;
        public bool VibranceTouched { get; set; }
        public int Hue { get; set; }
        public bool HueTouched { get; set; }

        public bool IsDefault => !RampTouched && !VibranceTouched && !HueTouched;
    }
}
