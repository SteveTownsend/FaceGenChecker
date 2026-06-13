using Mutagen.Bethesda.Synthesis.Settings;

namespace FaceGenChecker
{
    public class Paths
    {
        [SynthesisSettingName("Path for Conflict Winners")]
        [SynthesisTooltip("This is usually the MO2 VFS 'Mods' path")]
        [SynthesisDescription("Used to detect winning NPC_ record override and the corresponding winning .NIF.")]
        public string ConflictWinnerLocation { get; set; } = "J:/SteamLibrary/steamapps/common/Skyrim Special Edition/Data";

        [SynthesisSettingName("Output Folder")]
        [SynthesisTooltip("This must be a valid path on your computer. Typically this points to a new mod directory in your Mod Manager VFS, e.g. 'D:/ModdedSkyrim/mods/FaceGenChecker Output'. The suffix 'meshes/', where NIF files are read in-game, is added in the patcher and not needed here. Relative or absolute path is allowed.")]
        [SynthesisDescription("Path for writing updated meshes and plugin with flagged NPC overrides.")]
        //public string OutputFolder { get; set; } = "";
        public string OutputFolder { get; set; } = "J:/OmegaLoTD/Tools/mods/FaceGenChecker Output";
    }
}
