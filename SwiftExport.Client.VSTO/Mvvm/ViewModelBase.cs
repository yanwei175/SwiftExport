using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SwiftExport.Client.VSTO.Mvvm
{
    /// <summary>提供页面通知、忙碌状态和幂等释放；所有状态由 UI 线程维护。</summary>
    public abstract class ViewModelBase : ObservableObject, IDisposable
    {
        private readonly List<IRelayCommand> commands = new List<IRelayCommand>();
        private string title = string.Empty;
        private string errorMessage = string.Empty;
        private bool isBusy;
        public string Title { get { return title; } protected set { SetProperty(ref title, value); } }
        public string ErrorMessage { get { return errorMessage; } protected set { SetProperty(ref errorMessage, value); } }
        public bool IsBusy
        {
            get { return isBusy; }
            protected set { if (SetProperty(ref isBusy, value)) RefreshCommands(); }
        }
        public bool IsDisposed { get; private set; }

        /// <summary>登记需随 Busy 和释放状态重新计算的命令。</summary>
        protected void RegisterCommand(IRelayCommand command) { commands.Add(command); }
        /// <summary>通知命令重新计算可执行条件。</summary>
        protected void RefreshCommands() { foreach (var command in commands) command.NotifyCanExecuteChanged(); }
        /// <summary>终止页面生命周期并禁止再次执行命令。</summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            DisposeCore();
            RefreshCommands();
        }
        /// <summary>供持有订阅或操作的页面清理资源。</summary>
        protected virtual void DisposeCore() { }
    }
}
