using System;
using System.IO;
using System.Text.Encodings.Web;

namespace AutoUpdater.ZipExtractor
{
    public class AutoUpdaterConfig
    {
        public static readonly string FileName = "AutoUpdaterConfig.json";


        public string NewVersion { get; set; }
        public string PackageUrl { get; set; }
        public string ChangeLogUrl { get; set; }
        public bool IsMandatory { get; set; }
        public bool BackupOldFile { get; set; }

        public ExtractorInfoModel ExtractorInfo { get; set; } = new ExtractorInfoModel();



        public static AutoUpdaterConfig GetLocalFile()
        {
            if (File.Exists(FileName))
            {
                string json = File.ReadAllText(FileName);
                AutoUpdaterConfig config = System.Text.Json.JsonSerializer.Deserialize<AutoUpdaterConfig>(json);
                return config;
            }
            return null;
        }

        public static AutoUpdaterConfig GetRemoteFile(string url)
        {
            string json = MyHttpClient.HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            AutoUpdaterConfig config = System.Text.Json.JsonSerializer.Deserialize<AutoUpdaterConfig>(json);
            return config;
        }

        public static void Save(AutoUpdaterConfig config)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(config,
                options: new System.Text.Json.JsonSerializerOptions() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.WriteAllText(FileName, json);
        }
    }

    public class ExtractorInfoModel
    {
        public string WindowTitle { get; set; }
        public string CurrentVersion { get; set; }
        public int WaitingCloseProcessID { get; set; }
        public string ProcessPath { get; set; }
        public bool IsExtractSuccess { get; set; }
        public DateTime ExtractTime { get; set; }
    }
}
