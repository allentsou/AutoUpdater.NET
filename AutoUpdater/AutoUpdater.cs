using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>
        /// 檢查是否有新版本，有的話顯示更新視窗。
        /// 必須在 UI（STA）執行緒呼叫，下載遠端設定檔期間會卡住該執行緒；
        /// <see cref="CheckUpdateAsync"/> 沒有這兩個限制。
        /// </summary>
        /// <returns>有新版本時回傳 true（已顯示更新視窗或已開始更新）。</returns>
        public static bool CheckUpdate(string configURL, IntPtr? windowHandle = null, bool forceUpdate = false, string appName = null, byte[] appIcon = null, string appIconUri = null)
        {
            AutoUpdaterConfig config;
            try
            {
                logger.Info($"Get config file:{configURL}");
                config = AutoUpdaterConfig.GetRemoteFile(configURL);
            }
            catch (Exception ex)
            {
                return HandleCheckUpdateError(ex, configURL);
            }
            return CheckUpdateCore(config, configURL, windowHandle, forceUpdate, appName, appIcon, appIconUri);
        }

        /// <summary>
        /// 與 <see cref="CheckUpdate"/> 相同，但下載遠端設定檔時不會卡住呼叫端的執行緒，
        /// 而且可從任何執行緒呼叫：更新視窗一律在 UI 執行緒顯示。
        /// 更新視窗關閉後才會完成。
        /// </summary>
        /// <returns>有新版本時回傳 true（已顯示更新視窗或已開始更新）。</returns>
        public static async Task<bool> CheckUpdateAsync(string configURL, IntPtr? windowHandle = null, bool forceUpdate = false, string appName = null, byte[] appIcon = null, string appIconUri = null)
        {
            SynchronizationContext callerContext = SynchronizationContext.Current;
            AutoUpdaterConfig config;
            try
            {
                logger.Info($"Get config file:{configURL}");
                config = await AutoUpdaterConfig.GetRemoteFileAsync(configURL).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await RunOnUiThreadAsync(callerContext, () => HandleCheckUpdateError(ex, configURL)).ConfigureAwait(false);
            }
            return await RunOnUiThreadAsync(callerContext,
                () => CheckUpdateCore(config, configURL, windowHandle, forceUpdate, appName, appIcon, appIconUri)).ConfigureAwait(false);
        }

        /// <summary>
        /// 在 UI（STA）執行緒執行 func：優先用 WPF 應用程式的 Dispatcher，其次用呼叫端的 UI 內容
        /// （例如 WinForms），都沒有時另開一條專用的 STA 執行緒。
        /// </summary>
        private static Task<bool> RunOnUiThreadAsync(SynchronizationContext callerContext, Func<bool> func)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                return dispatcher.InvokeAsync(func).Task;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Run()
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }

            // 基底 SynchronizationContext 只會丟到執行緒集區（MTA），無法顯示 WPF 視窗。
            if (callerContext != null && callerContext.GetType() != typeof(SynchronizationContext))
            {
                callerContext.Post(_ => Run(), null);
            }
            else
            {
                var thread = new Thread(Run) { IsBackground = true, Name = "AutoUpdaterUI" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            return tcs.Task;
        }

        private static bool HandleCheckUpdateError(Exception ex, string configURL)
        {
            logger.Error(ex, $"CheckUpdate Url={configURL}");
            OnError?.Invoke(ex.Message, ex);
            return false;
        }

        private static bool CheckUpdateCore(AutoUpdaterConfig config, string configURL, IntPtr? windowHandle, bool forceUpdate, string appName, byte[] appIcon, string appIconUri)
        {
            try
            {
                if (config == null)
                {
                    throw new InvalidOperationException("Remote config file is empty.");
                }
                // ExtractorInfo 是本機狀態，不沿用遠端設定檔帶來的值（例如舊的 IsExtractSuccess）。
                config.ExtractorInfo = new ExtractorInfoModel();
                Version newVersion = new Version(config.NewVersion);

                Assembly mainAssembly = Assembly.GetEntryAssembly();
                Version currentVersion = mainAssembly.GetName().Version;
                if (newVersion <= currentVersion)
                {
                    logger.Info($"Already latest version.(Current={currentVersion},New={newVersion})");
                    return false;
                }
                logger.Info($"Has new version.(Current={currentVersion},New={newVersion})");

                // 在這裡就先報錯，而不是等主程式關閉後才失敗。
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
                string processPath = Environment.ProcessPath;
                config.ExtractorInfo.ProcessPath = processPath != null ? Path.Combine(baseDir, Path.GetFileName(processPath)) : null;
                // extractor 會等待這個 PID 結束；用路徑比對在 "dotnet app.dll"、8.3 短路徑或 \\?\ 路徑時會失敗。
                config.ExtractorInfo.ProcessId = Environment.ProcessId;

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
                return HandleCheckUpdateError(ex, configURL);
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
            Uri changeLogUri = null;
            if (!string.IsNullOrWhiteSpace(config.ChangeLogUrl) && !TryGetHttpUri(config.ChangeLogUrl, out changeLogUri))
            {
                // 更新說明網址無效時不可影響更新本身，只是不顯示更新說明。
                logger.Warn($"ChangeLogUrl is not a valid http(s) URL, change log is hidden: {config.ChangeLogUrl}");
            }
            if (changeLogUri != null)
            {
                // 加上時間戳避開快取。UriBuilder 會保留原有的查詢字串與 #fragment。
                // 更新視窗另外會停用 WebView2 快取，連頁面的 CSS/JS/圖片也不使用快取。
                var builder = new UriBuilder(changeLogUri);
                string query = builder.Query.TrimStart('?');
                builder.Query = (query.Length > 0 ? query + "&" : "") + $"t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                form.SetChangeLog(builder.Uri, changeLogUri);
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
                    // 不可透過 cmd.exe 開啟網址：'&' 等字元會被當成指令執行。
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
        /// 將內嵌的 AutoUpdater.ZipExtractor 解壓到程式目錄，並回傳其 exe 路徑。
        /// 解壓後若有檔案不存在（例如沒有寫入權限）就拋出例外，讓錯誤在這裡就出現，
        /// 而不是等使用者按下「更新」才發生。
        /// </summary>
        private static string ExtractZipExtractor(string baseDir)
        {
            var missingFiles = new System.Collections.Generic.List<string>();
            string fullBaseDir = Path.GetFullPath(baseDir);
            string baseDirWithSeparator = fullBaseDir.EndsWith(Path.DirectorySeparatorChar) ? fullBaseDir : fullBaseDir + Path.DirectorySeparatorChar;
            logger.Info($"Extract zip extractor to: {fullBaseDir}");

            using (var ms = new MemoryStream(Properties.Resources.AutoUpdater_ZipExtractor, false))
            using (var zipArch = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var entry in zipArch.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // 資料夾項目

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
                        // 檔案被仍在執行的 extractor 鎖住沒關係，只要檔案存在即可。
                        logger.Error(ex, $"Extract {path}");
                        if (!File.Exists(path))
                        {
                            missingFiles.Add(entry.FullName);
                        }
                    }
                }
            }

            string exePath = Path.Combine(fullBaseDir, ZipExtractorExeName);
            if (!File.Exists(exePath) && !missingFiles.Contains(ZipExtractorExeName))
            {
                missingFiles.Add(ZipExtractorExeName);
            }
            if (missingFiles.Count > 0)
            {
                throw new IOException($"Cannot write the updater to {fullBaseDir} (missing: {string.Join(", ", missingFiles)}). Check write permission of the application directory.");
            }
            logger.Info("Done extracting zip extractor.");
            return exePath;
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
                // 使用完整路徑並設定工作目錄，不論主程式目前的工作目錄為何，
                // extractor 都能找到 AutoUpdaterConfig.json。
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
                    ExitApplication();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "StartUpdateAndExit");
                OnError?.Invoke(ex.Message, ex);
            }
        }

        /// <summary>
        /// 關閉主程式。WPF 應用程式會正常關閉（視窗會關閉、Exit 事件會執行，可以存檔）；
        /// 其他類型的主程式則用 Environment.Exit 結束。由於 extractor 無法覆寫執行中程式的檔案，
        /// 正常關閉 10 秒後仍未結束時，監看執行緒會再呼叫 Environment.Exit。
        /// </summary>
        private static void ExitApplication()
        {
            Application app = Application.Current;
            if (app == null)
            {
                logger.Info("Call Environment.Exit");
                Environment.Exit(Environment.ExitCode);
                return;
            }

            var watchdog = new Thread(() =>
            {
                Thread.Sleep(10000);
                logger.Warn("Application did not exit within 10 seconds, call Environment.Exit");
                Environment.Exit(Environment.ExitCode);
            })
            { IsBackground = true, Name = "AutoUpdaterExitWatchdog" };
            watchdog.Start();

            logger.Info("Call Application.Shutdown");
            if (app.Dispatcher.CheckAccess())
            {
                app.Shutdown();
            }
            else
            {
                app.Dispatcher.BeginInvoke(new Action(() => app.Shutdown()));
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
