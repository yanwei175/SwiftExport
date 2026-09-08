using System;
using System.ComponentModel;
using System.Windows;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>仅桥接 PasswordBox 与命令；关闭时清除密码和页面订阅。</summary>
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel model;
        /// <summary>绑定注入的页面模型，不自行构建服务。</summary>
        public LoginWindow(LoginViewModel model)
        {
            InitializeComponent();
            this.model = model;
            DataContext = model;
            model.Succeeded += Model_Succeeded;
            model.PropertyChanged += Model_Changed;
            Loaded += (sender, args) => UserNameBox.Focus();
            Closed += Window_Closed;
        }
        private void Model_Succeeded(object sender, EventArgs args) { Close(); }
        private void Model_Changed(object sender, PropertyChangedEventArgs args) { LoginButton.IsEnabled = !model.IsBusy; }
        // 在事件边界观察异步异常，避免泄漏到 Excel 消息循环。
        private async void Login_Click(object sender, RoutedEventArgs args)
        {
            var password = PasswordInput.Password;
            PasswordInput.Clear();
            try { if (model.LoginCommand.CanExecute(password)) await model.LoginCommand.ExecuteAsync(password); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { new Diagnostics.AddInLog().Write("LoginEventFailure", ex); }
            finally { password = null; }
        }
        private void Cancel_Click(object sender, RoutedEventArgs args) { Close(); }
        private void Window_Closed(object sender, EventArgs args)
        {
            PasswordInput.Clear();
            model.Succeeded -= Model_Succeeded;
            model.PropertyChanged -= Model_Changed;
            model.Dispose();
            DataContext = null;
        }
    }
}
