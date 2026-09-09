using System.Windows;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Resources.Controls;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 使用产品颜色缓存按中文名称筛选，并向 ViewModel 提交完整颜色 DTO。
/// </summary>
public partial class ProductColorAutoSuggestBox : CachedAutoSuggestBoxBase
{
    /// <summary>
    /// 标识当前已确认产品颜色。
    /// </summary>
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(ProductColorDto),
        typeof(ProductColorAutoSuggestBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedColorChanged));

    /// <summary>
    /// 初始化产品颜色缓存搜索控件。
    /// </summary>
    public ProductColorAutoSuggestBox()
    {
        PlaceholderText = "搜索颜色中文名称";
        InitializeComponent();
        InitializeEditor(SuggestEditor);
    }

    /// <summary>
    /// 获取或设置当前已确认的完整产品颜色 DTO。
    /// </summary>
    public ProductColorDto? SelectedColor
    {
        get => (ProductColorDto?)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <inheritdoc />
    protected override object? GetSelectedItemCore() => SelectedColor;

    /// <inheritdoc />
    protected override void SetSelectedItemCore(object? item) =>
        SetCurrentValue(SelectedColorProperty, item as ProductColorDto);

    /// <inheritdoc />
    protected override string GetDisplayText(object item) =>
        item is ProductColorDto color ? color.ColorNameZh ?? string.Empty : string.Empty;

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ProductColorAutoSuggestBox)d).SynchronizeSelectedItem(e.NewValue);
}
