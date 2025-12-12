using SwiftExport.AppLayer.DTOs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork
{
    public partial class NaviUserControl : UserControl
    {
        private ViewUserRoleDTo _userRoleDTo;
        public NaviUserControl(ViewUserRoleDTo userRoleDTo)
        {
            _userRoleDTo=userRoleDTo;
            InitializeComponent();
            LoadNavi();
        }

        private void LoadNavi()
        {
            if (_userRoleDTo == null ) { return;}

            if (_userRoleDTo.RoleID ==1)
            { 
                //加载全部菜单


            }else if (_userRoleDTo.RoleID == 2)
            {
                //加载部分菜单

            };

        }

        private void MainBtn_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            string perm = btn.Tag.ToString();

            UserControl uc = null;

            if (perm == "UserEdit")
           
            {
                //uc = UserEditControl.Instance;
            }
            else if (perm == "UserView")
                //uc = UserViewControl.Instance;
                // 其他权限控件...

                if (uc != null)
                {
                    flowLayoutPanel1.Controls.Clear();
                    flowLayoutPanel1.Controls.Add(uc);
                    uc.Dock = DockStyle.Fill;
                }
        }






    }
}
