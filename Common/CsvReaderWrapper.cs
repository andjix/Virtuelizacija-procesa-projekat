using System;
using System.IO;
using System.Text;

namespace Common
{
    public class CsvReaderWrapper : IDisposable
    {
        private bool disposed = false;
        private FileStream fileStream;
        private StreamReader streamReader;

        public CsvReaderWrapper(string filePath)
        {
            fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            streamReader = new StreamReader(fileStream, Encoding.UTF8);
        }

        ~CsvReaderWrapper()
        {
            Dispose(false);
        }

        public bool EndOfStream => streamReader == null || streamReader.EndOfStream;

        public string ReadLine()
        {
            return streamReader?.ReadLine();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (streamReader != null)
                    {
                        streamReader.Dispose();
                        streamReader = null;
                    }
                    if (fileStream != null)
                    {
                        fileStream.Dispose();
                        fileStream = null;
                    }
                }
                disposed = true;
            }
        }
    }
}
