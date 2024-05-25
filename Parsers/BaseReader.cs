using System;
using System.IO;

namespace VBXProj.Parsers
{
    public abstract class BaseDataReader
    {
        public int CurrentLine { get; private set; }
        private protected StreamReader _reader;
        
        /// <summary>
        /// Reads the next line, also removes things like comments or whitespace
        /// </summary>
        /// <returns></returns>
        private protected string ReadCleanLine()
        {
            string line = _reader.ReadLine();
            line = line?.Trim();

            if (line != null)
            {
                int commentPosition = line.IndexOf("//", StringComparison.Ordinal); //Remove comments
                if (commentPosition != -1)
                {
                    // Remove comment
                    // We need to account for strings and such potentially containing // as well
                    bool inStr = false;
                    bool foundComment = false;

                    for (var i = 1; i < line.Length; i++)
                    {
                        if (foundComment)
                            break;
                        
                        char c = line[i];
                        char previous = line[i - 1];
                        
                        switch (c)
                        {
                            case '/':
                            {
                                if (!inStr && previous == '/')
                                {
                                    commentPosition = i - 1;
                                    foundComment = true;
                                }
                            } break;
                            case '"':
                            {
                                // Don't end the string if its escaped
                                if (inStr && previous != '\\')
                                {
                                    inStr = false;
                                }
                                else
                                {
                                    inStr = true;
                                }
                            } break;
                        }
                    }

                    if (foundComment)
                    {
                        line = line.Remove(commentPosition).Trim();
                    }
                }
            }

            CurrentLine++;
            return line;
        }
        
        public virtual void Dispose()
        {
            _reader?.Close();
            _reader?.Dispose();
        }
    }
}