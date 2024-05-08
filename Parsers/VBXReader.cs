using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Frosty.Core;
using Frosty.Core.Controls.Editors;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;

namespace VBXProj.Parsers
{
    public class VbxDataReader : BaseDataReader
    {
        public EbxAssetEntry AssetEntry;
        public EbxAsset Ebx;
        private List<object> _rootObjects = new List<object>();
        private Dictionary<AssetClassGuid, object> _objects = new Dictionary<AssetClassGuid, object>();
        private FileInfo _file;

        public VbxDataReader(string filepath)
        {
            FileInfo fi = new FileInfo(filepath);
            if (fi.Directory != null && !fi.Directory.Exists) 
            {
                return;
            }

            _file = fi;
            _reader = new StreamReader(filepath);
        }

        public VbxDataReader()
        {
        }

        #region Asset Reading

        public EbxAssetEntry ReadAsset(string filePath, bool overwrite = true)
        {
            FileInfo fi = new FileInfo(filePath);
            if (fi.Directory != null && !fi.Directory.Exists) 
            {
                return null;
            }

            _file = fi;
            _reader = new StreamReader(filePath);
            return ReadAsset(overwrite);
        }
        
        public EbxAssetEntry ReadAsset(bool overwrite = true)
        {
            if (_reader == null)
                return null;
            
            string line = ReadCleanLine();

            while (line != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    line = ReadCleanLine();
                    continue;
                }

                switch (line)
                {
                    case "FILEDATA":
                    {
                        VFileData fileData = ReadFileData();
                        
                        // This asset is a handler, therefore needs none of this extra work
                        if (fileData.HasModifiedResource)
                        {
                            AssetEntry = App.AssetManager.GetEbxEntry(fileData.AssetPath);
                            if (AssetEntry == null) // TODO: Does this even work?
                            {
                                AssetEntry = new EbxAssetEntry
                                {
                                    Name = fileData.AssetPath,
                                    AddedBundles = fileData.Bundles,
                                };
                                App.AssetManager.AddEbx(AssetEntry);
                            }
                            string path = _file.FullName.Replace(".vbx", ".md");
                            NativeReader reader = new NativeReader(new FileStream(path, FileMode.Open));
                            int length = reader.ReadInt();
                            ModifiedResource resource = ModifiedResource.Read(reader.ReadBytes(length));
                            AssetEntry.ModifiedEntry = new ModifiedAssetEntry { DataObject = resource };
                            Dispose();
                            return AssetEntry;
                        }
                        
                        line = ReadCleanLine();

                        // Stupid work around to forcefully initialized the ebx asset
                        // Why the default constructor even exists when you can't fucking use it I do not know
                        Ebx = new EbxAsset(new object[]{});
                        Ebx.SetFileGuid(fileData.FileId);
                        foreach (object assetObject in _objects.Values)
                        {
                            Ebx.AddObject(assetObject);
                        }

                        foreach (Guid dependency in fileData.Dependencies)
                        {
                            Ebx.AddDependency(dependency);
                        }

                        if (App.AssetManager.GetEbxEntry(fileData.FileId) != null && (overwrite || !App.AssetManager.GetEbxEntry(fileData.FileId).IsModified))
                        {
                            AssetEntry = App.AssetManager.GetEbxEntry(fileData.FileId);
                            AssetEntry.ModifiedEntry = new ModifiedAssetEntry { DataObject = Ebx, IsTransientModified = fileData.TransientEdit};
                        }
                        else if (App.AssetManager.GetEbxEntry(fileData.FileId) == null)
                        {
                            AssetEntry = App.AssetManager.AddEbx(fileData.AssetPath, Ebx);
                            AssetEntry.ModifiedEntry = new ModifiedAssetEntry { DataObject = Ebx, IsTransientModified = fileData.TransientEdit };
                        }
                        else
                        {
                            AssetEntry = new EbxAssetEntry
                            {
                                Name = fileData.AssetPath, Type = fileData.Type,
                                // TODO: This makes modified data invalid. But since we are not adding it to the asset manager, why bother?
                                ModifiedEntry = new ModifiedAssetEntry { DataObject = Ebx, IsTransientModified = fileData.TransientEdit }
                            };
                        }

                        AssetEntry.AddToBundles(fileData.Bundles);
                    } break;
                    default:
                    {
                        // TODO: Work around to us writing root objects twice
                        ReadClass(line);
                        line = ReadCleanLine();
                    } break;
                }
            }

            // Hoping this will fix some dependency issues
            AssetEntry.ModifiedEntry.DependentAssets.AddRange(Ebx.Dependencies);
            Ebx.OnLoadComplete();
            AssetEntry.OnModified();
            AssetEntry.IsDirty = false;

            return AssetEntry;
        }

        #endregion

        #region Class reading

        public VFileData ReadFileData(string filePath)
        {
            FileInfo fi = new FileInfo(filePath);
            if (fi.Directory != null && !fi.Directory.Exists)
            {
                throw new FileNotFoundException();
            }

            _file = fi;
            _reader = new StreamReader(filePath);
            VFileData fileData = ReadFileData();
            Dispose();
            return fileData;
        }

        private VFileData ReadFileData()
        {
            VFileData fileData = new VFileData
            {
                Objects = new Dictionary<AssetClassGuid, object>(),
                Bundles = new List<int>(),
                Dependencies = new List<Guid>()
            };

            string line = ReadCleanLine();
            while (line != "{")
            {
                line = ReadCleanLine();
            }

            line = ReadCleanLine();
            while (line != "}")
            {
                if (string.IsNullOrEmpty(line))
                {
                    line = ReadCleanLine();
                    continue;
                }
                            
                var props = line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries);
                switch (props[0].Trim('"'))
                {
                    case "origpath":
                    {
                        fileData.AssetPath = props[1].Trim('"');
                    } break;
                    case "transient":
                    {
                        fileData.TransientEdit = true;
                    } break;
                    case "modified_resource":
                    {
                        fileData.HasModifiedResource = true;
                    } break;
                    case "projdir":
                    {
                        fileData.ProjectDir = props[1].Trim('"');
                    } break;
                    case "fid":
                    {
                        fileData.FileId = Guid.Parse(props[1].Trim('"'));
                    } break;
                    case "type":
                    {
                        fileData.Type = props[1].Trim('"');
                    } break;
                    case "Objects":
                    {
                        while (line != "{")
                        {
                            line = ReadCleanLine();
                        }
                        line = ReadCleanLine();

                        while (line != "}")
                        {
                            var objStr = line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries);
                            object obj = TypeLibrary.CreateObject(objStr[0].Trim('"'));
                            Guid guid = Guid.Parse(objStr[1].Trim('"'));
                            AssetClassGuid classGuid = new AssetClassGuid(guid, int.Parse(objStr[2].Trim('"')));
                                        
                            // TODO: Make everything else rely on AssetClassGuid instead of guid
                            ((dynamic)obj).SetInstanceGuid(classGuid);
                            _objects.Add(classGuid, obj);
                            fileData.Objects.Add(classGuid, obj);
                            line = ReadCleanLine();
                        }
                    } break;
                    case "Bundles":
                    {
                        while (line != "{")
                        {
                            line = ReadCleanLine();
                        }
                        line = ReadCleanLine();

                        while (line != "}")
                        {
                            int bid = App.AssetManager.GetBundleId(line);
                            fileData.Bundles.Add(bid);
                            line = ReadCleanLine();
                        }
                    } break;
                    case "Dependencies":
                    {
                        while (line != "{")
                        {
                            line = ReadCleanLine();
                        }
                        line = ReadCleanLine();

                        while (line != "}")
                        {
                            fileData.Dependencies.Add(Guid.Parse(line));
                            line = ReadCleanLine();
                        }
                    } break;
                }
                line = ReadCleanLine();
            }

            if (fileData.ProjectDir == null)
            {
                int idx = _file.FullName.IndexOf(@"Vbx\");
                fileData.ProjectDir = _file.FullName.Remove(idx + 4);
            }
            // Determine the asset path based on the physical path
            if (fileData.AssetPath == null)
            {
                fileData.AssetPath = _file.FullName.Replace($"{fileData.ProjectDir}", "").Replace("\\", "/").Replace(".vbx", "");
            }
            fileData.PhysicalPath = _file.FullName;
            return fileData;
        }

        private object ReadClass(string header)
        {
            object obj = _objects[new AssetClassGuid(Guid.Parse(header.Split(' ')[1]), int.Parse(header.Split(' ')[2]))];
            string line = ReadCleanLine();
            while (line != "{")
            {
                line = ReadCleanLine();
            }

            line = ReadCleanLine();
            while (line != "}")
            {
                if (string.IsNullOrEmpty(line))
                {
                    line = ReadCleanLine();
                    continue;
                }
                
                // Enumerate over properties
                var propDet = line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries);
                if (propDet.Length == 3)
                {
                    ReadProperty(obj, propDet[0].Trim('"'), propDet[1].Trim('"'), propDet[2].Trim('"'));
                }
                else
                {
                    ReadProperty(obj, propDet[0].Trim('"'), propDet[1].Trim('"'));
                }

                line = ReadCleanLine();
            }

            return obj;
        }

        #endregion

        #region Properties

        private dynamic ReadProperty(object obj, string type, string name, string value = null)
        {
            Type objType = obj.GetType();
            PropertyInfo propInfo = objType.GetProperty(name);
            if (propInfo == null)
            {
                App.Logger.LogError("Property {0} on {1} does not exist!", name, objType.Name);
                return null;
            }
            
            object propObj;
            
            switch (type)
            {
                #region Generic types
                
                case "Byte":
                {
                    propObj = byte.Parse(value);
                } break;
                
                case "UInt":
                case "UInt64":
                case "UInt32":
                {
                    propObj = uint.Parse(value);
                } break;

                case "UInt16":
                {
                    propObj = UInt16.Parse(value);
                } break;

                case "Int":
                case "Int64":
                case "Int32":
                {
                    propObj = int.Parse(value);
                } break;
                
                case "Int16":
                {
                    propObj = Int16.Parse(value);
                } break;
                
                case "Vec2":
                {
                    dynamic vec = TypeLibrary.CreateObject(type);
                    var trans = value.Split(',');
                    vec.x = float.Parse(trans[0], NumberStyles.Float);
                    vec.y = float.Parse(trans[1], NumberStyles.Float);
                    propObj = vec;
                } break;
                case "Vec3":
                {
                    dynamic vec = TypeLibrary.CreateObject(type);
                    var trans = value.Split(',');
                    vec.x = float.Parse(trans[0], NumberStyles.Float);
                    vec.y = float.Parse(trans[1], NumberStyles.Float);
                    vec.z = float.Parse(trans[2], NumberStyles.Float);
                    propObj = vec;
                } break;
                case "Vec4":
                {
                    dynamic vec = TypeLibrary.CreateObject(type);
                    var trans = value.Split(',');
                    vec.x = float.Parse(trans[0], NumberStyles.Float);
                    vec.y = float.Parse(trans[1], NumberStyles.Float);
                    vec.z = float.Parse(trans[2], NumberStyles.Float);
                    vec.w = float.Parse(trans[3], NumberStyles.Float);
                    propObj = vec;
                } break;

                case "Boolean":
                {
                    propObj = bool.Parse(value);
                } break;

                case "Single":
                {
                    propObj = Single.Parse(value, NumberStyles.Float);
                } break;
                case "Float":
                {
                    propObj = float.Parse(value, NumberStyles.Float);
                } break;
                case "Double":
                {
                    propObj = double.Parse(value, NumberStyles.Float);
                } break;

                #endregion

                #region FB Types

                case "AssetClassGuid":
                {
                    var guidProps = value.Split(',');
                    propObj = new AssetClassGuid(Guid.Parse(guidProps[0].Trim('"')), int.Parse(guidProps[1].Trim('"')));
                } break;

                case "CString":
                {
                    propObj = new CString(value);
                } break;

                case "PointerRef":
                {
                    var pointerProps = value.Split(new[] { " : " }, StringSplitOptions.RemoveEmptyEntries);
                    PointerRef pointerRef;
                    switch (pointerProps[0].Trim('"'))
                    {
                        case "external":
                        {
                            Guid fid = Guid.Parse(pointerProps[1].Split(',')[0].Trim('"'));
                            pointerRef = new PointerRef(new EbxImportReference()
                            {
                                FileGuid = fid,
                                ClassGuid = Guid.Parse(pointerProps[1].Split(',')[1].Trim('"'))
                            });
                            Ebx.AddDependency(fid); // TODO: Work around to fixing dependencies. Consider a more proper method?
                        } break;
                        case "internal":
                        {
                            // Problem is we have no way of(most likely) getting the object this is meant to reference, since we haven't
                            // Finished loading the file yet.
                            pointerRef = new PointerRef(_objects[new AssetClassGuid(Guid.Parse(pointerProps[1].Split(',')[0].Trim('"')), int.Parse(pointerProps[1].Split(',')[1].Trim('"')))]);
                            // Perhaps consider loading objects as empty shells, then once all objects are loaded we load everything a second time
                            // This comes with the unfortunate problem of loading the file twice though.
                            // Another thing we could do to fix this is have the header contain all of the objects in the file, except them being stripped down to guid and type.
                            // That would mean we load header, have empty shells, set properties of empty shells.
                        } break;
                        default:
                        {
                            pointerRef = new PointerRef();
                        } break;
                    }

                    propObj = pointerRef;
                } break;

                case "Guid":
                {
                    propObj = Guid.Parse(value);
                } break;
                
                case "ResourceRef":
                {
                    propObj = new ResourceRef(ulong.Parse(value, NumberStyles.AllowHexSpecifier));
                } break;
                
                
                /*case "LinearTransform":
                {
                    propObj = TypeLibrary.CreateObject(type);
                    string line = ReadCleanLine();
                    while (line != "{")
                    {
                        line = ReadCleanLine();
                    }

                    line = ReadCleanLine();
                    ReadProperty(propObj, "Vec3", "right", line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries)[2].Trim('"'));
                    line = ReadCleanLine();
                    ReadProperty(propObj, "Vec3", "up", line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries)[2].Trim('"'));
                    line = ReadCleanLine();
                    ReadProperty(propObj, "Vec3", "forward", line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries)[2].Trim('"'));
                    line = ReadCleanLine();
                    ReadProperty(propObj, "Vec3", "trans", line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries)[2].Trim('"'));
                    line = ReadCleanLine();
                } break;*/

                #endregion

                #region Advanced types

                case "List": //TODO: Account for the possibility of a list containing a list
                {
                    dynamic list = Activator.CreateInstance(propInfo.PropertyType);
                    
                    string line = ReadCleanLine();
                    while (line != "{")
                    {
                        line = ReadCleanLine();
                    }

                    line = ReadCleanLine();
                    while (line != "}")
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            line = ReadCleanLine();
                            continue;
                        }
                
                        // Enumerate over properties
                        var propDet = line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries);
                        dynamic property = ReadProperty(propDet[0].Trim('"'), propDet[1].Trim('"'));

                        list.Add(property);
                
                        line = ReadCleanLine();
                    }

                    propObj =  list;
                } break;

                #endregion

                default:
                {
                    if (propInfo.PropertyType.IsEnum)
                    {
                        propObj = Enum.Parse(propInfo.PropertyType, value);
                        break;
                    }
                    
                    propObj = TypeLibrary.CreateObject(type);
                    string line = ReadCleanLine();
                    while (line != "{")
                    {
                        line = ReadCleanLine();
                    }

                    line = ReadCleanLine();
                    while (line != "}")
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            line = ReadCleanLine();
                            continue;
                        }
                
                        // Enumerate over properties
                        var propDet = line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries);
                        object property;
                        if (propDet.Length == 3)
                        {
                            property = ReadProperty(propObj, propDet[0].Trim('"'), propDet[1].Trim('"'), propDet[2].Trim('"'));
                        }
                        else
                        {
                            property = ReadProperty(propObj, propDet[0].Trim('"'), propDet[1].Trim('"'));
                        }
                
                        SetValue(propObj, property, propDet[1].Trim('"'));
                
                        line = ReadCleanLine();
                    }
                } break;
            }
            
            SetValue(obj, propObj, propInfo);
            
            return propObj;
        }
        
        private dynamic ReadProperty(string type, string value)
        {
            object propObj;
            
            switch (type)
            {
                #region Generic types
                
                case "Byte":
                {
                    propObj = byte.Parse(value);
                } break;

                case "UInt":
                case "UInt64":
                case "UInt32":
                {
                    propObj = uint.Parse(value);
                } break;

                case "UInt16":
                {
                    propObj = UInt16.Parse(value);
                } break;

                case "Int":
                case "Int64":
                case "Int32":
                {
                    propObj = int.Parse(value);
                } break;
                
                case "Int16":
                {
                    propObj = Int16.Parse(value);
                } break;
                
                case "Vec2":
                {
                    dynamic vec = TypeLibrary.CreateObject(type);
                    var trans = value.Split(',');
                    vec.x = float.Parse(trans[0]);
                    vec.y = float.Parse(trans[1]);
                    propObj = vec;
                } break;
                case "Vec3":
                {
                    dynamic vec = TypeLibrary.CreateObject(type);
                    var trans = value.Split(',');
                    vec.x = float.Parse(trans[0]);
                    vec.y = float.Parse(trans[1]);
                    vec.z = float.Parse(trans[2]);
                    propObj = vec;
                } break;
                case "Vec4":
                {
                    dynamic vec = TypeLibrary.CreateObject(type);
                    var trans = value.Split(',');
                    vec.x = float.Parse(trans[0]);
                    vec.y = float.Parse(trans[1]);
                    vec.z = float.Parse(trans[2]);
                    vec.w = float.Parse(trans[3]);
                    propObj = vec;
                } break;
                
                case "Boolean":
                {
                    propObj = bool.Parse(value);
                } break;

                case "Single":
                {
                    propObj = Single.Parse(value);
                } break;
                case "Float":
                {
                    propObj = float.Parse(value);
                } break;
                case "Double":
                {
                    propObj = double.Parse(value);
                } break;

                #endregion

                #region FB Types
                
                case "AssetClassGuid":
                {
                    var guidProps = value.Split(',');
                    propObj = new AssetClassGuid(Guid.Parse(guidProps[0].Trim('"')), int.Parse(guidProps[1].Trim('"')));
                } break;

                case "CString":
                {
                    propObj = new CString(value);
                } break;

                case "PointerRef":
                {
                    var pointerProps = value.Split(new[] { " : " }, StringSplitOptions.RemoveEmptyEntries);
                    PointerRef pointerRef;
                    switch (pointerProps[0].Trim('"'))
                    {
                        case "external":
                        {
                            Guid fid = Guid.Parse(pointerProps[1].Split(',')[0].Trim('"'));
                            pointerRef = new PointerRef(new EbxImportReference()
                            {
                                FileGuid = fid,
                                ClassGuid = Guid.Parse(pointerProps[1].Split(',')[1].Trim('"'))
                            });
                            Ebx.AddDependency(fid); // TODO: Work around to fixing dependencies. Consider a more proper method?
                        } break;
                        case "internal":
                        {
                            // Problem is we have no way of(most likely) getting the object this is meant to reference, since we haven't
                            // Finished loading the file yet.
                            pointerRef = new PointerRef(_objects[new AssetClassGuid(Guid.Parse(pointerProps[1].Split(',')[0].Trim('"')), int.Parse(pointerProps[1].Split(',')[1].Trim('"')))]);
                            // Perhaps consider loading objects as empty shells, then once all objects are loaded we load everything a second time
                            // This comes with the unfortunate problem of loading the file twice though.
                            // Another thing we could do to fix this is have the header contain all of the objects in the file, except them being stripped down to guid and type.
                            // That would mean we load header, have empty shells, set properties of empty shells.
                        } break;
                        default:
                        {
                            pointerRef = new PointerRef();
                        } break;
                    }

                    propObj = pointerRef;
                } break;
                
                case "Guid":
                {
                    propObj = Guid.Parse(value);
                } break;
                
                case "ResourceRef":
                {
                    propObj = new ResourceRef(ulong.Parse(value, NumberStyles.AllowHexSpecifier));
                } break;
                
                
                /*case "LinearTransform":
                {
                    propObj = TypeLibrary.CreateObject(type);
                    string line = ReadCleanLine();
                    while (line != "{")
                    {
                        line = ReadCleanLine();
                    }

                    line = ReadCleanLine();

                    EditorLinearTransform transform = new EditorLinearTransform();
                    while (line != "}")
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            line = ReadCleanLine();
                            continue;
                        }
                
                        var propdet = line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries);

                        switch (propdet[1])
                        {
                            case "Translate":
                            {
                                transform.Translation = ReadProperty("Vec3", propdet[2].Trim('"'));
                            } break;
                            case "Scale":
                            {
                                transform.Scale = ReadProperty("Vec3", propdet[2].Trim('"'));
                            } break;
                            case "Rotation":
                            {
                                transform.Rotation = ReadProperty("Vec3", propdet[2].Trim('"'));
                            } break;
                        }

                        line = ReadCleanLine();
                    }

                    LinearTransformConverter converter = new LinearTransformConverter();
                    propObj = converter.ConvertBack(transform, propObj.GetType(), propObj, CultureInfo.CurrentCulture);
                } break;*/

                #endregion

                default:
                {
                    propObj = TypeLibrary.CreateObject(type);
                    Type propType = propObj.GetType();
                    if (propType.IsEnum)
                    {
                        propObj = Enum.Parse(propType, value);
                        break;
                    }
                    
                    string line = ReadCleanLine();
                    while (line != "{")
                    {
                        line = ReadCleanLine();
                    }

                    line = ReadCleanLine();
                    while (line != "}")
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            line = ReadCleanLine();
                            continue;
                        }
                
                        // Enumerate over properties
                        var propDet = line.Split(new[] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries);
                        object property;
                        if (propDet.Length == 3)
                        {
                            property = ReadProperty(propObj, propDet[0].Trim('"'), propDet[1].Trim('"'), propDet[2].Trim('"'));
                        }
                        else
                        {
                            property = ReadProperty(propObj, propDet[0].Trim('"'), propDet[1].Trim('"'));
                        }
                
                        SetValue(propObj, property, propDet[1].Trim('"'));
                
                        line = ReadCleanLine();
                    }
                } break;
            }

            return propObj;
        }

        #region Value setters

        /// <summary>
        /// Sets a property in the object to the specified value
        /// </summary>
        /// <param name="objToSet">The object who's property will be changed</param>
        /// <param name="value">The value it will be changed to</param>
        /// <param name="name">The name of the property to change</param>
        private void SetValue(object objToSet, object value, string name)
        {
            Type objType = objToSet.GetType();
            PropertyInfo propInfo = objType.GetProperty(name);
            try
            {
                propInfo?.SetValue(objToSet, value);
            }
            catch (Exception e)
            {
                App.Logger.LogError("Encountered an error setting property {0} to {1}", propInfo != null ? propInfo.Name : "null", value ?? "null");
                return;
            }
        }

        /// <summary>
        /// Sets a property in the object to the specified value
        /// </summary>
        /// <param name="objToSet">The object who's property will be changed</param>
        /// <param name="value">The value it will be changed to</param>
        /// <param name="propInfo">The Property Info of the property to set</param>
        private void SetValue(object objToSet, object value, PropertyInfo propInfo)
        {
            try
            {
                propInfo.SetValue(objToSet, value);
            }
            catch (Exception e)
            {
                App.Logger.LogError("Encountered an error setting property {0} to {1}", propInfo.Name, value ?? "null");
                return;
            }
        }

        #endregion

        #endregion

        public override void Dispose()
        {
            base.Dispose();
            _objects.Clear();
            _rootObjects.Clear();
            AssetEntry = null;
            Ebx = null;
        }
    }

    public struct VFileData
    {
        public string AssetPath { get; set; }
        public string ProjectDir { get; set; }
        public string PhysicalPath { get; set; }
        public Guid FileId { get; set; }
        public string Type { get; set; }
        public Dictionary<AssetClassGuid, object> Objects { get; set; }
        public List<int> Bundles { get; set; }
        public List<Guid> Dependencies { get; set; }
        public bool HasModifiedResource { get; set; }
        public bool TransientEdit { get; set; }
    }
}