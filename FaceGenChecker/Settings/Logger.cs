using Noggog;
using System;
using System.IO;
using System.Threading;

namespace FaceGenChecker
{
    public class Logger : IDisposable
    {
        private TextWriter? logWriter;
        Lock _lock = new();

        public Logger(string fileName)
        {
            if (!String.IsNullOrEmpty(fileName))
            {
                logWriter = TextWriter.Synchronized(File.AppendText(fileName));
            }
        }

        public void WriteLine(string format, params object?[] args)
        {
            if (logWriter != null)
            {
                lock (_lock)
                {
                    logWriter.WriteLine(String.Format("{0:D6} {1}", System.Threading.Thread.CurrentThread.ManagedThreadId, format), args);
                }
            }
            Console.WriteLine(format, args);
        }

        public void Dispose()
        {
            if (logWriter != null)
            {
                logWriter.Flush();
                logWriter.Dispose();
            }
        }
    }
}