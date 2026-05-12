using System;
using System.IO;
using System.Text;

namespace Common
{
    public class StreamWriterWrapper : IDisposable
    {
        private bool disposed = false;
        private FileStream fileStream;
        private StreamWriter streamWriter;

        public StreamWriterWrapper(string filePath, bool append = false)
        {
            fileStream = new FileStream(filePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write);
            streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
        }

        ~StreamWriterWrapper()
        {
            Dispose(false);
        }

        public void WriteLine(string line)
        {
            streamWriter?.WriteLine(line);
        }

        public void Flush()
        {
            streamWriter?.Flush();
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
                    if (streamWriter != null)
                    {
                        streamWriter.Flush();
                        streamWriter.Dispose();
                        streamWriter = null;
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
