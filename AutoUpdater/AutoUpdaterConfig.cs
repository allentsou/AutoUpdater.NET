using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoUpdater
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
        /// <summary>
        /// 選填。更新包的 SHA-256（十六進位）。有設定時，雜湊不符的更新包會被 extractor 拒絕安裝。
        /// </summary>
        public string PackageSha256 { get; set; }
        public string ChangeLogUrl { get; set; }
        public bool IsMandatory { get; set; }
        public bool BackupOldFile { get; set; }

        public ExtractorInfoModel ExtractorInfo { get; set; } = new ExtractorInfoModel();

        /// <summary>
        /// 本機設定檔的完整路徑（在程式目錄下，不受目前工作目錄影響）。
        /// </summary>
        private static string FilePath => Path.Combine(AutoUpdater.GaeBaseDirectory(), FileName);

        public static AutoUpdaterConfig GetLocalFile()
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AutoUpdaterConfig>(File.ReadAllText(path));
            }
            return null;
        }

        public static AutoUpdaterConfig GetRemoteFile(string url)
        {
            string json = MyHttpClient.HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            return JsonSerializer.Deserialize<AutoUpdaterConfig>(json);
        }

        public static async Task<AutoUpdaterConfig> GetRemoteFileAsync(string url)
        {
            string json = await MyHttpClient.HttpClient.GetStringAsync(url).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AutoUpdaterConfig>(json);
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
