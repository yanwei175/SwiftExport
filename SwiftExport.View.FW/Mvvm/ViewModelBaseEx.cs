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


        /// <summary>Busy 影响的命令管理</summary>
        private readonly List<(RelayCommand Command, bool AffectedByBusy)> _busyNotifyCommands
            = new List<(RelayCommand, bool)>();


        /// <summary>注意只是通知..还需另外去实现CanExecute,注册一个命令，绑定它的CanExecute是否受到IsBusy的影响。</summary>
        protected void RegisterBusyNotifyCommand(RelayCommand command, bool affectedByBusy = true)
        {
            if (command != null)
                _busyNotifyCommands.Add((command, affectedByBusy));
        }

        /// <summary>Busy 状态改变时刷新命令 </summary>
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



        // ===========================
        // 5. SuppressDirty 状态
        // ===========================

        #region 设置程序内部修改抑制器（用于计算属性）

        private int _suppressDirtyCount = 0;

        /// <summary>
        ///  指示当前是否处于禁用状态（当内部抑制计数大于零时为 true）。
        /// </summary>
        /// <remarks>用于在抑制变更通知或临时禁止脏标记期间判断组件行为。内部字段 _suppressDirtyCount 为计数器，递增表示进入禁用状态，递减表示退出。</remarks>
        public bool DisabledState => _suppressDirtyCount > 0;

        /// <summary>
        /// 用于程序自动计算的一些属性又不想触发脏数据变更时，可以在计算时使用这个方法包裹起来，来抑制脏数据变更的触发。
        /// </summary>
        protected IDisposable DisabledStateScope()
        {
            Interlocked.Increment(ref _suppressDirtyCount);
            return new ActionOnDispose(() =>
                Interlocked.Decrement(ref _suppressDirtyCount));
        }

        #endregion


    }
}