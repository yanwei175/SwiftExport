using MateWpfThemes.FW.Expand; // 引用你的底层引擎库 (UndoManager, CellTrackerState)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// DataGrid 类 Excel 复杂交互外挂 (Ctrl+V, 向下填充, 脏数据追踪, 撤销)
    /// </summary>
    public static class DataGridExcelAssist
    {
        // ==========================================
        // 1. EnableDirtyTracking: 开启脏数据(单元格编辑)追踪
        // ==========================================
        #region EnableDirtyTracking
        public static readonly DependencyProperty EnableDirtyTrackingProperty =
            DependencyProperty.RegisterAttached("EnableDirtyTracking", typeof(bool), typeof(DataGridExcelAssist), new UIPropertyMetadata(false, OnEnableDirtyTrackingChanged));

        public static bool GetEnableDirtyTracking(DependencyObject obj) => (bool)obj.GetValue(EnableDirtyTrackingProperty);
        public static void SetEnableDirtyTracking(DependencyObject obj, bool value) => obj.SetValue(EnableDirtyTrackingProperty, value);

        // 内部使用的行版本号，用于强制触发 UI 刷新
        public static readonly DependencyProperty RowVersionProperty =
            DependencyProperty.RegisterAttached("RowVersion", typeof(int), typeof(DataGridExcelAssist), new PropertyMetadata(0));

        public static int GetRowVersion(DependencyObject obj) => (int)obj.GetValue(RowVersionProperty);
        public static void SetRowVersion(DependencyObject obj, int value) => obj.SetValue(RowVersionProperty, value);

        // 内部使用的临时旧值口袋
        private static readonly DependencyProperty TempOldValueProperty =
            DependencyProperty.RegisterAttached("TempOldValue", typeof(string), typeof(DataGridExcelAssist));

        public static void BumpRowVersion(DataGrid grid, object rowItem)
        {
            if (rowItem == null) return;
            if (grid.ItemContainerGenerator.ContainerFromItem(rowItem) is DataGridRow row)
            {
                SetRowVersion(row, GetRowVersion(row) + 1);
            }
        }

        private static void OnEnableDirtyTrackingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid)
            {
                grid.PreparingCellForEdit -= Grid_PreparingCellForEdit;
                grid.CellEditEnding -= Grid_CellEditEnding;

                if ((bool)e.NewValue)
                {
                    grid.PreparingCellForEdit += Grid_PreparingCellForEdit;
                    grid.CellEditEnding += Grid_CellEditEnding;
                }
            }
        }

        private static void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Column is DataGridBoundColumn boundColumn)
            {
                string oldValue = UndoManager.GetPropertyValue(e.Row.Item, boundColumn.Binding);
                e.EditingElement.SetValue(TempOldValueProperty, oldValue);
            }
        }

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
                    string newValue = UndoManager.GetPropertyValue(rowItem, binding);
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
        #endregion

        // ==========================================
        // 2. EnableExcelShortcuts: 开启 Ctrl+V, Delete, Ctrl+D, Ctrl+Z 快捷键
        // ==========================================
        #region EnableExcelShortcuts
        public static readonly DependencyProperty EnableExcelShortcutsProperty =
            DependencyProperty.RegisterAttached("EnableExcelShortcuts", typeof(bool), typeof(DataGridExcelAssist), new UIPropertyMetadata(false, OnEnableExcelShortcutsChanged));

        public static bool GetEnableExcelShortcuts(DependencyObject obj) => (bool)obj.GetValue(EnableExcelShortcutsProperty);
        public static void SetEnableExcelShortcuts(DependencyObject obj, bool value) => obj.SetValue(EnableExcelShortcutsProperty, value);

        private static void OnEnableExcelShortcutsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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

            if (e.Key == Key.V && isCtrlDown) { HandlePaste(grid); e.Handled = true; }
            else if (e.Key == Key.D && isCtrlDown) { HandleFillDown(grid); e.Handled = true; }
            else if (e.Key == Key.Delete) { HandleDelete(grid); e.Handled = true; }
            // Ctrl+Z 的职责已经移交给了 UndoManager 里的独立外挂
            // else if (e.Key == Key.Z && isCtrlDown) { UndoManager.Undo(grid); e.Handled = true; }
        }

        // --- 具体快捷键实现方法直接保留你原来的逻辑 ---
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

                    string oldValue = UndoManager.GetPropertyValue(rowItem, column.Binding);
                    batchHistory.Add(new CellHistory { RowItem = rowItem, PropertyName = propertyName, OldValue = oldValue });

                    UndoManager.SetPropertyValueByString(rowItem, propertyName, string.Empty);
                    CellTrackerState.MarkDirty(rowItem, propertyName);
                }
                BumpRowVersion(grid, rowItem); // 使用合并后的方法
            }
            UndoManager.Push(grid, batchHistory);
        }

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

                    string oldValue = UndoManager.GetPropertyValue(rowItem, column.Binding);
                    batchHistory.Add(new CellHistory { RowItem = rowItem, PropertyName = propertyName, OldValue = oldValue });

                    string cleanedValue = CleanseData(cellValues[c], column.Header?.ToString());
                    UndoManager.SetPropertyValueByString(rowItem, propertyName, cleanedValue);

                    CellTrackerState.MarkDirty(rowItem, propertyName);
                    rowModified = true;
                }
                if (rowModified) BumpRowVersion(grid, rowItem); // 使用合并后的方法
            }
            UndoManager.Push(grid, batchHistory);
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

                    string oldValue = UndoManager.GetPropertyValue(rowItem, column.Binding);
                    batchHistory.Add(new CellHistory { RowItem = rowItem, PropertyName = propertyName, OldValue = oldValue });

                    UndoManager.SetPropertyValueByString(rowItem, propertyName, sourceValue);
                    CellTrackerState.MarkDirty(rowItem, propertyName);
                    modifiedRows.Add(rowItem);
                }
            }

            foreach (var rowItem in modifiedRows) BumpRowVersion(grid, rowItem);
            UndoManager.Push(grid, batchHistory);
        }
        #endregion




    }
}