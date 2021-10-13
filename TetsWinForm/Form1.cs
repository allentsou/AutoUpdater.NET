using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Interop;

namespace TetsWinForm
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //var window = new WindowInteropHelper(this);

            AutoUpdater.AutoUpdater.CheckUpdate("http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", this.Handle, false, "ETF造市避險",
                null);
        }
    }
}
