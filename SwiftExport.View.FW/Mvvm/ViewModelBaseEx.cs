using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using GalaSoft.MvvmLight.Views;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;

namespace SwiftExport.View.FW.Mvvm
{
    /// <summary>
    /// 所有 ViewModel 的基类
    /// </summary>
    public abstract class ViewModelBaseEx : ViewModelBase
    {

        // ===========================
        // 1. Busy 状态（全局可用）
        // ===========================

        #region Busy 状态（全局可用）
        private int _busyCount = 0;
        public bool IsBusy => _busyCount > 0;
        public bool IsNotBusy => !IsBusy;

        // 自动管理 Busy 的辅助方法
        protected IDisposable BusyScope()
        {
            Interlocked.Increment(ref _busyCount);
            RaiseBusyChanged();
            return new ActionOnDispose(() =>
            {
                Interlocked.Decrement(ref _busyCount);
                RaiseBusyChanged();
            });
        }

        private sealed class ActionOnDispose : IDisposable
        {
            private readonly Action _action;
            public ActionOnDispose(Action action) => _action = action;
            public void Dispose() => _action?.Invoke();
        }

        // ===========================
        // 2. Busy 影响的命令管理
        // ===========================
        private readonly List<(RelayCommand Command, bool AffectedByBusy)> _busyNotifyCommands
            = new List<(RelayCommand, bool)>();


        /// <summary>
        /// 注册命令，并指定是否受 Busy 限制
        /// </summary>
        protected void RegisterBusyNotifyCommand(RelayCommand command, bool affectedByBusy = true)
        {
            if (command != null)
                _busyNotifyCommands.Add((command, affectedByBusy));
        }

        /// <summary>
        /// Busy 状态改变时刷新命令
        /// </summary>
        private void RaiseBusyChanged()
        {
            RaisePropertyChanged(nameof(IsBusy));
            RaisePropertyChanged(nameof(IsNotBusy));

            foreach (var (cmd, affected) in _busyNotifyCommands)
            {
                if (affected)
                    cmd.RaiseCanExecuteChanged();
            }
        }






        #endregion


        // ===========================
        // 2. Messenger（直接使用 MVVM Light）
        // ===========================
        protected IMessenger Messenger => GalaSoft.MvvmLight.Messaging.Messenger.Default;

        // ===========================
        // 3. 导航服务（可选）
        // ===========================
        protected INavigationService Navigation { get; set; }

        // ===========================
        // 4. 日志（可选）
        // ===========================
        protected Action<string> Logger { get; set; }

        protected void Log(string msg)
        {
            Logger?.Invoke(msg);
        }


        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;

            storage = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

    }
}