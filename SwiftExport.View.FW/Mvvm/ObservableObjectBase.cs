using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SwiftExport.View.FW.Mvvm
{
    /// <summary>
    /// 轻量可绑定对象基类，只提供属性通知和程序内部状态抑制。
    /// 不包含 Busy、Messenger、Navigation 等页面 ViewModel 能力。
    /// </summary>
    public abstract class ObservableObjectBase : INotifyPropertyChanged
    {
        private int _suppressDirtyCount;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 当前是否处于程序内部赋值状态。行模型可用它避免把加载、计算或 UI 状态变更记为脏数据。
        /// </summary>
        public bool DisabledState => _suppressDirtyCount > 0;

        protected virtual bool SetProperty<T>(
            ref T storage,
            T value,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected IDisposable DisabledStateScope()
        {
            Interlocked.Increment(ref _suppressDirtyCount);
            return new ActionOnDispose(() =>
                Interlocked.Decrement(ref _suppressDirtyCount));
        }

        protected sealed class ActionOnDispose : IDisposable
        {
            private readonly Action _action;

            public ActionOnDispose(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                _action?.Invoke();
            }
        }
    }
}
