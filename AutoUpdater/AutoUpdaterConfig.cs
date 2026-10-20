using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

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
        /// Optional. Hex SHA-256 of the package. When set, the extractor refuses to install a package that does not match.
        /// </summary>
        public string PackageSha256 { get; set; }
        public string ChangeLogUrl { get; set; }
        public bool IsMandatory { get; set; }
        public bool BackupOldFile { get; set; }

        public ExtractorInfoModel ExtractorInfo { get; set; } = new ExtractorInfoModel();

        /// <summary>
        /// Full path of the local config file (next to the application, independent of the current directory).
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

        public static void Save(AutoUpdaterConfig config)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, SaveOptions));
        }
    }

    public class ExtractorInfoModel
    {
        public string WindowTitle { get; set; }
        public string CurrentVersion { get; set; }
        public string ProcessPath { get; set; }
        public bool IsExtractSuccess { get; set; }
        public DateTime ExtractTime { get; set; }
    }
}
