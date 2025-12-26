using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.FrmUtility.Validation
{
    public static class LabelExtensions
    {

        public static ToolStripLabel GoodNews(this ToolStripLabel lbl, string message)
        {
            lbl.Visible = true;
            lbl.Text = message;
            lbl.ForeColor = Color.Green;
            return lbl;
        }

        public static ToolStripLabel BadNews(this ToolStripLabel lbl, string message)
        {
            lbl.Visible = true;
            lbl.Text = message;
            lbl.ForeColor = Color.Red;
            return lbl;
        }
        public static ToolStripLabel GeneralNews(this ToolStripLabel lbl, string message)
        {
            lbl.Visible = true;
            lbl.Text = message;
            lbl.ForeColor = Color.Blue;
            return lbl;
        }
        public static Label GoodNews(this Label lbl, string message)
        {
            lbl.Visible = true;
            lbl.Text = message;
            lbl.ForeColor = Color.Green;
            return lbl;
        }

        public static Label BadNews(this Label lbl, string message)
        {

            lbl.Visible = true;
            lbl.Text = message;
            lbl.ForeColor = Color.Red;
            return lbl;
        }
        public static Label GeneralNews(this Label lbl, string message)
        {
            lbl.Visible = true;
            lbl.Text = message;
            lbl.ForeColor = Color.Blue;
            return lbl;
        }




    }
}
