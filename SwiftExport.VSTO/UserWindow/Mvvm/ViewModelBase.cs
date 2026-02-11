using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwiftExport.VSTO.UserWindow.Mvvm
{
    /// <summary>
    /// 所有 ViewModel 的基类
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 触发属性更改通知
        /// </summary>
        /// <param name="propertyName">属性名（自动捕获）</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 核心方法：设置属性值并触发通知
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="storage">引用对应的私有字段</param>
        /// <param name="value">新值</param>
        /// <param name="propertyName">属性名（自动捕获）</param>
        /// <returns>如果值确实改变了返回 true，否则返回 false</returns>
        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            // 检查值是否相同，如果相同就不浪费资源去更新 UI
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}