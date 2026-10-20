using System;
using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AutoUpdater.ZipExtractor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        public MainWindow()
        {
            InitializeComponent();
            lblVersion.Text = GetVersionText();
            this.Loaded += MainWindow_Loaded;
        }

        /// <summary>
        /// 專案的 &lt;Version&gt;（例如 1.2026.0723.0）。資訊版本會保留數字版本去掉的前導 0；
        /// SDK 自動加上的「+commit」後綴會被移除。
        /// </summary>
        private static string GetVersionText()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                return assembly.GetName().Version?.ToString() ?? "";
            }
            int plus = version.IndexOf('+');
            return plus >= 0 ? version.Substring(0, plus) : version;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
        }

        private INotifyCollectionChanged incc;
        private void listFiles_Loaded(object sender, RoutedEventArgs e)
        {
            //https://stackoverflow.com/questions/2006729/how-can-i-have-a-listbox-auto-scroll-when-a-new-item-is-added/2007072
            if (listFiles.ItemsSource is INotifyCollectionChanged)
            {
                incc = listFiles.ItemsSource as INotifyCollectionChanged;
                incc.CollectionChanged += Incc_CollectionChanged;

                if (listFiles.Items != null && listFiles.Items.Count > 0)
                {
                    listFiles.ScrollIntoView(listFiles.Items[listFiles.Items.Count - 1]);
                    listFiles.SelectedItem = listFiles.Items[listFiles.Items.Count - 1];
                }
            }
        }

        private void Incc_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null || e.NewItems.Count == 0) return;

            listFiles.ScrollIntoView(e.NewItems[0]);
            listFiles.SelectedItem = e.NewItems[0];
        }

        private void listFiles_Unloaded(object sender, RoutedEventArgs e)
        {
            if (incc != null)
            {
                incc.CollectionChanged -= Incc_CollectionChanged;
                incc = null;
            }
        }
    }
}
