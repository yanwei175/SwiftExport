using System.Windows;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Resources.Controls;

namespace SwiftExportClient.Features.Suppliers.Components;

/// <summary>
/// 使用供应商缓存按供应商编码筛选，并向 ViewModel 提交完整供应商 DTO。
/// </summary>
public partial class SupplierAutoSuggestBox : CachedAutoSuggestBoxBase
{
    /// <summary>
    /// 标识当前已确认供应商。
    /// </summary>
    public static readonly DependencyProperty SelectedSupplierProperty = DependencyProperty.Register(
        nameof(SelectedSupplier),
        typeof(SupplierDto),
        typeof(SupplierAutoSuggestBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedSupplierChanged));

    /// <summary>
    /// 初始化供应商缓存搜索控件。
    /// </summary>
    public SupplierAutoSuggestBox()
    {
        PlaceholderText = "搜索供应商编码";
        InitializeComponent();
        InitializeEditor(SuggestEditor);
    }

    /// <summary>
    /// 获取或设置当前已确认的完整供应商 DTO。
    /// </summary>
    public SupplierDto? SelectedSupplier
    {
        get => (SupplierDto?)GetValue(SelectedSupplierProperty);
        set => SetValue(SelectedSupplierProperty, value);
    }

    /// <inheritdoc />
    protected override object? GetSelectedItemCore() => SelectedSupplier;

    /// <inheritdoc />
    protected override void SetSelectedItemCore(object? item) =>
        SetCurrentValue(SelectedSupplierProperty, item as SupplierDto);

    /// <inheritdoc />
    protected override string GetDisplayText(object item) =>
        item is SupplierDto supplier ? supplier.SupplierCode ?? string.Empty : string.Empty;

    private static void OnSelectedSupplierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SupplierAutoSuggestBox)d).SynchronizeSelectedItem(e.NewValue);
}
