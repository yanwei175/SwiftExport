using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SwiftExport.UI.Controls.Expand
{
    //滚动到新增项并选中行为
    public static class DataGridScrollBehavior
    {
        public static readonly DependencyProperty ScrollOnNewItemProperty =
            DependencyProperty.RegisterAttached(
                "ScrollOnNewItem",
                typeof(bool),
                typeof(DataGridScrollBehavior),
                new UIPropertyMetadata(false, OnScrollOnNewItemChanged));

        // ★ 新增：私有附加属性，用于存储每个 DataGrid 独立的防抖状态
        private static readonly DependencyProperty GridStateProperty =
            DependencyProperty.RegisterAttached(
                "GridState",
                typeof(ScrollState),
                typeof(DataGridScrollBehavior),
                new PropertyMetadata(null));

        public static bool GetScrollOnNewItem(DependencyObject obj) => (bool)obj.GetValue(ScrollOnNewItemProperty);
        public static void SetScrollOnNewItem(DependencyObject obj, bool value) => obj.SetValue(ScrollOnNewItemProperty, value);

        // ★ 新增：状态机类，用于记录新增的数量和定时器
        private class ScrollState
        {
            public int AddCount { get; set; }
            public object LastAddedItem { get; set; }
            public DispatcherTimer Timer { get; set; }
        }

        private static void OnScrollOnNewItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid && (bool)e.NewValue)
            {
                DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));
                descriptor.AddValueChanged(grid, OnItemsSourceChanged);

                HookCollectionChanged(grid.ItemsSource as INotifyCollectionChanged, grid);
            }
        }

        private static void OnItemsSourceChanged(object sender, EventArgs e)
        {
            if (sender is DataGrid grid)
            {
                HookCollectionChanged(grid.ItemsSource as INotifyCollectionChanged, grid);
            }
        }

        private static void HookCollectionChanged(INotifyCollectionChanged collection, DataGrid grid)
        {
            if (collection == null) return;

            // 1. 初始化 DataGrid 的专属状态机
            var state = (ScrollState)grid.GetValue(GridStateProperty);
            if (state == null)
            {
                state = new ScrollState();
                // 设定 50 毫秒的防抖窗口期
                state.Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                state.Timer.Tick += (s, args) =>
                {
                    state.Timer.Stop();

                    // ★ 核心魔法：只有当防抖期内仅仅新增了 1 条数据时，才认为是用户点击了“新建”！
                    if (state.AddCount == 1 && state.LastAddedItem != null)
                    {
                        PerformScrollAndSelect(grid, state.LastAddedItem);
                    }

                    // 结算完毕，重置计数器
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
                    state.LastAddedItem = args.NewItems[0];

                    // 重置定时器（只要还在循环 Add，定时器就不会触发）
                    state.Timer.Stop();
                    state.Timer.Start();
                }
            }
        }

        // 把原来滚动的代码抽离出来
        private static void PerformScrollAndSelect(DataGrid grid, object newItem)
        {
            grid.Dispatcher.InvokeAsync(() =>
            {
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
                else
                {
                    grid.SelectedItem = newItem;
                }

                grid.Focus();

            }, DispatcherPriority.Background);
        }
    }




}