using System;
using System.Windows.Input;

namespace SwiftExport.VSTO.UserWindow.Mvvm
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        // 构造函数：只需要执行逻辑
        public RelayCommand(Action execute) : this(p => execute(), null)
        {
        }

        // 构造函数：执行逻辑 + 判断逻辑
        public RelayCommand(Action execute, Func<bool> canExecute) : this(p => execute(), p => canExecute())
        {
        }

        // 通用构造函数
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // 判断命令是否可执行
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        // 执行命令
        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        // 自动挂钩 WPF 的命令管理系统
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        /// <summary>
        /// 手动触发命令状态检查
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            // 强制 WPF 命令管理器重新查询所有命令的可执行状态
            CommandManager.InvalidateRequerySuggested();
        }
    }
}