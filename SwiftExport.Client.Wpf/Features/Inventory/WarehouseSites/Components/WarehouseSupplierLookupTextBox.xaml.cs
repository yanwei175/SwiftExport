using SwiftExport.Contracts.DTOs.Inventory;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Components;

/// <summary>虚拟仓管理专用供应商选择器；只承载窄候选显示与选择，不直接访问 API。</summary>
public partial class WarehouseSupplierLookupTextBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(WarehouseSupplierLookupTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(WarehouseSupplierLookupTextBox));
    public static readonly DependencyProperty SelectedSupplierProperty = DependencyProperty.Register(
        nameof(SelectedSupplier), typeof(WarehouseSupplierLookupDto), typeof(WarehouseSupplierLookupTextBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty SearchCommandProperty = DependencyProperty.Register(
        nameof(SearchCommand), typeof(ICommand), typeof(WarehouseSupplierLookupTextBox));
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(WarehouseSupplierLookupTextBox));
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(WarehouseSupplierLookupTextBox),
        new PropertyMetadata("搜索供应商代码或名称"));

    /// <summary>初始化组件。</summary>
    public WarehouseSupplierLookupTextBox() => InitializeComponent();

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public WarehouseSupplierLookupDto? SelectedSupplier { get => (WarehouseSupplierLookupDto?)GetValue(SelectedSupplierProperty); set => SetValue(SelectedSupplierProperty, value); }
    public ICommand? SearchCommand { get => (ICommand?)GetValue(SearchCommandProperty); set => SetValue(SearchCommandProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
}
