using SwiftExport.View.FW.SystemViews.ViewModels;
using SwiftExport.View.FW.DI;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExport.View.FW.SystemViews.Views
{
    /// <summary>
    /// 登录页面后台代码。
    /// PasswordBox 不支持 MVVM 双向绑定，在登录按钮点击时由 code-behind 将密码传给 ViewModel。
    /// ViewModel 登录成功后触发 LoginSucceeded 事件，宿主（Window/Frame）响应跳转。
    /// </summary>
    public partial class LoginView : Window
    {
        // 中文注释：密码显示状态。true=显示明文 TextBox，false=显示 PasswordBox。
        private bool _isPasswordVisible;

        public LoginView()
        {
            InitializeComponent();
        }

        public LoginView(LoginViewModel vm) : this()
        {
            DataContext = vm;

            // 中文注释：初始化本机记住密码信息，并设置初始密码可见性状态。
            InitializeRememberPassword();
            SetPasswordVisibility(false);

            // 中文注释：明文输入框改动时，同步回 PasswordBox（仅在明文可见状态下）。
            PwdTextBox.TextChanged += (s, e) =>
            {
                if (_isPasswordVisible && PwdBox.Password != PwdTextBox.Text)
                {
                    PwdBox.Password = PwdTextBox.Text;
                }
            };

            // 中文注释：密文输入框改动时，同步回明文输入框（仅在密文状态下）。
            PwdBox.PasswordChanged += (s, e) =>
            {
                if (!_isPasswordVisible && PwdTextBox.Text != PwdBox.Password)
                {
                    PwdTextBox.Text = PwdBox.Password;
                }
            };

            // 中文注释：点击按钮切换密码显示状态。
            TogglePwdVisibleBtn.Click += (s, e) =>
            {
                SetPasswordVisibility(!_isPasswordVisible);
            };

            // 绑定登录按钮点击：将 PasswordBox 密码明文传给 LoginCommand
            LoginBtn.Click += (s, e) =>
            {
                if (vm.LoginCommand.CanExecute(null))
                    vm.LoginCommand.Execute(PwdBox.Password);
            };

            // 绑定忘记密码按钮：弹出重置密码窗口
            ResetPasswdBtn.Click += (s, e) =>
            {
                var resetView = IocProvider.GetService<ResetPasswdView>();
                resetView.Owner = this;
                resetView.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                resetView.ShowDialog();
            };

            // 登录成功后清空密码框（安全起见）
            vm.LoginSucceeded += () =>
            {
                // 中文注释：登录成功后按“记住密码”选项决定是否本机持久化账号密码。
                if (RememberPwdCheckBox.IsChecked == true)
                {
                    LoginCredentialStore.Save(vm.UserName?.Trim(), PwdBox.Password);
                }
                else
                {
                    LoginCredentialStore.Clear();
                }

                PwdBox.Clear();
                PwdTextBox.Clear();
            };
        }

        /// <summary>
        /// 初始化“记住密码”状态：读取本机缓存并回填用户名和密码。
        /// </summary>
        private void InitializeRememberPassword()
        {
            var credential = LoginCredentialStore.Load();
            if (credential == null)
            {
                RememberPwdCheckBox.IsChecked = false;
                return;
            }

            if (DataContext is LoginViewModel vm)
            {
                vm.UserName = credential.UserName;
            }

            PwdBox.Password = credential.Password;
            PwdTextBox.Text = credential.Password;
            RememberPwdCheckBox.IsChecked = true;
        }

        /// <summary>
        /// 切换密码显示状态，并保持两个输入框值一致。
        /// </summary>
        private void SetPasswordVisibility(bool visible)
        {
            _isPasswordVisible = visible;

            if (visible)
            {
                // 中文注释：进入明文模式，先同步文本，再显示 TextBox。
                PwdTextBox.Text = PwdBox.Password;
                PwdTextBox.Visibility = Visibility.Visible;
                PwdBox.Visibility = Visibility.Collapsed;
                TogglePwdVisibleBtn.Content = "隐藏";
                PwdTextBox.Focus();
                PwdTextBox.SelectionStart = PwdTextBox.Text?.Length ?? 0;
            }
            else
            {
                // 中文注释：回到密文模式，先同步密码，再显示 PasswordBox。
                if (PwdBox.Password != PwdTextBox.Text)
                {
                    PwdBox.Password = PwdTextBox.Text ?? string.Empty;
                }

                PwdTextBox.Visibility = Visibility.Collapsed;
                PwdBox.Visibility = Visibility.Visible;
                TogglePwdVisibleBtn.Content = "显示";
                PwdBox.Focus();
            }
        }

        private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove(); // 允许用户点击背景拖动窗口
            }
        }

        /// <summary>
        /// 本地登录凭据存储（DPAPI 当前用户范围）。
        /// 说明：仅用于“记住密码”体验，不用于服务端鉴权。
        /// </summary>
        private static class LoginCredentialStore
        {
            // 中文注释：本地缓存文件目录与文件名（当前 Windows 用户 AppData 目录）。
            private static readonly string FolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SwiftExport",
                "AuthCache");

            private static readonly string FilePath = Path.Combine(FolderPath, "login.cache");

            /// <summary>
            /// 保存用户名与密码（先拼接，再 DPAPI 加密后写入本地文件）。
            /// </summary>
            public static void Save(string userName, string password)
            {
                if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
                    return;

                var raw = userName + "\n" + password;
                var rawBytes = Encoding.UTF8.GetBytes(raw);
                var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                    rawBytes,
                    null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);

                Directory.CreateDirectory(FolderPath);
                File.WriteAllBytes(FilePath, encrypted);
            }

            /// <summary>
            /// 读取并解密本机登录缓存。读取失败或数据异常时返回 null。
            /// </summary>
            public static CredentialData Load()
            {
                try
                {
                    if (!File.Exists(FilePath)) return null;

                    var encrypted = File.ReadAllBytes(FilePath);
                    var rawBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                        encrypted,
                        null,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    var raw = Encoding.UTF8.GetString(rawBytes);
                    var arr = raw.Split(new[] { '\n' }, 2);
                    if (arr.Length != 2) return null;

                    return new CredentialData
                    {
                        UserName = arr[0],
                        Password = arr[1]
                    };
                }
                catch
                {
                    return null;
                }
            }

            /// <summary>
            /// 清除本机缓存凭据。
            /// </summary>
            public static void Clear()
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        File.Delete(FilePath);
                    }
                }
                catch
                {
                    // 中文注释：本地缓存删除失败不影响主流程，静默处理。
                }
            }

            /// <summary>
            /// 凭据数据模型。
            /// </summary>
            public class CredentialData
            {
                public string UserName { get; set; }
                public string Password { get; set; }
            }

        }
    }
}
