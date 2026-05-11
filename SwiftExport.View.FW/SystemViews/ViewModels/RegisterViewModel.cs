using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Http;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SystemViews.ViewModels
{
    /// <summary>
    /// 注册新用户页面 ViewModel。
    /// 规则：仅 admin 账号登录后可用，其他用户按钮不可点击且显示提示。
    /// </summary>
    public class RegisterViewModel : ViewModelBaseEx
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
                    RegisterCommand.RaiseCanExecuteChanged();
            }
        }

        private string _nickName = string.Empty;
        public string NickName
        {
            get => _nickName;
            set => SetProperty(ref _nickName, value);
        }

        private string _email = string.Empty;
        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        private string _phoneNumber = string.Empty;
        public string PhoneNumber
        {
            get => _phoneNumber;
            set => SetProperty(ref _phoneNumber, value);
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

        /// <summary>当前是否为管理员（基于 /me 返回的角色列表，RoleName 含 "Admin"）</summary>
        public bool IsAdmin => AuthTokenStore.IsAdmin;

        /// <summary>非管理员时显示的提示语</summary>
        public string AdminHint => IsAdmin
            ? string.Empty
            : "仅拥有 Admin 角色的账号可注册新用户，请联系管理员或切换账号。";

        // ============= 命令 =============

        /// <summary>
        /// 注册命令，CommandParameter 传入 "password|confirmPassword" 拼接字符串
        /// （由 View 的 code-behind 合并后传入）。
        /// </summary>
        public RelayCommand<string> RegisterCommand { get; }

        /// <summary>清空表单命令</summary>
        public RelayCommand ClearCommand { get; }

        public RegisterViewModel(IAuthApi authApi, IDialogService dialog)
        {
            _authApi = authApi;
            _dialog = dialog;

            RegisterCommand = new RelayCommand<string>(
                async pwdPair => await RegisterAsync(pwdPair),
                _ => IsAdmin && !string.IsNullOrWhiteSpace(UserName) && !IsBusy);

            ClearCommand = new RelayCommand(ClearForm);
        }

        // ============= 注册逻辑 =============

        private async Task RegisterAsync(string passwordPair)
        {
            // passwordPair 格式："{password}|{confirmPassword}"，由 View code-behind 拼接
            var parts = (passwordPair ?? string.Empty).Split('|');
            var password = parts.Length > 0 ? parts[0] : string.Empty;
            var confirmPwd = parts.Length > 1 ? parts[1] : string.Empty;

            // 前端校验
            if (string.IsNullOrWhiteSpace(UserName))
            {
                StatusMessage = "用户名不能为空";
                IsError = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                StatusMessage = "密码不能少于6位";
                IsError = true;
                return;
            }

            if (password != confirmPwd)
            {
                StatusMessage = "两次密码不一致，请重新输入";
                IsError = true;
                return;
            }

            using (BusyScope())
            {
                StatusMessage = "注册中...";
                IsError = false;
                RegisterCommand.RaiseCanExecuteChanged();

                try
                {
                    var result = await _authApi.RegisterAsync(new RegisterRequestDto
                    {
                        UserName = UserName.Trim(),
                        Password = password,
                        ConfirmPassword = confirmPwd,
                        NickName = string.IsNullOrWhiteSpace(NickName) ? UserName.Trim() : NickName.Trim(),
                        Email = Email?.Trim(),
                        PhoneNumber = PhoneNumber?.Trim()
                    });

                    if (result != null && result.Success)
                    {
                        StatusMessage = $"用户 {UserName} 注册成功！";
                        IsError = false;
                        await _dialog.ShowInfoAsync($"用户 {UserName} 注册成功！", "注册成功");
                        ClearForm();
                    }
                    else
                    {
                        StatusMessage = "注册失败：" + (result?.Message ?? "未知错误");
                        IsError = true;
                    }
                }
                catch (Refit.ApiException apiEx) when (apiEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    StatusMessage = "权限不足：仅管理员可注册新用户";
                    IsError = true;
                }
                catch (Refit.ApiException apiEx) when (apiEx.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    StatusMessage = "注册失败：" + apiEx.Message;
                    IsError = true;
                }
                catch (Exception ex)
                {
                    StatusMessage = "注册异常：" + ex.Message;
                    IsError = true;
                }
                finally
                {
                    RegisterCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private void ClearForm()
        {
            UserName = string.Empty;
            NickName = string.Empty;
            Email = string.Empty;
            PhoneNumber = string.Empty;
            StatusMessage = string.Empty;
            IsError = false;
        }
    }
}
