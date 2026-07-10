using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Media;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExport.Wpf.EnterpriseGrid.Filtering;

/// <summary>
/// 可放在 DataGrid 上方的轻量 FilterRow。它只生成可见且可筛选列的原生 TextBox，不替换列体系。
/// </summary>
public class GridFilterRow : StackPanel
{
    /// <summary>目标原生 DataGrid。</summary>
    public static readonly DependencyProperty TargetGridProperty = DependencyProperty.Register(
        nameof(TargetGrid),
        typeof(DataGrid),
        typeof(GridFilterRow),
        new PropertyMetadata(null, OnTargetGridChanged));

    /// <summary>输入去抖时长。</summary>
    public static readonly DependencyProperty FilterDelayProperty = DependencyProperty.Register(
        nameof(FilterDelay),
        typeof(TimeSpan),
        typeof(GridFilterRow),
        new PropertyMetadata(TimeSpan.FromMilliseconds(300)),
        value => (TimeSpan)value >= TimeSpan.Zero);

    private DataGrid? _subscribedGrid;
    private ScrollViewer? _scrollViewer;
    private readonly TranslateTransform _horizontalTransform = new();

    public GridFilterRow()
    {
        Orientation = Orientation.Horizontal;
        RenderTransform = _horizontalTransform;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>获取或设置目标 DataGrid。</summary>
    public DataGrid? TargetGrid
    {
        get => (DataGrid?)GetValue(TargetGridProperty);
        set => SetValue(TargetGridProperty, value);
    }

    /// <summary>获取或设置输入去抖时长。</summary>
    public TimeSpan FilterDelay
    {
        get => (TimeSpan)GetValue(FilterDelayProperty);
        set => SetValue(FilterDelayProperty, value);
    }

    internal void ApplyFilterText(DataGridColumn column, string? text)
    {
        if (TargetGrid is not null)
        {
            GridFilter.SetColumnFilter(TargetGrid, column, string.IsNullOrWhiteSpace(text) ? null : text.Trim());
        }
    }

    private static void OnTargetGridChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var filterRow = (GridFilterRow)dependencyObject;
        filterRow.Detach();
        filterRow.Attach();
    }

    private void Attach()
    {
        if (!IsLoaded || TargetGrid is null || ReferenceEquals(TargetGrid, _subscribedGrid))
        {
            return;
        }

        _subscribedGrid = TargetGrid;
        _subscribedGrid.Columns.CollectionChanged += OnColumnsChanged;
        _subscribedGrid.LayoutUpdated += OnGridLayoutUpdated;
        Rebuild();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, HookScrollViewer);
    }

    private void Detach()
    {
        if (_subscribedGrid is null)
        {
            return;
        }

        _subscribedGrid.Columns.CollectionChanged -= OnColumnsChanged;
        _subscribedGrid.LayoutUpdated -= OnGridLayoutUpdated;
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnGridScrollChanged;
            _scrollViewer = null;
        }

        _horizontalTransform.X = 0;
        _subscribedGrid = null;
        Children.Clear();
    }

    private void Rebuild()
    {
        Children.Clear();
        if (_subscribedGrid is null)
        {
            return;
        }

        Children.Add(new Border { Width = _subscribedGrid.RowHeaderActualWidth });
        foreach (var column in GetExpectedColumns())
        {
            var existing = GridFilter.GetFilterSet(_subscribedGrid).Descriptors
                .FirstOrDefault(descriptor =>
                    string.Equals(descriptor.ColumnKey, GridColumn.GetKey(column), StringComparison.Ordinal));
            Children.Add(new FilterCell(this, column, existing?.Value?.ToString()));
        }
    }

    private DataGridColumn[] GetExpectedColumns() =>
        _subscribedGrid?.Columns
                     .Where(column => column.Visibility == Visibility.Visible && GridColumn.GetCanFilter(column))
                     .OrderBy(column => column.DisplayIndex)
            .ToArray() ?? Array.Empty<DataGridColumn>();

    private void OnColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Rebuild();
    }

    private void OnGridLayoutUpdated(object? sender, EventArgs e)
    {
        var currentColumns = Children.OfType<FilterCell>().Select(cell => cell.Column).ToArray();
        if (!currentColumns.SequenceEqual(GetExpectedColumns()))
        {
            Rebuild();
            return;
        }

        if (Children.Count > 0)
        {
            Children[0].SetCurrentValue(WidthProperty, _subscribedGrid?.RowHeaderActualWidth ?? 0);
        }

        foreach (var cell in Children.OfType<FilterCell>())
        {
            cell.SynchronizeWidth();
        }
    }

    private void HookScrollViewer()
    {
        if (_subscribedGrid is null || _scrollViewer is not null)
        {
            return;
        }

        _subscribedGrid.ApplyTemplate();
        _scrollViewer = _subscribedGrid.Template?.FindName("DG_ScrollViewer", _subscribedGrid) as ScrollViewer;
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnGridScrollChanged;
            _horizontalTransform.X = -_scrollViewer.HorizontalOffset;
        }
    }

    private void OnGridScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0)
        {
            _horizontalTransform.X = -e.HorizontalOffset;
        }
    }

    private sealed class FilterCell : Border
    {
        private readonly GridFilterRow _owner;
        private readonly DataGridColumn _column;
        private readonly TextBox _textBox;
        private readonly DispatcherTimer _timer;

        public DataGridColumn Column => _column;

        public FilterCell(GridFilterRow owner, DataGridColumn column, string? value)
        {
            _owner = owner;
            _column = column;
            Padding = new Thickness(2);
            _textBox = new TextBox
            {
                Text = value ?? string.Empty,
                ToolTip = $"筛选：{column.Header}",
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(_textBox, $"筛选 {column.Header}");
            Child = _textBox;
            _timer = new DispatcherTimer { Interval = owner.FilterDelay };
            _timer.Tick += OnTimerTick;
            _textBox.TextChanged += OnTextChanged;
            Unloaded += (_, _) => _timer.Stop();
            SynchronizeWidth();
        }

        public void SynchronizeWidth()
        {
            var width = _column.ActualWidth;
            if (!double.IsNaN(width) && width > 0 && !DoubleUtil.AreClose(Width, width))
            {
                Width = width;
            }
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            _timer.Stop();
            _timer.Interval = _owner.FilterDelay;
            _timer.Start();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            _timer.Stop();
            _owner.ApplyFilterText(_column, _textBox.Text);
        }
    }

    private static class DoubleUtil
    {
        public static bool AreClose(double first, double second) => Math.Abs(first - second) < 0.1;
    }
}
