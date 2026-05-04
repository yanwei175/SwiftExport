using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// DataGrid 同步辅助类
    /// 用于同步主网格（MainGrid）与汇总网格（SummaryGrid）的列宽、冻结列、滚动及可见性
    /// </summary>
    public static class DataGridSyncAttach
    {
        public static readonly DependencyProperty SyncTargetProperty =
            DependencyProperty.RegisterAttached("SyncTarget", typeof(DataGrid), typeof(DataGridSyncAttach),
                new PropertyMetadata(null, OnSyncTargetChanged));

        public static void SetSyncTarget(DependencyObject element, DataGrid value) => element.SetValue(SyncTargetProperty, value);
        public static DataGrid GetSyncTarget(DependencyObject element) => (DataGrid)element.GetValue(SyncTargetProperty);

        private static void OnSyncTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid mainGrid)
            {
                if (e.OldValue is DataGrid oldTarget)
                {
                    Unsubscribe(mainGrid, oldTarget);
                }
                if (e.NewValue is DataGrid newTarget)
                {
                    Subscribe(mainGrid, newTarget);
                }
            }
        }

        private static void Subscribe(DataGrid main, DataGrid summary)
        {
            // 1. 初始化 Summary 样式
            summary.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            summary.IsHitTestVisible = false; // 禁止用户在汇总行操作滚动或点击

            // 2. 监听主表列变化
            main.Columns.CollectionChanged += (s, e) => SyncAllColumns(main, summary);

            // 3. 监听布局变化（捕获列宽拖动、窗口缩放）
            main.LayoutUpdated += (s, e) => SyncColumnWidths(main, summary);

            // 4. 监听属性变化（冻结列等）
            DependencyPropertyDescriptor.FromProperty(DataGrid.FrozenColumnCountProperty, typeof(DataGrid))
                .AddValueChanged(main, (s, e) => summary.FrozenColumnCount = main.FrozenColumnCount);

            // 5. 滚动同步
            main.Loaded += (s, e) =>
            {
                var mainScroll = GetScrollViewer(main);
                var summaryScroll = GetScrollViewer(summary);

                if (mainScroll != null && summaryScroll != null)
                {
                    mainScroll.ScrollChanged += (sender, args) =>
                    {
                        summaryScroll.ScrollToHorizontalOffset(args.HorizontalOffset);
                    };
                }

                // 初次强制同步一次
                SyncAllColumns(main, summary);
                summary.FrozenColumnCount = main.FrozenColumnCount;
            };
        }

        private static void Unsubscribe(DataGrid main, DataGrid summary)
        {
            // 实际开发中可根据需要实现解绑逻辑
        }

        private static void SyncAllColumns(DataGrid main, DataGrid summary)
        {
            if (main.Columns.Count == 0 || summary.Columns.Count == 0) return;

            // 同步 Visibility 和 DisplayIndex
            for (int i = 0; i < Math.Min(main.Columns.Count, summary.Columns.Count); i++)
            {
                var mCol = main.Columns[i];
                var sCol = summary.Columns[i];

                // 绑定 Visibility
                BindingOperations.SetBinding(sCol, DataGridColumn.VisibilityProperty, new Binding("Visibility") { Source = mCol });

                // 同步 DisplayIndex
                sCol.DisplayIndex = mCol.DisplayIndex;
            }
        }

        private static void SyncColumnWidths(DataGrid main, DataGrid summary)
        {
            if (main.Columns.Count == 0 || summary.Columns.Count == 0) return;

            for (int i = 0; i < Math.Min(main.Columns.Count, summary.Columns.Count); i++)
            {
                var mCol = main.Columns[i];
                var sCol = summary.Columns[i];

                // 使用 ActualWidth 强制同步，确保 Star 和 Auto 列也能对齐
                if (Math.Abs(sCol.Width.DisplayValue - mCol.ActualWidth) > 0.1)
                {
                    sCol.Width = new DataGridLength(mCol.ActualWidth);
                }
            }
        }

        private static ScrollViewer GetScrollViewer(DependencyObject element)
        {
            if (element == null) return null;
            if (element is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
