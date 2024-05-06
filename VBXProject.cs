using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostyEditor;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using VBXProj.Parsers;
using App = Frosty.Core.App;

namespace VBXProj
{
    public static class VBXProject
    {
        public static bool IsLoaded
        {
            get
            {
                MainWindow frosty = null;

                Application.Current.Dispatcher.Invoke(delegate
                {
                    frosty = Application.Current.MainWindow as MainWindow;
                });
                return frosty?.Project.DisplayName == "New Project.fbproject" && CurrentProject.DisplayName != "New Project.vproj";
            }
        }
        
        #region Project Data
        
        public static int Version => 1005;
        public static VProject CurrentProject { get; set; }
        
        public static void WriteProject(string projectPath)
        {
            // Make sure to create our directory!
            FileInfo fi = new FileInfo(projectPath);
            if (fi.Directory != null && !fi.Directory.Exists) 
            { 
                Directory.CreateDirectory(fi.DirectoryName); 
            }

            StreamWriter writer = new StreamWriter(projectPath);
            
            //Write header
            writer.WriteLine("//=================================================//");
            writer.WriteLine("//");
            writer.WriteLine("// VBX Project File generated with VBXProject by Y wingpilot2");
            writer.WriteLine("//");
            writer.WriteLine("//=================================================//");
            writer.WriteLine("");
            
            writer.WriteLine($"Version {Version}");
            VProject currentProject = CurrentProject;
            currentProject.Version = Version;
            writer.WriteLine($"ItemsCount {App.AssetManager.GetModifiedCount()}");
            currentProject.ItemsCount = (int)App.AssetManager.GetModifiedCount();

            writer.Close();
        }

        public static bool LoadProject(string projectPath)
        {
            FileInfo fi = new FileInfo(projectPath);
            if (fi.Directory != null && !fi.Directory.Exists) 
            { 
                App.Logger.LogError("Not a valid project");
                return false;
            }

            VProject current = new VProject
            {
                Location = projectPath
            };
            
            StreamReader reader = new StreamReader(projectPath);

            string line = reader.ReadLine();
            while (line != null)
            {
                if (line.StartsWith("//") || string.IsNullOrEmpty(line))
                {
                    line = reader.ReadLine();
                    continue;
                }

                // TODO: Mod Settings
                switch (line.Split(' ')[0])
                {
                    case "Version":
                    {
                        int version = int.Parse(line.Split(' ')[1]);
                        if (version != Version)
                        {
                            MessageBoxResult result = FrostyMessageBox.Show(
                                "This VBX Project appears to be older then the current plugin version. If this is a legacy project, loading it could cause errors, are you sure you wish to continue?",
                                "VBX Project Manager", MessageBoxButton.YesNo);
                            if (result == MessageBoxResult.No) return false;
                        }

                        current.Version = version;
                    } break;
                    case "ItemsCount":
                    {
                        current.ItemsCount = int.Parse(line.Split(' ')[1]);
                    } break;
                }
                line = reader.ReadLine();
            }

            CurrentProject = current;
            return true;
        }

        #endregion

        #region Save

        public static void Save(string path)
        {
            // Are we saving to an existing project or a new one?
            FileInfo fi = new FileInfo(path);
            Debug.Assert(fi.Directory != null, "fi.DirectoryName != null");
            if (fi.Directory != null && !fi.Directory.Exists)
            {
                Directory.CreateDirectory(fi.DirectoryName);
            }

            bool needsRefresh = fi.Exists;
            
            FrostyTaskWindow.Show("Saving...", "", task =>
            {
                task.Update("Cleaning directory...");
                
                // Don't delete any files if this project does not exist yet
                // Otherwise you'll make the incompetant mistake I did...
                if (needsRefresh)
                {
                    foreach (string file in Directory.EnumerateFiles(fi.Directory.FullName, "*.vbx", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }
                    foreach (string file in Directory.EnumerateFiles(fi.Directory.FullName, "*.bdl", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }
                    foreach (string file in Directory.EnumerateFiles(fi.Directory.FullName, "*.res", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }
                    foreach (string file in Directory.EnumerateFiles(fi.Directory.FullName, "*.chunk", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }
                }
                
                task.Update("Writing Bundles...");
                SaveBundleEntries(fi.Directory.FullName);
                
                task.Update("Writing project...");
                WriteProject(path);

                #region VBX

                task.Update("Writing VBX...");
                SaveVbxEntries(fi.Directory.FullName);

                #endregion

                #region Res

                task.Update("Writing Res...");
                SaveResEntries(fi.Directory.FullName);
                WrittenRes.Clear();

                #endregion

                #region Chunks

                task.Update("Writing Chunks...");
                SaveChunkEntries(fi.Directory.FullName);
                WrittenRes.Clear();

                #endregion
                
                App.Logger.Log("Saved");
            });
        }

        #region Writing

        private static void SaveBundleEntries(string directory)
        {
            foreach (BundleEntry bundle in App.AssetManager.EnumerateBundles(modifiedOnly:true))
            {
                BundleWriter writer = new BundleWriter($@"{directory}\Bundles\{bundle.Name.Replace("/", "\\")}.bdl");
                writer.WriteBundle(bundle);
                writer.Dispose();
            }
        }

        private static void SaveVbxEntries(string directory)
        {
            VbxDataWriter dataWriter = new VbxDataWriter();
            foreach (EbxAssetEntry assetEntry in App.AssetManager.EnumerateEbx("", true))
            {
                string path = $@"{directory}\Vbx\{assetEntry.Name.Replace("/", "\\")}.vbx";
                dataWriter.WriteAsset(assetEntry, RemoveIllegalCharacters(path));
                if (assetEntry.HasModifiedData)
                {
                    assetEntry.ModifiedEntry.IsDirty = false;
                }

                if (assetEntry.LinkedAssets.Count != 0)
                {
                    NativeWriter writer = new NativeWriter(new FileStream(path.Replace(".vbx", ".bin"), FileMode.Create));
                    
                    // First write the entries
                    writer.Write(assetEntry.LinkedAssets.Count);
                    foreach (AssetEntry linkedAsset in assetEntry.LinkedAssets)
                    {
                        SaveLinkedAssets(linkedAsset, writer);
                    }
                }

                assetEntry.IsDirty = false;
            }
            
            dataWriter.Dispose();
        }
        
        #region Res

        private static readonly List<ResAssetEntry> WrittenRes = new List<ResAssetEntry>();
        public static void SaveResEntries(string directory)
        {
            foreach (ResAssetEntry entry in App.AssetManager.EnumerateRes(modifiedOnly:true))
            {
                if (WrittenRes.Contains(entry))
                    continue;
                
                string path = $@"{directory}\Res\{entry.Name.Replace("/", "\\")}.res";
                FileInfo fi = new FileInfo(RemoveIllegalCharacters(path));
                if (fi.Directory != null && !fi.Directory.Exists) 
                { 
                    Directory.CreateDirectory(fi.DirectoryName); 
                }

                NativeWriter writer = new NativeWriter(new FileStream(fi.FullName, FileMode.Create));
                WriteResEntry(writer, entry);

                if (entry.HasModifiedData)
                {
                    entry.ModifiedEntry.IsDirty = false;
                }

                entry.IsDirty = false;
                writer.Dispose();
            }
        }

        private static void WriteResEntry(NativeWriter writer, ResAssetEntry entry)
        {
            writer.Write(entry.IsAdded);
            writer.WriteNullTerminatedString(entry.Name);
            writer.Write(entry.ResRid);
            writer.Write(entry.ResType);
            writer.Write(entry.ResMeta);
                
            SaveLinkedResources(entry, writer);
            writer.Write(entry.AddedBundles.Count);
            foreach (int addedBundle in entry.AddedBundles)
            {
                // Write the name instead of the id in case the ID changes
                writer.WriteNullTerminatedString(App.AssetManager.GetBundleEntry(addedBundle).Name);
            }
                
            writer.Write(entry.HasModifiedData);
            if (entry.HasModifiedData)
            {
                writer.Write(entry.ModifiedEntry.Sha1);
                writer.Write(entry.ModifiedEntry.OriginalSize);
                if (entry.ModifiedEntry.ResMeta != null)
                {
                    writer.Write(entry.ModifiedEntry.ResMeta.Length);
                    writer.Write(entry.ModifiedEntry.ResMeta);
                }
                else
                {
                    // no res meta
                    writer.Write(0);
                }
                writer.WriteNullTerminatedString(entry.ModifiedEntry.UserData);

                byte[] buffer = entry.ModifiedEntry.Data;
                if (entry.ModifiedEntry.DataObject != null)
                {
                    ModifiedResource md = entry.ModifiedEntry.DataObject as ModifiedResource;
                    buffer = md.Save();
                }

                writer.Write(buffer.Length);
                writer.Write(buffer);
            }
        }

        #endregion

        #region Chunk

        private static readonly List<ChunkAssetEntry> WrittenChunks = new List<ChunkAssetEntry>();
        public static void SaveChunkEntries(string directory)
        {
            foreach (ChunkAssetEntry entry in App.AssetManager.EnumerateChunks(true))
            {
                if (WrittenChunks.Contains(entry))
                    continue;
                
                string path = $@"{directory}\Chunks\{entry.Name}.chunk";
                FileInfo fi = new FileInfo(RemoveIllegalCharacters(path));
                if (fi.Directory != null && !fi.Directory.Exists) 
                { 
                    Directory.CreateDirectory(fi.DirectoryName); 
                }

                NativeWriter writer = new NativeWriter(new FileStream(fi.FullName, FileMode.Create));
                WriteChunkEntry(writer, entry);

                entry.IsDirty = false;
                writer.Dispose();
            }
        }

        private static void WriteChunkEntry(NativeWriter writer, ChunkAssetEntry entry)
        {
            writer.Write(entry.IsAdded);
            writer.Write(entry.Id);
            writer.Write(entry.HasModifiedData ? entry.ModifiedEntry.H32 : entry.H32);
                
            writer.Write(entry.AddedBundles.Count);
            foreach (int bid in entry.AddedBundles)
            {
                writer.WriteNullTerminatedString(App.AssetManager.GetBundleEntry(bid).Name);
            }
                
            writer.Write(entry.HasModifiedData ? entry.ModifiedEntry.FirstMip : entry.FirstMip);

            writer.Write(entry.HasModifiedData);
            if (entry.HasModifiedData)
            {
                writer.Write(entry.ModifiedEntry.Sha1);
                writer.Write(entry.ModifiedEntry.LogicalOffset);
                writer.Write(entry.ModifiedEntry.LogicalSize);
                writer.Write(entry.ModifiedEntry.RangeStart);
                writer.Write(entry.ModifiedEntry.RangeEnd);
                writer.Write(entry.ModifiedEntry.AddToChunkBundle);
                writer.WriteNullTerminatedString(entry.ModifiedEntry.UserData);

                writer.Write(entry.ModifiedEntry.Data.Length);
                writer.Write(entry.ModifiedEntry.Data);
                entry.ModifiedEntry.IsDirty = false;
            }
        }

        #endregion

        #region Links

        private static void SaveLinkedResources(AssetEntry entry, NativeWriter writer)
        {
            writer.Write(entry.LinkedAssets.Count);
            foreach (AssetEntry linkedEntry in entry.LinkedAssets)
            {
                writer.WriteNullTerminatedString(linkedEntry.AssetType);
                if (linkedEntry is ChunkAssetEntry assetEntry)
                    writer.Write(assetEntry.Id);
                else
                    writer.WriteNullTerminatedString(linkedEntry.Name);
            }
        }

        private static void SaveLinkedAssets(AssetEntry entry, NativeWriter writer)
        {
            writer.WriteNullTerminatedString(entry.AssetType);
            if (entry is ResAssetEntry res)
            {
                WrittenRes.Add(res);
                writer.WriteNullTerminatedString(res.Name);
                WriteResEntry(writer, res);
            }
            else if (entry is ChunkAssetEntry chunk)
            {
                WrittenChunks.Add(chunk);
                writer.Write(chunk.Id);
                WriteChunkEntry(writer, chunk);
            }
            else
            {
                App.Logger.LogWarning("Vbx Project doesn't support linking asset of type {0}", entry.AssetType);
                return;
            }
            
            writer.Write(entry.LinkedAssets.Count);
            foreach (AssetEntry linkedAsset in entry.LinkedAssets)
            {
                SaveLinkedAssets(linkedAsset, writer);
            }
        }

        #endregion

        #endregion

        #endregion

        #region Load

        public static void Load(string path)
        {
            FileInfo fi = new FileInfo(path);
            if (!fi.Exists)
            {
                App.Logger.LogError("Specified path does not exist");
                return;
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            
            FrostyTaskWindow.Show("Loading...", "", task =>
            {
                task.Update("Reading project...");
                LoadProject(path);
                
                task.Update("Reading Bundles...");
                ReadBundle(fi.Directory.FullName);
                
                task.Update("Reading VBX...");
                ReadVbx(fi.Directory.FullName);
                
                task.Update("Reading Res...");
                LoadResEntries(fi.Directory.FullName);
                
                task.Update("Reading chunks...");
                LoadChunkEntries(fi.Directory.FullName);
                
                task.Update("Cleanup...");
                // TODO: proper linked assets
                foreach (BundleEntry bundle in App.AssetManager.EnumerateBundles(modifiedOnly:true))
                {
                    if (bundle.Type == BundleType.SharedBundle)
                        continue;
                    
                    bundle.Blueprint = App.AssetManager.GetEbxEntry(bundle.Name.Remove(0, 6));
                }
            });
            
            stopwatch.Stop();
            App.Logger.Log("Loaded project {1} in {0}", stopwatch.Elapsed.ToString(), CurrentProject.DisplayName);
        }

        #region Reading

        private static void ReadBundle(string projectDir)
        {
            foreach (string file in Directory.EnumerateFiles(projectDir, "*.bdl", SearchOption.AllDirectories))
            {
                BundleReader reader = new BundleReader(file);
                reader.ReadBundle();
                reader.Dispose();
            }
        }

        private static void ReadVbx(string projectDir)
        {
            VbxDataReader dataReader = new VbxDataReader();
            foreach (string file in Directory.EnumerateFiles(projectDir, "*.vbx", SearchOption.AllDirectories))
            {
                EbxAssetEntry asset = dataReader.ReadAsset(file);
                if (File.Exists(file.Replace(".vbx", ".bin")))
                {
                    NativeReader reader = new NativeReader(new FileStream(file.Replace(".vbx", ".bin"), FileMode.Open));
                    int count = reader.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        LoadLinkedAssets(reader, asset, projectDir);
                    }
                }
            }

            // TODO: We should load all entries as "husks" before ever reading raw vbx data
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx(modifiedOnly:true))
            {
                // Don't bother
                if (entry.ModifiedEntry.DependentAssets.Count == 0)
                    continue;
                
                List<Guid> invalids = new List<Guid>();
                foreach (Guid dependency in entry.EnumerateDependencies())
                {
                    if (App.AssetManager.GetEbxEntry(dependency) != null)
                        continue;
                    
                    App.Logger.LogWarning("{0} contained an invalid reference!", entry.Name);
                    invalids.Add(dependency);
                }

                foreach (Guid invalid in invalids)
                {
                    entry.ModifiedEntry.DependentAssets.Remove(invalid);
                }
            }
            
            dataReader.Dispose();
        }

        #region Chunks

        public static void LoadChunkEntries(string projectDir)
        {
            foreach (string file in Directory.EnumerateFiles(projectDir, "*.chunk", SearchOption.AllDirectories))
            {
                NativeReader reader = new NativeReader(new FileStream(file, FileMode.Open));
                ReadChunk(reader, file);
            }
        }

        private static ChunkAssetEntry ReadChunk(NativeReader reader, string file)
        {
            bool isAdded = reader.ReadBoolean();
            Guid guid = reader.ReadGuid();
            int h32 = reader.ReadInt();

            ChunkAssetEntry entry;
            if (isAdded)
            {
                entry = new ChunkAssetEntry
                {
                    Id = guid,
                    H32 = h32
                };
                App.AssetManager.AddChunk(entry);
            }
            else
            {
                entry = App.AssetManager.GetChunkEntry(guid);
                if (entry == null)
                {
                    App.Logger.LogError("Unable to read chunk: {0}", file);
                    return null;
                }
            }
                
            List<int> bundles = new List<int>();

            int length = reader.ReadInt();
            for (int j = 0; j < length; j++)
            {
                string bundleName = reader.ReadNullTerminatedString();
                int bid = App.AssetManager.GetBundleId(bundleName);
                if (bid != -1)
                    bundles.Add(bid);
            }
                
            Sha1 sha1 = Sha1.Zero;
            uint logicalOffset = 0;
            uint logicalSize = 0;
            uint rangeStart = 0;
            uint rangeEnd = 0;
            int firstMip = -1;
            bool addToChunkBundles = false;
            string userData = "";
            byte[] data = null;

            firstMip = reader.ReadInt();

            bool isModified = true;
            isModified = reader.ReadBoolean();

            if (isModified)
            {
                sha1 = reader.ReadSha1();
                logicalOffset = reader.ReadUInt();
                logicalSize = reader.ReadUInt();
                rangeStart = reader.ReadUInt();
                rangeEnd = reader.ReadUInt();

                addToChunkBundles = reader.ReadBoolean();
                userData = reader.ReadNullTerminatedString();

                data = reader.ReadBytes(reader.ReadInt());
            }

            entry.AddedBundles.AddRange(bundles);
            if (isModified)
            {
                entry.ModifiedEntry = new ModifiedAssetEntry
                {
                    Sha1 = sha1,
                    LogicalOffset = logicalOffset,
                    LogicalSize = logicalSize,
                    RangeStart = rangeStart,
                    RangeEnd = rangeEnd,
                    FirstMip = firstMip,
                    H32 = h32,
                    AddToChunkBundle = addToChunkBundles,
                    UserData = userData,
                    Data = data
                };
                entry.OnModified();
            }
            else
            {
                entry.H32 = h32;
                entry.FirstMip = firstMip;
            }

            return entry;
        }

        #endregion

        #region Res

        public static void LoadResEntries(string projectDir)
        {
            foreach (string file in Directory.EnumerateFiles(projectDir, "*.res", SearchOption.AllDirectories))
            {
                NativeReader reader = new NativeReader(new FileStream(file, FileMode.Open));
                ReadRes(reader, file, projectDir);

                reader.Dispose();
            }
        }

        private static ResAssetEntry ReadRes(NativeReader reader, string file, string projectDir)
        {
            bool isAdded = reader.ReadBoolean();
            string name = reader.ReadNullTerminatedString();
            ulong rid = reader.ReadULong();
            uint type = reader.ReadUInt();
            byte[] meta = reader.ReadBytes(0x10);

            ResAssetEntry entry;
            if (isAdded)
            {
                string realName = file.Replace($"{projectDir}\\Res\\", "").Replace("\\", "/").Replace(".res", "").Trim().ToLower();
                App.AssetManager.AddRes(new ResAssetEntry
                {
                    Name = realName,
                    ResRid = rid,
                    ResType = type,
                    ResMeta = meta
                });
                entry = App.AssetManager.GetResEntry(realName);
            }
            else
            {
                entry = App.AssetManager.GetResEntry(name);
            }

            if (entry == null)
            {
                App.Logger.LogError("Unable to read res file: {0}", file);
                return null;
            }

            List<AssetEntry> linkedEntries = LoadLinkedResources(reader);
            List<int> bundles = new List<int>();

            int length = reader.ReadInt();
            for (int j = 0; j < length; j++)
            {
                string bundleName = reader.ReadNullTerminatedString();
                int bid = App.AssetManager.GetBundleId(bundleName);
                if (bid != -1)
                    bundles.Add(bid);
            }

            entry.LinkedAssets.AddRange(linkedEntries);
            entry.AddedBundles.AddRange(bundles);
            bool isModified = reader.ReadBoolean();

            Sha1 sha1 = Sha1.Zero;
            long originalSize = 0;
            byte[] resMeta = null;
            byte[] data = null;
            string userData = "";

            if (isModified)
            {
                sha1 = reader.ReadSha1();
                originalSize = reader.ReadLong();

                length = reader.ReadInt();
                if (length > 0)
                    resMeta = reader.ReadBytes(length);

                userData = reader.ReadNullTerminatedString();

                data = reader.ReadBytes(reader.ReadInt());
                    
                entry.ModifiedEntry = new ModifiedAssetEntry
                {
                    Sha1 = sha1,
                    OriginalSize = originalSize,
                    ResMeta = resMeta,
                    UserData = userData
                };

                if (sha1 == Sha1.Zero)
                {
                    // store as modified resource data object
                    entry.ModifiedEntry.DataObject = ModifiedResource.Read(data);
                }
                else
                {
                    if (!entry.IsAdded && App.PluginManager.GetCustomHandler((ResourceType)entry.ResType) != null)
                    {
                        // @todo: throw some kind of error here
                    }

                    // store as normal data
                    entry.ModifiedEntry.Data = data;
                }

                entry.OnModified();
            }

            return entry;
        }

        #endregion
        
        private static List<AssetEntry> LoadLinkedResources(NativeReader reader)
        {
            int numItems = reader.ReadInt();
            List<AssetEntry> linkedEntries = new List<AssetEntry>();

            for (int i = 0; i < numItems; i++)
            {
                string type = reader.ReadNullTerminatedString();
                if (type == "ebx")
                {
                    string name = reader.ReadNullTerminatedString();
                    EbxAssetEntry ebxEntry = App.AssetManager.GetEbxEntry(name);
                    if (ebxEntry != null)
                        linkedEntries.Add(ebxEntry);
                }
                else if (type == "res")
                {
                    string name = reader.ReadNullTerminatedString();
                    ResAssetEntry resEntry = App.AssetManager.GetResEntry(name);
                    if (resEntry != null)
                        linkedEntries.Add(resEntry);
                }
                else if (type == "chunk")
                {
                    Guid id = reader.ReadGuid();
                    ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(id);
                    if (chunkEntry != null)
                        linkedEntries.Add(chunkEntry);
                }
                else
                {
                    string name = reader.ReadNullTerminatedString();
                    AssetEntry customEntry = App.AssetManager.GetCustomAssetEntry(type, name);
                    if (customEntry != null)
                        linkedEntries.Add(customEntry);
                }
            }

            return linkedEntries;
        }
        
        private static void LoadLinkedAssets(NativeReader reader, AssetEntry source, string projectDir)
        {
            string type = reader.ReadNullTerminatedString();
            AssetEntry entry = null;
            switch (type)
            {
                case "res":
                {
                    string name = reader.ReadNullTerminatedString();
                    entry = ReadRes(reader, name, projectDir);
                } break;
                case "chunk":
                {
                    entry = ReadChunk(reader, reader.ReadGuid().ToString());
                } break;
            }

            if (entry == null)
            {
                App.Logger.LogError("Failed to read linked assets for {0}!", source.Name);
            }

            source.LinkAsset(entry);
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                LoadLinkedAssets(reader, entry, projectDir);
            }
        }

        #endregion

        #endregion

        #region Utils

        // I don't actually know what characters are illegal, so most of these are probs fine
        private static char[] _illegalChars = new[]
        {
            '!',
            '?',
            '/',
            '*',
            '$',
            '"',
            '\'',
            '[',
            ']',
            '@'
        };
        private static string RemoveIllegalCharacters(string str)
        {
            if (str.Any(c => _illegalChars.Contains(c)))
            {
                foreach (char illegalChar in _illegalChars)
                {
                    
                    str = str.Replace($"{illegalChar}", "");
                }
            }

            return str;
        }

        #endregion

        static VBXProject()
        {
            CurrentProject = new VProject
            {
                Location = "New Project.vproj",
                ItemsCount = 0,
                Version = Version
            };
        }
    }

    public struct VProject
    {
        public string Location { get; set; }
        public string DisplayName => Location.Split('\\').Last();
        public int ItemsCount { get; set; }
        public int Version { get; set; }
    }
}