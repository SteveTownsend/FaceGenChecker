using System.Collections.Generic;
using Mutagen.Bethesda.Synthesis.Settings;

namespace FaceGenChecker
{
    internal class Settings
    {
        [SynthesisSettingName("Diagnostics")]
        public Diagnostics diagnostics { get; set; } = new Diagnostics();

        [SynthesisSettingName("Meshes for Weapons")]
        public Paths paths { get; set; } = new Paths();

        //public List<string> GetConfigErrors()
        //{
        //    var errors = diagnostics.GetConfigErrors();
        //    errors.AddRange(paths.GetConfigErrors());
        //    return errors;
        //}
    }
}