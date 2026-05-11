using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Http;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SystemViews.ViewModels
{
    /// <summary>
    /// 登录页面 ViewModel。
    /// 登录成功后将 Token 存入 <see cref="AuthTokenStore"/>，供后续所有 API 请求使用。
    /// </summary>
    public class LoginViewModel : ViewModelBaseEx
    {
        private readonly IAuthApi _authApi;
        private readonly IDialogService _dialog;

        // ============= 属性 =============

        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set
            {
                if (SetProperty(ref _userName, value))
                    LoginCommand.RaiseCanExecuteChanged();
            }
        }

        // 密码由 View 的 PasswordBox 直接传入，ViewModel 不保存明文
        // 通过 LoginCommand 参数传递（CommandParameter binding 见 XAML）

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set => SetProperty(ref _isError, value);
        }

        /// <summary>登录成功后触发，通知宿主关闭窗口或跳转页面</summary>
        public event Action LoginSucceeded;

        // ============= 命令 =============

        /// <summary>
        /// 登录命令，CommandParameter 传入 PasswordBox.Password 明文字符串。
        /// </summary>
        public RelayCommand<string> LoginCommand { get; }

        public LoginViewModel(IAuthApi authApi, IDialogService dialog)
        {
            _authApi = authApi;
            _dialog = dialog;

            LoginCommand = new RelayCommand<string>(
                async pwd => await LoginAsync(pwd),
                pwd => !string.IsNullOrWhiteSpace(UserName) && !IsBusy);
        }

        // ============= 登录逻辑 =============

        private async Task LoginAsync(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                StatusMessage = "请输入密码";
                IsError = true;
                return;
            }

            using (BusyScope())
            {
                StatusMessage = "登录中...";
                IsError = false;
                LoginCommand.RaiseCanExecuteChanged();

                try
                {
                    // 调用后端 /api/auth/login，返回 access_token
                    var tokenDto = await _authApi.LoginAsync(new LoginRequestDto
                    {
                        UserName = UserName.Trim(),
                        Password = password
                    });

                    if (tokenDto == null || string.IsNullOrEmpty(tokenDto.access_token))
                    {
                        StatusMessage = "登录失败：服务器未返回 Token";
                        IsError = true;
                        return;
                    }

                    // 直接从 JWT Payload 解析用户名，无需再请求 /me
                    // 避免 /me 权限问题导致显示错误用户名
                    var parsedUserName = ParseUserNameFromJwt(tokenDto.access_token);
                    var displayName = !string.IsNullOrEmpty(parsedUserName)
                        ? parsedUserName
                        : UserName.Trim();

                    AuthTokenStore.Set(tokenDto.access_token, displayName);

                    // 调用 /me 补充 UserId、NickName 和角色列表（失败不影响登录）
                    try
                    {
                        var me = await _authApi.GetMeAsync();
                        if (me != null)
                        {
                            AuthTokenStore.Set(tokenDto.access_token, me.UserName, me.UserId, me.NickName);
                            // 写入角色列表，供全局权限判断使用（如 AuthTokenStore.IsAdmin）
                            AuthTokenStore.SetRoles(me.Roles ?? new System.Collections.Generic.List<string>());
                        }
                    }
                    catch { /* /me 失败不阻断登录，使用 JWT 解析的用户名即可 */ }

                    StatusMessage = $"欢迎，{AuthTokenStore.CurrentNickName ?? AuthTokenStore.CurrentUserName}！";
                    IsError = false;

                    // 通知宿主登录成功
                    LoginSucceeded?.Invoke();
                }
                catch (Refit.ApiException apiEx) when (apiEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    StatusMessage = "用户名或密码错误";
                    IsError = true;
                }
                catch (Exception ex)
                {
                    StatusMessage = "登录异常：" + ex.Message;
                    IsError = true;
                }
                finally
                {
                    LoginCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 从 JWT Token 字符串中解析用户名（unique_name Claim）。
        /// 只做 Base64 Payload 解码，不验签，仅用于前端展示。
        /// </summary>
        private static string ParseUserNameFromJwt(string token)
        {
            try
            {
                // JWT 格式：header.payload.signature
                var parts = token.Split('.');
                if (parts.Length != 3) return string.Empty;

                // Base64Url → Base64 补全 padding
                var payload = parts[1];
                payload = payload.Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));

                // 用正则从 JSON 中提取 unique_name 或 name 字段值
                var match = Regex.Match(json, "\"unique_name\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;

                match = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
