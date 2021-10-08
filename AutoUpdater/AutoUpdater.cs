using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace AutoUpdater
{
    public class AutoUpdater
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
        public static bool CheckUpdate(Window owner, string configURL, bool forceUpdate = false, string appName = null)
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
                    logger.Info($"Already latest Version.(Current={currentVersion},New={newVersion})");
                    return false;
                }
                logger.Info($"Has new Version.(Current={currentVersion},New={newVersion})");
                string baseDir = GaeBaseDirectory();
                zipExtractorPath = Path.Combine(baseDir, "AutoUpdater.ZipExtractor.exe");
                if (!File.Exists(zipExtractorPath))
                {
                    string msg = $"{zipExtractorPath} not found.";
                    logger.Error(msg);
                    OnError?.Invoke(msg, null);
                    return false;
                }

                if (string.IsNullOrEmpty(appName))
                {
                    var titleAttribute = (AssemblyTitleAttribute)GetAttribute(mainAssembly, typeof(AssemblyTitleAttribute));
                    appName = titleAttribute != null ? titleAttribute.Title : mainAssembly.GetName().Name;
                }
                config.ExtractorInfo.WindowTitle = $"{appName} {Localization.LocalizationResources.ResourceManager.GetString("AutoUpdating")}";
                config.ExtractorInfo.CurrentVersion = currentVersion.ToString();
                config.ExtractorInfo.ProcessPath = Path.Combine(GaeBaseDirectory(), System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName);
                config.ExtractorInfo.WaitingCloseProcessID = Process.GetCurrentProcess().Id;

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
                    form.btnIgnore.Click += BtnIgnore_Click;
                    form.btnUpdate.Click += BtnUpdate_Click;
                    form.Closed += Form_Closed;
                    if (!string.IsNullOrEmpty(config.ChangeLogUrl))
                    {
                        form.chgLogBrowser.Navigate(config.ChangeLogUrl);
                    }
                    else
                    {
                        //form.ChangeLogPanel.Visibility = Visibility.Hidden;
                        form.chgLogBrowser.Visibility = Visibility.Hidden;
                        form.Width = 150;
                        form.Height = 600;
                    }
                    form.Owner = owner;
                    form.ShowDialog();
                }
                //UpdateForm form = new UpdateForm();

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                OnError?.Invoke(ex.Message, ex);
                return false;
            }
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
                logger.Info($"Start {zipExtractorPath}");
                var proc = Process.Start(zipExtractorPath);
                Thread.Sleep(1000);//等一下才不會掛掉
                if (NeedCloseApplication != null)
                {
                    logger.Info($"Call NeedCloseApplication()");
                    NeedCloseApplication();
                }
                else
                {
                    logger.Info($"Call Environment.Exit");
                    //Environment.Exit(Environment.ExitCode);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
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
