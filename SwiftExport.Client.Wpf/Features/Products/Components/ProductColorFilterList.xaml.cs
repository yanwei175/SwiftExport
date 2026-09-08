using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Components.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>从只读颜色缓存创建独立选项，并通过原生 ListBox 输出多选颜色快照。</summary>
public partial class ProductColorFilterList : UserControl
{
    /// <summary>标识 <see cref="Colors"/> 依赖属性。</summary>
    public static readonly DependencyProperty ColorsProperty = DependencyProperty.Register(
        nameof(Colors),
        typeof(IEnumerable<ProductColorDto>),
        typeof(ProductColorFilterList),
        new PropertyMetadata(null, OnColorsChanged));

    /// <summary>标识 <see cref="Selection"/> 依赖属性。</summary>
    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection),
        typeof(ProductColorFilterSelection),
        typeof(ProductColorFilterList),
        new FrameworkPropertyMetadata(
            ProductColorFilterSelection.AllColors,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectionChanged,
            CoerceSelection));

    private readonly ObservableCollection<ProductColorFilterOption> _options = [];
    private INotifyCollectionChanged? _colorsNotifier;
    private bool _isSynchronizing;

    /// <summary>初始化颜色筛选列表并建立可绑定的只读选项集合。</summary>
    public ProductColorFilterList()
    {
        Options = new ReadOnlyObservableCollection<ProductColorFilterOption>(_options);
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RebuildOptions();
    }

    /// <summary>获取或设置共享只读颜色缓存。</summary>
    public IEnumerable<ProductColorDto>? Colors
    {
        get => (IEnumerable<ProductColorDto>?)GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    /// <summary>获取或设置当前颜色筛选快照；该属性默认支持双向绑定。</summary>
    public ProductColorFilterSelection Selection
    {
        get => (ProductColorFilterSelection)GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>获取由颜色缓存转换出的只读筛选选项。</summary>
    public ReadOnlyObservableCollection<ProductColorFilterOption> Options { get; }

    private static object CoerceSelection(DependencyObject d, object? baseValue) =>
        baseValue as ProductColorFilterSelection ?? ProductColorFilterSelection.AllColors;

    private static void OnColorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ProductColorFilterList)d;
        control.DetachColorsNotifier();
        control.AttachColorsNotifier();
        control.RebuildOptions();
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ProductColorFilterList)d;
        if (!control._isSynchronizing)
        {
            control.ApplyExternalSelection((ProductColorFilterSelection)e.NewValue);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachColorsNotifier();
        RebuildOptions();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachColorsNotifier();

    private void AttachColorsNotifier()
    {
        if (!IsLoaded || _colorsNotifier is not null || Colors is not INotifyCollectionChanged notifier)
        {
            return;
        }

        _colorsNotifier = notifier;
        _colorsNotifier.CollectionChanged += OnColorsCollectionChanged;
    }

    private void DetachColorsNotifier()
    {
        if (_colorsNotifier is null)
        {
            return;
        }

        _colorsNotifier.CollectionChanged -= OnColorsCollectionChanged;
        _colorsNotifier = null;
    }

    private void OnColorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildOptions();

    /// <summary>从缓存重新创建 UI 选项，并按颜色标识恢复仍有效的选择。</summary>
    private void RebuildOptions()
    {
        var requestedSelection = Selection;
        _isSynchronizing = true;
        try
        {
            _options.Clear();
            if (Colors is null)
            {
                return;
            }

            var selectedIds = requestedSelection.ColorIds.ToHashSet();
            foreach (var color in Colors)
            {
                var option = color.Adapt<ProductColorFilterOption>();
                option.IsChecked = selectedIds.Contains(option.Uid);
                _options.Add(option);
            }

            SetCurrentValue(SelectionProperty, CreateSelection());
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void ApplyExternalSelection(ProductColorFilterSelection selection)
    {
        _isSynchronizing = true;
        try
        {
            var selectedIds = selection.ColorIds.ToHashSet();
            foreach (var option in _options)
            {
                option.IsChecked = selectedIds.Contains(option.Uid);
            }

            if (Colors is not null)
            {
                SetCurrentValue(SelectionProperty, CreateSelection());
            }
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void ColorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizing)
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            foreach (var removed in e.RemovedItems.OfType<ProductColorFilterOption>())
            {
                removed.IsChecked = false;
            }
            foreach (var added in e.AddedItems.OfType<ProductColorFilterOption>())
            {
                added.IsChecked = true;
            }

            SetCurrentValue(SelectionProperty, CreateSelection());
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private ProductColorFilterSelection CreateSelection() =>
        new(_options.Where(option => option.IsChecked).Select(option => option.Uid).ToArray());
}
