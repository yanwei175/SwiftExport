using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using GalaSoft.MvvmLight.Views;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        // 自动管理 Busy 的辅助方法
        protected IDisposable BusyScope()
        {
            IsBusy = true;
            return new ActionOnDispose(() => IsBusy = false);
        }

        private class ActionOnDispose : IDisposable
        {
            private readonly Action _action;
            public ActionOnDispose(Action action) => _action = action;
            public void Dispose() => _action?.Invoke();
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



        protected void RefreshCommandState(ICommand command, string canPropertyName = null)
        {
            // 刷新 CanExecute
            if (command is RelayCommand rc)
                rc.RaiseCanExecuteChanged();

            // 刷新 CanSave / CanDelete / CanAdd 等计算属性
            if (!string.IsNullOrEmpty(canPropertyName))
                base.RaisePropertyChanged(canPropertyName);
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