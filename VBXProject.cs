using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Windows;
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
        #region Project Data
        
        public static int Version => 1001;
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
            
            FrostyTaskWindow.Show("Saving...", "", task =>
            {
                task.Update("Cleaning directory...");
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
                
                task.Update("Writing Bundles...");
                WriteBundles(fi.Directory.FullName);
                
                task.Update("Writing project...");
                WriteProject(path);

                #region VBX

                task.Update("Writing VBX...");
                WriteVbx(fi.Directory.FullName);

                #endregion

                #region Res

                task.Update("Writing Res...");
                WriteRes(fi.Directory.FullName);

                #endregion

                #region Chunks

                task.Update("Writing Chunks...");
                WriteChunk(fi.Directory.FullName);

                #endregion
                
                App.Logger.Log("Saved");
            });
        }

        #region Writing

        private static void WriteBundles(string directory)
        {
            foreach (BundleEntry bundle in App.AssetManager.EnumerateBundles(modifiedOnly:true))
            {
                BundleWriter writer = new BundleWriter($@"{directory}\Bundles\{bundle.Name.Replace("/", "\\")}.bdl");
                writer.WriteBundle(bundle);
                writer.Dispose();
            }
        }

        private static void WriteVbx(string directory)
        {
            VBXWriter writer = new VBXWriter();
            foreach (EbxAssetEntry assetEntry in App.AssetManager.EnumerateEbx("", true))
            {
                string path = $@"{directory}\Vbx\{assetEntry.Name.Replace("/", "\\")}.vbx";
                writer.WriteAsset(assetEntry, RemoveIllegalCharacters(path));
                if (assetEntry.HasModifiedData)
                {
                    assetEntry.ModifiedEntry.IsDirty = false;
                }

                assetEntry.IsDirty = false;
            }
            
            writer.Dispose();
        }

        private static void WriteRes(string directory)
        {
            foreach (ResAssetEntry entry in App.AssetManager.EnumerateRes(modifiedOnly:true))
            {
                string path = $@"{directory}\Res\{entry.Name.Replace("/", "\\")}.res";
                FileInfo fi = new FileInfo(RemoveIllegalCharacters(path));
                if (fi.Directory != null && !fi.Directory.Exists) 
                { 
                    Directory.CreateDirectory(fi.DirectoryName); 
                }

                NativeWriter writer = new NativeWriter(new FileStream(fi.FullName, FileMode.Create));
                writer.Write(entry.IsAdded);
                writer.WriteNullTerminatedString(entry.Name);
                writer.Write(entry.ResRid);
                writer.Write(entry.ResType);
                writer.Write(entry.ResMeta);
                
                SaveLinkedAssets(entry, writer);
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
                    entry.ModifiedEntry.IsDirty = false;
                }
                
                writer.Dispose();
            }
        }

        private static void WriteChunk(string directory)
        {
            foreach (ChunkAssetEntry entry in App.AssetManager.EnumerateChunks(true))
            {
                string path = $@"{directory}\Chunks\{entry.Name}.chunk";
                FileInfo fi = new FileInfo(RemoveIllegalCharacters(path));
                if (fi.Directory != null && !fi.Directory.Exists) 
                { 
                    Directory.CreateDirectory(fi.DirectoryName); 
                }

                NativeWriter writer = new NativeWriter(new FileStream(fi.FullName, FileMode.Create));
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

                entry.IsDirty = false;
                writer.Dispose();
            }
        }
        
        private static void SaveLinkedAssets(AssetEntry entry, NativeWriter writer)
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
                ReadRes(fi.Directory.FullName);
                
                task.Update("Reading chunks...");
                ReadChunk(fi.Directory.FullName);
                
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
            App.Logger.Log("Loaded project in {0}", stopwatch.Elapsed.ToString());
        }

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
                dataReader.ReadAsset(file);
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

        private static void ReadChunk(string projectDir)
        {
            foreach (string file in Directory.EnumerateFiles(projectDir, "*.chunk", SearchOption.AllDirectories))
            {
                NativeReader reader = new NativeReader(new FileStream(file, FileMode.Open));
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
                        continue;
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
            }
        }
        
        private static void ReadRes(string projectDir)
        {
            foreach (string file in Directory.EnumerateFiles(projectDir, "*.res", SearchOption.AllDirectories))
            {
                NativeReader reader = new NativeReader(new FileStream(file, FileMode.Open));
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
                    continue;
                }

                List<AssetEntry> linkedEntries = LoadLinkedAssets(reader);
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

                reader.Dispose();
            }
        }
        
        private static List<AssetEntry> LoadLinkedAssets(NativeReader reader)
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