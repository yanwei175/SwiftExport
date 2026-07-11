using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExport.Wpf.EnterpriseGrid.Layout;

/// <summary>
/// 纯原生可组合列选择器；可直接放入页面、Popup 或宿主 WPF-UI ContentDialog。
/// </summary>
public class GridColumnChooser : StackPanel
{
    /// <summary>目标原生 DataGrid。</summary>
    public static readonly DependencyProperty TargetGridProperty = DependencyProperty.Register(
        nameof(TargetGrid),
        typeof(DataGrid),
        typeof(GridColumnChooser),
        new PropertyMetadata(null, OnTargetGridChanged));

    private DataGrid? _subscribedGrid;

    /// <summary>初始化列选择器并配置加载/卸载生命周期。</summary>
    public GridColumnChooser()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>获取或设置目标原生 DataGrid。</summary>
    public DataGrid? TargetGrid
    {
        get => (DataGrid?)GetValue(TargetGridProperty);
        set => SetValue(TargetGridProperty, value);
    }

    private static void OnTargetGridChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var chooser = (GridColumnChooser)dependencyObject;
        chooser.Detach();
        chooser.Attach();
    }

    private void Attach()
    {
        if (!IsLoaded || TargetGrid is null || ReferenceEquals(TargetGrid, _subscribedGrid))
        {
            return;
        }

        _subscribedGrid = TargetGrid;
        _subscribedGrid.Columns.CollectionChanged += OnColumnsChanged;
        _subscribedGrid.ColumnDisplayIndexChanged += OnColumnDisplayIndexChanged;
        _subscribedGrid.LayoutUpdated += OnGridLayoutUpdated;
        Rebuild();
    }

    private void Detach()
    {
        if (_subscribedGrid is not null)
        {
            _subscribedGrid.Columns.CollectionChanged -= OnColumnsChanged;
            _subscribedGrid.ColumnDisplayIndexChanged -= OnColumnDisplayIndexChanged;
            _subscribedGrid.LayoutUpdated -= OnGridLayoutUpdated;
        }

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

        foreach (var column in _subscribedGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            var checkBox = new CheckBox
            {
                Content = column.Header?.ToString() ?? GridColumn.GetKey(column) ?? "未命名列",
                IsChecked = column.Visibility == Visibility.Visible,
                Margin = new Thickness(4),
                Tag = column
            };
            AutomationProperties.SetName(checkBox, $"显示列 {checkBox.Content}");
            checkBox.Checked += OnVisibilityChanged;
            checkBox.Unchecked += OnVisibilityChanged;
            Children.Add(checkBox);
        }
    }

    private void OnVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: DataGridColumn column, IsChecked: { } isChecked })
        {
            column.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Rebuild();

    private void OnColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) => Rebuild();

    private void OnGridLayoutUpdated(object? sender, EventArgs e)
    {
        if (_subscribedGrid is null)
        {
            return;
        }

        var expected = _subscribedGrid.Columns.OrderBy(column => column.DisplayIndex).ToArray();
        var actual = Children.OfType<CheckBox>().Select(checkBox => checkBox.Tag).Cast<DataGridColumn>().ToArray();
        if (!actual.SequenceEqual(expected))
        {
            Rebuild();
            return;
        }

        foreach (var checkBox in Children.OfType<CheckBox>())
        {
            var column = (DataGridColumn)checkBox.Tag;
            checkBox.IsChecked = column.Visibility == Visibility.Visible;
        }
    }
}
