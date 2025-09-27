using System;
using System.IO;
using System.Threading;

namespace OsEngine
{
    public static class OlegUtils
    {
        private static readonly bool LOGGING_ENABLED = false;

        public static void Log(string messageTemplate, params object[] messageArgs)
        {
            if (LOGGING_ENABLED)
            {
                string message = String.Format(messageTemplate, messageArgs);
                Log(message);
            }
        }

        public static void Log(string message)
        {
            if (LOGGING_ENABLED)
            {
                string logFilePath = GetLogFilePath();
                string logLine = String.Format("THREAD = {0} : {1} : {2}",
                    Thread.CurrentThread.ManagedThreadId, DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff"), message);
                LogLine(logFilePath, logLine);
            }
        }

        public static void LogSeparationLine()
        {
            if (LOGGING_ENABLED)
            {
                string logFilePath = GetLogFilePath();
                string logLine = "**********************************************************************************";
                LogLine(logFilePath, logLine);
            }
        }

        private static string GetLogFilePath()
        {
            string logFileName = String.Format("OLEG_LOG.txt");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), logFileName);
        }

        private static void LogLine(string logFilePath, string logLine)
        {
            File.AppendAllLines(logFilePath, new[] { logLine });
        }
    }
}
