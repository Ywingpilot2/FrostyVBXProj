using System;
using System.IO;

namespace VBXProj.Parsers
{
    /// <summary>
    /// A basic writer for writing text data with indentation management
    /// </summary>
    public abstract class BaseDataWriter : IDisposable
    {
        private protected StreamWriter _writer;
        private int _level; //Indentation level

        private protected void WriteHeader()
        {
            WriteIndentedLine("//=================================================//");
            WriteIndentedLine("//");
            WriteIndentedLine("// File generated with VBX Project by Ywingpilot2");
            WriteIndentedLine("//");
            WriteIndentedLine("//=================================================//");
            WriteIndentedLine("");
        }
        
        #region Indentation management/Writing

        /// <summary>
        /// Writes a line with proper indentation
        /// </summary>
        /// <param name="line"></param>
        private protected void WriteIndentedLine(string line)
        {
            _writer.WriteLine($"{GetIndentation()}{line}");
        }

        private protected void PreviousLevel()
        {
            if (_level == 0)
                return;
            _level--;
        }

        private protected void NextLevel()
        {
            _level++;
        }

        /// <summary>
        /// Get the amount of indentations we need based on the current level.
        /// </summary>
        /// <returns>Indentations for class properties on this level</returns>
        public string GetIndentation()
        {
            string indentations = "";
            for (int i = 0; i < _level; i++)
            {
                indentations += "\t";
            }

            return indentations;
        }

        #endregion
        
        public void Dispose()
        {
            _writer.Close();
        }
    }
}