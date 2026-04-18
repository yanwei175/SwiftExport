using MateWpfThemes.FW.AttachedProperties;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MateWpfThemes.FW.Expand
{
    // 1. 记忆碎片结构
    public class CellHistory
    {
        public object RowItem { get; set; }
        public string PropertyName { get; set; }
        public string OldValue { get; set; }
    }

    // 2. 撤销大管家
    public static class UndoManager
    {
        private const int MaxSteps = 50; // ★ 50 步防内存爆炸限制

        // ==========================================
        // ★ 核心外挂属性：开启独立撤销功能
        // ==========================================
        #region IsUndoEnabled
        public static readonly DependencyProperty IsUndoEnabledProperty =
            DependencyProperty.RegisterAttached("IsUndoEnabled", typeof(bool), typeof(UndoManager), new UIPropertyMetadata(false, OnIsUndoEnabledChanged));

        public static bool GetIsUndoEnabled(DependencyObject obj) => (bool)obj.GetValue(IsUndoEnabledProperty);
        public static void SetIsUndoEnabled(DependencyObject obj, bool value) => obj.SetValue(IsUndoEnabledProperty, value);

        private static readonly DependencyProperty TempOldValueProperty =
            DependencyProperty.RegisterAttached("TempOldValue", typeof(string), typeof(UndoManager));

        private static void OnIsUndoEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid)
            {
                // 卸载旧事件
                grid.PreparingCellForEdit -= Grid_PreparingCellForEdit;
                grid.CellEditEnding -= Grid_CellEditEnding;
                grid.PreviewKeyDown -= Grid_PreviewKeyDown;

                if ((bool)e.NewValue)
                {
                    // 挂载新事件：记录编辑 & 监听Ctrl+Z
                    grid.PreparingCellForEdit += Grid_PreparingCellForEdit;
                    grid.CellEditEnding += Grid_CellEditEnding;
                    grid.PreviewKeyDown += Grid_PreviewKeyDown;
                }
            }
        }

        // 1. 监听 Ctrl+Z
        private static void Grid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is DataGrid grid && !grid.IsReadOnly)
            {
                // 如果按下了 Ctrl + Z
                if (e.Key == System.Windows.Input.Key.Z && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
                {
                    Undo(grid);
                    e.Handled = true;
                }
            }
        }

        // 2. 准备编辑时，把原来的值塞进私有口袋
        private static void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Column is DataGridBoundColumn boundColumn)
            {
                string oldValue = GetPropertyValue(e.Row.Item, boundColumn.Binding);
                e.EditingElement.SetValue(TempOldValueProperty, oldValue);
            }
        }

        // 3. 编辑结束时，如果值变了，就记录到历史堆栈
        private static void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit && e.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding)
            {
                var grid = (DataGrid)sender;
                string propertyName = binding.Path.Path;
                string oldValue = e.EditingElement.GetValue(TempOldValueProperty) as string ?? string.Empty;
                object rowItem = e.Row.Item;

                if (e.EditingElement is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                else if (e.EditingElement is ComboBox cb) cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                else if (e.EditingElement is CheckBox chk) chk.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();

                grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    string newValue = GetPropertyValue(rowItem, binding);
                    if (oldValue != newValue)
                    {
                        MateWpfThemes.FW.Expand.CellTrackerState.MarkDirty(rowItem, propertyName);
                        MateWpfThemes.FW.AttachedProperties.DataGridExcelAssist.BumpRowVersion(grid, rowItem);

                        var batch = new List<CellHistory> { new CellHistory { RowItem = rowItem, PropertyName = propertyName, OldValue = oldValue } };
                        Push(grid, batch);
                    }
                }));
            }
        }
        #endregion


        // 使用双向链表替代 Stack，方便砍掉尾部
        private static readonly DependencyProperty UndoStackProperty =
            DependencyProperty.RegisterAttached("UndoStack", typeof(LinkedList<List<CellHistory>>), typeof(UndoManager), new PropertyMetadata(null));

        public static LinkedList<List<CellHistory>> GetUndoStack(DataGrid grid)
        {
            var stack = (LinkedList<List<CellHistory>>)grid.GetValue(UndoStackProperty);
            if (stack == null)
            {
                stack = new LinkedList<List<CellHistory>>();
                grid.SetValue(UndoStackProperty, stack);
            }
            return stack;
        }

        // 压入历史记录
        public static void Push(DataGrid grid, List<CellHistory> batch)
        {
            if (batch == null || batch.Count == 0) return;

            var stack = GetUndoStack(grid);
            stack.AddLast(batch); // 压入顶部

            // 如果超过 50 步，无情砍掉最底部的最老记录
            if (stack.Count > MaxSteps)
            {
                stack.RemoveFirst();
            }
        }

        // 执行撤销
        public static void Undo(DataGrid grid)
        {
            var stack = GetUndoStack(grid);
            if (stack.Count == 0) return;

            // 1. 拿出最新的一批历史记录
            var lastBatch = stack.Last.Value;
            stack.RemoveLast(); // 从栈中移除

            var affectedRows = new HashSet<object>();

            // 2. 时光倒流：逐个单元格恢复旧值
            foreach (var history in lastBatch)
            {
                SetPropertyValueByString(history.RowItem, history.PropertyName, history.OldValue);
                affectedRows.Add(history.RowItem);

                // 恢复后重新标记为脏数据（或者你可以根据业务逻辑在这里清除脏标记）
                CellTrackerState.MarkDirty(history.RowItem, history.PropertyName);
            }

            // 3. 敲响铃铛：唤醒 UI 变色并刷新
            foreach (var rowItem in affectedRows)
            {
                DataGridExcelAssist.BumpRowVersion(grid, rowItem);
            }
        }

        // 万能深度反射读取 (支持类似 Customer.Contact.Phone 这种深层嵌套)
        public static string GetPropertyValue(object item, BindingBase binding)
        {
            if (!(binding is Binding b) || string.IsNullOrEmpty(b.Path.Path)) return string.Empty;

            string[] props = b.Path.Path.Split('.');
            object currentItem = item;
            PropertyInfo prop = null;

            foreach (var p in props)
            {
                if (currentItem == null) return string.Empty;
                prop = currentItem.GetType().GetProperty(p);
                if (prop == null) return string.Empty;
                if (p != props[props.Length - 1])
                    currentItem = prop.GetValue(currentItem);
            }

            if (prop != null && prop.CanRead && currentItem != null)
            {
                return prop.GetValue(currentItem)?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        // 万能深度反射写入 (支持 Nullable 清空和 Enum 枚举)
        public static void SetPropertyValueByString(object item, string propertyPath, string stringValue)
        {
            string[] props = propertyPath.Split('.');
            object currentItem = item;
            PropertyInfo prop = null;

            for (int i = 0; i < props.Length; i++)
            {
                if (currentItem == null) return;
                prop = currentItem.GetType().GetProperty(props[i]);
                if (prop == null) return;
                if (i < props.Length - 1)
                    currentItem = prop.GetValue(currentItem);
            }

            if (prop != null && prop.CanWrite && currentItem != null)
            {
                try
                {
                    Type targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    object convertedValue;

                    // 处理清空可空类型
                    if (string.IsNullOrEmpty(stringValue) && Nullable.GetUnderlyingType(prop.PropertyType) != null)
                    {
                        convertedValue = null;
                    }
                    // 处理枚举类型
                    else if (targetType.IsEnum)
                    {
                        convertedValue = Enum.Parse(targetType, stringValue, true);
                    }
                    // 基础类型转换
                    else
                    {
                        convertedValue = Convert.ChangeType(stringValue, targetType);
                    }

                    prop.SetValue(currentItem, convertedValue);
                }
                catch { } // 转换失败静默忽略，保证程序不崩
            }
        }
    }
}