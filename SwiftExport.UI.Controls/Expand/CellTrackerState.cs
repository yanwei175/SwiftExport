using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExport.UI.Controls.Expand
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

    // 2. DataGrid 的编辑拦截器行为
    public static class CellTracker
    {
        public static readonly DependencyProperty IsTrackingEnabledProperty =
            DependencyProperty.RegisterAttached("IsTrackingEnabled", typeof(bool), typeof(CellTracker), new UIPropertyMetadata(false, OnIsTrackingEnabledChanged));

        public static bool GetIsTrackingEnabled(DependencyObject obj) => (bool)obj.GetValue(IsTrackingEnabledProperty);
        public static void SetIsTrackingEnabled(DependencyObject obj, bool value) => obj.SetValue(IsTrackingEnabledProperty, value);

        public static readonly DependencyProperty RowVersionProperty =
            DependencyProperty.RegisterAttached("RowVersion", typeof(int), typeof(CellTracker), new PropertyMetadata(0));

        public static int GetRowVersion(DependencyObject obj) => (int)obj.GetValue(RowVersionProperty);
        public static void SetRowVersion(DependencyObject obj, int value) => obj.SetValue(RowVersionProperty, value);

        // ★ 新增：临时存储旧值的口袋
        private static readonly DependencyProperty TempOldValueProperty =
            DependencyProperty.RegisterAttached("TempOldValue", typeof(string), typeof(CellTracker));

        public static void BumpRowVersion(DataGrid grid, object rowItem)
        {
            if (rowItem == null) return;
            if (grid.ItemContainerGenerator.ContainerFromItem(rowItem) is DataGridRow row)
            {
                SetRowVersion(row, GetRowVersion(row) + 1);
            }
        }

        private static void OnIsTrackingEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid)
            {
                // 卸载旧事件
                grid.PreparingCellForEdit -= Grid_PreparingCellForEdit;
                grid.CellEditEnding -= Grid_CellEditEnding;

                if ((bool)e.NewValue)
                {
                    // 挂载新事件
                    grid.PreparingCellForEdit += Grid_PreparingCellForEdit;
                    grid.CellEditEnding += Grid_CellEditEnding;
                }
            }
        }

        // ★ 1. 准备编辑时：拍下旧照片，存在当前生成的独立编辑控件 (EditingElement) 上
        private static void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Column is DataGridBoundColumn boundColumn)
            {
                string oldValue = UndoManager.GetPropertyValue(e.Row.Item, boundColumn.Binding);
                // 完美运行！把旧值死死绑定在这个用来打字的控件身上
                e.EditingElement.SetValue(TempOldValueProperty, oldValue);
            }
        }

        // ★ 2. 结束编辑时：彻底粉碎 WPF 时序 Bug
        private static void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit && e.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding)
            {
                var grid = (DataGrid)sender;
                string propertyName = binding.Path.Path;
                string oldValue = e.EditingElement.GetValue(TempOldValueProperty) as string ?? string.Empty;

                // ★ 死死抓住当前行的数据实体引用，防止虚拟化复用导致的对象丢失
                object rowItem = e.Row.Item;

                // ==========================================
                // ★ 核心修复 1：强行逼迫控件立刻交出最新数据！
                // 解决 LostFocus 没触发导致 ViewModel 数据滞后的千古难题
                // ==========================================
                if (e.EditingElement is TextBox tb)
                {
                    tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                }
                else if (e.EditingElement is ComboBox cb)
                {
                    cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                }
                else if (e.EditingElement is CheckBox chk)
                {
                    chk.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
                }

                // ==========================================
                // ★ 核心修复 2：使用更稳健的 Loaded 优先级 (Priority 6)
                // 确保 ViewModel 完全消化了数据（包括各种校验、类型转换）后，再去查验真伪
                // ==========================================
                grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    string newValue = UndoManager.GetPropertyValue(rowItem, binding);

                    // 只有真改了，才记录撤销和触发变色
                    if (oldValue != newValue)
                    {
                        CellTrackerState.MarkDirty(rowItem, propertyName);
                        BumpRowVersion(grid, rowItem);

                        var batch = new List<CellHistory> { new CellHistory { RowItem = rowItem, PropertyName = propertyName, OldValue = oldValue } };
                        UndoManager.Push(grid, batch);
                    }
                }));
            }
        }
    }

    // 3. XAML 桥梁：抗虚拟化智能转换器
    public class CellDirtyConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // 防御性判断：处理 WPF 虚拟化时的断开连接状态
            if (values.Length < 2 || values[0] == null || values[0] == DependencyProperty.UnsetValue)
                return false;

            if (values[1] is DataGridCell cell && cell.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding)
            {
                return CellTrackerState.IsDirty(values[0], binding.Path.Path);
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();











    }
}