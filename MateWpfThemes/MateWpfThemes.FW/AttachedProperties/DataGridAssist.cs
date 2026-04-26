using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Collections.Generic;
using MateWpfThemes.FW.Converters;

namespace MateWpfThemes.FW.AttachedProperties
{
    public static class DataGridAssist
    {
        // 默认单元格内边距（用于统一表格视觉密度）
        private static readonly Thickness DefaultCellPaddingThickness = new Thickness(16, 8, 16, 8);
        // 默认表头内边距
        private static readonly Thickness DefaultColumnHeaderPadding = new Thickness(16, 10, 16, 10);
        // 默认圆角
        private static readonly CornerRadius DefaultCornerRadius = new CornerRadius(4);
        // ==========================================
        // 1. EmptyText: 表格无数据时显示的占位提示语
        // ==========================================
        #region EmptyText
        public static readonly DependencyProperty EmptyTextProperty =
            DependencyProperty.RegisterAttached("EmptyText", typeof(string), typeof(DataGridAssist), new PropertyMetadata(string.Empty));

        public static void SetEmptyText(DependencyObject element, string value) => element.SetValue(EmptyTextProperty, value);
        public static string GetEmptyText(DependencyObject element) => (string)element.GetValue(EmptyTextProperty);
        #endregion

        // ==========================================
        // 2. ShowRowNumber: 自动显示行号
        // ==========================================
        #region ShowRowNumber
        public static readonly DependencyProperty ShowRowNumberProperty =
            DependencyProperty.RegisterAttached("ShowRowNumber", typeof(bool), typeof(DataGridAssist), new PropertyMetadata(false, OnShowRowNumberChanged));

        public static bool GetShowRowNumber(DependencyObject obj) => (bool)obj.GetValue(ShowRowNumberProperty);
        public static void SetShowRowNumber(DependencyObject obj, bool value) => obj.SetValue(ShowRowNumberProperty, value);

        private static void OnShowRowNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 开关控制：开启时挂事件，关闭时卸事件，避免重复订阅
            if (d is DataGrid grid)
            {
                if ((bool)e.NewValue)
                {
                    grid.LoadingRow += Grid_LoadingRow;
                    grid.UnloadingRow += Grid_UnloadingRow;
                }
                else
                {
                    grid.LoadingRow -= Grid_LoadingRow;
                    grid.UnloadingRow -= Grid_UnloadingRow;
                }
            }
        }

        private static void Grid_LoadingRow(object sender, DataGridRowEventArgs e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();

        private static void Grid_UnloadingRow(object sender, DataGridRowEventArgs e)
        {
            var grid = (DataGrid)sender;
            grid.Dispatcher.InvokeAsync(() =>
            {
                // 行被卸载后重新刷新可见行头，避免行号错位
                if (grid.Items.Count == 0) return;
                foreach (var item in grid.Items)
                {
                    if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                    {
                        row.Header = (row.GetIndex() + 1).ToString();
                    }
                }
            });
        }
        #endregion

        // ==========================================
        // 3. AutoScrollToNewItem: 滚动到新增项并选中
        // ==========================================
        #region AutoScrollToNewItem
        public static readonly DependencyProperty AutoScrollToNewItemProperty = DependencyProperty.RegisterAttached("AutoScrollToNewItem", typeof(bool), typeof(DataGridAssist), new UIPropertyMetadata(false, OnAutoScrollToNewItemChanged));

        // 私有防抖状态存储
        private static readonly DependencyProperty GridStateProperty = DependencyProperty.RegisterAttached("GridState", typeof(ScrollState), typeof(DataGridAssist), new PropertyMetadata(null));

        public static bool GetAutoScrollToNewItem(DependencyObject obj) => (bool)obj.GetValue(AutoScrollToNewItemProperty);
        public static void SetAutoScrollToNewItem(DependencyObject obj, bool value) => obj.SetValue(AutoScrollToNewItemProperty, value);

        private class ScrollState
        {
            public int AddCount { get; set; }
            public object LastAddedItem { get; set; }
            public DispatcherTimer Timer { get; set; }
        }

        private static void OnAutoScrollToNewItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 仅在开启时监听 ItemsSource 与集合变化
            if (d is DataGrid grid && (bool)e.NewValue)
            {
                DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));
                descriptor.AddValueChanged(grid, OnItemsSourceChanged);
                HookCollectionChanged(grid.ItemsSource as INotifyCollectionChanged, grid);
            }
        }

        private static void OnItemsSourceChanged(object sender, EventArgs e)
        {
            if (sender is DataGrid grid) HookCollectionChanged(grid.ItemsSource as INotifyCollectionChanged, grid);
        }

        private static void HookCollectionChanged(INotifyCollectionChanged collection, DataGrid grid)
        {
            // 仅对支持集合通知的数据源生效
            if (collection == null) return;

            var state = (ScrollState)grid.GetValue(GridStateProperty);
            if (state == null)
            {
                state = new ScrollState();
                // 设定 50 毫秒的防抖窗口期
                state.Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                state.Timer.Tick += (s, args) =>
                {
                    state.Timer.Stop();

                    // 防抖窗口内只新增 1 条时，视为用户单次“新建”，自动滚动并选中
                    if (state.AddCount == 1 && state.LastAddedItem != null) PerformScrollAndSelect(grid, state.LastAddedItem);
                    // 结算后重置状态
                    state.AddCount = 0;
                    state.LastAddedItem = null;
                };
                grid.SetValue(GridStateProperty, state);
            }

            collection.CollectionChanged -= Collection_CollectionChanged;
            collection.CollectionChanged += Collection_CollectionChanged;

            void Collection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
            {
                if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null && args.NewItems.Count > 0)
                {
                    // 每次有数据 Add 进来，计数器 +1
                    state.AddCount++;
                    // 这里要保存“新增的那一项”，不能保存 NewItems 集合本身
                    // 否则后续 grid.Items.Contains(newItem) 会始终失败，导致无法滚动定位
                    state.LastAddedItem = args.NewItems[args.NewItems.Count - 1];
                    // 重置定时器（只要还在循环 Add，定时器就不会触发）
                    state.Timer.Stop();
                    state.Timer.Start();
                }
            }
        }

        private static void PerformScrollAndSelect(DataGrid grid, object newItem)
        {
            // 延迟到 UI 空闲阶段执行，避免容器尚未生成导致定位失败
            grid.Dispatcher.InvokeAsync(() =>
            {
                if (newItem == null || !grid.Items.Contains(newItem)) return;

                // 滚动到新增项并设置当前焦点/选中
                grid.ScrollIntoView(newItem);
                if (grid.SelectionUnit == DataGridSelectionUnit.Cell || grid.SelectionUnit == DataGridSelectionUnit.CellOrRowHeader)
                {
                    if (grid.Columns.Count > 0)
                    {
                        var cellInfo = new DataGridCellInfo(newItem, grid.Columns[0]);
                        grid.CurrentCell = cellInfo;
                        grid.SelectedCells.Clear();
                        grid.SelectedCells.Add(cellInfo);
                    }
                }
                else grid.SelectedItem = newItem;
                grid.Focus();
            }, DispatcherPriority.Background);
        }
        #endregion

        // ==========================================
        // 4. UseDefaultStyles: 智能注入默认行列样式
        // ==========================================
        #region UseDefaultStyles

        //public static readonly DependencyProperty UseDefaultStylesProperty =
        //    DependencyProperty.RegisterAttached("UseDefaultStyles", typeof(bool), typeof(DataGridAssist), new PropertyMetadata(false, OnUseDefaultStylesChanged));

        //public static bool GetUseDefaultStyles(DependencyObject obj) => (bool)obj.GetValue(UseDefaultStylesProperty);
        //public static void SetUseDefaultStyles(DependencyObject obj, bool value) => obj.SetValue(UseDefaultStylesProperty, value);

        //private static void OnUseDefaultStylesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        //{
        //    if (d is DataGrid grid && (bool)e.NewValue)
        //    {
        //        grid.Loaded -= StyleInjector_Grid_Loaded; // 修复了冲突名
        //        grid.Loaded += StyleInjector_Grid_Loaded;
        //    }
        //}

        //private static void StyleInjector_Grid_Loaded(object sender, RoutedEventArgs e)
        //{
        //    var grid = (DataGrid)sender;
        //    var normalTextStyle = grid.TryFindResource("DataGridTextBlockStyle") as Style;
        //    var normalEditStyle = grid.TryFindResource("DataGridTextBoxStyle") as Style;

        //    if (normalTextStyle == null || normalEditStyle == null) return;

        //    foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
        //    {
        //        var editStyleSource = DependencyPropertyHelper.GetValueSource(column, DataGridTextColumn.EditingElementStyleProperty);
        //        var textStyleSource = DependencyPropertyHelper.GetValueSource(column, DataGridTextColumn.ElementStyleProperty);

        //        if (textStyleSource.BaseValueSource == BaseValueSource.Default) column.ElementStyle = normalTextStyle;
        //        if (editStyleSource.BaseValueSource == BaseValueSource.Default) column.EditingElementStyle = normalEditStyle;
        //    }
        //}

        #endregion

        // ==========================================
        // 5. EnableSoftValidation: 注入软校验红框
        // ==========================================
        #region EnableSoftValidation
        public static readonly DependencyProperty EnableSoftValidationProperty =
            DependencyProperty.RegisterAttached("EnableSoftValidation", typeof(bool), typeof(DataGridAssist), new PropertyMetadata(false, OnEnableSoftValidationChanged));

        public static void SetEnableSoftValidation(DependencyObject element, bool value) => element.SetValue(EnableSoftValidationProperty, value);
        public static bool GetEnableSoftValidation(DependencyObject element) => (bool)element.GetValue(EnableSoftValidationProperty);

        // 中文注释：列级样式只注入一次，避免 Loaded 多次触发后样式链不断叠加，导致验证视觉状态异常。
        private static readonly DependencyProperty SoftValidationAppliedProperty =
            DependencyProperty.RegisterAttached("SoftValidationApplied", typeof(bool), typeof(DataGridAssist), new PropertyMetadata(false));

        private static bool GetSoftValidationApplied(DependencyObject element) => (bool)element.GetValue(SoftValidationAppliedProperty);
        private static void SetSoftValidationApplied(DependencyObject element, bool value) => element.SetValue(SoftValidationAppliedProperty, value);

        private static void OnEnableSoftValidationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 软校验在 Loaded 后注入列级 CellStyle
            if (d is DataGrid grid)
            {
                grid.Loaded -= SoftValidation_Grid_Loaded;
                if ((bool)e.NewValue)
                {
                    grid.Loaded += SoftValidation_Grid_Loaded;
                }
            }
        }

        private static void SoftValidation_Grid_Loaded(object sender, RoutedEventArgs e)
        {
            var grid = (DataGrid)sender;
            var errorBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
            var converter = new ErrorToBoolConverter();

            // 提取基础单元格样式，确保不破坏现有主题风格
            var baseCellStyle = grid.CellStyle ?? grid.TryFindResource(typeof(DataGridCell)) as Style;

            foreach (var col in grid.Columns.OfType<DataGridBoundColumn>())
            {
                // 中文注释：避免重复注入导致同一列叠加多个触发器，进而出现红框不消失等视觉异常。
                if (GetSoftValidationApplied(col))
                    continue;

                if (col.Binding is Binding binding && !string.IsNullOrEmpty(binding.Path.Path))
                {
                    var propName = binding.Path.Path;

                    // 基于当前的列样式或者是 DataGrid 顶级的单元格样式创建
                    var style = new Style(typeof(DataGridCell), col.CellStyle ?? baseCellStyle);

                    // 中文注释：企业级非阻断验证方案 —— 绕过 WPF 原生 ValidationRule 阻断，直接监听 IDataErrorInfo 属性！
                    var trigger = new DataTrigger
                    {
                        Binding = new Binding($"[{propName}]") { Converter = converter },
                        Value = true
                    };

                    // 中文注释：按用户交互规范将“验证态”和“选中态”解耦。
                    // 这里只保留红色边框与提示，不再改背景色；背景色由 DataGridCell 的 IsSelected 触发器统一控制。
                    trigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Red));
                    trigger.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    trigger.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding($"[{propName}]")));

                    style.Triggers.Add(trigger);
                    col.CellStyle = style;
                    SetSoftValidationApplied(col, true);
                }
            }
        }
        #endregion

        // ==========================================
        // 6. EnableEditBoxAssist: 移植 MD 框架的一键交互/一键编辑
        // ==========================================
        #region EnableEditBoxAssist
        public static readonly DependencyProperty EnableEditBoxAssistProperty =
            DependencyProperty.RegisterAttached("EnableEditBoxAssist", typeof(bool), typeof(DataGridAssist),
                new PropertyMetadata(false, OnEnableEditBoxAssistChanged));

        public static bool GetEnableEditBoxAssist(DataGrid element) => (bool)element.GetValue(EnableEditBoxAssistProperty);
        public static void SetEnableEditBoxAssist(DataGrid element, bool value) => element.SetValue(EnableEditBoxAssistProperty, value);

        private static void OnEnableEditBoxAssistChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 开启“单击即编辑”交互：挂接鼠标与键盘事件
            if (d is DataGrid dataGrid)
            {
                if ((bool)e.NewValue)
                {
                    dataGrid.AddHandler(UIElement.MouseLeftButtonDownEvent, (RoutedEventHandler)OnMouseLeftButtonDown, true);
                    dataGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;
                }
                else
                {
                    dataGrid.RemoveHandler(UIElement.MouseLeftButtonDownEvent, (RoutedEventHandler)OnMouseLeftButtonDown);
                    dataGrid.PreviewKeyDown -= DataGrid_PreviewKeyDown;
                }
            }
        }

        private static void OnMouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            // 鼠标左键按下时，尝试将点击单元格直接切入编辑态
            if (e is MouseButtonEventArgs mouseArgs && sender is DataGrid dataGrid)
            {
                AllowDirectEditWithoutFocus(dataGrid, mouseArgs);
            }
        }

        private static void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 空格快捷进入编辑：仅支持 ComboBox 和 CheckBox 列，不支持 TextBox 列
            if (e.Key == Key.Space && sender is DataGrid dataGrid && e.OriginalSource is DataGridCell cell && !cell.IsReadOnly)
            {
                if (cell.Column is DataGridComboBoxColumn || cell.Column is DataGridCheckBoxColumn)
                {
                    dataGrid.BeginEdit();
                    e.Handled = true;
                }
            }
        }

        private static void AllowDirectEditWithoutFocus(DataGrid dataGrid, MouseButtonEventArgs mouseArgs)
        {
            // 命中源不是可视对象则直接返回
            if (!(mouseArgs.OriginalSource is DependencyObject originalSource)) return;

            // 由命中元素向上查找所属 DataGridCell
            var dataGridCell = originalSource
                            .GetVisualAncestry()
                            .OfType<DataGridCell>()
                            .FirstOrDefault();

            // 空单元格 / 只读 / 正在编辑时不处理
            if (dataGridCell == null || dataGridCell.IsReadOnly || dataGridCell.IsEditing)
                return;

            // 模板列交还控件自身处理，避免误拦截
            if (dataGridCell.Column is DataGridTemplateColumn)
                return;

            // 防止嵌套 DataGrid 误触发
            var parentDataGrid = dataGridCell
                .GetVisualAncestry()
                .OfType<DataGrid>()
                .FirstOrDefault();
            if (parentDataGrid != dataGrid)
                return;

            // 不支持文本列（DataGridTextColumn）：TextBox 直接进入编辑态会导致光标定位混乱
            // TextBox 应由用户双击或按 F2 键进入编辑状态，以保留原生焦点和光标定位行为
            if (dataGridCell.Column is System.Windows.Controls.DataGridTextColumn)
                return;

            if (dataGridCell.Content is UIElement element)
            {
                // 设置当前单元格并进入编辑
                dataGrid.CurrentCell = new DataGridCellInfo(dataGridCell);
                dataGrid.BeginEdit();

                switch (dataGridCell.Content)
                {
                    case ToggleButton toggleButton:
                        // CheckBox 单击：仅当点击命中 ToggleButton 可视区域时才切换选中状态
                        var mousePosition = mouseArgs.GetPosition(element);
                        var elementHitBox = new Rect(element.RenderSize);
                        if (elementHitBox.Contains(mousePosition))
                        {
                            var newMouseEvent = new MouseButtonEventArgs(mouseArgs.MouseDevice, mouseArgs.Timestamp, mouseArgs.ChangedButton)
                            {
                                RoutedEvent = Mouse.MouseDownEvent,
                                Source = dataGrid
                            };
                            toggleButton.RaiseEvent(newMouseEvent);
                        }
                        break;

                    case ComboBox comboBox:
                        // ComboBox 单击：下拉列直接展开，提高一次点击可操作性
                        comboBox.IsDropDownOpen = true;
                        break;
                }
            }
        }

        #endregion

        // ==========================================
        // 7. AutoGeneratedCheckBoxStyle: 自动生成列时注入 CheckBox 展示样式
        // ==========================================
        #region AttachedProperty : AutoGeneratedCheckboxStyleProperty
        public static readonly DependencyProperty AutoGeneratedCheckBoxStyleProperty
            = DependencyProperty.RegisterAttached("AutoGeneratedCheckBoxStyle", typeof(Style), typeof(DataGridAssist),
                new PropertyMetadata(default(Style), AutoGeneratedCheckBoxStylePropertyChangedCallback));

        public static Style GetAutoGeneratedCheckBoxStyle(DataGrid element)
            => (Style)element.GetValue(AutoGeneratedCheckBoxStyleProperty);
        public static void SetAutoGeneratedCheckBoxStyle(DataGrid element, Style value)
            => element.SetValue(AutoGeneratedCheckBoxStyleProperty, value);

        private static void AutoGeneratedCheckBoxStylePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = (DataGrid)d;

            dataGrid.AutoGeneratingColumn -= SetGeneratedCheckboxColumnStyle;
            dataGrid.AutoGeneratingColumn += SetGeneratedCheckboxColumnStyle;

            foreach (var column in dataGrid.Columns.OfType<DataGridCheckBoxColumn>())
            {
                ApplyMateThemeColumnStyleForColumn(dataGrid, column);
            }
        }

        // 复选框列自动生成时套用展示样式
        private static void SetGeneratedCheckboxColumnStyle(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is DataGridCheckBoxColumn column &&
                sender is DataGrid dataGrid)
            {
                column.ElementStyle = GetAutoGeneratedCheckBoxStyle(dataGrid);
            }
        }
        #endregion

        // ==========================================
        // 8. AutoGeneratedEditingCheckBoxStyle: 自动生成列时注入 CheckBox 编辑样式
        // ==========================================
        #region AttachedProperty : AutoGeneratedEditingCheckBoxStyleProperty
        public static readonly DependencyProperty AutoGeneratedEditingCheckBoxStyleProperty
            = DependencyProperty.RegisterAttached("AutoGeneratedEditingCheckBoxStyle", typeof(Style), typeof(DataGridAssist),
                new PropertyMetadata(default(Style), AutoGeneratedEditingCheckBoxStylePropertyChangedCallback));

        public static Style GetAutoGeneratedEditingCheckBoxStyle(DataGrid element)
            => (Style)element.GetValue(AutoGeneratedEditingCheckBoxStyleProperty);
        public static void SetAutoGeneratedEditingCheckBoxStyle(DataGrid element, Style value)
            => element.SetValue(AutoGeneratedEditingCheckBoxStyleProperty, value);

        private static void AutoGeneratedEditingCheckBoxStylePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = (DataGrid)d;

            dataGrid.AutoGeneratingColumn -= SetGeneratedCheckBoxEditingStyle;
            dataGrid.AutoGeneratingColumn += SetGeneratedCheckBoxEditingStyle;

            foreach (var column in dataGrid.Columns.OfType<DataGridCheckBoxColumn>())
            {
                ApplyMateThemeColumnStyleForColumn(dataGrid, column);
            }
        }

        // 复选框列自动生成时套用编辑样式
        private static void SetGeneratedCheckBoxEditingStyle(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is DataGridCheckBoxColumn column &&
                sender is DataGrid dataGrid)
            {
                column.EditingElementStyle = GetAutoGeneratedEditingCheckBoxStyle(dataGrid);
            }
        }
        #endregion

        // ==========================================
        // 9. AutoGeneratedTextStyle: 自动生成列时注入 Text 展示样式
        // ==========================================
        #region AttachedProperty : AutoGeneratedTextStyleProperty
        public static readonly DependencyProperty AutoGeneratedTextStyleProperty
            = DependencyProperty.RegisterAttached("AutoGeneratedTextStyle", typeof(Style), typeof(DataGridAssist),
                new PropertyMetadata(default(Style), AutoGeneratedTextStylePropertyChangedCallback));

        public static Style GetAutoGeneratedTextStyle(DataGrid element)
            => (Style)element.GetValue(AutoGeneratedTextStyleProperty);
        public static void SetAutoGeneratedTextStyle(DataGrid element, Style value)
            => element.SetValue(AutoGeneratedTextStyleProperty, value);

        private static void AutoGeneratedTextStylePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = (DataGrid)d;

            dataGrid.AutoGeneratingColumn -= SetGeneratedTextColumnStyle;
            dataGrid.AutoGeneratingColumn += SetGeneratedTextColumnStyle;

            foreach (var column in dataGrid.Columns.OfType<DataGridTextColumn>())
            {
                ApplyMateThemeColumnStyleForColumn(dataGrid, column);
            }
        }

        // 文本列自动生成时套用展示样式
        private static void SetGeneratedTextColumnStyle(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is System.Windows.Controls.DataGridTextColumn column &&
                sender is DataGrid dataGrid)
            {
                column.ElementStyle = GetAutoGeneratedTextStyle(dataGrid);
            }
        }
        #endregion

        // ==========================================
        // 10. AutoGeneratedEditingTextStyle: 自动生成列时注入 Text 编辑样式
        // ==========================================
        #region AttachedProperty : AutoGeneratedEditingTextStyleProperty
        public static readonly DependencyProperty AutoGeneratedEditingTextStyleProperty
            = DependencyProperty.RegisterAttached("AutoGeneratedEditingTextStyle", typeof(Style), typeof(DataGridAssist),
                new PropertyMetadata(default(Style), AutoGeneratedEditingTextStylePropertyChangedCallback));

        public static Style GetAutoGeneratedEditingTextStyle(DataGrid element)
            => (Style)element.GetValue(AutoGeneratedEditingTextStyleProperty);
        public static void SetAutoGeneratedEditingTextStyle(DataGrid element, Style value)
            => element.SetValue(AutoGeneratedEditingTextStyleProperty, value);

        private static void AutoGeneratedEditingTextStylePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = (DataGrid)d;

            dataGrid.AutoGeneratingColumn -= SetGeneratedTextColumnEditingStyle;
            dataGrid.AutoGeneratingColumn += SetGeneratedTextColumnEditingStyle;

            foreach (var column in dataGrid.Columns.OfType<DataGridTextColumn>())
            {
                ApplyMateThemeColumnStyleForColumn(dataGrid, column);
            }
        }

        // 文本列自动生成时套用编辑样式
        private static void SetGeneratedTextColumnEditingStyle(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is System.Windows.Controls.DataGridTextColumn column &&
                sender is DataGrid dataGrid)
            {
                column.EditingElementStyle = GetAutoGeneratedEditingTextStyle(dataGrid);
            }
        }
        #endregion

        // ==========================================
        // 11. AutoGeneratedComboBoxStyle: 自动生成列时注入 ComboBox 展示样式
        // ==========================================
        #region AttachedProperty : AutoGeneratedComboBoxStyleProperty
        public static readonly DependencyProperty AutoGeneratedComboBoxStyleProperty
            = DependencyProperty.RegisterAttached("AutoGeneratedComboBoxStyle", typeof(Style), typeof(DataGridAssist),
                new PropertyMetadata(default(Style), AutoGeneratedComboBoxStylePropertyChangedCallback));

        public static Style GetAutoGeneratedComboBoxStyle(DataGrid element)
            => (Style)element.GetValue(AutoGeneratedComboBoxStyleProperty);
        public static void SetAutoGeneratedComboBoxStyle(DataGrid element, Style value)
            => element.SetValue(AutoGeneratedComboBoxStyleProperty, value);

        private static void AutoGeneratedComboBoxStylePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = (DataGrid)d;

            dataGrid.AutoGeneratingColumn -= SetGeneratedComboBoxColumnStyle;
            dataGrid.AutoGeneratingColumn += SetGeneratedComboBoxColumnStyle;

            foreach (var column in dataGrid.Columns.OfType<DataGridComboBoxColumn>())
            {
                    ApplyMateThemeColumnStyleForColumn(dataGrid, column);
            }
        }

        // 下拉列自动生成时套用展示样式
        private static void SetGeneratedComboBoxColumnStyle(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is System.Windows.Controls.DataGridComboBoxColumn column &&
                sender is DataGrid dataGrid)
            {
                column.ElementStyle = GetAutoGeneratedComboBoxStyle(dataGrid);
            }
        }
        #endregion

        // ==========================================
        // 12. AutoGeneratedEditingComboBoxStyle: 自动生成列时注入 ComboBox 编辑样式
        // ==========================================
        #region AttachedProperty : AutoGeneratedEditingComboBoxStyleProperty
        public static readonly DependencyProperty AutoGeneratedEditingComboBoxStyleProperty
            = DependencyProperty.RegisterAttached("AutoGeneratedEditingComboBoxStyle", typeof(Style), typeof(DataGridAssist),
                new PropertyMetadata(default(Style), AutoGeneratedEditingComboBoxStylePropertyChangedCallback));

        public static Style GetAutoGeneratedEditingComboBoxStyle(DataGrid element)
            => (Style)element.GetValue(AutoGeneratedEditingComboBoxStyleProperty);
        public static void SetAutoGeneratedEditingComboBoxStyle(DataGrid element, Style value)
            => element.SetValue(AutoGeneratedEditingComboBoxStyleProperty, value);

        private static void AutoGeneratedEditingComboBoxStylePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = (DataGrid)d;

            dataGrid.AutoGeneratingColumn -= SetGeneratedComboBoxColumnEditingStyle;
            dataGrid.AutoGeneratingColumn += SetGeneratedComboBoxColumnEditingStyle;

            foreach (var column in dataGrid.Columns.OfType<DataGridComboBoxColumn>())
            {
                ApplyMateThemeColumnStyleForColumn(dataGrid, column);
            }
        }

        // 下拉列自动生成时套用编辑样式
        private static void SetGeneratedComboBoxColumnEditingStyle(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is System.Windows.Controls.DataGridComboBoxColumn column &&
                sender is DataGrid dataGrid)
            {
                column.EditingElementStyle = GetAutoGeneratedEditingComboBoxStyle(dataGrid);
            }
        }
        #endregion

        // ==========================================
        // 13. ApplyMateThemeColumnStyles: 对已存在/后续新增列统一应用列样式
        // ==========================================
        #region AttachedProperty : ApplyMateThemeColumnStylesProperty
        public static readonly DependencyProperty ApplyMateThemeColumnStylesProperty
            = DependencyProperty.RegisterAttached("ApplyMateThemeColumnStyles", typeof(bool), typeof(DataGridAssist),
                new PropertyMetadata(default(bool), ApplyMateThemeColumnStylesPropertyChangedCallback));

        public static void SetApplyMateThemeColumnStyles(DataGrid element, bool value)
            => element.SetValue(ApplyMateThemeColumnStylesProperty, value);

        public static bool GetApplyMateThemeColumnStyles(DataGrid element)
            => (bool)element.GetValue(ApplyMateThemeColumnStylesProperty);

        private static void ApplyMateThemeColumnStylesPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = (DataGrid)d;
            dataGrid.Columns.CollectionChanged -= ColumnsCollectionChanged;
            if (Equals(true, e.NewValue))
            {
                // 自动列晚于初始化时被加入，因此监听 Columns 变化持续应用样式
                dataGrid.Columns.CollectionChanged += ColumnsCollectionChanged;
                foreach (var column in dataGrid.Columns)
                {
                    ApplyMateThemeColumnStyleForColumn(dataGrid, column);
                }
            }

            void ColumnsCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
            {
                foreach (DataGridColumn column in args.NewItems?.OfType<DataGridColumn>() ?? Enumerable.Empty<DataGridColumn>())
                {
                    ApplyMateThemeColumnStyleForColumn(dataGrid, column);
                }
            }
        }

        private static void ApplyMateThemeColumnStyleForColumn(DataGrid dataGrid, DataGridColumn column)
        {
            // 只在“列仍为默认样式”时才注入，避免覆盖业务侧手工设置
            Style defaultElementStyle = (Style)DataGridBoundColumn.ElementStyleProperty.GetMetadata(column.GetType()).DefaultValue;
            Style defaultEditingElementStyle = (Style)DataGridBoundColumn.EditingElementStyleProperty.GetMetadata(column.GetType()).DefaultValue;

            bool applyElementStyle;
            bool applyEditingElementStyle;
            switch (column)
            {
                case DataGridCheckBoxColumn checkBoxColumn:
                    applyElementStyle = Equals(checkBoxColumn.ElementStyle, defaultElementStyle);
                    applyEditingElementStyle = Equals(checkBoxColumn.EditingElementStyle, defaultEditingElementStyle);

                    var checkBoxElementStyle = GetAutoGeneratedCheckBoxStyle(dataGrid);
                    if (applyElementStyle && checkBoxElementStyle != null)
                    {
                        checkBoxColumn.ElementStyle = checkBoxElementStyle;
                    }

                    var checkBoxEditingElementStyle = GetAutoGeneratedEditingCheckBoxStyle(dataGrid);
                    if (applyEditingElementStyle && checkBoxEditingElementStyle != null)
                    {
                        checkBoxColumn.EditingElementStyle = checkBoxEditingElementStyle;
                    }
                    break;
                case System.Windows.Controls.DataGridTextColumn textColumn:
                    applyElementStyle = Equals(textColumn.ElementStyle, defaultElementStyle);
                    applyEditingElementStyle = Equals(textColumn.EditingElementStyle, defaultEditingElementStyle);

                    var textElementStyle = GetAutoGeneratedTextStyle(dataGrid);
                    if (applyElementStyle && textElementStyle != null)
                    {
                        textColumn.ElementStyle = textElementStyle;
                    }

                    var textEditingElementStyle = GetAutoGeneratedEditingTextStyle(dataGrid);
                    if (applyEditingElementStyle && textEditingElementStyle != null)
                    {
                        textColumn.EditingElementStyle = textEditingElementStyle;
                    }
                    break;
                case System.Windows.Controls.DataGridComboBoxColumn comboBoxColumn:
                    applyElementStyle = Equals(comboBoxColumn.ElementStyle, defaultElementStyle);
                    applyEditingElementStyle = Equals(comboBoxColumn.EditingElementStyle, defaultEditingElementStyle);

                    var comboBoxElementStyle = GetAutoGeneratedComboBoxStyle(dataGrid);
                    if (applyElementStyle && comboBoxElementStyle != null)
                    {
                        comboBoxColumn.ElementStyle = comboBoxElementStyle;
                    }

                    var comboBoxEditingElementStyle = GetAutoGeneratedEditingComboBoxStyle(dataGrid);
                    if (applyEditingElementStyle && comboBoxEditingElementStyle != null)
                    {
                        comboBoxColumn.EditingElementStyle = comboBoxEditingElementStyle;
                    }
                    break;
            }
        }
        #endregion

        // ==========================================
        // 14. CellPadding: 控制 DataGrid 单元格内容内边距（支持继承）
        // ==========================================
        #region AttachedProperty : CellPaddingProperty
        public static readonly DependencyProperty CellPaddingProperty
            = DependencyProperty.RegisterAttached("CellPadding", typeof(Thickness), typeof(DataGridAssist),
                new FrameworkPropertyMetadata(DefaultCellPaddingThickness, FrameworkPropertyMetadataOptions.Inherits));

        public static Thickness GetCellPadding(DataGrid element)
            => (Thickness)element.GetValue(CellPaddingProperty);
        public static void SetCellPadding(DataGrid element, Thickness value)
            => element.SetValue(CellPaddingProperty, value);
        #endregion

        // ==========================================
        // 15. SelectedCellBorderBrush: 控制选中单元格边框画刷
        // ==========================================
        #region AttachedProperty : SelectedCellBorderBrushProperty
        public static readonly DependencyProperty SelectedCellBorderBrushProperty
            = DependencyProperty.RegisterAttached("SelectedCellBorderBrush", typeof(Brush), typeof(DataGridAssist),
                new PropertyMetadata(null));

        public static Brush GetSelectedCellBorderBrush(DataGrid element)
            => (Brush)element.GetValue(SelectedCellBorderBrushProperty);
        public static void SetSelectedCellBorderBrush(DataGrid element, Brush value)
            => element.SetValue(SelectedCellBorderBrushProperty, value);
        #endregion

        // ==========================================
        // 16. ColumnHeaderPadding: 控制列表头内边距（支持继承）
        // ==========================================
        #region AttachedProperty : ColumnHeaderPaddingProperty
        public static readonly DependencyProperty ColumnHeaderPaddingProperty
            = DependencyProperty.RegisterAttached("ColumnHeaderPadding", typeof(Thickness), typeof(DataGridAssist),
                new FrameworkPropertyMetadata(DefaultColumnHeaderPadding, FrameworkPropertyMetadataOptions.Inherits));

        public static Thickness GetColumnHeaderPadding(DataGrid element)
            => (Thickness)element.GetValue(ColumnHeaderPaddingProperty);
        public static void SetColumnHeaderPadding(DependencyObject element, Thickness value)
            => element.SetValue(ColumnHeaderPaddingProperty, value);
        #endregion

        // ==========================================
        // 17. CornerRadius: 控制 DataGrid 圆角
        // ==========================================
        #region AttachedProperty : CornerRadiusProperty
        public static readonly DependencyProperty CornerRadiusProperty
            = DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(DataGridAssist),
                new PropertyMetadata(DefaultCornerRadius));

        public static CornerRadius GetCornerRadius(DataGrid element)
            => (CornerRadius)element.GetValue(CornerRadiusProperty);
        public static void SetCornerRadius(DataGrid element, CornerRadius value)
            => element.SetValue(CornerRadiusProperty, value);
        #endregion

        private static void EditOnSpacebarPress(object sender, KeyEventArgs e)
        {
            var dataGrid = (DataGrid)sender;
            var cell = e.OriginalSource as DataGridCell;
            if (e.Key == Key.Space && cell != null && !cell.IsReadOnly)
            {
                if (cell.Column is DataGridComboBoxColumn ||
                    cell.Column is System.Windows.Controls.DataGridTextColumn)
                {
                    // 空格键直接进入编辑态，提升键盘操作效率
                    dataGrid.BeginEdit();
                    e.Handled = true;
                }
            }
        }
    }
}