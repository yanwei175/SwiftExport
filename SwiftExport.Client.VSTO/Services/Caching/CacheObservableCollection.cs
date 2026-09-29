using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftExport.Client.VSTO.Services.Caching
{
    /// <summary>
    /// 缓存内部使用的集合。批量替换时只发送一次 Reset，避免控件观察到半更新状态。
    /// </summary>
    internal sealed class CacheObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>替换内部快照并一次通知绑定控件；调用方必须位于 UI 线程。</summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            CheckReentrancy();
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
