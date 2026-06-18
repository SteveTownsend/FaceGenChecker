using IniParser;
using IniParser.Model.Configuration;
using IniParser.Parser;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Inis;
using Mutagen.Bethesda.Inis.DI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using nifly;
using Noggog;
using Noggog.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SSEForms = Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace FaceGenChecker
{
    public class MeshHandler
    {
        internal Settings.Settings _settings { get; }
        internal IPatcherState<ISkyrimMod, ISkyrimModGetter> _state { get; }

        // use backslashes to match paths in BSA
        public static readonly string MeshPrefix = "meshes\\actors\\character\\facegendata\\facegeom\\";
        public static readonly string FaceGenRootNode = "BSFaceGenNiNodeSkinned";
        public static readonly string DuplicateTag = "DUPLICATE";

        private HashSet<INpcGetter> npcs = new HashSet<INpcGetter>();
        private IDictionary<FormKey, ICollection<IHeadPartGetter>> headPartsByNpc = new Dictionary<FormKey, ICollection<IHeadPartGetter>>();
        private HashSet<HeadPart.TypeEnum> raceHeadParts = new HashSet<HeadPart.TypeEnum>
        {
            HeadPart.TypeEnum.Eyebrows,
            HeadPart.TypeEnum.Eyes,
            HeadPart.TypeEnum.Face,
            HeadPart.TypeEnum.Hair
        };

        private int countSkipped;
        private int countCandidates;
        internal int countGenerated;
        private int countFailed;

        internal MeshHandler(Settings.Settings settings, IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            _settings = settings;
            _state = state;
        }

        // no blacklist for NPC or Race at this time
        private bool IsIncluded(INpcGetter npc)
        {
            // ignore presets
            if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsCharGenFacePreset))
            {
                _settings.diagnostics.logger.WriteLine("Skip Preset {0}", npc.FormKey);
                return false;
            }
            // ignore player
            if (npc.FormKey.ID == 7 && npc.FormKey.ModKey.FileName.String.Equals("skyrim.esm", StringComparison.InvariantCultureIgnoreCase))
            {
                _settings.diagnostics.logger.WriteLine("Skip Player {0}", npc.FormKey);
                return false;
            }
            // ignore if this uses template NPC_ record's traits
            if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits))
            {
                _settings.diagnostics.logger.WriteLine("Skip NPC using template traits {0}", npc.FormKey);
                return false;
            }
            // ignore unless NPC_'s Race has head data
            IRaceGetter race = npc.Race.Resolve<IRaceGetter>(_state.LinkCache);
            if (race is null || !race.Flags.HasFlag(Race.Flag.FaceGenHead))
            {
                _settings.diagnostics.logger.WriteLine("Skip NPC with no facegen {0}", npc.FormKey);
                return false;
            }
            // ignore Ghost NPCs
            if (npc.HasKeyword("ActorTypeGhost", _state.LinkCache))
            {
                _settings.diagnostics.logger.WriteLine("Skip Ghost {0}", npc.FormKey);
                return false;
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
                _settings.diagnostics.logger.WriteLine("{0} in {1}", npc, context.ModKey.FileName);
                var headParts = new HashSet<IHeadPartGetter>();
                var updatedHeadParts = new HashSet<IHeadPartGetter>();
                // We must fill in any missing essential HDPTs from NPC's RACE
                var getFromRace = new HashSet<HeadPart.TypeEnum>(raceHeadParts);
                foreach (var headPartLink in npc.HeadParts)
                {
                    var headPart = headPartLink.Resolve(_state.LinkCache);
                    _settings.diagnostics.logger.WriteLine("  HeadPart {0}", headPart);
                    // every HDPT has to be checked for ZMerge/CK munging
                    int index = headPart.EditorID.IndexOf(DuplicateTag);
                    if (index != -1)
                    {
                        // Check for possible NPC makeover merge rename on save in CK. Save is required to resolve ZMerge HITMEs.
                        if (_settings.control.FixMergedEditorID)
                        {
                            if (!updatedHeadParts.Contains(headPart))
                            {
                                // If this HeadPart is in a merge and looks like it was renamed to avoid a clash with existing, 
                                // revert the EditorID in our Synthesis patch
                                if (Program.MergeInfo.MergeResults.Contains(headPart.FormKey.ModKey.FileName.String))
                                {
                                    string originalName = headPart.EditorID.Substring(0, index);
                                    HeadPart renamed = _state.PatchMod.HeadParts.GetOrAddAsOverride(headPart);
                                    renamed.EditorID = originalName;
                                    _settings.diagnostics.logger.WriteLine("  EditorID was {0}, now {1}", headPart.EditorID, originalName);
                                    // store the updated HeadPart for use in validation
                                    updatedHeadParts.Add(renamed);
                                }
                                else
                                {
                                    updatedHeadParts.Add(headPart);
                                }
                            }
                        }
                        else
                        {
                            _settings.diagnostics.logger.WriteLine("    possible duplicate renamed in CK", headPart.EditorID);
                        }
                    }
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
                        foreach (var headPartLink in raceHeadData.HeadParts)
                        {
                            if (headPartLink.Head.TryResolveSimpleContext(_state.LinkCache, out var raceHeadPart))
                            {
                                if (getFromRace.Remove((HeadPart.TypeEnum)raceHeadPart.Record.Type))
                                {
                                    _settings.diagnostics.logger.WriteLine("  RACE HeadPart {0}", raceHeadPart);
                                    headParts.Add(raceHeadPart.Record);
                                }
                            }
                        }
                    }
                }
                headPartsByNpc.Add(npc.FormKey, headParts);
                npcs.Add(npc);
                return true;
            }
            else
            {
                _settings.diagnostics.logger.WriteLine("Failed to resolve NPC {0}/{1:X8}", npc.FormKey.ModKey.FileName, npc.FormKey.ID);
                return false;
            }
        }


        public void DoMesh(NifFile nif, string originalPath, string newPath, INpcGetter npc)
        {
            try
            {
                // match head parts with the winning NPC record's list
                _settings.diagnostics.logger.WriteLine("Check HeadParts in NIF {0} vs {1}", originalPath, npc);
                var headParts = headPartsByNpc[npc.FormKey];
                using var rootNode = nif.FindBlockByNameNiNode(FaceGenRootNode);
                if (rootNode is null)
                    return;
                UInt32 mismatches = 0;
                foreach (var headPart in headParts)
                {
                    using var headPartNode = nif.FindBlockByNameNiNode(headPart.EditorID);
                    if (headPartNode is null)
                    {
                        _settings.diagnostics.logger.WriteLine("{0} HeadPart {1} not matched in NIF", npc, headPart.EditorID);
                        ++mismatches;
                    }
                    else
                    {
                        _settings.diagnostics.logger.WriteLine("{0} HeadPart {1} matched in NIF matched", npc, headPart.EditorID);
                    }
                }

                //using var header = nif.GetHeader();
                //using niflycpp.BlockCache blockCache = new niflycpp.BlockCache(header);
                //using var childNodes = rootNode.GetChildren().GetRefs();
                //foreach (var childNode in childNodes)
                //{
                //    using (childNode)
                //    {
                //        using NiAVObject nodeBlock = blockCache!.EditableBlockById<NiAVObject>(childNode.index);
                //        if (nodeBlock != null)
                //        {
                //            using var blockName = nodeBlock.name;
                //            bool headPartFound = false;
                //            var headPartName = blockName.get();
                //            foreach (var headPart in headParts)
                //            {
                //                headPartFound = headPart.EditorID == headPartName;
                //                if (headPartFound)
                //                    break;
                //            }
                //            if (!headPartFound)
                //            {
                //                _settings.diagnostics.logger.WriteLine("{0} HeadPart {1} in NIF not matched", npc, headPartName);
                //                ++mismatches;
                //            }
                //            else
                //            {
                //                _settings.diagnostics.logger.WriteLine("{0} HeadPart {1} in NIF matched", npc, headPartName);
                //            }
                //        }
                //    }
                //}
                // all plugin headparts must be present in the NIF, while the NIF may contain 'defaults' not present in the NPC_ record
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
                Interlocked.Increment(ref countFailed);
                _settings.diagnostics.logger.WriteLine("Exception processing {0}: {1}", originalPath, e.GetBaseException());
            }
        }

        /* engine reverts to defaults if it can't read files (the archaic GetPrivateProfile api is used), we might as well 
         * throw and force the user to sort out their stuff */
        //internal static string? ReadIniValue(FileIniDataParser a_parser, FilePath a_path, string a_section, string a_key)
        //{
        //    var data = a_parser.ReadData(new StreamReader(IFileSystemExt.DefaultFilesystem.File.OpenRead(a_path)));

        //    var section = data[a_section];
        //    if (section != null)
        //    {
        //        return section[a_key];
        //    }
        //    else
        //    {
        //        return null;
        //    }
        //}

        //// get a value from the winning mod ini override or Skyrim.ini if none exist
        //internal static string? GetWinningIniValue(GameRelease a_gameRelease, string a_section, string a_key)
        //{
        //    IniParserConfiguration parserConfig = new()
        //    {
        //        AllowDuplicateKeys = true,
        //        AllowDuplicateSections = true,
        //        AllowKeysWithoutSection = true,
        //        AllowCreateSectionsOnFly = true,
        //        CaseInsensitive = true,
        //        SkipInvalidLines = true,
        //    };
        //    var parser = new FileIniDataParser(new IniDataParser(parserConfig));

        //    foreach (var e in ScriptLess.PatcherState.LoadOrder.PriorityOrder)
        //    {
        //        if (!e.Enabled)
        //        {
        //            continue;
        //        }

        //        FilePath path = Path.Combine(ScriptLess.PatcherState.DataFolderPath, e.ModKey.Name + ".ini");

        //        if (!path.CheckExists())
        //        {
        //            continue;
        //        }

        //        var value = ReadIniValue(parser, path, a_section, a_key);
        //        if (value != null)
        //        {
        //            return value;
        //        }
        //    }

        //    FilePath basePath = Ini.GetTypicalPath(a_gameRelease);

        //    return ReadIniValue(parser, basePath, a_section, a_key);
        //}

        //public enum ResourceArchiveList
        //{
        //    Primary,
        //    Secondary
        //};

        //// retrieve a list, parse comma-delimited filenames, prune zero-length strings and non-existent paths and return as FilePath list
        //internal static List<FilePath>? GetResourceArchiveList(GameRelease a_gameRelease, ResourceArchiveList a_list)
        //{
        //    string key = a_list == ResourceArchiveList.Secondary ?
        //        "sResourceArchiveList2" :
        //        "sResourceArchiveList";

        //    return
        //        GetWinningIniValue(a_gameRelease, "Archive", key)?
        //        .Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        //        .Select(x => new FilePath(Path.Combine(ScriptLess.PatcherState.DataFolderPath, x)))
        //        .Where(x => x.CheckExists())
        //        .ToList();
        //}

        //internal static List<FilePath> GetBaseArchivePaths(GameRelease a_gameRelease)
        //{
        //    var l1 = GetResourceArchiveList(a_gameRelease, ResourceArchiveList.Primary);
        //    var l2 = GetResourceArchiveList(a_gameRelease, ResourceArchiveList.Secondary);

        //    return l1.EmptyIfNull().And(l2.EmptyIfNull()).ToList();
        //}

        //internal static List<FilePath> GetPossibleModArchives(GameRelease a_gameRelease, ModKey a_modKey)
        //{
        //    var ext = Archive.GetExtension(a_gameRelease);

        //    return new()
        //    {
        //        Path.Combine(ScriptLess.PatcherState.DataFolderPath, a_modKey.Name + ext),
        //        Path.Combine(ScriptLess.PatcherState.DataFolderPath, a_modKey.Name + " - Textures" + ext)
        //    };
        //}

        //// get archive path list according to load order
        //internal static List<FilePath> GetOrderedArchivePaths(GameRelease a_gameRelease)
        //{
        //    var result = GetBaseArchivePaths(a_gameRelease);

        //    ScriptLess.PatcherState.LoadOrder.ListedOrder.ForEach(x =>
        //    {
        //        if (x.Enabled)
        //        {
        //            result.AddRange(
        //                GetPossibleModArchives(a_gameRelease, x.ModKey)
        //                .Where(y => y.CheckExists() && !result.Contains(y)));
        //        }
        //    });

        //    return result;
        //}

        //// get priority archive path list 
        //internal static List<FilePath> GetPriorityArchivePaths(GameRelease a_gameRelease)
        //{
        //    var result = GetOrderedArchivePaths(a_gameRelease);

        //    result.Reverse();

        //    return result;
        //}

        // Mesh Generation logic originally from 'AllGUD Weapon Mesh Generator.pas'
        internal void ProcessMeshes()
        {
            // no op if empty
            if (npcs.Count == 0)
            {
                _settings.diagnostics.logger.WriteLine("No NPCs found");
                return;
            }
            IDictionary<string, INpcGetter> bsaFiles = new ConcurrentDictionary<string, INpcGetter>();
            int totalNPCs = npcs.Count;

            IDictionary<INpcGetter, byte> looseDone = new ConcurrentDictionary<INpcGetter, byte>();
            Parallel.ForEach(npcs, npc =>
            {
                // loose file wins over BSA contents
                string relativePath = String.Format("{0}{1}\\{2:X8}.nif", MeshPrefix, npc.FormKey.ModKey.FileName, npc.FormKey.ID);
                string fullPath = String.Format("{0}/{1}", _settings.paths.ConflictWinnerLocation, relativePath);
                string newFile = String.Format("{0}\\{1}{2}\\{3:X8}.nif", _settings.paths.OutputFolder, MeshPrefix, npc.FormKey.ModKey.FileName, npc.FormKey.ID);
                if (File.Exists(fullPath))
                {
                    _settings.diagnostics.logger.WriteLine("Found mesh for {0}/{1:X8} in loose file {2}", npc.FormKey.ModKey.Name, npc.FormKey.ID, fullPath);

                    using NifFile nif = new NifFile();
                    nif.Load(fullPath);
                    DoMesh(nif, relativePath, newFile, npc);
                    looseDone.Add(npc, 1);
                }
                else
                {
                    // check for this file in archives
                    _settings.diagnostics.logger.WriteLine("Search BSAs for NPC {0}/{1:X8} with mesh file {2}", npc.FormKey.ModKey.FileName, npc.FormKey.ID, relativePath);
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

                    archivePaths.ForEach(x => _settings.diagnostics.logger.WriteLine("\t{0}", x));
                }
                // Introspect all known BSAs to locate meshes not found as loose files. Dups are ignored - first find wins.
                foreach (var bsaFile in archivePaths)
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
            }

            _settings.diagnostics.logger.WriteLine("Generated {0}, Candidates {1}, Skipped {2}, Failed {3}",
                countGenerated, countCandidates, countSkipped, countFailed);
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