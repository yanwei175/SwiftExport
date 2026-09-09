using System.Windows;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExportClient.Resources.Controls;

namespace SwiftExportClient.Features.Customers.Components;

/// <summary>
/// 使用客户缓存按客户编码筛选，并向 ViewModel 提交完整客户 DTO。
/// </summary>
public partial class CustomerAutoSuggestBox : CachedAutoSuggestBoxBase
{
    /// <summary>
    /// 标识当前已确认客户。
    /// </summary>
    public static readonly DependencyProperty SelectedCustomerProperty = DependencyProperty.Register(
        nameof(SelectedCustomer),
        typeof(CustomerDto),
        typeof(CustomerAutoSuggestBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedCustomerChanged));

    /// <summary>
    /// 初始化客户缓存搜索控件。
    /// </summary>
    public CustomerAutoSuggestBox()
    {
        PlaceholderText = "搜索客户编码";
        InitializeComponent();
        InitializeEditor(SuggestEditor);
    }

    /// <summary>
    /// 获取或设置当前已确认的完整客户 DTO。
    /// </summary>
    public CustomerDto? SelectedCustomer
    {
        get => (CustomerDto?)GetValue(SelectedCustomerProperty);
        set => SetValue(SelectedCustomerProperty, value);
    }

    /// <inheritdoc />
    protected override object? GetSelectedItemCore() => SelectedCustomer;

    /// <inheritdoc />
    protected override void SetSelectedItemCore(object? item) =>
        SetCurrentValue(SelectedCustomerProperty, item as CustomerDto);

    /// <inheritdoc />
    protected override string GetDisplayText(object item) =>
        item is CustomerDto customer ? customer.CustomerCode ?? string.Empty : string.Empty;

    private static void OnSelectedCustomerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CustomerAutoSuggestBox)d).SynchronizeSelectedItem(e.NewValue);
}
