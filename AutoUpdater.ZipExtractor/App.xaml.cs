using AutoUpdater.ZipExtractor.ViewModel;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AutoUpdater.ZipExtractor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static Logger logger = LogManager.GetCurrentClassLogger();

        App()
        {
            //https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-lcid/a9eac961-e77d-41a6-90a5-ce1a8b0cdb9c?redirectedfrom=MSDN
            //System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            process();
        }


        private async void process()
        {
            logger.Info($"--------------AutoUpdater.ZipExtractor Start--------------");

            MainWindowViewModel vm = new MainWindowViewModel();
            MainWindow window = new MainWindow();
            window.DataContext = vm;
            window.Show();

            void ShowMsgBoxAndExit(string msg)
            {
                Action action = () =>
                {
                    logger.Error($"ShowMsgBoxAndExit Msg={msg}");
                    if (MessageBoxResult.OK == MessageBox.Show(window, msg, "", MessageBoxButton.OK, MessageBoxImage.Error))
                    {
                        Environment.Exit(Environment.ExitCode);
                    }
                };

                if (Application.Current.Dispatcher.Thread == Thread.CurrentThread)
                {
                    action();
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(action, null);
                }
            }

            //return;

            await Task.Run(async () =>
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Encoding zipArchiveEncoding = Encoding.GetEncoding(950);

                #region Get local config file
                AutoUpdaterConfig config = null;
                try
                {
                    config = AutoUpdaterConfig.GetLocalFile();
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                    ShowMsgBoxAndExit(ex.Message);
                    return;
                }

                if (config == null)
                {
                    vm.WindowTitle = Localization.LocalizationResources.ResourceManager.GetString("WindowTitle");
                    string msg = string.Format(Localization.LocalizationResources.ResourceManager.GetString("ConfigNotFound"), AutoUpdaterConfig.FileName);
                    logger.Error(msg);
                    ShowMsgBoxAndExit(msg);
                    return;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(config.ExtractorInfo.WindowTitle))
                    {
                        vm.WindowTitle = config.ExtractorInfo.WindowTitle;
                    }
                    else
                    {
                        vm.WindowTitle = Localization.LocalizationResources.ResourceManager.GetString("WindowTitle");
                    }
                    logger.Info($"Get config file : {AutoUpdaterConfig.FileName}");
                }
                #endregion


                #region Waiting app closed

                try
                {
                    vm.UpdateStepText(MainWindowViewModel.enumStep.WaitingAppClose);
                    vm.ProgressBarValue = 0;
                    vm.ProgressText = "";
                    vm.AddMsg(Localization.LocalizationResources.ResourceManager.GetString("LabelWaitClose"));

                    int timeout = 60 * 1000;
                    if (!string.IsNullOrWhiteSpace(config.ExtractorInfo.ProcessPath))
                    {
                        logger.Info($"Start search process and wait exit : {config.ExtractorInfo.ProcessPath}");
                        bool isFound = false;
                        int loopCount = 0;
                        foreach (Process proc in Process.GetProcesses())
                        {
                            loopCount++;
                            string procPath = null;
                            int pid = 0;
                            try
                            {
                                procPath = proc.MainModule.FileName;
                                pid = proc.Id;
                            }
                            catch (System.ComponentModel.Win32Exception win32ex)
                            {
                                continue;
                            }
                            catch (Exception ex)
                            {
                                continue;
                            }

                            try
                            {
                                if (procPath == config.ExtractorInfo.ProcessPath)
                                {
                                    isFound = true;
                                    logger.Info($"WaitForExit,PID={pid},Timeout={timeout},LoopCount={loopCount}");
                                    vm.AddMsg(string.Format(Localization.LocalizationResources.ResourceManager.GetString("WaitingProcExit"), $"PID {pid}"));
                                    bool isClose = proc.WaitForExit(timeout);
                                    if (!isClose)
                                    {
                                        logger.Warn($"WaitForExit Timeout, Kill PID {pid}");
                                        vm.AddMsg(Localization.LocalizationResources.ResourceManager.GetString("ForceKillProc"));
                                        proc.Kill();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, $"WaitForExit LoopCount={loopCount}");
                                vm.AddMsg(ex.Message, true);
                            }

                        }
                        if (!isFound)
                        {
                            logger.Error($"Not found process,Path={config.ExtractorInfo.ProcessPath}");
                        }
                    }
                    else
                    {
                        logger.Error("Config.ExtractorInfo.ProcessPath is NULL");
                    }

                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                    vm.AddMsg(ex.Message, true);
                    ShowMsgBoxAndExit(ex.Message);
                    return;
                }

                #endregion
                Thread.Sleep(2000);
                string baseDir = Misc.GaeBaseDirectory();
                logger.Info($"BaseDir = {baseDir}");

                #region Download ZIP package file
                string updateZIPPackageFilePath = null;
                try
                {
                    logger.Info($"Start Download : {config.PackageUrl}");
                    vm.UpdateStepText(MainWindowViewModel.enumStep.DownloadPackage);
                    vm.ProgressBarValue = 0;
                    vm.ProgressText = "";
                    vm.AddMsg(string.Format(Localization.LocalizationResources.ResourceManager.GetString("DownloadFile"), config.PackageUrl));

                    Uri uri = new Uri(config.PackageUrl);
                    updateZIPPackageFilePath = Path.Combine(baseDir, System.IO.Path.GetFileName(uri.LocalPath));
                    using (HttpClientDownloadWithProgress c = new HttpClientDownloadWithProgress(config.PackageUrl, updateZIPPackageFilePath))
                    {
                        c.ProgressChanged += (long? totalFileSize, long totalBytesDownloaded, double? progressPercentage, double kbPerSeconds) =>
                        {
                            if (totalFileSize.HasValue && progressPercentage.HasValue)
                            {
                                vm.ProgressBarValue = Convert.ToInt32(progressPercentage.Value);
                                if (kbPerSeconds > 1024)
                                {
                                    vm.ProgressText = $"{totalBytesDownloaded / 1024.0 / 1024.0:N1} MB / {totalFileSize.Value / 1024.0 / 1024.0:N1} MB  ({kbPerSeconds / 1024:N1} MB/s)";
                                }
                                else
                                {
                                    vm.ProgressText = $"{totalBytesDownloaded / 1024.0 / 1024.0:N1} MB / {totalFileSize.Value / 1024.0 / 1024.0:N1} MB  ({kbPerSeconds:N0} KB/s)";
                                }
                            }
                            else
                            {
                                vm.ProgressText = $"{Math.Round(totalBytesDownloaded / 1024.0 / 1024.0, 1)} MB";
                            }
                        };
                        await c.StartDownload();
                    }
                    logger.Info($"Download done.");
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                    vm.AddMsg(ex.Message, true);
                    ShowMsgBoxAndExit(ex.Message);
                    return;
                }
                #endregion

                int errorCounts = 0;

                #region Backup
                try
                {
                    logger.Info("Backup...");
                    vm.UpdateStepText(MainWindowViewModel.enumStep.Backup);
                    vm.ProgressBarValue = 0;
                    vm.ProgressText = "";

                    List<string> needBackupFilePath = new List<string>();
                    if (config.BackupOldFile)
                    {
                        using (FileStream fs = File.Open(updateZIPPackageFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            using (ZipArchive zipArch = new ZipArchive(fs, ZipArchiveMode.Read, false, zipArchiveEncoding))
                            {
                                foreach (var entry in zipArch.Entries)
                                {
                                    bool isDirectory = (entry.FullName.EndsWith('\\') || entry.FullName.EndsWith('/'));
                                    if (!isDirectory)
                                    {
                                        string path = Path.Combine(baseDir, entry.FullName);
                                        if (File.Exists(path))
                                        {
                                            needBackupFilePath.Add(path);
                                        }
                                        else
                                        {
                                            Debug.WriteLine(path);
                                        }
                                    }
                                }
                            }
                            fs.Close();
                        }

                        int needBackupFileCount = needBackupFilePath.Count;
                        if (needBackupFileCount > 0)
                        {
                            Directory.CreateDirectory("AutoUpdaterBackup");
                            string zipFile = Path.Combine(baseDir, $"AutoUpdaterBackup\\{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                            logger.Info($"Need backup file count : {needBackupFileCount}, Backup path : {zipFile}");
                            int addFileCount = 0;
                            using (FileStream fs = File.Open(zipFile, FileMode.Create, FileAccess.Write))
                            {
                                using (ZipArchive zipArch = new ZipArchive(fs, ZipArchiveMode.Create))
                                {
                                    foreach (string bakPath in needBackupFilePath)
                                    {
                                        addFileCount++;
                                        vm.ProgressBarValue = Convert.ToInt32(Convert.ToDouble(addFileCount) / Convert.ToDouble(needBackupFileCount) * 100d);
                                        vm.ProgressText = $"{addFileCount} / {needBackupFileCount}";

                                        logger.Info($"Adding ({addFileCount}/{needBackupFileCount}) : {bakPath}");
                                        string removeBaseDirPath = bakPath.Replace(baseDir, "").TrimStart('\\');
                                        vm.AddMsg(string.Format(Localization.LocalizationResources.ResourceManager.GetString("AddingFile"), removeBaseDirPath));

                                        ZipArchiveEntry entry = zipArch.CreateEntry(removeBaseDirPath);
                                        using (var zipStream = entry.Open())
                                        {
                                            byte[] bytes = File.ReadAllBytes(bakPath);
                                            await zipStream.WriteAsync(bytes, 0, bytes.Length);
                                        }
                                        logger.Info($"Done.");
                                    }
                                }
                                fs.Close();
                            }
                        }
                        else
                        {
                            logger.Info($"No file to backup.");
                        }
                    }
                    else
                    {
                        logger.Info($"BackupOldFile = False, Skip backup files.");
                    }
                    logger.Info("Backup done.");
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                    vm.AddMsg(ex.Message, true);
                    errorCounts++;
                    ShowMsgBoxAndExit(ex.Message);
                    return;
                }
                #endregion

                #region Extractor
                try
                {
                    vm.UpdateStepText(MainWindowViewModel.enumStep.Extract);
                    vm.ProgressBarValue = 0;
                    vm.ProgressText = "";
                    logger.Info($"Start Extractor {updateZIPPackageFilePath}");
                    using (FileStream fs = File.Open(updateZIPPackageFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        using (ZipArchive zipArch = new ZipArchive(fs, ZipArchiveMode.Read, false, zipArchiveEncoding))
                        {
                            int totalCount = zipArch.Entries.Count;
                            int extractCount = 0;
                            foreach (var entry in zipArch.Entries)
                            {
                                extractCount++;
                                vm.ProgressBarValue = Convert.ToInt32(Convert.ToDouble(extractCount) / Convert.ToDouble(totalCount) * 100d);
                                vm.ProgressText = $"{extractCount} / {totalCount}";

                                bool isDirectory = (entry.FullName.EndsWith('\\') || entry.FullName.EndsWith('/'));
                                if (!isDirectory)
                                {
                                    string path = Path.Combine(baseDir, entry.FullName);
                                    logger.Info($"Extracting : {path}");
                                    vm.AddMsg(string.Format(Localization.LocalizationResources.ResourceManager.GetString("ExtractingFile"), entry.FullName));
                                    try
                                    {
                                        string dir = Path.GetDirectoryName(path);
                                        if (dir != null && !Directory.Exists(dir))
                                        {
                                            Directory.CreateDirectory(dir);
                                        }
                                        entry.ExtractToFile(path, true);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Error(ex);
                                        vm.AddMsg($"{entry.FullName} {ex.Message}", true);
                                        errorCounts++;
                                    }
                                    logger.Info($"Done.");
                                }
                            }
                        }
                        fs.Close();
                    }
                    logger.Info($"Extractor done.");

                    try
                    {
                        logger.Info($"Delete {updateZIPPackageFilePath}");
                        File.Delete(updateZIPPackageFilePath);
                        logger.Info($"Delete done.");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Delete {updateZIPPackageFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                    vm.AddMsg(ex.Message, true);
                    errorCounts++;
                    ShowMsgBoxAndExit(ex.Message);
                    return;
                }
                #endregion

                if (config.ExtractorInfo.ProcessPath != null && File.Exists(config.ExtractorInfo.ProcessPath))
                {
                    //string fn = Path.GetFileName(config.ExtractorInfo.ProcessPath);
                    logger.Info($"Start {config.ExtractorInfo.ProcessPath}");
                    Process.Start(config.ExtractorInfo.ProcessPath);
                }
                else
                {
                    logger.Warn($"No path to start process.");
                }

                if (errorCounts > 0)
                {
                    vm.AddMsg(string.Format(Localization.LocalizationResources.ResourceManager.GetString("ErrorCountMsg"), errorCounts), true);
                    config.ExtractorInfo.IsExtractSuccess = false;
                }
                else
                {
                    config.ExtractorInfo.IsExtractSuccess = true;
                }

                config.ExtractorInfo.ExtractTime = DateTime.Now;
                AutoUpdaterConfig.Save(config);

                vm.AddMsg("Done");
                logger.Info("All done, Close application.");
                //#if !DEBUG
                Thread.Sleep(1500);
                Environment.Exit(Environment.ExitCode);
                //#endif
            });
        }
    }
}
