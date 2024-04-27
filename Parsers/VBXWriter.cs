using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Frosty.Core;
using Frosty.Core.Controls.Editors;
using FrostySdk.Attributes;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;

namespace VBXProj.Parsers
{
    /// <summary>
    /// Writer for writing EBX into a visualized text format
    /// </summary>
    public class VBXWriter : BaseDataWriter
    {
        public VBXWriter(string file)
        {
            FileInfo fi = new FileInfo(file);
            if (fi.Directory != null && !fi.Directory.Exists) 
            { 
                Directory.CreateDirectory(fi.DirectoryName); 
            }
            
            _writer = new StreamWriter(file);
        }

        public VBXWriter()
        {
        }

        #region Writing assets

        public void WriteAsset(EbxAssetEntry assetEntry, string file, string projectDir)
        {
            FileInfo fi = new FileInfo(file);
            if (fi.Directory != null && !fi.Directory.Exists) 
            { 
                Directory.CreateDirectory(fi.DirectoryName); 
            }
            
            _writer = new StreamWriter(file);
            WriteAsset(assetEntry, projectDir);
        }
        
        public void WriteAsset(EbxAssetEntry assetEntry, string projectDir)
        {
            if (_writer == null)
            {
                throw new FileNotFoundException();
            }
            EbxAsset asset = App.AssetManager.GetEbx(assetEntry);
            
            //Write header
            WriteHeader();
            
            //Write file header
            WriteIndentedLine("FILEDATA");
            WriteIndentedLine("{");
            NextLevel();

            // This asset is a handler, meaning we need to store it in binary
            if (assetEntry.ModifiedEntry.DataObject is ModifiedResource modifiedResource)
            {
                string path = $@"{projectDir}\Vbx\{assetEntry.Name.Replace('/', '\\')}.md";
                WriteIndentedLine("modified_resource");
                WriteIndentedLine($"\"projdir\" \"{projectDir}\"");
                WriteIndentedLine($"\"type\" \"{assetEntry.Type}\"");
                WriteIndentedLine($"\"fid\" \"{asset.FileGuid}\"");
                PreviousLevel();
                WriteIndentedLine("}");

                NativeWriter writer = new NativeWriter(new FileStream(path, FileMode.Create));
                byte[] buf = modifiedResource.Save();
                writer.Write(buf.Length);
                writer.Write(buf);
                writer.Dispose();
                _writer.Close();
                return;
            }

            // Write file data
            WriteIndentedLine($"\"projdir\" \"{projectDir}\"");
            WriteIndentedLine($"\"type\" \"{assetEntry.Type}\"");
            WriteIndentedLine($"\"fid\" \"{asset.FileGuid}\"");
            // I hope I get to meet gman some day so I can punch him in the fucking face for this
            // Transient edits are so bafflingly stupid
            if (assetEntry.ModifiedEntry.IsTransientModified)
            {
                WriteIndentedLine("transient"); 
            }
            WriteIndentedLine("");

            // Write object shells
            WriteIndentedLine("Objects");
            WriteIndentedLine("{");
            NextLevel();
            foreach (object assetObject in asset.Objects)
            {
                AssetClassGuid guid = ((dynamic)assetObject).GetInstanceGuid();
                
                WriteIndentedLine($"\"{assetObject.GetType().Name}\" \"{guid.ExportedGuid}\" \"{guid.InternalId}\"");
            }
            PreviousLevel();
            WriteIndentedLine("}");
            
            // Write added bundles
            WriteIndentedLine("Bundles");
            WriteIndentedLine("{");
            NextLevel();

            foreach (int bid in assetEntry.AddedBundles)
            {
                BundleEntry bentry = App.AssetManager.GetBundleEntry(bid);
                WriteIndentedLine(bentry.Name);
            }
            
            PreviousLevel();
            WriteIndentedLine("}");
            WriteIndentedLine("");

            PreviousLevel();
            WriteIndentedLine("}");

            //Write object data
            foreach (object assetObject in asset.Objects)
            {
                WriteObject(assetObject);
                WriteIndentedLine("");
            }
            
            _writer.Close();
        }

        public void WriteObject(object obj)
        {
            AssetClassGuid guid = ((dynamic)obj).GetInstanceGuid();
            
            WriteIndentedLine($"{obj.GetType().Name} {guid.ExportedGuid} {guid.InternalId}");
            WriteIndentedLine("{");
            NextLevel();

            foreach (PropertyInfo property in obj.GetType().GetProperties())
            {
                // Don't write transient data
                if (property.GetCustomAttribute<IsTransientAttribute>() != null)
                    continue;
                
                WriteProperty(property.GetValue(obj), property.Name);
            }
            
            PreviousLevel();
            WriteIndentedLine("}");
        }
        
        private void WriteProperty(object prop, string name = null)
        {
            if (name == "__InstanceGuid")
                return;
            
            switch (prop.GetType().Name) // All of these must be formatted as: "{type}" "{name}" "{property}" so that the reader can parse properly
            {
                case "AssetClassGuid":
                {
                    AssetClassGuid guid = (AssetClassGuid)prop;
                    WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" \"{guid},{guid.InternalId}\"");
                } break;
                case "Vec4":
                {
                    WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" \"{((dynamic)prop).x},{((dynamic)prop).y},{((dynamic)prop).z},{((dynamic)prop).w}\"");
                } break;
                case "Vec3":
                {
                    WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" \"{((dynamic)prop).x},{((dynamic)prop).y},{((dynamic)prop).z}\"");
                } break;
                case "Vec2":
                {
                    WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" \"{((dynamic)prop).x},{((dynamic)prop).y}\"");
                } break;
                /*case "LinearTransform":
                {
                    WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\"");
                    WriteIndentedLine("{");
                    NextLevel();

                    dynamic transform = prop;
                    WriteProperty(transform.right, "right");
                    WriteProperty(transform.up, "up");
                    WriteProperty(transform.forward, "forward");
                    WriteProperty(transform.trans, "trans");

                    PreviousLevel();
                    WriteIndentedLine("}");
                } break;*/
                case "EventConnection":
                {
                    WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\"");
                    WriteIndentedLine("{");
                    NextLevel();
                    foreach (PropertyInfo property in prop.GetType().GetProperties())
                    {
                        WriteProperty(property.GetValue(prop), property.Name);
                    }
                    PreviousLevel();
                    WriteIndentedLine("}");
                } break;
                case "PropertyConnection":
                {
                    WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\"");
                    WriteIndentedLine("{");
                    NextLevel();
                    foreach (PropertyInfo property in prop.GetType().GetProperties())
                    {
                        WriteProperty(property.GetValue(prop), property.Name);
                    }
                    PreviousLevel();
                    WriteIndentedLine("}");
                } break;
                case "LinkConnection":
                {
                    WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\"");
                    WriteIndentedLine("{");
                    NextLevel();
                    foreach (PropertyInfo property in prop.GetType().GetProperties())
                    {
                        WriteProperty(property.GetValue(prop), property.Name);
                    }
                    PreviousLevel();
                    WriteIndentedLine("}");
                } break;
                case "List`1":
                {
                    WriteIndentedLine($"\"List\" \"{name}\"");
                    WriteIndentedLine("{");
                    NextLevel();
                    foreach (object obj in (dynamic)prop)
                    {
                        WriteProperty(obj);
                    }
                    PreviousLevel();
                    WriteIndentedLine("}");
                } break;
                case "PointerRef":
                {
                    PointerRef ptr = (PointerRef)prop;
                    switch (ptr.Type)
                    {
                        case PointerRefType.External:
                        {
                            WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" \"external : {ptr.External.FileGuid},{ptr.External.ClassGuid}\"");
                        } break;
                        case PointerRefType.Internal:
                        {
                            WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" \"internal : {((dynamic)ptr.Internal).GetInstanceGuid().ExportedGuid},{((dynamic)ptr.Internal).GetInstanceGuid().InternalId}\"");
                        } break;
                        default:
                        {
                            WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" \"null\"");
                        } break;
                    }
                } break;
                default:
                {
                    //Write down as an object
                    Type propType = prop.GetType();
                    if (propType.IsEnum || propType.IsValueType || propType.IsPrimitive)
                    {
                        WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" \"{prop}\"");
                    }
                    else
                    {
                        WriteIndentedLine($"\"{prop.GetType().Name}\" \"{name}\" ");
                        WriteIndentedLine("{");
                        NextLevel();
                        foreach (PropertyInfo property in prop.GetType().GetProperties())
                        {
                            // Don't write transient data
                            if (property.GetCustomAttribute<IsTransientAttribute>() != null)
                                continue;
                            
                            WriteProperty(property.GetValue(prop), property.Name);
                        }
                        PreviousLevel();
                        WriteIndentedLine("}");
                    }
                } break;
            }
        }

        #endregion
    }
}