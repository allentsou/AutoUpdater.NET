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
        /// 判斷是否為更新程式自己的檔案（AutoUpdater.ZipExtractor.dll、.deps.json、zh-TW\AutoUpdater.ZipExtractor.resources.dll 等）。
        /// 這些檔案在 extractor 執行時會被鎖住，而且函式庫每次更新都會重新放出，
        /// 所以更新包不小心包含它們時（例如直接壓縮輸出資料夾），不可嘗試覆寫。
        /// </summary>
        public static bool IsUpdaterOwnFile(string fileName)
        {
            return fileName.StartsWith("AutoUpdater.ZipExtractor.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 合併 baseDir 與 zip 項目名稱；結果若落在 baseDir 之外（zip-slip）則回傳 null。
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
