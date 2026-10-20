using System;
using System.Diagnostics;
using System.IO;

namespace AutoUpdater.ZipExtractor
{
    /// <summary>
    /// 簡易檔案 logger，寫入程式目錄的 AutoUpdater.ZipExtractor.txt；
    /// 程式目錄無法寫入時（例如 Program Files）改寫到 %LocalAppData%\AutoUpdater。
    /// </summary>
    public class SimpleLogger
    {
        private const string LogFileNameOnly = "AutoUpdater.ZipExtractor.txt";
        // 啟動時 log 超過此大小就改名為 AutoUpdater.ZipExtractor.1.txt（取代上一份），
        // 因此最多保留兩個約 1 MB 的檔案。
        private const long MaxLogSize = 1024 * 1024;
        private static readonly string LogFileName = ResolveLogFileName();
        private static readonly object LockObject = new object();
        private static readonly string EventSourceName = "AutoUpdater.ZipExtractor";

        private static string ResolveLogFileName()
        {
            string appDirLog = Path.Combine(Misc.GaeBaseDirectory(), LogFileNameOnly);
            try
            {
                RotateIfTooLarge(appDirLog);
                using (new FileStream(appDirLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { }
                return appDirLog;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoUpdater");
                Directory.CreateDirectory(dir);
                string localLog = Path.Combine(dir, LogFileNameOnly);
                RotateIfTooLarge(localLog);
                return localLog;
            }
        }

        /// <summary>
        /// 只在啟動時執行一次（extractor 執行時間短且只有單一實例），所以寫入過程中不會換檔。
        /// </summary>
        private static void RotateIfTooLarge(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxLogSize)
                {
                    string backupPath = Path.Combine(info.DirectoryName, Path.GetFileNameWithoutExtension(path) + ".1" + info.Extension);
                    File.Move(path, backupPath, true);
                }
            }
            catch (Exception)
            {
                // 例如檔案正被編輯器開啟：繼續附加寫入，下次啟動再試。
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
