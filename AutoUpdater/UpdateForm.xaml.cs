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
        /// Shows the change log in the WebView2 control, or a clickable link when the WebView2 Runtime is not installed.
        /// </summary>
        /// <param name="navigateUri">Address to load (contains a cache-busting parameter).</param>
        /// <param name="displayUri">Address shown to the user in the fallback link.</param>
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
                // The default user data folder is next to the exe, which is often not writable (e.g. Program Files).
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoUpdater", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await chgLogBrowser.EnsureCoreWebView2Async(env);

                // Always fetch the page and its CSS/JS/images from the server instead of the cache.
                await chgLogBrowser.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                await chgLogBrowser.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");

                chgLogBrowser.CoreWebView2.Navigate(changeLogNavigateUri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                // Typically WebView2RuntimeNotFoundException: fall back to a link that opens the default browser.
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
