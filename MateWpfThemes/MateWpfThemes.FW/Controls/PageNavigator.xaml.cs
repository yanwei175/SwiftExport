using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.Models;

namespace MateWpfThemes.FW.Controls
{
    //#region 使用方式
    //==============================
    //<!-- 引入命名空间 -->
    //xmlns:controls="clr-namespace:MateWpfThemes.FW.Controls;assembly=MateWpfThemes.FW"

    //<!-- 使用组件，注意 PageIndex/PageSize 必须 Mode=OneWay -->
    //<controls:PageNavigator
    //    TotalCount = "{Binding TotalCount}"
    //    PageIndex="{Binding PageIndex, Mode=OneWay}"
    //    PageSize="{Binding PageSize, Mode=OneWay}"
    //    PageSizeOptions="{Binding MyPageSizeOptions}" />自定义每页条数列表,默认是 10, 20, 50, 100, 200，可以覆盖：
    //    PageChangeCommand="{Binding PageChangeCommand}" />

    // ================== 分页属性 ==================
    //public IEnumerable<int> MyPageSizeOptions { get; } = new[] { 20, 50, 100 }; //如果需要自定义每页条数列表，提供一个属性并绑定到 PageSizeOptions
    //private int _pageIndex = 1;
    //public int PageIndex
    //{
    //    get => _pageIndex;
    //    set
    //    {
    //        if (_pageIndex == value) return;
    //        if (SetProperty(ref _pageIndex, value))
    //            _ = LoadDataAsync(); // 页码变化 → 加载数据
    //    }
    //}

    //private int _pageSize = 50;
    //public int PageSize
    //{
    //    get => _pageSize;
    //    set
    //    {
    //        if (_pageSize == value) return;
    //        if (SetProperty(ref _pageSize, value))
    //        {
    //            // 换页大小时回第一页
    //            if (PageIndex == 1) _ = LoadDataAsync();
    //            else PageIndex = 1;
    //        }
    //    }
    //}

    //private int _totalCount;
    //public int TotalCount
    //{
    //    get => _totalCount;
    //    set => SetProperty(ref _totalCount, value);
    //}

    //// ================== 翻页命令 ==================

    //public ICommand PageChangeCommand => new RelayCommand<PageChangeRequest>(req =>
    //{
    //    // ★ 拦截点：检查是否有未保存数据
    //    if (HasChanges)
    //    {
    //        var ok = MessageBox.Show("当前有未保存修改，翻页将丢失，是否继续？",
    //                                 "提示", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    //        if (!ok) return; // 不执行 → UI 因 Mode=OneWay 自动保持原值
    //    }

    //    // 正常翻页：更新属性 → 触发 LoadDataAsync
    //    PageSize = req.NewSize;
    //    PageIndex = req.NewIndex;
    //});
    //
    //
    //
    //==============================
    //#endregion
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

        #region Internal Properties for UI Binding

        public static readonly DependencyProperty TotalPagesProperty =
            DependencyProperty.Register("TotalPages", typeof(int), typeof(PageNavigator), new PropertyMetadata(0));
        public int TotalPages
        {
            get => (int)GetValue(TotalPagesProperty);
            private set => SetValue(TotalPagesProperty, value);
        }

        public static readonly DependencyProperty CanGoBackProperty =
            DependencyProperty.Register("CanGoBack", typeof(bool), typeof(PageNavigator), new PropertyMetadata(false));
        public bool CanGoBack
        {
            get => (bool)GetValue(CanGoBackProperty);
            private set => SetValue(CanGoBackProperty, value);
        }

        public static readonly DependencyProperty CanGoForwardProperty =
            DependencyProperty.Register("CanGoForward", typeof(bool), typeof(PageNavigator), new PropertyMetadata(false));
        public bool CanGoForward
        {
            get => (bool)GetValue(CanGoForwardProperty);
            private set => SetValue(CanGoForwardProperty, value);
        }

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
                // 设置内部ComboBox值而不触发SelectionChanged
                control.CboPageSize.SelectionChanged -= control.CboPageSize_SelectionChanged;
                control.CboPageSize.SelectedItem = control.PageSize;
                control.CboPageSize.SelectionChanged += control.CboPageSize_SelectionChanged;

                control.UpdatePageItems();
            }
        }

        /// <summary>
        /// 核心：计算页码分布 (例如 1 2 ... 7 8 9)
        /// </summary>
        private void UpdatePageItems()
        {
            TotalPages = PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
            CanGoBack = PageIndex > 1;
            CanGoForward = PageIndex < TotalPages && TotalPages > 0;

            var items = new ObservableCollection<PageItem>();
            int total = TotalPages;
            int current = PageIndex;

            if (total == 0)
            {
                PageItems = items;
                return;
            }

            // 最多显示页码按钮数量(不含省略号)
            const int maxVisible = 5;

            if (total <= maxVisible)
            {
                for (int i = 1; i <= total; i++)
                    items.Add(new PageItem(i, current));
            }
            else
            {
                // 总页数较多时，引入省略号
                items.Add(new PageItem(1, current));

                if (current > 3)
                    items.Add(new PageItem(-1, current)); // ...

                int start = Math.Max(2, current - 1);
                int end = Math.Min(total - 1, current + 1);

                // 边界补偿
                if (current == 1) end = 3;
                if (current == total) start = total - 2;

                for (int i = start; i <= end; i++)
                {
                    items.Add(new PageItem(i, current));
                }

                if (current < total - 2)
                    items.Add(new PageItem(-1, current)); // ...

                items.Add(new PageItem(total, current));
            }

            PageItems = items;
        }

        /// <summary>
        /// 统一派发导航请求
        /// </summary>
        private void RequestPageChange(int newIndex, int newSize)
        {
            if (newIndex == PageIndex && newSize == PageSize) return;
            if (newIndex < 1 || newIndex > Math.Max(1, TotalPages)) return;

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
                // 如果没有Command拦截，或者拒接执行，重置ComboBox UI 状态
                CboPageSize.SelectionChanged -= CboPageSize_SelectionChanged;
                CboPageSize.SelectedItem = PageSize;
                CboPageSize.SelectionChanged += CboPageSize_SelectionChanged;
            }
        }

        #region UI Event Handlers
        private void BtnFirst_Click(object sender, RoutedEventArgs e) => RequestPageChange(1, PageSize);
        private void BtnPrev_Click(object sender, RoutedEventArgs e) => RequestPageChange(PageIndex - 1, PageSize);
        private void BtnNext_Click(object sender, RoutedEventArgs e) => RequestPageChange(PageIndex + 1, PageSize);
        private void BtnLast_Click(object sender, RoutedEventArgs e) => RequestPageChange(TotalPages, PageSize);

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
                // 更改大小优先重置回第一页
                RequestPageChange(1, newSize);
            }
        }
        #endregion
    }

    /// <summary>
    /// 用于装载动态页码的UI类
    /// </summary>
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