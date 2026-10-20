using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AutoUpdater
{
    public static class AutoUpdater
    {
        private const string ZipExtractorExeName = "AutoUpdater.ZipExtractor.exe";

        static Logger logger = LogManager.GetCurrentClassLogger();

        public delegate void OnErrorHandler(string msg, Exception ex);
        public static event OnErrorHandler OnError;

        public delegate void OnIgnoreUpdateHandler();
        public static event OnIgnoreUpdateHandler OnIgnoreUpdate;

        public delegate void NeedCloseApplicationHandler();
        public static event NeedCloseApplicationHandler NeedCloseApplication;

        static UpdateForm form = null;
        static string zipExtractorPath = null;

        public static bool CheckUpdate(string configURL, IntPtr? windowHandle = null, bool forceUpdate = false, string appName = null, byte[] appIcon = null, string appIconUri = null)
        {
            try
            {
                logger.Info($"Get config file:{configURL}");
                AutoUpdaterConfig config = AutoUpdaterConfig.GetRemoteFile(configURL)
                    ?? throw new InvalidOperationException("Remote config file is empty.");
                config.ExtractorInfo ??= new ExtractorInfoModel();
                Version newVersion = new Version(config.NewVersion);

                Assembly mainAssembly = Assembly.GetEntryAssembly();
                Version currentVersion = mainAssembly.GetName().Version;
                if (newVersion <= currentVersion)
                {
                    logger.Info($"Already latest version.(Current={currentVersion},New={newVersion})");
                    return false;
                }
                logger.Info($"Has new version.(Current={currentVersion},New={newVersion})");

                // Fail early here instead of after the application has been closed.
                if (!TryGetHttpUri(config.PackageUrl, out _))
                {
                    throw new InvalidOperationException($"Invalid PackageUrl: {config.PackageUrl}");
                }

                string baseDir = GaeBaseDirectory();
                zipExtractorPath = ExtractZipExtractor(baseDir);

                if (string.IsNullOrEmpty(appName))
                {
                    var titleAttribute = mainAssembly.GetCustomAttribute<AssemblyTitleAttribute>();
                    appName = titleAttribute != null ? titleAttribute.Title : mainAssembly.GetName().Name;
                }
                config.ExtractorInfo.WindowTitle = $"{appName} {Localization.LocalizationResources.ResourceManager.GetString("AutoUpdating")}";
                config.ExtractorInfo.CurrentVersion = currentVersion.ToString();
                config.ExtractorInfo.ProcessPath = Path.Combine(baseDir, Path.GetFileName(Environment.ProcessPath));

                AutoUpdaterConfig.Save(config);

                if (forceUpdate)
                {
                    StartUpdateAndExit();
                }
                else
                {
                    ShowUpdateForm(config, appName, currentVersion, windowHandle, appIcon, appIconUri);
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"CheckUpdate Url={configURL}");
                OnError?.Invoke(ex.Message, ex);
                return false;
            }
        }

        private static void ShowUpdateForm(AutoUpdaterConfig config, string appName, Version currentVersion, IntPtr? windowHandle, byte[] appIcon, string appIconUri)
        {
            form = new UpdateForm();
            form.Title = string.Format(Localization.LocalizationResources.ResourceManager.GetString("WindowTitle"), appName, config.NewVersion);//{0} {1} is available!
            form.Title1.Text = string.Format(Localization.LocalizationResources.ResourceManager.GetString("Title1"), appName);//A new version of {0} is available!
            form.Title2.Text = string.Format(Localization.LocalizationResources.ResourceManager.GetString("Title2"), appName, config.NewVersion, currentVersion);//{0} {1} is now available. You have version {2} installed. Would you like to download it now?
            try
            {
                if (appIcon != null)
                {
                    form.AppIcon.Source = LoadImage(appIcon);
                }
                else if (!string.IsNullOrWhiteSpace(appIconUri))
                {
                    form.AppIcon.Source = new BitmapImage(new Uri(appIconUri));
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Load AppIcon");
            }
            if (config.IsMandatory)
            {
                form.btnIgnore.Visibility = Visibility.Hidden;
            }

            form.btnIgnore.Click += BtnIgnore_Click;
            form.btnUpdate.Click += BtnUpdate_Click;
            form.Closed += Form_Closed;
            if (!string.IsNullOrEmpty(config.ChangeLogUrl))
            {
                // Append a timestamp to bypass the cache (the URL may already contain a query string).
                // The form additionally disables the WebView2 cache for the page's CSS/JS/images.
                string separator = config.ChangeLogUrl.Contains('?') ? "&" : "?";
                form.SetChangeLog(
                    new Uri($"{config.ChangeLogUrl}{separator}t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"),
                    new Uri(config.ChangeLogUrl));
            }
            else
            {
                form.chgLogBrowser.Visibility = Visibility.Hidden;
                form.Height = form.row1.Height.Value + form.row3.Height.Value + 50;
                form.row2.Height = new GridLength(0, GridUnitType.Pixel);
            }
            if (windowHandle != null)
            {
                WindowInteropHelper helper = new WindowInteropHelper(form);
                helper.Owner = windowHandle.Value;
            }
            form.ShowDialog();
        }

        public static void OpenChangeLogInBrowser(string configURL)
        {
            try
            {
                AutoUpdaterConfig config = AutoUpdaterConfig.GetRemoteFile(configURL);
                if (config == null || string.IsNullOrWhiteSpace(config.ChangeLogUrl))
                {
                    logger.Warn("ChangeLogUrl is empty.");
                }
                else if (!TryGetHttpUri(config.ChangeLogUrl, out Uri uri))
                {
                    logger.Warn($"ChangeLogUrl is not a valid http(s) URL: {config.ChangeLogUrl}");
                }
                else
                {
                    // Never pass the URL through cmd.exe: characters like '&' would be interpreted as shell commands.
                    using (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })) { }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "OpenChangeLogInBrowser");
            }
        }

        private static bool TryGetHttpUri(string url, out Uri uri)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Extracts the embedded AutoUpdater.ZipExtractor package next to the application and returns the path of its exe.
        /// </summary>
        private static string ExtractZipExtractor(string baseDir)
        {
            string fullBaseDir = Path.GetFullPath(baseDir);
            string baseDirWithSeparator = fullBaseDir.EndsWith(Path.DirectorySeparatorChar) ? fullBaseDir : fullBaseDir + Path.DirectorySeparatorChar;
            logger.Info($"Extract zip extractor to: {fullBaseDir}");

            using (var ms = new MemoryStream(Properties.Resources.AutoUpdater_ZipExtractor, false))
            using (var zipArch = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var entry in zipArch.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

                    string path = Path.GetFullPath(Path.Combine(fullBaseDir, entry.FullName));
                    if (!path.StartsWith(baseDirWithSeparator, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.Warn($"Skip entry outside of base directory: {entry.FullName}");
                        continue;
                    }
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        logger.Info($"Extracting : {path}");
                        entry.ExtractToFile(path, true);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Extract {path}");
                    }
                }
            }
            logger.Info("Done extracting zip extractor.");
            return Path.Combine(fullBaseDir, ZipExtractorExeName);
        }

        private static BitmapImage LoadImage(byte[] imageData)
        {
            //https://stackoverflow.com/questions/9564174/convert-byte-array-to-image-in-wpf
            if (imageData == null || imageData.Length == 0) return null;
            var image = new BitmapImage();
            using (var mem = new MemoryStream(imageData))
            {
                image.BeginInit();
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = null;
                image.StreamSource = mem;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        private static void Form_Closed(object sender, EventArgs e)
        {
            if (form != null)
            {
                form.btnIgnore.Click -= BtnIgnore_Click;
                form.btnUpdate.Click -= BtnUpdate_Click;
                form.Closed -= Form_Closed;
                form = null;
                logger.Info("UpdateForm Closed.");
            }
        }

        private static void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            logger.Info("Click Update button.");
            StartUpdateAndExit();
        }

        private static void BtnIgnore_Click(object sender, RoutedEventArgs e)
        {
            logger.Info("Click Ignore button.");
            form.Close();
            OnIgnoreUpdate?.Invoke();
        }

        private static void StartUpdateAndExit()
        {
            try
            {
                logger.Info($"Start {zipExtractorPath}");
                // Use the full path and set the working directory so the extractor finds AutoUpdaterConfig.json
                // no matter what the current directory of the host application is.
                var psi = new ProcessStartInfo(zipExtractorPath) { WorkingDirectory = Path.GetDirectoryName(zipExtractorPath) };
                using (Process.Start(psi)) { }

                Thread.Sleep(1500);//給更新程式一點時間啟動，再關閉本程式
                if (NeedCloseApplication != null)
                {
                    logger.Info($"Call NeedCloseApplication()");
                    NeedCloseApplication();
                }
                else
                {
                    logger.Info($"Call Environment.Exit");
                    Environment.Exit(Environment.ExitCode);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "StartUpdateAndExit");
                OnError?.Invoke(ex.Message, ex);
            }
        }

        public static string GaeBaseDirectory()
        {
#if DEBUG
            return AppDomain.CurrentDomain.BaseDirectory;
#else
            //發行為Single File執行檔要這樣抓才能得到實際路徑
            return Path.GetDirectoryName(Environment.ProcessPath);
#endif
        }
    }
}
