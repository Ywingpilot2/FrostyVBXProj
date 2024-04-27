using System;
using System.IO;
using FrostyEditor;
using FrostySdk.Managers;

namespace VBXProj.Parsers
{
    public class BundleReader : BaseDataReader
    {
        public void ReadBundle()
        {
            string name = "";
            BundleType type = BundleType.None;
            EbxAssetEntry blueprint = null;
            int superBundle = -1;
            
            string line = ReadCleanLine();
            while (line != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    line = ReadCleanLine();
                    continue;
                }

                var args = line.Split(new[] { " = " }, StringSplitOptions.None);

                switch (args[0])
                {
                    case "name":
                    {
                        name = args[1];
                    } break;
                    case "type":
                    {
                        type = (BundleType)Enum.Parse(typeof(BundleType), args[1]);
                    } break;
                    case "superbundle":
                    {
                        superBundle = App.AssetManager.GetSuperBundleId(args[1]);
                    } break;
                }

                line = ReadCleanLine();
            }
            
            App.AssetManager.AddBundle(name, type, superBundle);
        }

        public BundleReader(string path)
        {
            _reader = new StreamReader(path);
        }
    }
}