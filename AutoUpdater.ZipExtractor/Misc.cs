using System;
using System.Collections.Generic;
using System.Text;

namespace AutoUpdater.ZipExtractor
{
    public static class Misc
    {
        public static string GaeBaseDirectory()
        {
            string path = "";
#if DEBUG
            path = AppDomain.CurrentDomain.BaseDirectory;
#else
            //發行為Single File執行檔要這樣抓才能得到實際路徑
            path = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
#endif
            return path;
        }
    }
}
