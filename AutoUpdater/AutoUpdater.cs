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
        /// Checks for a new version and shows the update window when there is one.
        /// Must be called on the UI (STA) thread, and blocks it while the remote config is downloaded;
        /// <see cref="CheckUpdateAsync"/> does neither.
        /// </summary>
        /// <returns>True when a new version is available (the update window was shown or the update started).</returns>
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
        /// Same as <see cref="CheckUpdate"/>, but downloads the remote config without blocking the calling thread
        /// and can be called from any thread: the update window is always shown on a UI thread.
        /// Completes after the update window has been closed.
        /// </summary>
        /// <returns>True when a new version is available (the update window was shown or the update started).</returns>
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
        /// Runs func on a UI (STA) thread: the WPF application's dispatcher, otherwise the caller's UI context
        /// (e.g. WinForms), otherwise a dedicated STA thread.
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

            // The base SynchronizationContext just posts to the thread pool (MTA), which cannot host a WPF window.
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
                // ExtractorInfo is local state; never carry over values (e.g. an old IsExtractSuccess) from the remote file.
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
                string processPath = Environment.ProcessPath;
                config.ExtractorInfo.ProcessPath = processPath != null ? Path.Combine(baseDir, Path.GetFileName(processPath)) : null;
                // The extractor waits for this PID; matching by path fails for e.g. "dotnet app.dll" or short/\\?\ paths.
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
                // An invalid change log URL must not prevent the update itself; just show no change log.
                logger.Warn($"ChangeLogUrl is not a valid http(s) URL, change log is hidden: {config.ChangeLogUrl}");
            }
            if (changeLogUri != null)
            {
                // Append a timestamp to bypass the cache. UriBuilder keeps an existing query string and #fragment intact.
                // The form additionally disables the WebView2 cache for the page's CSS/JS/images.
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
        /// Throws when a file is missing afterwards (e.g. no write permission), so the error surfaces here instead of
        /// when the user clicks "Update".
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
                        // A file locked by a still running extractor is fine as long as it exists.
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
        /// Closes the host application. A WPF application is shut down normally (windows close and the Exit event
        /// runs, so it can save its data); other hosts are ended with Environment.Exit. Because the extractor cannot
        /// overwrite files of a running application, a watchdog still calls Environment.Exit if the normal shutdown
        /// has not finished after 10 seconds.
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
