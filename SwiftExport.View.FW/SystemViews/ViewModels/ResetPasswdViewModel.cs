using SwiftExport.View.FW.Mvvm;
using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using System;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SystemViews.ViewModels
{
    /// <summary>
    /// 重置密码页面 ViewModel。
    /// 规则：校验码使用“管理员密码”进行后端校验，通过后重置目标用户密码。
    /// </summary>
    public class ResetPasswdViewModel : ViewModelBaseEx
    {
        private readonly IAuthApi _authApi;

        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set
            {
                if (SetProperty(ref _userName, value))
                    ConfirmCommand.RaiseCanExecuteChanged();
            }
        }

        private string _newPassword = string.Empty;
        public string NewPassword
        {
            get => _newPassword;
            set
            {
                if (SetProperty(ref _newPassword, value))
                    ConfirmCommand.RaiseCanExecuteChanged();
            }
        }

        private string _verificationCode = string.Empty;
        public string VerificationCode
        {
            get => _verificationCode;
            set
            {
                if (SetProperty(ref _verificationCode, value))
                    ConfirmCommand.RaiseCanExecuteChanged();
            }
        }

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

        /// <summary>
        /// 重置成功后触发，通知 View 关闭窗口。
        /// </summary>
        public event Action ResetSucceeded;

        /// <summary>
        /// 确认重置密码命令。
        /// </summary>
        public RelayCommand ConfirmCommand { get; }

        /// <summary>
        /// 设计器/预览器使用的无参构造。
        /// 仅用于 XAML 设计时数据上下文，运行时请通过 DI 调用有参构造。
        /// </summary>
        public ResetPasswdViewModel() : this(null)
        {
        }

        public ResetPasswdViewModel(IAuthApi authApi)
        {
            _authApi = authApi;

            ConfirmCommand = new RelayCommand(
                async () => await ResetAsync(),
                () => !string.IsNullOrWhiteSpace(UserName)
                      && !string.IsNullOrWhiteSpace(NewPassword)
                      && !string.IsNullOrWhiteSpace(VerificationCode)
                      && !IsBusy);
        }

        /// <summary>
        /// 执行重置密码请求。
        /// </summary>
        private async Task ResetAsync()
        {
            // 中文注释：设计时或 DI 异常导致 API 未注入时，给出明确提示，避免空引用异常。
            if (_authApi == null)
            {
                StatusMessage = "重置服务未初始化，请检查依赖注入配置";
                IsError = true;
                return;
            }

            if (NewPassword.Trim().Length < 6)
            {
                StatusMessage = "新密码长度不能少于6位";
                IsError = true;
                return;
            }

            using (BusyScope())
            {
                StatusMessage = "重置中...";
                IsError = false;
                ConfirmCommand.RaiseCanExecuteChanged();

                try
                {
                    var result = await _authApi.ResetPasswordAsync(new ResetPasswordRequestDto
                    {
                        UserName = UserName.Trim(),
                        NewPassword = NewPassword,
                        VerificationCode = VerificationCode
                    });

                    if (result == null)
                    {
                        StatusMessage = "重置失败：服务器未返回结果";
                        IsError = true;
                        return;
                    }

                    StatusMessage = result.Message;
                    IsError = !result.Success;

                    if (result.Success)
                    {
                        ResetSucceeded?.Invoke();
                    }
                }
                catch (Refit.ApiException apiEx)
                {
                    try
                    {
                        var msg = await apiEx.GetContentAsAsync<ApiErrorMessage>();
                        StatusMessage = string.IsNullOrWhiteSpace(msg?.message)
                            ? "重置失败，请检查输入信息"
                            : msg.message;
                    }
                    catch
                    {
                        StatusMessage = "重置失败，请检查输入信息";
                    }
                    IsError = true;
                }
                catch (Exception ex)
                {
                    StatusMessage = "重置异常：" + ex.Message;
                    IsError = true;
                }
                finally
                {
                    ConfirmCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 错误消息模型（匹配后端 { message = "..." } 返回格式）。
        /// </summary>
        private class ApiErrorMessage
        {
            public string message { get; set; }
        }
    }
}
