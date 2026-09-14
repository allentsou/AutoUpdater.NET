using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AutoUpdater
{
    public static class AutoUpdater
    {
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
                AutoUpdaterConfig config = AutoUpdaterConfig.GetRemoteFile(configURL);
                Version newVersion = new Version(config.NewVersion);

                Assembly mainAssembly = Assembly.GetEntryAssembly();
                Version currentVersion = mainAssembly.GetName().Version;
                if (newVersion <= currentVersion)
                {
                    logger.Info($"Already latest version.(Current={currentVersion},New={newVersion})");
                    return false;
                }
                logger.Info($"Has new version.(Current={currentVersion},New={newVersion})");
                string baseDir = GaeBaseDirectory();
                zipExtractorPath = Path.Combine(baseDir, "AutoUpdater.ZipExtractor.zip");
                logger.Info($"Write zip extractor to: {zipExtractorPath}");
                File.WriteAllBytes(zipExtractorPath, Properties.Resources.AutoUpdater_ZipExtractor);
                using (FileStream fs = File.Open(zipExtractorPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (ZipArchive zipArch = new ZipArchive(fs, ZipArchiveMode.Read, false, Encoding.ASCII))
                    {
                        int totalCount = zipArch.Entries.Count;
                        foreach (var entry in zipArch.Entries)
                        {
                            bool isDirectory = (entry.FullName.EndsWith("\\") || entry.FullName.EndsWith("/"));
                            if (!isDirectory)
                            {
                                string path = Path.Combine(baseDir, entry.FullName);
                                try
                                {
                                    string dir = Path.GetDirectoryName(path);
                                    if (dir != null && !Directory.Exists(dir))
                                    {
                                        Directory.CreateDirectory(dir);
                                    }
                                    logger.Info($"Extracting : {path}");
                                    entry.ExtractToFile(path, true);
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex);
                                }
                                logger.Info($"Done.");
                            }
                        }
                    }
                    logger.Info($"Done extracting zip extractor.");
                    fs.Close();
                }

                if (string.IsNullOrEmpty(appName))
                {
                    var titleAttribute = (AssemblyTitleAttribute)GetAttribute(mainAssembly, typeof(AssemblyTitleAttribute));
                    appName = titleAttribute != null ? titleAttribute.Title : mainAssembly.GetName().Name;
                }
                config.ExtractorInfo.WindowTitle = $"{appName} {Localization.LocalizationResources.ResourceManager.GetString("AutoUpdating")}";
                config.ExtractorInfo.CurrentVersion = currentVersion.ToString();
                config.ExtractorInfo.ProcessPath = Path.Combine(GaeBaseDirectory(), System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName);

                AutoUpdaterConfig.Save(config);

                if (forceUpdate)
                {
                    StartUpdateAndExit();
                }
                else
                {
                    form = new UpdateForm();
                    form.Title = string.Format(Localization.LocalizationResources.ResourceManager.GetString("WindowTitle"), appName, config.NewVersion);//{0} {1} is available!
                    form.Title1.Text = string.Format(Localization.LocalizationResources.ResourceManager.GetString("Title1"), appName);//A new version of {0} is available!
                    form.Title2.Text = string.Format(Localization.LocalizationResources.ResourceManager.GetString("Title2"), appName, config.NewVersion, currentVersion);//{0} {1} is now available. You have version {2} installed. Would you like to download it now?
                    //form.AppIcon.Source = Application.Current.Resources["Icon_Main"]
                    try
                    {
                        if (appIcon != null)
                        {
                            form.AppIcon.Source = LoadImage(appIcon);
                        }
                        else if (!string.IsNullOrWhiteSpace(appIconUri))
                        {
                            //form.AppIcon.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/Icon_Main.ico"));
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
                        form.chgLogBrowser.Navigate(config.ChangeLogUrl + $"?t={DateTime.UtcNow.Ticks}");
                    }
                    else
                    {
                        //form.ChangeLogPanel.Visibility = Visibility.Hidden;
                        form.chgLogBrowser.Visibility = Visibility.Hidden;
                        form.Height = form.Height - (form.Height - form.row1.Height.Value - form.row3.Height.Value) + 50;
                        form.row2.Height = new GridLength(0, GridUnitType.Pixel);
                    }
                    if (windowHandle != null)
                    {
                        WindowInteropHelper helper = new WindowInteropHelper(form);
                        helper.Owner = windowHandle.Value;
                    }
                    form.ShowDialog();
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

        public static void OpenChangeLogInBrowser(string configURL)
        {
            try
            {
                AutoUpdaterConfig config = AutoUpdaterConfig.GetRemoteFile(configURL);
                if (string.IsNullOrWhiteSpace(config.ChangeLogUrl))
                {
                    logger.Warn("ChangeLogUrl is empty.");
                }
                else
                {
                    //https://brockallen.com/2016/09/24/process-start-for-urls-on-net-core/
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {config.ChangeLogUrl}") { CreateNoWindow = true });
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "OpenChangeLogInBrowser");
            }
        }

        private static BitmapImage LoadImage(byte[] imageData)
        {
            //https://stackoverflow.com/questions/9564174/convert-byte-array-to-image-in-wpf
            if (imageData == null || imageData.Length == 0) return null;
            var image = new BitmapImage();
            using (var mem = new MemoryStream(imageData))
            {
                mem.Position = 0;
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

        private static void BtnUpdate_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            logger.Info("Click Update button.");
            StartUpdateAndExit();
        }

        private static void BtnIgnore_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            logger.Info("Click Ignore button.");
            form.Close();
            OnIgnoreUpdate?.Invoke();
        }
        private static void StartUpdateAndExit()
        {
            try
            {
                string fn = Path.GetFileName(zipExtractorPath).Replace(".zip",".exe");
                logger.Info($"Start {fn}");
                var proc = Process.Start(fn);

                Thread.Sleep(1500);//等一下才不會掛掉
                if (NeedCloseApplication != null)
                {
                    logger.Info($"Call NeedCloseApplication()");
                    NeedCloseApplication();
                }
                else
                {
                    logger.Info($"Call Environment.Exit");
                    //#if !DEBUG
                    Environment.Exit(Environment.ExitCode);
                    //#endif
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "StartUpdateAndExit");
                OnError?.Invoke(ex.Message, ex);
            }
        }

        private static Attribute GetAttribute(Assembly assembly, Type attributeType)
        {
            object[] attributes = assembly.GetCustomAttributes(attributeType, false);
            if (attributes.Length == 0)
            {
                return null;
            }
            return (Attribute)attributes[0];
        }

        public static string GaeBaseDirectory()
        {
            string path = "";
#if DEBUG
            path = AppDomain.CurrentDomain.BaseDirectory;
#else
            //發行為Single File執行檔要這樣抓才能得到實際路徑
            path = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
#endif
            return path;
        }
    }
}
