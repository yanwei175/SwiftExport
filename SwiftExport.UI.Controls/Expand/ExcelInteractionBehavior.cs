using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SwiftExport.UI.Controls.Expand
{
    public static class ExcelInteractionBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
                    DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ExcelInteractionBehavior), new UIPropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid)
            {
                grid.PreviewKeyDown -= Grid_PreviewKeyDown;
                if ((bool)e.NewValue) grid.PreviewKeyDown += Grid_PreviewKeyDown;
            }
        }

        private static void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is DataGrid grid) || grid.IsReadOnly) return;
            bool isCtrlDown = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (e.Key == Key.V && isCtrlDown) { HandlePaste(grid); e.Handled = true; } // 1：拦截 Ctrl + V (从 Excel 粘贴并清洗)
            else if (e.Key == Key.D && isCtrlDown) { HandleFillDown(grid); e.Handled = true; }// 2：拦截 Ctrl + D (向下填充)
            else if (e.Key == Key.Delete) { HandleDelete(grid); e.Handled = true; }//3. 拦截 Delete (批量清空)
            else if (e.Key == Key.Z && isCtrlDown) { UndoManager.Undo(grid); e.Handled = true; }//4：拦截 Ctrl + Z (撤销)

        }
        #region Delete 批量清空单元格
        private static void HandleDelete(DataGrid grid)
        {
            if (grid.SelectedCells.Count == 0) return;

            var batchHistory = new List<CellHistory>();
            var cellsByRow = grid.SelectedCells.GroupBy(c => c.Item).ToList();

            foreach (var rowGroup in cellsByRow)
            {
                object rowItem = rowGroup.Key;
                foreach (var cellInfo in rowGroup)
                {
                    if (!(cellInfo.Column is DataGridBoundColumn column) || column.IsReadOnly || column.Visibility != Visibility.Visible) continue;
                    string propertyName = ((Binding)column.Binding).Path.Path;

                    // ★ 拍照留念
                    string oldValue = UndoManager.GetPropertyValue(rowItem, column.Binding);
                    batchHistory.Add(new CellHistory { RowItem = rowItem, PropertyName = propertyName, OldValue = oldValue });

                    UndoManager.SetPropertyValueByString(rowItem, propertyName, string.Empty);
                    CellTrackerState.MarkDirty(rowItem, propertyName);
                }
                CellTracker.BumpRowVersion(grid, rowItem);
            }
            UndoManager.Push(grid, batchHistory); // ★ 压入历史
        }
        #endregion

        #region Ctrl+V 批量粘贴
        private static readonly Regex _regex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        private static void HandlePaste(DataGrid grid)
        {
            string clipboardText = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(clipboardText) || grid.CurrentCell.Column == null || grid.CurrentCell.Item == null) return;

            int startRowIndex = grid.Items.IndexOf(grid.CurrentCell.Item);
            int startColIndex = grid.Columns.IndexOf(grid.CurrentCell.Column);
            if (startRowIndex < 0 || startColIndex < 0) return;

            string[] rows = clipboardText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            if (rows.Length > 0 && string.IsNullOrEmpty(rows[rows.Length - 1])) rows = rows.Take(rows.Length - 1).ToArray();

            var batchHistory = new List<CellHistory>();

            for (int r = 0; r < rows.Length; r++)
            {
                int currentRowIndex = startRowIndex + r;
                if (currentRowIndex >= grid.Items.Count) break;

                object rowItem = grid.Items[currentRowIndex];
                string[] cellValues = rows[r].Split('\t');
                bool rowModified = false;

                for (int c = 0; c < cellValues.Length; c++)
                {
                    int currentColIndex = startColIndex + c;
                    if (currentColIndex >= grid.Columns.Count) break;
                    if (!(grid.Columns[currentColIndex] is DataGridBoundColumn column) || column.IsReadOnly || column.Visibility != Visibility.Visible) continue;

                    string propertyName = ((Binding)column.Binding).Path.Path;

                    // ★ 拍照留念
                    string oldValue = UndoManager.GetPropertyValue(rowItem, column.Binding);
                    batchHistory.Add(new CellHistory { RowItem = rowItem, PropertyName = propertyName, OldValue = oldValue });

                    // 清洗与赋值
                    string cleanedValue = CleanseData(cellValues[c], column.Header?.ToString());
                    UndoManager.SetPropertyValueByString(rowItem, propertyName, cleanedValue);

                    CellTrackerState.MarkDirty(rowItem, propertyName);
                    rowModified = true;
                }
                if (rowModified) CellTracker.BumpRowVersion(grid, rowItem);
            }
            UndoManager.Push(grid, batchHistory); // ★ 压入历史
        }

        private static string CleanseData(string rawValue, string columnHeader)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;
            string cleaned = rawValue.Replace("\u00A0", " ");
            cleaned = _regex.Replace(cleaned, " ").Trim();
            if (columnHeader == "客户编码") cleaned = cleaned.ToUpper();
            else if (columnHeader == "起泡器") cleaned = cleaned.Replace("*", "").Replace("#", "");
            return cleaned;
        }
        #endregion

        #region Ctrl+D 向下填充
        private static void HandleFillDown(DataGrid grid)
        {
            if (grid.SelectedCells.Count <= 1) return;

            var batchHistory = new List<CellHistory>();
            var cellsByColumn = grid.SelectedCells.GroupBy(c => c.Column).ToList();
            var modifiedRows = new HashSet<object>();

            foreach (var colGroup in cellsByColumn)
            {
                if (!(colGroup.Key is DataGridBoundColumn column) || column.IsReadOnly) continue;

                var sortedItems = colGroup.Select(c => c.Item).OrderBy(item => grid.Items.IndexOf(item)).ToList();
                string sourceValue = UndoManager.GetPropertyValue(sortedItems.First(), column.Binding);
                string propertyName = ((Binding)column.Binding).Path.Path;

                for (int i = 1; i < sortedItems.Count; i++)
                {
                    object rowItem = sortedItems[i];

                    // ★ 拍照留念
                    string oldValue = UndoManager.GetPropertyValue(rowItem, column.Binding);
                    batchHistory.Add(new CellHistory { RowItem = rowItem, PropertyName = propertyName, OldValue = oldValue });

                    UndoManager.SetPropertyValueByString(rowItem, propertyName, sourceValue);
                    CellTrackerState.MarkDirty(rowItem, propertyName);
                    modifiedRows.Add(rowItem);
                }
            }

            foreach (var rowItem in modifiedRows) CellTracker.BumpRowVersion(grid, rowItem);
            UndoManager.Push(grid, batchHistory); // ★ 压入历史
        }
        #endregion

        #region 核心反射引擎 (读写 ViewModel 属性)
        // 从 Binding 中提取属性名，并利用反射赋值
        private static void SetPropertyValue(object item, BindingBase binding, string stringValue)
        {
            if (!(binding is Binding b) || string.IsNullOrEmpty(b.Path.Path)) return;

            string propertyName = b.Path.Path;
            PropertyInfo prop = item.GetType().GetProperty(propertyName);

            if (prop != null && prop.CanWrite)
            {
                try
                {
                    // 处理基本的数据类型转换 (String -> int/decimal/bool 等)
                    object convertedValue = Convert.ChangeType(stringValue, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
                    prop.SetValue(item, convertedValue);
                }
                catch
                {
                    // 转换失败（比如给 int 字段粘贴了字母），这里可以静默忽略，或者给该字段赋 null
                }
            }
        }

        private static string GetPropertyValue(object item, BindingBase binding)
        {
            if (!(binding is Binding b) || string.IsNullOrEmpty(b.Path.Path)) return null;

            string propertyName = b.Path.Path;
            PropertyInfo prop = item.GetType().GetProperty(propertyName);

            if (prop != null && prop.CanRead)
            {
                object value = prop.GetValue(item);
                return value?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }
        #endregion
    }
}