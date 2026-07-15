using System;
using System.Diagnostics;
using System.IO;

namespace AutoUpdater.ZipExtractor
{
    /// <summary>
    /// Simple file-based logger that writes to AutoUpdater.ZipExtractor.txt
    /// </summary>
    public class SimpleLogger
    {
        private static readonly string LogFileName = "AutoUpdater.ZipExtractor.txt";
        private static readonly object LockObject = new object();
        private static readonly string EventSourceName = "AutoUpdater.ZipExtractor";

        public SimpleLogger()
        {
            // Ensure event source exists
            try
            {
                if (!EventLog.SourceExists(EventSourceName))
                {
                    EventLog.CreateEventSource(EventSourceName, "Application");
                }
            }
            catch
            {
                // Ignore errors during event source creation
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
            string message = string.IsNullOrEmpty(context)
                ? $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
                : $"{context} - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            WriteLog("ERROR", message);
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
