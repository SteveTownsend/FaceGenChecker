using Mutagen.Bethesda.Synthesis.Settings;
using System;
using System.Collections.Generic;
using System.Text;

namespace FaceGenChecker.Settings
{
    public class Control
    {
        [SynthesisSettingName("Re-duplicate HDPT EditorId")]
        [SynthesisTooltip("Zmerge causing HITMEs, fixed by Creation Kit's Editor ID de-duplication, may result in NIF-vs-plugin EditorID matching. This attempts to revert affected HDPT records to the NPC Overhaul's original EditorID.")]
        [SynthesisDescription("Correct NIF-HDPT matching after ZMerge.")]
        public bool FixMergedEditorID { get; set; } = true;

    }
}
