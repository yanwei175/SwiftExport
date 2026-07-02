using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MateWpfThemes.FW.Expand
{
    // 1. 全局状态存储中心 (防御内存泄漏的弱引用字典)
    public static class CellTrackerState
    {
        // 存储字典：某行数据 -> 变脏的属性名集合
        public static ConditionalWeakTable<object, HashSet<string>> DirtyRegistry = new ConditionalWeakTable<object, HashSet<string>>();

        public static void MarkDirty(object rowItem, string propertyName)
        {
            if (rowItem == null || string.IsNullOrEmpty(propertyName)) return;

            if (!DirtyRegistry.TryGetValue(rowItem, out var set))
            {
                set = new HashSet<string>();
                DirtyRegistry.Add(rowItem, set);
            }
            set.Add(propertyName);
        }

        public static bool IsDirty(object rowItem, string propertyName)
        {
            if (rowItem == null || string.IsNullOrEmpty(propertyName)) return false;
            return DirtyRegistry.TryGetValue(rowItem, out var set) && set.Contains(propertyName);
        }

        // 洗白单行数据（保存成功后调用）
        public static void ClearDirty(object rowItem)
        {
            if (rowItem != null)
            {
                DirtyRegistry.Remove(rowItem);
            }
        }

        // 洗白所有数据（重新加载数据时调用）
        public static void ClearAll()
        {
            // 直接换个新字典，旧的会被 GC 自动回收，极度安全
            DirtyRegistry = new ConditionalWeakTable<object, HashSet<string>>();
        }

    }

}