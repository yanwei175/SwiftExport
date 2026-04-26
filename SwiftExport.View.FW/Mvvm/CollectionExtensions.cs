using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mvvm
{
    public static class CollectionExtensions
    {
        /// <summary>
        /// 安全地刷新 ObservableCollection，保持引用不变
        /// </summary>
        public static void Refresh<T>(this ObservableCollection<T> collection, IEnumerable<T> newData)
        {
            if (collection == null) return;
            collection.Clear();
            if (newData == null) return;

            foreach (var item in newData)
            {
                collection.Add(item);
            }
        }
    }
}
