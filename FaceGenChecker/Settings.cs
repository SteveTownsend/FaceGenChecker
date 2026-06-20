using System.Collections.Generic;
using Mutagen.Bethesda.Synthesis.Settings;

namespace FaceGenChecker
{
    public class Settings
    {
        [SynthesisSettingName("Control")]
        public Configuration.Control control { get; set; } = new Configuration.Control();
        [SynthesisSettingName("Diagnostics")]
        public Configuration.Diagnostics diagnostics { get; set; } = new Configuration.Diagnostics();

        [SynthesisSettingName("Meshes for Weapons")]
        public Configuration.Paths paths { get; set; } = new Configuration.Paths();
    }
}