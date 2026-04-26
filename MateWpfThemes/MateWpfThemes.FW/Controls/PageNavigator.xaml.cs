using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.Models;

namespace MateWpfThemes.FW.Controls
{
    public partial class PageNavigator : UserControl
    {
        public PageNavigator()
        {
            InitializeComponent();
        }

        #region Dependency Properties

        public static readonly DependencyProperty TotalCountProperty =
            DependencyProperty.Register("TotalCount", typeof(int), typeof(PageNavigator), new PropertyMetadata(0, OnPaginationChanged));
        public int TotalCount
        {
            get => (int)GetValue(TotalCountProperty);
            set => SetValue(TotalCountProperty, value);
        }

        public static readonly DependencyProperty PageIndexProperty =
            DependencyProperty.Register("PageIndex", typeof(int), typeof(PageNavigator), new PropertyMetadata(1, OnPaginationChanged));
        public int PageIndex
        {
            get => (int)GetValue(PageIndexProperty);
            set => SetValue(PageIndexProperty, value);
        }

        public static readonly DependencyProperty PageSizeProperty =
            DependencyProperty.Register("PageSize", typeof(int), typeof(PageNavigator), new PropertyMetadata(50, OnPaginationChanged));
        public int PageSize
        {
            get => (int)GetValue(PageSizeProperty);
            set => SetValue(PageSizeProperty, value);
        }

        public static readonly DependencyProperty PageSizeOptionsProperty =
            DependencyProperty.Register("PageSizeOptions", typeof(IEnumerable<int>), typeof(PageNavigator), new PropertyMetadata(new[] { 10, 20, 50, 100, 200 }));
        public IEnumerable<int> PageSizeOptions
        {
            get => (IEnumerable<int>)GetValue(PageSizeOptionsProperty);
            set => SetValue(PageSizeOptionsProperty, value);
        }

        public static readonly DependencyProperty PageChangeCommandProperty =
            DependencyProperty.Register("PageChangeCommand", typeof(ICommand), typeof(PageNavigator), new PropertyMetadata(null));
        public ICommand PageChangeCommand
        {
            get => (ICommand)GetValue(PageChangeCommandProperty);
            set => SetValue(PageChangeCommandProperty, value);
        }

        #endregion

        #region Internal Properties

        public static readonly DependencyProperty PageItemsProperty =
            DependencyProperty.Register("PageItems", typeof(ObservableCollection<PageItem>), typeof(PageNavigator), new PropertyMetadata(new ObservableCollection<PageItem>()));
        public ObservableCollection<PageItem> PageItems
        {
            get => (ObservableCollection<PageItem>)GetValue(PageItemsProperty);
            private set => SetValue(PageItemsProperty, value);
        }

        #endregion

        private static void OnPaginationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PageNavigator control)
            {
                control.CboPageSize.SelectionChanged -= control.CboPageSize_SelectionChanged;
                control.CboPageSize.SelectedItem = control.PageSize;
                control.CboPageSize.SelectionChanged += control.CboPageSize_SelectionChanged;

                control.UpdatePageItems();
            }
        }
        /// <summary>
        /// 核心：计算页码分布 (固定 9 个槽位显示)
        /// </summary>
        private void UpdatePageItems()
        {
            int total = PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
            int current = PageIndex;

            var items = new ObservableCollection<PageItem>();
            if (total <= 0)
            {
                PageItems = items;
                return;
            }

            // 规则：
            // 1. 如果总页数 <= 9，直接显示全部。
            // 2. 如果总页数 > 9，固定显示 9 个槽位（含省略号）。

            if (total <= 9)
            {
                for (int i = 1; i <= total; i++) items.Add(new PageItem(i, current));
            }
            else
            {
                // 槽位计算
                int start, end;

                if (current <= 5)
                {
                    // 在首页附近: 1 2 3 4 5 6 7 ... total (9个槽位)
                    start = 1;
                    end = 7;
                    for (int i = start; i <= end; i++) items.Add(new PageItem(i, current));
                    items.Add(new PageItem(-1, current)); // 第8位
                    items.Add(new PageItem(total, current)); // 第9位
                }
                else if (current >= total - 4)
                {
                    // 在末页附近: 1 ... total-6 total-5 total-4 total-3 total-2 total-1 total (9个槽位)
                    items.Add(new PageItem(1, current)); // 第1位
                    items.Add(new PageItem(-1, current)); // 第2位
                    start = total - 6;
                    end = total;
                    for (int i = start; i <= end; i++) items.Add(new PageItem(i, current));
                }
                else
                {
                    // 在中间部分: 1 ... 48 49 50 51 52 ... total (9个槽位)
                    items.Add(new PageItem(1, current)); // 第1位
                    items.Add(new PageItem(-1, current)); // 第2位

                    start = current - 2;
                    end = current + 2;
                    for (int i = start; i <= end; i++) items.Add(new PageItem(i, current));

                    items.Add(new PageItem(-1, current)); // 第8位
                    items.Add(new PageItem(total, current)); // 第9位
                }
            }

            PageItems = items;
        }

        private void RequestPageChange(int newIndex, int newSize)
        {
            if (newIndex == PageIndex && newSize == PageSize) return;

            var request = new PageChangeRequest
            {
                OldIndex = PageIndex,
                NewIndex = newIndex,
                OldSize = PageSize,
                NewSize = newSize
            };

            if (PageChangeCommand?.CanExecute(request) == true)
            {
                PageChangeCommand.Execute(request);
            }
            else
            {
                CboPageSize.SelectionChanged -= CboPageSize_SelectionChanged;
                CboPageSize.SelectedItem = PageSize;
                CboPageSize.SelectionChanged += CboPageSize_SelectionChanged;
            }
        }

        #region UI Event Handlers
        private void BtnPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PageItem item && item.PageIndex != -1)
            {
                RequestPageChange(item.PageIndex, PageSize);
            }
        }

        private void CboPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboPageSize.SelectedItem is int newSize)
            {
                RequestPageChange(1, newSize);
            }
        }
        #endregion
    }

    public class PageItem
    {
        public int PageIndex { get; }
        public bool IsCurrent { get; }
        public bool IsClickable => PageIndex != -1 && !IsCurrent;
        public string DisplayText => PageIndex == -1 ? "..." : PageIndex.ToString();

        public PageItem(int pageIndex, int currentIndex)
        {
            PageIndex = pageIndex;
            IsCurrent = pageIndex == currentIndex;
        }
    }
}