using Microsoft.Extensions.DependencyInjection;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.AppLayer.Services;
using SwiftExport.Core.Interfaces;
using SwiftExport.UiWinForm.静态类;
using SwiftExportAddIn;
using SwiftExportVSTO.静态类;
using System;
using System.Windows.Forms;

namespace SwiftExportVSTO
{
    public partial class LoginFrm : Form
    {

        public LoginFrm()
        {
            InitializeComponent();
            this.Shown += (s, e) => this.txt_UserName.Focus();
        }

        private async void SignIn_Click(object sender, EventArgs e)
        {
            string usName = this.txt_UserName.Text;
            string pwd = this.txt_passwd.Text;

            if (string.IsNullOrEmpty(usName) || string.IsNullOrEmpty(pwd))
            {
                MessageBox.Show("用户名或密码不能为空");
                return;
            }

            var factory = Globals.ThisAddIn.ServerFactory;
            using (var scope = factory.CreateScope())
            {
                var _userService = scope.ServiceProvider.GetRequiredService<IViewUserRoleService>();

                var vwUserRole = await _userService.GetUserRole(usName, pwd);
                if (vwUserRole != null)
                {
                    // 登录成功，设置 DialogResult
                    ActiveUserClass.ActiveUser = vwUserRole;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("用户名或密码错误");
                }

            }
        }
      

        private void SignUp_Click(object sender, EventArgs e)
        {
            this.Close();
        }

    }
}
