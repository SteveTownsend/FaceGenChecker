using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Inis.DI;
using System.Threading.Tasks;
using SSEForms = Mutagen.Bethesda.FormKeys.SkyrimSE;
using nifly;
using IniParser;
using IniParser.Model.Configuration;
using IniParser.Parser;
using Mutagen.Bethesda.Inis;
using Noggog;

namespace FaceGenChecker
{
    public class MeshHandler
    {
        //private class TargetMeshInfo
        //{
        //    public readonly string originalName;
        //    public readonly ModelType modelType;
        //    public TargetMeshInfo(string name, ModelType model)
        //    {
        //        originalName = name;
        //        modelType = model;
        //    }
        //}
        internal Settings _settings { get; }
        internal IPatcherState<ISkyrimMod, ISkyrimModGetter> _state { get; }

        public static readonly string MeshPrefix = "meshes/actors/character/facegendata/facegeom/";

        private HashSet<FormKey> npcs = new HashSet<FormKey>();

        private int countSkipped;
        private int countCandidates;
        internal int countGenerated;
        private int countFailed;

        internal MeshHandler(Settings settings, IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            _settings = settings;
            _state = state;
        }

        private bool DoNPC(INpcGetter npc)
        {
            if (npc.ToLink<INpcGetter>().TryResolveSimpleContext(_state.LinkCache, out var context))
            {
                _settings.diagnostics.logger.WriteLine("NPC {0}/{1:X8} in {2}", npc.FormKey.ModKey.FileName, npc.FormKey.ID, context.ModKey.FileName);
                // load the NPC's winning NIF
                foreach (var headPartLink in npc.HeadParts)
                {
                    var headPart = headPartLink.Resolve(_state.LinkCache);
                    _settings.diagnostics.logger.WriteLine("  HeadPart {0}/{1:X8}/{2}", headPart.FormKey.ModKey.FileName, headPart.FormKey.ID, headPart.EditorID);
                }
                return true;
            }
            else
            {
                _settings.diagnostics.logger.WriteLine("Failed to resolve NPC {0}/{1:X8}", npc.FormKey.ModKey.FileName, npc.FormKey.ID);
                return false;
            }
        }


        //public void GenerateMesh(NifFile nif, string originalPath, string newPath, ModelType modelType)
        //{
        //    try
        //    {
        //        modelType = FinalizeModelType(nif, originalPath, modelType);

        //        WeaponType weaponType = weaponTypeByModelType[modelType];
        //        if (weaponType == WeaponType.OneHandMelee ||
        //            (weaponType == WeaponType.TwoHandMelee && _settings.meshes.Accept2HWeapons))
        //        {
        //            // TODO selective patching by weapon type would need a filter here
        //            Interlocked.Increment(ref countCandidates);
        //            using NifTransformer transformer = new NifTransformer(this, nif, originalPath, newPath, modelType, weaponType);
        //            transformer.Generate();
        //        }
        //        else
        //        {
        //            _settings.diagnostics.logger.WriteLine("Skip {0}, incorrect WeaponType {1}", originalPath, weaponType);
        //            Interlocked.Increment(ref countSkipped);
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        Interlocked.Increment(ref countFailed);
        //        _settings.diagnostics.logger.WriteLine("Exception processing {0}: {1}", originalPath, e.GetBaseException());
        //    }
        //}

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
            IDictionary<string, FormKey> bsaFiles = new Dictionary<string, FormKey>();
            int totalNPCs = npcs.Count;

            HashSet<FormKey> looseDone = new HashSet<FormKey>();
            Parallel.ForEach(npcs, npc =>
            {
                // loose file wins over BSA contents
                string originalFile = String.Format("{0}{1}/{2}/{3:X8}.nif", _settings.paths.ConflictWinnerLocation, MeshPrefix, npc.ModKey.FileName, npc.ID);
                //string newFile = _settings.paths.OutputFolder + MeshPrefix + kv.Key;
                if (File.Exists(originalFile))
                {
                    _settings.diagnostics.logger.WriteLine("Found mesh for {0}/{1:X8} in loose file {2}", npc.ModKey.Name, npc.ID, originalFile);

                    using NifFile nif = new NifFile();
                    nif.Load(originalFile);
                    //GenerateMesh(nif, originalFile, newFile, kv.Value.modelType);
                    looseDone.Add(npc);
                }
                else
                {
                    // check for this file in archives
                    bsaFiles.Add(originalFile.ToLower(), npc);
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
                        {
                            try
                            {
                                //                                string rawPath = bsaFiles[bsaMesh.Path.ToLower()];
                                //                                TargetMeshInfo meshInfo = targetMeshes[rawPath];

                                if (!bsaDone.TryAdd(bsaMesh.Path, bsaFile.Path))
                                {
                                    _settings.diagnostics.logger.WriteLine("Mesh {0} from BSA {1} already processed from BSA {2}", bsaMesh.Path, bsaFile.Path, bsaDone[bsaMesh.Path]);
                                    return;
                                }

                                // Load NIF from stream via String - must rewind first
                                byte[] bsaData = bsaMesh.GetBytes();
                                using vectoruchar bsaBytes = new vectoruchar(bsaData);

                                using (var nif = new NifFile(bsaBytes))
                                {
                                    _settings.diagnostics.logger.WriteLine("Transform mesh {0} from BSA {1}", bsaMesh.Path, bsaFile);
                                    string newFile = _settings.paths.OutputFolder + bsaMesh.Path;
                                    //GenerateMesh(nif, bsaMesh.Path, newFile, meshInfo.modelType);
                                }
                            }
                            catch (Exception e)
                            {
                                _settings.diagnostics.logger.WriteLine("Exception on mesh {0} from BSA {1}: {2}", bsaMesh.Path, bsaFile, e.GetBaseException());
                            }
                        });
                }
            }

            //var missingFiles = targetMeshes.Where(kv => !looseDone.ContainsKey(kv.Key) && !bsaDone.ContainsKey(kv.Key)).ToList();
            //foreach (var mesh in missingFiles)
            //{
            //    _settings.diagnostics.logger.WriteLine("Referenced Mesh {0} not found loose or in BSA", mesh.Key);
            //}
            //_settings.diagnostics.logger.WriteLine("{0} total meshes: found {1} Loose, {2} in BSA, {3} missing files",
            //    targetMeshes.Count, looseDone.Count, bsaDone.Count, missingFiles.Count);
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