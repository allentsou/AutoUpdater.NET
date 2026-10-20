using System;
using System.Diagnostics;
using System.IO;

namespace AutoUpdater.ZipExtractor
{
    /// <summary>
    /// Simple file-based logger that writes to AutoUpdater.ZipExtractor.txt in the application directory,
    /// or in %LocalAppData%\AutoUpdater when the application directory is not writable (e.g. Program Files).
    /// </summary>
    public class SimpleLogger
    {
        private const string LogFileNameOnly = "AutoUpdater.ZipExtractor.txt";
        private static readonly string LogFileName = ResolveLogFileName();
        private static readonly object LockObject = new object();
        private static readonly string EventSourceName = "AutoUpdater.ZipExtractor";

        private static string ResolveLogFileName()
        {
            string appDirLog = Path.Combine(Misc.GaeBaseDirectory(), LogFileNameOnly);
            try
            {
                using (new FileStream(appDirLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { }
                return appDirLog;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoUpdater");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, LogFileNameOnly);
            }
        }

        public void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public void Warn(string message)
        {
            WriteLog("WARN", message);
        }

        public void Error(string message)
        {
            WriteLog("ERROR", message);
        }

        public void Error(Exception ex, string context = "")
        {
            WriteLog("ERROR", $"{context}{Environment.NewLine}{ex?.ToString()}");
        }

        private void WriteLog(string level, string message)
        {
            try
            {
                lock (LockObject)
                {
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                    File.AppendAllText(LogFileName, logEntry + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Write to Event Viewer if file logging fails
                try
                {
                    using (EventLog eventLog = new EventLog("Application"))
                    {
                        eventLog.Source = EventSourceName;
                        string errorMessage = $"Failed to write log to {LogFileName}\n\nOriginal Log:\n[{level}] {message}\n\nError:\n{ex.GetType().Name}: {ex.Message}";
                        eventLog.WriteEntry(errorMessage, EventLogEntryType.Error);
                    }
                }
                catch
                {
                    // Silently fail if both file and event log writing fail to avoid disrupting application flow
                }
            }
        }
    }
}
