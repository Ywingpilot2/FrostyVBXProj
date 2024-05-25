using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FrostyEditor;
using FrostySdk.Ebx;
using FrostySdk.IO;

namespace VBXProj.Parsers
{
    public struct VClass
    {
        public List<VProperty> Properties { get; internal set; }
    }
    
    public struct VProperty
    {
        public string Name { get; }
        private object Value { get; }
        public string Type { get; }

        #region Getting as

        public Guid GetAsGuid()
        {
            if (Guid.TryParse((string)Value, out Guid guid))
            {
                return guid;
            }
            else
            {
                return Guid.Empty;
            }
        }

        public PointerRef GetAsPointerRef()
        {
            var pointerProps = ((string)Value).Split(new[] { " : " }, StringSplitOptions.RemoveEmptyEntries);
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
                } break;
                case "internal":
                {
                    App.Logger.LogError("Internal pointer refs are not supported by default");
                } break;
                default:
                {
                    pointerRef = new PointerRef();
                } break;
            }

            return pointerRef;
        }

        public uint GetAsUint()
        {
            string value = ((string)Value).StartsWith("0x") ? ((string)Value).Replace("0x", "") : ((string)Value);
            
            if (!uint.TryParse(value, NumberStyles.Integer & NumberStyles.HexNumber, new NumberFormatInfo(), out uint i))
            {
                App.Logger.LogError("Value {0} for property {1} is not a proper uint", Value, Name);
                return 0;
            }
            else
            {
                return i;
            }
        }
        
        public int GetAsInt()
        {
            string value = ((string)Value).StartsWith("0x") ? ((string)Value).Replace("0x", "") : ((string)Value);
            
            if (!int.TryParse(value, NumberStyles.Integer & NumberStyles.HexNumber, new NumberFormatInfo(), out int i))
            {
                App.Logger.LogError("Value {0} for property {1} is not a proper int", Value, Name);
                return 0;
            }
            else
            {
                return i;
            }
        }
        
        public float GetAsFloat()
        {
            if (!float.TryParse(((string)Value), NumberStyles.Float, new NumberFormatInfo(), out float i))
            {
                App.Logger.LogError("Value {0} for property {1} is not a proper float", Value, Name);
                return 0;
            }
            else
            {
                return i;
            }
        }
        
        public double GetAsDouble()
        {
            if (!double.TryParse(((string)Value), NumberStyles.Float, new NumberFormatInfo(), out double i))
            {
                App.Logger.LogError("Value {0} for property {1} is not a proper double", Value, Name);
                return 0;
            }
            else
            {
                return i;
            }
        }
        
        public bool GetAsBool()
        {
            if (bool.TryParse(((string)Value).ToLower(), out bool b))
            {
                return b;
            }
            else
            {
                if (GetAsInt() >= 1)
                {
                    return true;
                }
            }

            App.Logger.LogError("Value {0} for property {1} is not a proper bool", Value, Name);
            return false;
        }

        #endregion

        public VProperty(string name, string value, string type)
        {
            Name = name;
            Value = value;
            Type = type.ToLower();
        }

        public VProperty(string name, VClass vClass)
        {
            Name = name;
            Value = vClass;
            Type = "class";
        }
    }

    public class VMetaData
    {
        public List<VProperty> Properties { get; internal set; }
        public List<VClass> Classes { get; internal set; }

        public VMetaData()
        {
            Properties = new List<VProperty>();
            Classes = new List<VClass>();
        }
    }
    
    public class MetaDataReader : BaseDataReader
    {
        public VMetaData MetaData { get; set; }

        public MetaDataReader(string file)
        {
            _reader = new StreamReader(file);
        }

        public VMetaData ReadMetaData()
        {
            MetaData = new VMetaData();
            string line = ReadCleanLine();

            while (line != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line == "")
                    continue;
                
                if (line.StartsWith("\"class\""))
                {
                    VClass vClass = ReadClass();
                    MetaData.Classes.Add(vClass);
                }
                else
                {
                    MetaData.Properties.Add(ReadProperty(line));
                }
                
                line = ReadCleanLine();
            }
            
            return MetaData;
        }

        public VClass ReadClass()
        {
            VClass vClass = new VClass();
            
            string line = ReadCleanLine();
            while (line != "{")
            {
                line = ReadCleanLine();
            }

            line = ReadCleanLine();
            while (line != "}")
            {
                if (string.IsNullOrWhiteSpace(line) || line == "")
                    continue;

                string[] vars = line.Split(new []{"\" \""}, StringSplitOptions.None);
                if (line.StartsWith("\"class\""))
                {
                    VClass subClass = ReadClass();

                    vClass.Properties.Add(new VProperty(vars[1].Trim('"', ' ', '\t', '\r'), subClass));
                }
                else
                {
                    vClass.Properties.Add(ReadProperty(line));
                }
                
                line = ReadCleanLine();
            }

            return vClass;
        }

        private VProperty ReadProperty(string line)
        {
            string[] vars = line.Split(new []{"\" \""}, StringSplitOptions.None);
            return new VProperty(vars[1].Trim('"', ' ', '\t', '\r'), vars[2].Trim('"', ' ', '\t', '\r'),
                vars[0].Trim('"', ' ', '\t', '\r'));
        }
    }
}