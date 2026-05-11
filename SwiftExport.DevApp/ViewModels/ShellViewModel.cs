using GalaSoft.MvvmLight.Command;
using SwiftExport.View.FW.Http;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Services;
using System;

namespace SwiftExport.DevApp.ViewModels
{
    /// <summary>
    /// 主窗口 Shell ViewModel。
    /// 负责顶栏用户信息展示、退出登录、切换用户命令。
    /// 登录成功 / 退出后调用 <see cref="RefreshUserInfo"/> 刷新绑定。
    /// </summary>
    public class ShellViewModel : ViewModelBaseEx
    {
        private readonly ISessionService _session;

        // ============= 属性 =============

        /// <summary>显示名称（NickName，若为空则显示 UserName）</summary>
        public string DisplayName => string.IsNullOrEmpty(AuthTokenStore.CurrentNickName)
            ? AuthTokenStore.CurrentUserName
            : AuthTokenStore.CurrentNickName;

        /// <summary>登录名（括号内显示）</summary>
        public string LoginName => AuthTokenStore.CurrentUserName;

        /// <summary>角色标签（如 [Admin]）</summary>
        public string RoleTag => AuthTokenStore.IsAdmin ? "[管理员]" : string.Empty;

        /// <summary>是否已登录</summary>
        public bool IsLoggedIn => AuthTokenStore.IsLoggedIn;

        /// <summary>未登录时显示"未登录"</summary>
        public bool IsNotLoggedIn => !AuthTokenStore.IsLoggedIn;

        // ============= 命令 =============

        /// <summary>退出登录命令</summary>
        public RelayCommand LogoutCommand { get; }

        /// <summary>切换用户命令</summary>
        public RelayCommand SwitchUserCommand { get; }

        // ============= 事件（通知 Shell 弹出登录窗口）=============

        /// <summary>需要显示登录窗口时触发（退出 / 切换用户后）</summary>
        public event Action ShowLoginRequested;

        public ShellViewModel(ISessionService session)
        {
            _session = session;

            LogoutCommand = new RelayCommand(OnLogout, () => IsLoggedIn);
            SwitchUserCommand = new RelayCommand(OnSwitchUser);
        }

        // ============= 逻辑 =============

        private void OnLogout()
        {
            _session.Logout();
            RefreshUserInfo();
            // 退出后触发显示登录窗口
            ShowLoginRequested?.Invoke();
        }

        private void OnSwitchUser()
        {
            _session.SwitchUser();
            RefreshUserInfo();
            ShowLoginRequested?.Invoke();
        }

        /// <summary>
        /// 登录成功或退出后调用，刷新顶栏所有绑定属性。
        /// </summary>
        public void RefreshUserInfo()
        {
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(LoginName));
            RaisePropertyChanged(nameof(RoleTag));
            RaisePropertyChanged(nameof(IsLoggedIn));
            RaisePropertyChanged(nameof(IsNotLoggedIn));
            LogoutCommand.RaiseCanExecuteChanged();
        }
    }
}
