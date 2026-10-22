using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExportClient.Resources.Controls;

/// <summary>展示调用方已经接纳的分页事实，仅通过命令提出切页或更改页大小的请求。</summary>
public partial class PageNavigator : UserControl
{
    public PageNavigator()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TotalCountProperty =
        DependencyProperty.Register(
            nameof(TotalCount),
            typeof(int),
            typeof(PageNavigator),
            new PropertyMetadata(0, OnPaginationChanged));

    public int TotalCount
    {
        get => (int)GetValue(TotalCountProperty);
        set => SetValue(TotalCountProperty, value);
    }

    public static readonly DependencyProperty PageIndexProperty =
        DependencyProperty.Register(
            nameof(PageIndex),
            typeof(int),
            typeof(PageNavigator),
            new PropertyMetadata(1, OnPaginationChanged));

    public int PageIndex
    {
        get => (int)GetValue(PageIndexProperty);
        set => SetValue(PageIndexProperty, value);
    }

    public static readonly DependencyProperty PageSizeProperty =
        DependencyProperty.Register(
            nameof(PageSize),
            typeof(int),
            typeof(PageNavigator),
            new PropertyMetadata(20, OnPaginationChanged));

    public int PageSize
    {
        get => (int)GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public static readonly DependencyProperty PageSizeOptionsProperty =
        DependencyProperty.Register(
            nameof(PageSizeOptions),
            typeof(IEnumerable<int>),
            typeof(PageNavigator),
            new PropertyMetadata(new[] { 10, 20, 50, 100, 200 }));

    public IEnumerable<int> PageSizeOptions
    {
        get => (IEnumerable<int>)GetValue(PageSizeOptionsProperty);
        set => SetValue(PageSizeOptionsProperty, value);
    }

    public static readonly DependencyProperty PageChangeCommandProperty =
        DependencyProperty.Register(
            nameof(PageChangeCommand),
            typeof(ICommand),
            typeof(PageNavigator),
            new PropertyMetadata(null));

    public ICommand? PageChangeCommand
    {
        get => (ICommand?)GetValue(PageChangeCommandProperty);
        set => SetValue(PageChangeCommandProperty, value);
    }

    public static readonly DependencyProperty PageItemsProperty =
        DependencyProperty.Register(
            nameof(PageItems),
            typeof(ObservableCollection<PageItem>),
            typeof(PageNavigator),
            new PropertyMetadata(new ObservableCollection<PageItem>()));

    public ObservableCollection<PageItem> PageItems
    {
        get => (ObservableCollection<PageItem>)GetValue(PageItemsProperty);
        private set => SetValue(PageItemsProperty, value);
    }

    private static void OnPaginationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PageNavigator control)
        {
            return;
        }

        control.CboPageSize.SelectionChanged -= control.CboPageSize_SelectionChanged;
        control.CboPageSize.SelectedItem = control.PageSize;
        control.CboPageSize.SelectionChanged += control.CboPageSize_SelectionChanged;

        control.UpdatePageItems();
    }

    private void UpdatePageItems()
    {
        var total = PageSize > 0
            ? (int)Math.Ceiling(TotalCount / (double)PageSize)
            : 0;
        var current = Math.Clamp(PageIndex, 1, Math.Max(total, 1));
        var items = new ObservableCollection<PageItem>();

        if (total <= 0)
        {
            PageItems = items;
            return;
        }

        if (total <= 9)
        {
            for (var i = 1; i <= total; i++)
            {
                items.Add(new PageItem(i, current));
            }
        }
        else if (current <= 5)
        {
            for (var i = 1; i <= 7; i++)
            {
                items.Add(new PageItem(i, current));
            }

            items.Add(new PageItem(-1, current));
            items.Add(new PageItem(total, current));
        }
        else if (current >= total - 4)
        {
            items.Add(new PageItem(1, current));
            items.Add(new PageItem(-1, current));

            for (var i = total - 6; i <= total; i++)
            {
                items.Add(new PageItem(i, current));
            }
        }
        else
        {
            items.Add(new PageItem(1, current));
            items.Add(new PageItem(-1, current));

            for (var i = current - 2; i <= current + 2; i++)
            {
                items.Add(new PageItem(i, current));
            }

            items.Add(new PageItem(-1, current));
            items.Add(new PageItem(total, current));
        }

        PageItems = items;
    }

    /// <summary>兼容同步与异步命令；发出请求后仍显示绑定事实，成功由调用方回填，失败无需假定新页已生效。</summary>
    private void RequestPageChange(int newIndex, int newSize)
    {
        if (newIndex == PageIndex && newSize == PageSize)
        {
            return;
        }

        var request = new PageChangeRequest
        {
            OldIndex = PageIndex,
            NewIndex = newIndex,
            OldSize = PageSize,
            NewSize = newSize,
        };

        try
        {
            if (PageChangeCommand?.CanExecute(request) == true) PageChangeCommand.Execute(request);
        }
        finally
        {
            ResetPageSizeSelection();
        }
    }

    private void BtnPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PageItem item } && item.PageIndex != -1)
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

    private void ResetPageSizeSelection()
    {
        CboPageSize.SelectionChanged -= CboPageSize_SelectionChanged;
        CboPageSize.SelectedItem = PageSize;
        CboPageSize.SelectionChanged += CboPageSize_SelectionChanged;
    }
}
