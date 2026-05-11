using SwiftExport.View.FW.SystemViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.SystemViews.Views
{
    /// <summary>
    /// 注册页面后台代码。
    /// PasswordBox 不支持 MVVM 双向绑定，在点击注册按钮时由 code-behind
    /// 将 Password + ConfirmPassword 拼接成 "pwd|confirmPwd" 字符串传给 RegisterCommand。
    /// 非 admin 账号登录时：表单区置灰、显示警告横幅。
    /// </summary>
    public partial class RegisterView : UserControl
    {
        public RegisterView()
        {
            InitializeComponent();
        }

        public RegisterView(RegisterViewModel vm) : this()
        {
            DataContext = vm;

            // 根据是否为管理员控制表单可用性和警告可见性
            RefreshAdminState(vm);

            // 注册按钮点击：将两个 PasswordBox 的值合并后调用命令
            RegisterBtn.Click += (s, e) =>
            {
                if (!vm.IsAdmin)
                {
                    AdminWarningBorder.Visibility = Visibility.Visible;
                    return;
                }

                if (vm.RegisterCommand.CanExecute(null))
                {
                    // 格式："{password}|{confirmPassword}"
                    var pwdPair = PwdBox.Password + "|" + ConfirmPwdBox.Password;
                    vm.RegisterCommand.Execute(pwdPair);

                    // 注册成功后清空密码框（ViewModel 会清空其他字段）
                    if (!vm.IsError)
                    {
                        PwdBox.Clear();
                        ConfirmPwdBox.Clear();
                    }
                }
            };
        }

        /// <summary>
        /// 根据当前登录用户是否为 admin，控制表单区域和警告横幅的状态。
        /// </summary>
        private void RefreshAdminState(RegisterViewModel vm)
        {
            if (vm.IsAdmin)
            {
                // 管理员：隐藏警告，表单可用
                AdminWarningBorder.Visibility = Visibility.Collapsed;
                FormPanel.IsEnabled = true;
            }
            else
            {
                // 非管理员：显示警告，表单禁用
                AdminWarningBorder.Visibility = Visibility.Visible;
                FormPanel.IsEnabled = false;
            }
        }
    }
}

