using System;
using System.IO;
using System.Threading;

namespace OsEngine
{
    public static class OlegUtils
    {
        public static void Log(string messageTemplate, params object[] messageArgs)
        {
            string message = String.Format(messageTemplate, messageArgs);
            Log(message);
        }

        public static void Log(string message)
        {
            string logFileName = String.Format("OLEG_LOG.txt");
            string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), logFileName);
            string threadId = String.Format("THREAD = {0}; ", Thread.CurrentThread.ManagedThreadId);
            string logLine = String.Format("{0} : {1} : {2}", threadId, DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff"), message);
            File.AppendAllLines(logFilePath, new[] { logLine });
        }
    }
}
