using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExportClient.Features.Sales.Orders.Components;

/// <summary>
/// 销售订单行使用的产品远程搜索编辑器，封装客户产品候选的显示与选择语义。
/// </summary>
public partial class SalesProductLookupTextBox : UserControl
{
    public SalesProductLookupTextBox()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SalesProductLookupTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(SalesProductLookupTextBox),
        new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedProductProperty = DependencyProperty.Register(
        nameof(SelectedProduct),
        typeof(SalesOrderProductLookupDto),
        typeof(SalesProductLookupTextBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public SalesOrderProductLookupDto? SelectedProduct
    {
        get => (SalesOrderProductLookupDto?)GetValue(SelectedProductProperty);
        set => SetValue(SelectedProductProperty, value);
    }

    public static readonly DependencyProperty SelectedProductIdProperty = DependencyProperty.Register(
        nameof(SelectedProductId),
        typeof(Guid?),
        typeof(SalesProductLookupTextBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public Guid? SelectedProductId
    {
        get => (Guid?)GetValue(SelectedProductIdProperty);
        set => SetValue(SelectedProductIdProperty, value);
    }

    public static readonly DependencyProperty SearchCommandProperty = DependencyProperty.Register(
        nameof(SearchCommand), typeof(ICommand), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public static readonly DependencyProperty SelectionCommittedCommandProperty = DependencyProperty.Register(
        nameof(SelectionCommittedCommand), typeof(ICommand), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = DependencyProperty.Register(
        nameof(SelectionCommittedCommandParameter), typeof(object), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    public static readonly DependencyProperty SelectionClearedCommandProperty = DependencyProperty.Register(
        nameof(SelectionClearedCommand), typeof(ICommand), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

    public ICommand? SelectionClearedCommand
    {
        get => (ICommand?)GetValue(SelectionClearedCommandProperty);
        set => SetValue(SelectionClearedCommandProperty, value);
    }

    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = DependencyProperty.Register(
        nameof(SelectionClearedCommandParameter), typeof(object), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

    public object? SelectionClearedCommandParameter
    {
        get => GetValue(SelectionClearedCommandParameterProperty);
        set => SetValue(SelectionClearedCommandParameterProperty, value);
    }

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(SalesProductLookupTextBox), new PropertyMetadata(false));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public static readonly DependencyProperty SearchDelayProperty = DependencyProperty.Register(
        nameof(SearchDelay),
        typeof(TimeSpan),
        typeof(SalesProductLookupTextBox),
        new PropertyMetadata(TimeSpan.FromMilliseconds(300)),
        value => value is TimeSpan delay && delay >= TimeSpan.Zero);

    public TimeSpan SearchDelay
    {
        get => (TimeSpan)GetValue(SearchDelayProperty);
        set => SetValue(SearchDelayProperty, value);
    }

    public static readonly DependencyProperty MinimumSearchLengthProperty = DependencyProperty.Register(
        nameof(MinimumSearchLength),
        typeof(int),
        typeof(SalesProductLookupTextBox),
        new PropertyMetadata(1, null, static (_, value) => Math.Max(0, (int)value)));

    public int MinimumSearchLength
    {
        get => (int)GetValue(MinimumSearchLengthProperty);
        set => SetValue(MinimumSearchLengthProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(SalesProductLookupTextBox),
        new PropertyMetadata("搜索 SKU、客户型号或描述"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public void FocusEditor() => LookupEditor.FocusEditor();
}
