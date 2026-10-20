using AutoUpdater.ZipExtractor.ViewModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
        private static SimpleLogger logger = new SimpleLogger();

        App()
        {
            //https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-lcid/a9eac961-e77d-41a6-90a5-ce1a8b0cdb9c?redirectedfrom=MSDN
            //System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            process();
        }

        /// <summary>
        /// 結束程式。若 Environment.Exit 在數秒內沒有完成（例如被 finalizer 或 ProcessExit 處理常式卡住），
        /// 就直接終結行程，避免更新視窗一直留著。
        /// </summary>
        private static void ExitApplication(int exitCode)
        {
            logger.Info($"Exit({exitCode})");
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(5000);
                logger.Warn("Environment.Exit did not finish within 5 seconds, kill process.");
                Process.GetCurrentProcess().Kill();
            })
            { IsBackground = true, Name = "ExitWatchdog" };
            watchdog.Start();

            logger.Info($"--------------Call Environment.Exit({exitCode})--------------");
            Environment.Exit(exitCode);
        }

        /// <summary>
        /// 等待指定 PID 的主程式（由函式庫寫入設定檔）結束。
        /// </summary>
        private static void WaitForAppExitByPid(int pid, int timeout, MainWindowViewModel vm)
        {
            Process proc;
            try
            {
                proc = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                logger.Info($"PID {pid} has already exited.");
                return;
            }

            using (proc)
            {
                // 防止 PID 被重複使用：主程式啟動了 extractor，所以它的啟動時間一定比較早。
                try
                {
                    using (Process self = Process.GetCurrentProcess())
                    {
                        if (proc.StartTime > self.StartTime)
                        {
                            logger.Warn($"PID {pid} belongs to a process started after the extractor, the application has already exited.");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 系統管理員權限等程序無法讀取 StartTime，此時視為就是主程式。
                    logger.Warn($"Cannot read StartTime of PID {pid}: {ex.Message}");
                }

                WaitForAppExit(proc, pid, timeout, vm);
            }
        }

        private static void WaitForAppExit(Process proc, int pid, int timeout, MainWindowViewModel vm)
        {
            logger.Info($"WaitForExit,PID={pid},Timeout={timeout}");
            vm.AddMsg(string.Format(Localization.LocalizationResources.ResourceManager.GetString("WaitingProcExit"), $"PID {pid}"));
            if (!proc.WaitForExit(timeout))
            {
                logger.Warn($"WaitForExit Timeout, Kill PID {pid}");
                vm.AddMsg(Localization.LocalizationResources.ResourceManager.GetString("ForceKillProc"));
                proc.Kill();
                proc.WaitForExit(timeout);
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                logger.Info($"Delete {path}");
                File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Delete {path}");
            }
        }


        private async void process()
        {
            try
            {
                await ProcessAsync();
            }
            catch (Exception ex)
            {
                // async void 方法中未處理的例外會讓程式直接崩潰，而且沒有任何訊息。
                logger.Error(ex, "Unhandled exception");
                MessageBox.Show(ex.Message, "", MessageBoxButton.OK, MessageBoxImage.Error);
                ExitApplication(1);
            }
        }

        private async Task ProcessAsync()
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
                    logger.Info($"ShowMsgBoxAndExit Msg={msg}");
                    if (MessageBoxResult.OK == MessageBox.Show(window, msg, "", MessageBoxButton.OK, MessageBoxImage.Error))
                    {
                        ExitApplication(Environment.ExitCode);
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
                    logger.Error(ex, "AutoUpdaterConfig.GetLocalFile()");
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
                    config.ExtractorInfo ??= new ExtractorInfoModel();
                    string title = !string.IsNullOrWhiteSpace(config.ExtractorInfo.WindowTitle)
                        ? config.ExtractorInfo.WindowTitle
                        : Localization.LocalizationResources.ResourceManager.GetString("WindowTitle");

                    // 標題顯示版本變化，例如「TestApp2 自動更新 (0.0.0.0 到 0.0.0.1)」
                    if (!string.IsNullOrWhiteSpace(config.ExtractorInfo.CurrentVersion) && !string.IsNullOrWhiteSpace(config.NewVersion))
                    {
                        string versionChange = string.Format(Localization.LocalizationResources.ResourceManager.GetString("VersionChange"),
                            config.ExtractorInfo.CurrentVersion, config.NewVersion);
                        title = $"{title.TrimEnd('.', '…', ' ')} ({versionChange})";
                    }
                    vm.WindowTitle = title;
                    logger.Info($"Get config file : {AutoUpdaterConfig.FileName}");
                }
                #endregion


                #region Waiting app close

                try
                {
                    vm.UpdateStepText(MainWindowViewModel.enumStep.WaitingAppClose);
                    vm.ProgressBarValue = 0;
                    vm.ProgressText = "";
                    vm.AddMsg(Localization.LocalizationResources.ResourceManager.GetString("LabelWaitClose"));

                    int timeout = 60 * 1000;
                    if (config.ExtractorInfo.ProcessId > 0)
                    {
                        WaitForAppExitByPid(config.ExtractorInfo.ProcessId, timeout, vm);
                    }
                    else if (!string.IsNullOrWhiteSpace(config.ExtractorInfo.ProcessPath))
                    {
                        logger.Info($"Start search process and waiting close : {config.ExtractorInfo.ProcessPath}");
                        bool isFound = false;
                        int loopCount = 0;
                        foreach (Process proc in Process.GetProcesses())
                        {
                            using (proc)
                            {
                                loopCount++;
                                string procPath = null;
                                int pid = 0;
                                try
                                {
                                    // 系統或受保護的程序、不同位元數的程序會拋出例外，直接略過。
                                    procPath = proc.MainModule.FileName;
                                    pid = proc.Id;
                                }
                                catch (Exception)
                                {
                                    continue;
                                }

                                try
                                {
                                    if (string.Equals(procPath, config.ExtractorInfo.ProcessPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        isFound = true;
                                        WaitForAppExit(proc, pid, timeout, vm);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, $"WaitForExit LoopCount={loopCount}");
                                    vm.AddMsg(ex.Message, true);
                                }
                            }
                        }
                        if (!isFound)
                        {
                            logger.Warn($"Not found process,Path={config.ExtractorInfo.ProcessPath}");
                        }
                    }
                    else
                    {
                        logger.Error("Config.ExtractorInfo has neither ProcessId nor ProcessPath");
                    }

                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Waiting app close");
                    vm.AddMsg(ex.Message, true);
                    ShowMsgBoxAndExit(ex.Message);
                    return;
                }

                #endregion
                await Task.Delay(1000 * 2);
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
                    if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    {
                        throw new InvalidOperationException($"Unsupported PackageUrl scheme: {uri.Scheme}");
                    }
                    string packageFileName = Path.GetFileName(uri.LocalPath);
                    if (string.IsNullOrWhiteSpace(packageFileName))
                    {
                        packageFileName = "AutoUpdaterPackage.zip";
                    }
                    updateZIPPackageFilePath = Path.Combine(baseDir, packageFileName);
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

                    if (!string.IsNullOrWhiteSpace(config.PackageSha256))
                    {
                        string actualHash;
                        using (FileStream hashStream = File.OpenRead(updateZIPPackageFilePath))
                        {
                            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream));
                        }
                        if (!string.Equals(actualHash, config.PackageSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException($"Package SHA-256 mismatch. Expected={config.PackageSha256}, Actual={actualHash}");
                        }
                        logger.Info("Package SHA-256 verified.");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Download {config.PackageUrl}");
                    vm.AddMsg(ex.Message, true);
                    TryDeleteFile(updateZIPPackageFilePath);
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
                                    if (string.IsNullOrEmpty(entry.Name) || Misc.IsUpdaterOwnFile(entry.Name)) continue; // 資料夾項目或更新程式自己的檔案

                                    string path = Misc.GetSafeExtractPath(baseDir, entry.FullName);
                                    if (path != null && File.Exists(path))
                                    {
                                        needBackupFilePath.Add(path);
                                    }
                                }
                            }
                        }

                        int needBackupFileCount = needBackupFilePath.Count;
                        if (needBackupFileCount > 0)
                        {
                            string backupDir = Path.Combine(baseDir, "AutoUpdaterBackup");
                            Directory.CreateDirectory(backupDir);
                            string zipFile = Path.Combine(backupDir, $"{DateTime.Now:yyyyMMdd_HHmmss}.zip");
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
                                        // Zip 項目名稱必須用 '/' 當分隔符號。
                                        string relativePath = Path.GetRelativePath(baseDir, bakPath).Replace('\\', '/');
                                        vm.AddMsg(string.Format(Localization.LocalizationResources.ResourceManager.GetString("AddingFile"), relativePath));

                                        ZipArchiveEntry entry = zipArch.CreateEntry(relativePath);
                                        using (var zipStream = entry.Open())
                                        using (var source = new FileStream(bakPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                        {
                                            await source.CopyToAsync(zipStream);
                                        }
                                        logger.Info($"Done.");
                                    }
                                }
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
                    logger.Error(ex, "Backup");
                    vm.AddMsg(ex.Message, true);
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

                                if (string.IsNullOrEmpty(entry.Name)) continue; // 資料夾項目

                                if (Misc.IsUpdaterOwnFile(entry.Name))
                                {
                                    // 這些檔案正被本程式使用而鎖住，而且函式庫每次更新都會重新放出，所以略過。
                                    logger.Info($"Skip updater's own file : {entry.FullName}");
                                    continue;
                                }

                                string path = Misc.GetSafeExtractPath(baseDir, entry.FullName);
                                if (path == null)
                                {
                                    logger.Error($"Skip entry outside of base directory : {entry.FullName}");
                                    vm.AddMsg($"{entry.FullName} is outside of the application directory.", true);
                                    errorCounts++;
                                    continue;
                                }

                                logger.Info($"Extracting : {path}");
                                vm.AddMsg(string.Format(Localization.LocalizationResources.ResourceManager.GetString("ExtractingFile"), entry.FullName));
                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                                    entry.ExtractToFile(path, true);
                                    logger.Info($"Done.");
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, $"Extract {path}");
                                    vm.AddMsg($"{entry.FullName} {ex.Message}", true);
                                    errorCounts++;
                                }
                            }
                        }
                    }
                    logger.Info($"Extractor done.");

                    TryDeleteFile(updateZIPPackageFilePath);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Extractor");
                    vm.AddMsg(ex.Message, true);
                    errorCounts++;
                    ShowMsgBoxAndExit(ex.Message);
                    return;
                }
                #endregion

                // 在重新啟動主程式之前先寫入更新結果，讓主程式啟動時就能讀到。
                config.ExtractorInfo.IsExtractSuccess = errorCounts == 0;
                config.ExtractorInfo.ExtractTime = DateTime.Now;
                try
                {
                    AutoUpdaterConfig.Save(config);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Save config");
                    vm.AddMsg(ex.Message, true);
                    errorCounts++;
                }

                if (config.ExtractorInfo.ProcessPath != null && File.Exists(config.ExtractorInfo.ProcessPath))
                {
                    logger.Info($"Start {config.ExtractorInfo.ProcessPath}");
                    try
                    {
                        var psi = new ProcessStartInfo(config.ExtractorInfo.ProcessPath)
                        {
                            WorkingDirectory = Path.GetDirectoryName(config.ExtractorInfo.ProcessPath)
                        };
                        using (Process.Start(psi)) { }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Start {config.ExtractorInfo.ProcessPath}");
                        vm.AddMsg(ex.Message, true);
                        errorCounts++;
                    }
                }
                else
                {
                    logger.Warn($"No path to start process.");
                }

                string doneMsg = errorCounts > 0
                    ? $"Done - {string.Format(Localization.LocalizationResources.ResourceManager.GetString("ErrorCountMsg"), errorCounts)}"
                    : "Done";
                vm.AddMsg(doneMsg, errorCounts > 0);
                logger.Info(doneMsg);
                logger.Info($"--------------AutoUpdater.ZipExtractor Close--------------");
                //#if !DEBUG
                if (errorCounts > 0)
                {
                    await Task.Delay(30 * 1000);
                }
                else
                {
                    await Task.Delay(1500);
                }
                ExitApplication(Environment.ExitCode);
                //#endif
            });
        }
    }
}
