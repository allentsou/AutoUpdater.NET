using System.Windows;
using System.Windows.Interop;

namespace TestWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();

            // 指定更新視窗與 extractor 的語系（只影響 AutoUpdater，不會改變本程式的語系）；
            // 不設定時依 Windows 顯示語言，目前內建 zh-TW 與英文。
            //AutoUpdater.AutoUpdater.Culture = new System.Globalization.CultureInfo("zh-TW");
            //AutoUpdater.AutoUpdater.Culture = new System.Globalization.CultureInfo("en-US");
        }

        private void btnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            //TestWPF.Properties.Resources.Icon_Main
            AutoUpdater.AutoUpdaterConfig config = new AutoUpdater.AutoUpdaterConfig();
            AutoUpdater.AutoUpdaterConfig.Save(config);
        }

        private void btnStartUpdate_Click(object sender, RoutedEventArgs e)
        {
            var window = new WindowInteropHelper(this);
            //Set Icon 1
            //AutoUpdater.AutoUpdater.CheckUpdate("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", window.Handle, false, "TestApp1",
            //    appIcon: TestWPF.Properties.Resources.Icon_Main);


            //Set Icon 2
            //.ico的建置動作要設為 "資源" 才抓得到
            AutoUpdater.AutoUpdater.CheckUpdate("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", window.Handle, false, "TestApp2",
                appIconUri: "pack://application:,,,/Resources/MainIcon.ico");


            //No Icom
            //AutoUpdater.AutoUpdater.CheckUpdate("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", window.Handle, false, "TestApp3");
        }

        private async void btnStartUpdateAsync_Click(object sender, RoutedEventArgs e)
        {
            // 下載設定檔期間 UI 仍可操作。
            var window = new WindowInteropHelper(this);
            btnStartUpdateAsync.IsEnabled = false;
            try
            {
                await AutoUpdater.AutoUpdater.CheckUpdateAsync("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", window.Handle, false, "TestApp2",
                    appIconUri: "pack://application:,,,/Resources/Icon_Main.ico");
            }
            finally
            {
                btnStartUpdateAsync.IsEnabled = true;
            }
        }

        private void btnStartForceUpdate_Click(object sender, RoutedEventArgs e)
        {
            var window = new WindowInteropHelper(this);
            AutoUpdater.AutoUpdater.CheckUpdate("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", window.Handle, true, "TestApp4");
        }

        private void btnStartUpdateNoChgLog_Click(object sender, RoutedEventArgs e)
        {
            var window = new WindowInteropHelper(this);
            AutoUpdater.AutoUpdater.CheckUpdate("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfigNoChgLog.json", window.Handle, false, "TestApp5");
        }

        private void btnOpenChgLog_Click(object sender, RoutedEventArgs e)
        {
            AutoUpdater.AutoUpdater.OpenChangeLogInBrowser("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json");
        }
    }
}
