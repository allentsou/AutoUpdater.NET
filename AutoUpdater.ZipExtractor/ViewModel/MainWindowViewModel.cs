using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

namespace AutoUpdater.ZipExtractor.ViewModel
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private string windowTitle;
        private string progressText;
        private int progressBarValue;
        private string currentStep;

        public enum enumStep { WaitingAppClose, DownloadPackage, Backup, Extract }

        public MainWindowViewModel()
        {
            bool isInDesignModeProperty = false;
            if ((bool)(DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue))
            {
                isInDesignModeProperty = true;
            }

            if (isInDesignModeProperty)
            {
                int fileCount = 20;
                for (int i = 0; i < fileCount; i++)
                {
                    FileList.Add(new FileItemData() { Msg = $"File {i}", IsError = i % 5 == 0 });
                }
                ProgressText = "1/999";
                ProgressBarValue = 100;
                UpdateStepText(enumStep.WaitingAppClose);
            }
            else
            {
                //AutoUpdaterConfig.Save(new AutoUpdaterConfig());
            }
        }


        public string WindowTitle
        {
            get => windowTitle;
            set
            {
                windowTitle = value;
                RaiseNotifyPropertyChanged();
            }
        }

        public string ProgressText
        {
            get => progressText;
            set
            {
                progressText = value;
                RaiseNotifyPropertyChanged();
            }
        }

        /// <summary>
        /// 0 to 100
        /// </summary>
        public int ProgressBarValue
        {
            get => progressBarValue;
            set
            {
                progressBarValue = value;
                RaiseNotifyPropertyChanged();
            }
        }
        public string CurrentStepText
        {
            get => currentStep;
            set
            {
                currentStep = value;
                RaiseNotifyPropertyChanged();
            }
        }

        public void UpdateStepText(enumStep step)
        {
            switch (step)
            {
                case enumStep.WaitingAppClose:
                    CurrentStepText = Localization.LocalizationResources.ResourceManager.GetString("LabelWaitClose");
                    break;
                case enumStep.DownloadPackage:
                    CurrentStepText = Localization.LocalizationResources.ResourceManager.GetString("LabelDownloading");
                    break;
                case enumStep.Backup:
                    CurrentStepText = Localization.LocalizationResources.ResourceManager.GetString("LabelBackup");
                    break;
                case enumStep.Extract:
                    CurrentStepText = Localization.LocalizationResources.ResourceManager.GetString("LabelDecompressing");
                    break;
            }
        }

        public ObservableCollection<FileItemData> FileList { get; set; } = new ObservableCollection<FileItemData>();

        public void AddMsg(string msg, bool isError = false)
        {
            Action action = () =>
            {
                FileList.Add(new FileItemData() { Msg = $"{DateTime.Now:HH:mm:ss} {msg}", IsError = isError });
            };

            if (Application.Current.Dispatcher.Thread == Thread.CurrentThread)
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action, null);
            }
        }

        #region PropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaiseNotifyPropertyChanged([CallerMemberName] string propName = "")
        {
            Action action = () =>
            {
                //https://docs.microsoft.com/zh-tw/archive/msdn-magazine/2016/july/data-binding-a-better-way-to-implement-data-binding-in-net
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(propName));
                }
            };

            if (Application.Current.Dispatcher.Thread == Thread.CurrentThread)
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action, null);
            }
        }
        #endregion

    }

    public class FileItemData
    {
        public string Msg { get; set; }
        public bool IsError { get; set; }
    }
}
