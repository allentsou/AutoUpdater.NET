using System;
using System.IO;

namespace AutoUpdater.ZipExtractor
{
    public static class Misc
    {
        public static string GaeBaseDirectory()
        {
#if DEBUG
            return AppDomain.CurrentDomain.BaseDirectory;
#else
            //發行為Single File執行檔要這樣抓才能得到實際路徑
            return Path.GetDirectoryName(Environment.ProcessPath);
#endif
        }

        /// <summary>
        /// True for the updater's own files (AutoUpdater.ZipExtractor.dll, .deps.json, zh-TW\AutoUpdater.ZipExtractor.resources.dll ...).
        /// They are locked while the extractor is running and are refreshed by the host library on every update anyway,
        /// so a package that accidentally contains them (e.g. zipped from the output folder) must not try to overwrite them.
        /// </summary>
        public static bool IsUpdaterOwnFile(string fileName)
        {
            return fileName.StartsWith("AutoUpdater.ZipExtractor.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Combines baseDir and a zip entry name, returning null when the result would land outside baseDir (zip-slip).
        /// </summary>
        public static string GetSafeExtractPath(string baseDir, string entryFullName)
        {
            string fullBaseDir = Path.GetFullPath(baseDir);
            if (!fullBaseDir.EndsWith(Path.DirectorySeparatorChar))
            {
                fullBaseDir += Path.DirectorySeparatorChar;
            }
            string path = Path.GetFullPath(Path.Combine(fullBaseDir, entryFullName));
            return path.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
    }
}
