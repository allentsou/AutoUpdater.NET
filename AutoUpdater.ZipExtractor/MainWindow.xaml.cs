using System;
using System.Collections.Specialized;
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
            this.Loaded += MainWindow_Loaded;
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
