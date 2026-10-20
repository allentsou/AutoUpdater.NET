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
            AutoUpdater.AutoUpdater.CheckUpdate("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", window.Handle, false, "TestApp2",
                appIconUri: "pack://application:,,,/Resources/Icon_Main.ico");


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
