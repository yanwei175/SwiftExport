using SwiftExport.View.FW.SystemViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.SystemViews.Views
{
    /// <summary>
    /// 登录页面后台代码。
    /// PasswordBox 不支持 MVVM 双向绑定，在登录按钮点击时由 code-behind 将密码传给 ViewModel。
    /// ViewModel 登录成功后触发 LoginSucceeded 事件，宿主（Window/Frame）响应跳转。
    /// </summary>
    public partial class LoginView : UserControl
    {
        public LoginView()
        {
            InitializeComponent();
        }

        public LoginView(LoginViewModel vm) : this()
        {
            DataContext = vm;

            // 绑定登录按钮点击：将 PasswordBox 密码明文传给 LoginCommand
            LoginBtn.Click += (s, e) =>
            {
                if (vm.LoginCommand.CanExecute(null))
                    vm.LoginCommand.Execute(PwdBox.Password);
            };

            // 登录成功后清空密码框（安全起见）
            vm.LoginSucceeded += () =>
            {
                PwdBox.Clear();
            };
        }
    }
}
