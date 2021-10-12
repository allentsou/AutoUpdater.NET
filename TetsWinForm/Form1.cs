using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
            AutoUpdater.AutoUpdater.CheckUpdate(null, "http://172.24.26.38/Deploy/update/AutoUpdateCoreTest/AutoUpdaterConfig.json", false, "ETF造市避險",
                null);
        }
    }
}
