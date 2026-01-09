using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.UI.Framework.State;
using System;
using System.Windows.Forms;

namespace SwiftExport.UI.Framework
{
    public partial class LoginFrm : Form
    {
        private readonly IViewUserRoleService _viewUserService;
        private readonly IMapper _mapper;
        public LoginFrm(IViewUserRoleService viewUserRoleService, IMapper mapper)
        {
            InitializeComponent();
            _viewUserService = viewUserRoleService;
            _mapper = mapper;
            this.Shown += (s, e) => this.txt_UserName.Focus();
        }

        private async void SignIn_Click(object sender, EventArgs e)
        {
            string u = this.txt_UserName.Text.Trim();
            string p = this.txt_passwd.Text.Trim();

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            {
                MessageBox.Show("用户名或密码不能为空");
                return;
            }

            try
            {
                var result = await _viewUserService.GetUserRole(u, p);
                if (result.Success)
                {
                    // 1. 设置全局用户状态 (注意：ActiveUserClass 建议放到 Core 或 Infrastructure 层)
                    SessionContext.SetUser(_mapper.Map<ViewUserRoleDTo>(result.Data));

                    // 2. 核心：不跳转，只告诉外界“我成功了”
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("登录失败：" + result.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("错误：" + ex.Message);
            }
        }


        private void SignUp_Click(object sender, EventArgs e)
        {
            //this.Close();
        }

    }
}