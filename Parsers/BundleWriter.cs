using System.IO;
using FrostyEditor;
using FrostySdk.Managers;

namespace VBXProj.Parsers
{
    public class BundleWriter : BaseDataWriter
    {
        public void WriteBundle(BundleEntry bundleEntry)
        {
            WriteHeader();
            
            WriteIndentedLine("FILEDATA");
            WriteIndentedLine("{");
            NextLevel();
            WriteIndentedLine($"name = {bundleEntry.Name}");
            WriteIndentedLine($"type = {bundleEntry.Type}");

            SuperBundleEntry entry = App.AssetManager.GetSuperBundle(bundleEntry.SuperBundleId);
            WriteIndentedLine($"superbundle = {entry.Name}");
            PreviousLevel();
            WriteIndentedLine("}");
        }

        public BundleWriter(string path)
        {
            FileInfo fi = new FileInfo(path);
            if (fi.Directory != null && !fi.Directory.Exists) 
            { 
                Directory.CreateDirectory(fi.DirectoryName); 
            }
            _writer = new StreamWriter(path);
        }
    }
}