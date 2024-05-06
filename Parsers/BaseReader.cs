using System.IO;

namespace VBXProj.Parsers
{
    public abstract class BaseDataReader
    {
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
                int commentPosition = line.IndexOf("//"); //Remove comments
                if (commentPosition != -1)
                {
                    line = line.Remove(commentPosition).Trim();
                }
            }

            return line;
        }
        
        public virtual void Dispose()
        {
            _reader?.Close();
            _reader?.Dispose();
        }
    }
}