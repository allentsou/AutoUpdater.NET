using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AutoUpdater.ZipExtractor
{
    public class AutoUpdaterConfig
    {
        public static readonly string FileName = "AutoUpdaterConfig.json";

        private static readonly JsonSerializerOptions SaveOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public string NewVersion { get; set; }
        public string PackageUrl { get; set; }
        public string PackageSha256 { get; set; }
        public string ChangeLogUrl { get; set; }
        public bool IsMandatory { get; set; }
        public bool BackupOldFile { get; set; }

        public ExtractorInfoModel ExtractorInfo { get; set; } = new ExtractorInfoModel();

        private static string FilePath => Path.Combine(Misc.GaeBaseDirectory(), FileName);

        public static AutoUpdaterConfig GetLocalFile()
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AutoUpdaterConfig>(File.ReadAllText(path));
            }
            return null;
        }

        public static void Save(AutoUpdaterConfig config)
        {
            // 先寫入暫存檔再取代，程式中途崩潰或被終結時也不會留下寫到一半的 JSON。
            string path = FilePath;
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(config, SaveOptions));
            File.Move(tempPath, path, true);
        }
    }

    public class ExtractorInfoModel
    {
        public string WindowTitle { get; set; }
        public string CurrentVersion { get; set; }
        public string ProcessPath { get; set; }
        /// <summary>
        /// 要等待結束的主程式 PID。舊版函式庫寫入的設定檔為 0，此時改用 ProcessPath 比對。
        /// </summary>
        public int ProcessId { get; set; }
        public bool IsExtractSuccess { get; set; }
        public DateTime ExtractTime { get; set; }
    }
}
