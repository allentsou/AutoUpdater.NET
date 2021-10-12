using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
            //Set Icon 1
            //AutoUpdater.AutoUpdater.CheckUpdate(this, "http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", false, "ETF造市避險",
            //    appIcon: TestWPF.Properties.Resources.Icon_Main);
            
            
            //Set Icon 2
            AutoUpdater.AutoUpdater.CheckUpdate(this, "http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", false, "ETF造市避險",
                appIconUri: "pack://application:,,,/Resources/Icon_Main.ico");
            
            
            //No Icom
            //AutoUpdater.AutoUpdater.CheckUpdate(this, "http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", false, "ETF造市避險");
        }

        private void btnStartForceUpdate_Click(object sender, RoutedEventArgs e)
        {
            AutoUpdater.AutoUpdater.CheckUpdate(this, "http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", true, "TestAPP123");
        }

        private void btnStartUpdateNoChgLog_Click(object sender, RoutedEventArgs e)
        {
            AutoUpdater.AutoUpdater.CheckUpdate(this, "http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfigNoChgLog.json", false, "TestAPP123");
        }


    }
}
