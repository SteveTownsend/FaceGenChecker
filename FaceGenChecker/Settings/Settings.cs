using System.Collections.Generic;
using Mutagen.Bethesda.Synthesis.Settings;

namespace FaceGenChecker.Settings
{
    internal class Settings
    {
        [SynthesisSettingName("Control")]
        public Control control { get; set; } = new Control();
        [SynthesisSettingName("Diagnostics")]
        public Diagnostics diagnostics { get; set; } = new Diagnostics();

        [SynthesisSettingName("Meshes for Weapons")]
        public Paths paths { get; set; } = new Paths();
    }
}