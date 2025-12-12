using Microsoft.Extensions.DependencyInjection;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.UiWinForm.静态类;

namespace SwiftExport.UiWinForm
{
    public partial class LoginFrm : Form
    {

        private readonly IViewUserRoleService _vwUserRoleService;
        public LoginFrm(IViewUserRoleService vwUserRoleService)
        {
            InitializeComponent();
            _vwUserRoleService = vwUserRoleService;
        }
        //登录按钮点击事件
        private async void SignIn_Click(object sender, EventArgs e)
        {
            string usName = this.txt_UserName.Text;
            string pwd = this.txt_passwd.Text;
            if (string.IsNullOrEmpty(usName) || string.IsNullOrEmpty(pwd))
            {
                MessageBox.Show("用户名或密码不能为空");
                return;
            }

            var vwUserRole = await _vwUserRoleService.GetUserRole(usName, pwd);
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

        //注册按钮点击事件
        private void SignUp_Click(object sender, EventArgs e)
        { 

        }

        //private void button1_Click(object sender, EventArgs e)
        //{
        //    Form frm1 =new MainFrm();
        //    UserControl ctrl = new SwiftExport.UiKongJianFrameWork.ProductUserControl();
        //    frm1.Controls.Add(ctrl);

        //    ctrl.Dock = DockStyle.Fill;
        //    frm1.Show();
        //}



    }
}
