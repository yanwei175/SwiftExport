using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.FrmUtility.UIStyle
{
    public static class UITheme
    {
        public static void ApplyTheme(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is DataGridView dgv) dgv.ApplyModernStyle();
                if (c is Button btn) ApplyButtonStyle(btn);
                if (c is TextBox txt) ApplyTextStyle(txt);

                // 递归处理容器里的控件
                if (c.HasChildren) ApplyTheme(c);
            }
        }

        private static void ApplyButtonStyle(Button btn)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.BackColor = Color.White;
            btn.Cursor = Cursors.Hand;
        }
        private static void ApplyTextStyle(TextBox txt)
        {
            txt.BorderStyle = BorderStyle.FixedSingle;
            txt.BackColor = Color.White;
            txt.Cursor = Cursors.Hand;
        }
    }
}
