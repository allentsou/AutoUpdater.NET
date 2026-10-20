using Microsoft.Web.WebView2.Core;
using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Interop;

namespace AutoUpdater
{
    /// <summary>
    /// UpdateForm.xaml 的互動邏輯
    /// </summary>
    public partial class UpdateForm : Window
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private Uri changeLogNavigateUri;
        private Uri changeLogDisplayUri;

        public UpdateForm()
        {
            InitializeComponent();
            this.Loaded += Window_Loaded;
            this.Closed += (s, e) => chgLogBrowser.Dispose();
        }

        /// <summary>
        /// 在 WebView2 控制項顯示更新說明；未安裝 WebView2 Runtime 時改顯示可點擊的連結。
        /// </summary>
        /// <param name="navigateUri">要載入的網址（含避開快取的參數）。</param>
        /// <param name="displayUri">備援連結顯示給使用者的網址。</param>
        public void SetChangeLog(Uri navigateUri, Uri displayUri)
        {
            changeLogNavigateUri = navigateUri;
            changeLogDisplayUri = displayUri;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);

            if (changeLogNavigateUri != null)
            {
                await LoadChangeLogAsync();
            }
        }

        private async System.Threading.Tasks.Task LoadChangeLogAsync()
        {
            try
            {
                // 預設的使用者資料資料夾在 exe 旁邊，常常沒有寫入權限（例如 Program Files）。
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoUpdater", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await chgLogBrowser.EnsureCoreWebView2Async(env);

                // 頁面及其 CSS/JS/圖片一律從伺服器取得，不使用快取。
                await chgLogBrowser.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                await chgLogBrowser.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");

                chgLogBrowser.CoreWebView2.Navigate(changeLogNavigateUri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                // 通常是 WebView2RuntimeNotFoundException：改顯示用預設瀏覽器開啟的連結。
                logger.Warn(ex, "WebView2 is not available, show link instead.");
                ShowChangeLogFallback();
            }
        }

        private void ShowChangeLogFallback()
        {
            chgLogBrowser.Visibility = Visibility.Collapsed;
            chgLogLink.NavigateUri = changeLogDisplayUri;
            chgLogLink.Inlines.Clear();
            chgLogLink.Inlines.Add(changeLogDisplayUri.AbsoluteUri);
            chgLogFallback.Visibility = Visibility.Visible;
        }

        private void chgLogLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })) { }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Open change log link");
            }
            e.Handled = true;
        }
    }
}
