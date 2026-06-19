using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using nifly;
using Noggog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FaceGenChecker
{
    public class MeshHandler
    {
        internal Settings.Settings _settings { get; }
        internal IPatcherState<ISkyrimMod, ISkyrimModGetter> _state { get; }
        private ImmutableLoadOrderLinkUsageCache _references;

        // use backslashes to match paths in BSA
        public static readonly string _MeshPrefix = "meshes\\actors\\character\\facegendata\\facegeom\\";
        public static readonly string _FaceGenRootNode = "BSFaceGenNiNodeSkinned";
        public static readonly string _DuplicateTag = "DUPLICATE";

        private HashSet<INpcGetter> _npcs = new HashSet<INpcGetter>();
        private IDictionary<FormKey, ICollection<IHeadPartGetter>> _headPartsByNpc = new Dictionary<FormKey, ICollection<IHeadPartGetter>>();
        private HashSet<HeadPart.TypeEnum> _raceHeadParts = new HashSet<HeadPart.TypeEnum>
        {
            HeadPart.TypeEnum.Eyebrows,
            HeadPart.TypeEnum.Eyes,
            HeadPart.TypeEnum.Face,
            HeadPart.TypeEnum.Hair
        };
        HashSet<FormKey> _checkedHeadPartDuplicates = new HashSet<FormKey>();

        private int _countSkipped;
        private int _countCandidates;
        internal int _countGenerated;
        private int _countFailed;

        internal MeshHandler(Settings.Settings settings, IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            _settings = settings;
            _state = state;
            _references = new ImmutableLoadOrderLinkUsageCache(_state.LinkCache);
        }

        // no blacklist for NPC or Race at this time
        private bool IsIncluded(INpcGetter npc)
        {
            // ignore presets
            if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsCharGenFacePreset))
            {
                _settings.diagnostics.logger.WriteLine("Skip {0} - Preset", npc.FormKey);
                return false;
            }
            // ignore player
            if (npc.FormKey.ID == 7 && npc.FormKey.ModKey.FileName.String.Equals("skyrim.esm", StringComparison.InvariantCultureIgnoreCase))
            {
                _settings.diagnostics.logger.WriteLine("Skip {0} - Player", npc);
                return false;
            }
            // ignore if this uses template NPC_ record's traits
            if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits))
            {
                _settings.diagnostics.logger.WriteLine("Skip {0} - uses template traits", npc);
                return false;
            }
            // ignore Ghost NPCs
            if (npc.HasKeyword("ActorTypeGhost", _state.LinkCache))
            {
                _settings.diagnostics.logger.WriteLine("Skip {0} - Ghost", npc);
                return false;
            }
            // ignore unless NPC_'s Race has head data
            IRaceGetter race = npc.Race.Resolve<IRaceGetter>(_state.LinkCache);
            if (race is null || !race.Flags.HasFlag(Race.Flag.FaceGenHead))
            {
                _settings.diagnostics.logger.WriteLine("Skip {0} - no facegen in {1}", npc, race);
                return false;
            }
            // skip if RACE record has no Head Parts
            if (race.HeadData is null)
            {
                _settings.diagnostics.logger.WriteLine("Skip {0} - no headparts in {1}", npc, race);
                return false;
            }
            // Do not process unused NPCs
            var npcUsers = _references.GetUsagesOf(npc);
            if (npcUsers.UsageLinks.Count == 0)
            {
                _settings.diagnostics.logger.WriteLine("Skip {0} - unreferenced", npc);
                return false;
            }
            var usagesByNpc = _references.GetUsagesOf<INpcGetter>(npc).UsageLinks;
            foreach (var usageByNpc in usagesByNpc)
            {
                // referencing NPC must be in use to be of interest
                if (_references.GetUsagesOf(usageByNpc).UsageLinks.Count == 0)
                {
                    continue;
                }
                if (usageByNpc.TryResolveSimpleContext(_state.LinkCache, out var winningUser) &&
                    winningUser.Record.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits))
                {
                    _settings.diagnostics.logger.WriteLine("Keep {0} - template traits used in {1}", npc, winningUser);
                    return true;
                }
            }
            if (_references.GetUsagesOf<IPlacedNpcGetter>(npc).UsageLinks.Count > 0)
            {
                _settings.diagnostics.logger.WriteLine("Keep {0} - has Placed Actor(s)", npc);
                return true;
            }
            if (_references.GetUsagesOf<ILeveledNpcGetter>(npc).UsageLinks.Count > 0)
            {
                _settings.diagnostics.logger.WriteLine("Keep {0} - uses by Leveled NPC(s)", npc);
                return true;
            }
            return true;
        }

        private bool DoNPC(INpcGetter npc)
        {
            if (npc.ToLink<INpcGetter>().TryResolveSimpleContext(_state.LinkCache, out var context))
            {
	            if (!IsIncluded(npc))
                {
                    return false;
                }
                _settings.diagnostics.logger.WriteLine("{0} winning override in {1}", npc, context.ModKey.FileName);
                var headParts = new HashSet<IHeadPartGetter>();
                // We must fill in any missing essential HDPTs from NPC's RACE
                var getFromRace = new HashSet<HeadPart.TypeEnum>(_raceHeadParts);
                foreach (var headPartLink in npc.HeadParts)
                {
                    var headPart = headPartLink.Resolve(_state.LinkCache);
                    _settings.diagnostics.logger.WriteLine("  {0}", headPart);
                    headParts.Add(headPart);
                    getFromRace.Remove((HeadPart.TypeEnum)headPart.Type);
                }
                // fill in gaps from RACE record
                if (getFromRace.Count > 0)
                {
                    if (npc.Race.TryResolveSimpleContext(_state.LinkCache, out var npcRace) && npcRace.Record.HeadData is not null)
                    {
                        var raceHeadData = npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female) ?
                            npcRace.Record.HeadData.Female : npcRace.Record.HeadData.Male;
                        if (raceHeadData is not null)
                        {
                            foreach (var headPartLink in raceHeadData.HeadParts)
                            {
                                if (headPartLink.Head.TryResolveSimpleContext(_state.LinkCache, out var raceHeadPart))
                                {
                                    if (getFromRace.Remove((HeadPart.TypeEnum)raceHeadPart.Record.Type))
                                    {
                                        _settings.diagnostics.logger.WriteLine("  RACE {0}", raceHeadPart.Record);
                                        headParts.Add(raceHeadPart.Record);
                                    }
                                }
                            }
                        }
                    }
                }
                _headPartsByNpc.Add(npc.FormKey, headParts);
                _npcs.Add(npc);
                return true;
            }
            else
            {
                _settings.diagnostics.logger.WriteLine("Failed to resolve {0}", npc);
                return false;
            }
        }


        public void DoMesh(NifFile nif, string originalPath, string newPath, INpcGetter npc)
        {
            try
            {
                // match head parts with the winning NPC record's list
                _settings.diagnostics.logger.WriteLine("Check HeadParts in NIF {0} vs {1}", originalPath, npc);
                var headParts = _headPartsByNpc[npc.FormKey];
                UInt32 mismatches = 0;
                IDictionary<IHeadPartGetter, string> tryMatchInNif = new Dictionary<IHeadPartGetter, string>();
                foreach (var headPart in headParts)
                {
                    // skip HDPT with no model
                    if (headPart.Model is null)
                    {
                        _settings.diagnostics.logger.WriteLine("{0} {1} skipped, no Model", npc, headPart);
                        continue;
                    }
                    using var headPartNode = nif.FindBlockByNameNiAVObject(headPart.EditorID);
                    if (headPartNode is null)
                    {
                        bool matched = false;
                        if (_settings.control.FixMergedEditorID)
                        {
                            // mismatch on HDPT may be due to ZMerge/CK EditorID munging, try to remediate
                            int index = headPart.EditorID.IndexOf(_DuplicateTag);
                            if (index != -1)
                            {
                                string originalName = headPart.EditorID.Substring(0, index);
                                if (!_checkedHeadPartDuplicates.TryGetValue(headPart.FormKey, out var existing))
                                {
                                    // Check for possible NPC makeover merge rename on save in CK. Save is required to resolve ZMerge HITMEs.

                                    // If this HeadPart is in a merge and looks like it was renamed to avoid a clash with existing, 
                                    // revert the EditorID in our Synthesis patch
                                    if (Program.MergeInfo.MergeResults.Contains(headPart.FormKey.ModKey.FileName.String))
                                    {
                                        // Heuristics:
                                        // - if original EditorID ended in a number it looks like the number gets nuked, so allow fuzzy compare
                                        // - if original EditorID did not end in a number, merged ID up to 'DUPLICATE' should match exactly and we use it
                                        using var originalHeadPart = nif.FindBlockByNameNiAVObject(originalName);
                                        if (originalHeadPart is not null)
                                        {
                                            _settings.diagnostics.logger.WriteLine("0} {1} EditorID was {2}, now {3}", headPart.EditorID, originalName);
                                            HeadPart renamed = _state.PatchMod.HeadParts.GetOrAddAsOverride(headPart);
                                            renamed.EditorID = originalName;
											matched = true;
                                        }
                                        else
                                        {
                                            tryMatchInNif.Add(headPart, originalName);
                                        }
                                    }
                                    _checkedHeadPartDuplicates.Add(headPart.FormKey);
                                }
                            }
                            else
                            {
                                _settings.diagnostics.logger.WriteLine("    possible duplicate renamed in CK", headPart.EditorID);
                            }
                        }
                        if (!matched)
                        {
                            _settings.diagnostics.logger.WriteLine("{0} {1} no match in NIF", npc, headPart);
                            ++mismatches;
                        }
                    }
                    else
                    {
                        _settings.diagnostics.logger.WriteLine("{0} {1} has match in NIF", npc, headPart);
                    }
                }
                // Check Editor ID fuzzy match vs NIF for merged HDPTs that are 'missing'
                if (tryMatchInNif.Count > 0)
                {
                    using var rootNode = nif.FindBlockByNameNiNode(_FaceGenRootNode);
                    if (rootNode is null)
                    {
                        _settings.diagnostics.logger.WriteLine("{0} NIF has no root node {1}", npc, _FaceGenRootNode);
                    }
                    else
                    {
                        using var header = nif.GetHeader();
                        using niflycpp.BlockCache blockCache = new niflycpp.BlockCache(header);
                        using var childNodes = rootNode.GetChildren().GetRefs();
                        foreach (var childNode in childNodes)
                        {
                            using (childNode)
                            {
                                using NiAVObject nodeBlock = header.GetBlockById(childNode.index) as NiAVObject;
                                if (nodeBlock != null)
                                {
                                    using var blockName = nodeBlock.name;
                                    var headPartName = blockName.get();
                                    foreach (var possibleDup in tryMatchInNif)
                                    {
                                        if (headPartName.StartsWith(possibleDup.Value))
                                        {
                                            _settings.diagnostics.logger.WriteLine("{0} HeadPart {1} in NIF fuzzy matched {2}", npc, headPartName, possibleDup.Value);
                                            --mismatches;
                                            
                                            tryMatchInNif.Remove(possibleDup.Key);
                                            if (!_checkedHeadPartDuplicates.Contains(possibleDup.Key.FormKey))
                                            {
                                                HeadPart renamed = _state.PatchMod.HeadParts.GetOrAddAsOverride(possibleDup.Key);
                                                renamed.EditorID = headPartName;
                                                _checkedHeadPartDuplicates.Add(possibleDup.Key.FormKey);
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                foreach (var fuzzyMatcher in tryMatchInNif)
                {
                    _settings.diagnostics.logger.WriteLine("{0} HeadPart {1} no fuzzy match on {2} in NIF", npc, fuzzyMatcher.Key, fuzzyMatcher.Value);
                }
                // all plugin headparts must be present in the NIF
                if (mismatches > 0)
                {
                    _settings.diagnostics.logger.WriteLine("{0} forwarded, headparts mismatched", npc);
                    _state.PatchMod.Npcs.GetOrAddAsOverride(npc);
                }
                else
                {
                    _settings.diagnostics.logger.WriteLine("{0} headpart match, should be OK in game", npc);
                }
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref _countFailed);
                _settings.diagnostics.logger.WriteLine("Exception processing {0}: {1}", originalPath, e.GetBaseException());
            }
        }
        internal void ProcessMeshes()
        {
            // no op if empty
            if (_npcs.Count == 0)
            {
                _settings.diagnostics.logger.WriteLine("No NPCs found");
                return;
            }
            IDictionary<string, INpcGetter> bsaFiles = new ConcurrentDictionary<string, INpcGetter>();
            int totalNPCs = _npcs.Count;

            IDictionary<INpcGetter, byte> looseDone = new ConcurrentDictionary<INpcGetter, byte>();
            Parallel.ForEach(_npcs, npc =>
            {
                // loose file wins over BSA contents
                string relativePath = String.Format("{0}{1}\\{2:X8}.nif", _MeshPrefix, npc.FormKey.ModKey.FileName, npc.FormKey.ID);
                string fullPath = String.Format("{0}/{1}", _settings.paths.ConflictWinnerLocation, relativePath);
                string newFile = String.Format("{0}\\{1}{2}\\{3:X8}.nif", _settings.paths.OutputFolder, _MeshPrefix, npc.FormKey.ModKey.FileName, npc.FormKey.ID);
                if (File.Exists(fullPath))
                {
                    _settings.diagnostics.logger.WriteLine("Found mesh for {0} in loose file {1}", npc, fullPath);

                    using NifFile nif = new NifFile();
                    nif.Load(fullPath);
                    DoMesh(nif, relativePath, newFile, npc);
                    looseDone.Add(npc, 1);
                }
                else
                {
                    // check for this file in archives
                    _settings.diagnostics.logger.WriteLine("Search BSAs for NPC {0} with mesh file {1}", npc, relativePath);
                    bsaFiles.Add(relativePath.ToLower(), npc);
                }
            });

            IDictionary<string, string> bsaDone = new ConcurrentDictionary<string, string>();
            if (bsaFiles.Count > 0)
            {
                var archivePaths = Archive.GetApplicableArchivePaths(_state.GameRelease, _settings.paths.ConflictWinnerLocation);

                // debug
                if (archivePaths.Count() > 0)
                {
                    _settings.diagnostics.logger.WriteLine("Processing {0} BSA files:", archivePaths.Count());

                    archivePaths.ForEach(x => _settings.diagnostics.logger.WriteLine("  {0}", x));
                }
                // Introspect all known BSAs to locate meshes not found as loose files. Dups are ignored - first find wins, so we scan from the end of the list
                foreach (var bsaFile in archivePaths.Reverse())
                {
                    var bsaReader = Archive.CreateReader(_state.GameRelease, bsaFile);
                    bsaReader.Files.AsParallel().
                        Where(candidate => bsaFiles.ContainsKey(candidate.Path.ToLower())).
                        ForAll(bsaMesh =>
                        //foreach (var bsaMesh in bsaReader.Files.Where(candidate => bsaFiles.ContainsKey(candidate.Path.ToLower())))
                        {
                            try
                            {
                                var npc = bsaFiles.GetOrDefault(bsaMesh.Path.ToLower());
                                //                                string rawPath = bsaFiles[bsaMesh.Path.ToLower()];
                                //                                TargetMeshInfo meshInfo = targetMeshes[rawPath];

                                if (!bsaDone.TryAdd(bsaMesh.Path, bsaFile.Path))
                                {
                                    _settings.diagnostics.logger.WriteLine("{0} from BSA {1} already processed from BSA {2}", bsaMesh.Path, bsaFile.Path, bsaDone[bsaMesh.Path]);
                                    return;
                                }

                                // Load NIF from stream via String - must rewind first
                                byte[] bsaData = bsaMesh.GetBytes();
                                using vectoruchar bsaBytes = new vectoruchar(bsaData);
                                using var nif = new NifFile(bsaBytes);

                                _settings.diagnostics.logger.WriteLine("Process {0} from BSA {1}", bsaMesh.Path, bsaFile);
                                string newFile = _settings.paths.OutputFolder + bsaMesh.Path;
                                DoMesh(nif, bsaMesh.Path, newFile, npc);
                            }
                            catch (Exception e)
                            {
                                _settings.diagnostics.logger.WriteLine("Exception on {0} from BSA {1}: {2}", bsaMesh.Path, bsaFile, e.GetBaseException());
                            }
                        }
                    );
                }
                // alter for any missed meshes
                foreach (var required in bsaFiles)
                {
                    if (!bsaDone.ContainsKey(required.Key))
                    {
                        _settings.diagnostics.logger.WriteLine(" No NIF found for NPC {0}", required.Value.FormKey);
                    }
                }
            }

            _settings.diagnostics.logger.WriteLine("Generated {0}, Candidates {1}, Skipped {2}, Failed {3}",
                _countGenerated, _countCandidates, _countSkipped, _countFailed);
        }

        internal void Analyze()
        {
            // inventory the meshes to be transformed
            foreach (var npc in Program.PatcherState.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>())
            {
                DoNPC(npc);
            }
        }
    }
}