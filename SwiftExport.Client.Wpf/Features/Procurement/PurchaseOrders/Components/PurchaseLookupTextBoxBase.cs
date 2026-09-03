using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Components;

/// <summary>采购专用远程 Lookup 的公共依赖属性外壳；不执行网络请求或业务回填。</summary>
public class PurchaseLookupTextBoxBase : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(PurchaseLookupTextBoxBase), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(PurchaseLookupTextBoxBase));
    public static readonly DependencyProperty SearchCommandProperty = DependencyProperty.Register(nameof(SearchCommand), typeof(ICommand), typeof(PurchaseLookupTextBoxBase));
    public static readonly DependencyProperty SelectionCommittedCommandProperty = DependencyProperty.Register(nameof(SelectionCommittedCommand), typeof(ICommand), typeof(PurchaseLookupTextBoxBase));
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = DependencyProperty.Register(nameof(SelectionCommittedCommandParameter), typeof(object), typeof(PurchaseLookupTextBoxBase));
    public static readonly DependencyProperty SelectionClearedCommandProperty = DependencyProperty.Register(nameof(SelectionClearedCommand), typeof(ICommand), typeof(PurchaseLookupTextBoxBase));
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = DependencyProperty.Register(nameof(SelectionClearedCommandParameter), typeof(object), typeof(PurchaseLookupTextBoxBase));
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(PurchaseLookupTextBoxBase));
    public static readonly DependencyProperty SearchDelayProperty = DependencyProperty.Register(nameof(SearchDelay), typeof(TimeSpan), typeof(PurchaseLookupTextBoxBase), new PropertyMetadata(TimeSpan.FromMilliseconds(300)));
    public static readonly DependencyProperty MinimumSearchLengthProperty = DependencyProperty.Register(nameof(MinimumSearchLength), typeof(int), typeof(PurchaseLookupTextBoxBase), new PropertyMetadata(1));
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PurchaseLookupTextBoxBase), new PropertyMetadata(string.Empty));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ICommand? SearchCommand { get => (ICommand?)GetValue(SearchCommandProperty); set => SetValue(SearchCommandProperty, value); }
    public ICommand? SelectionCommittedCommand { get => (ICommand?)GetValue(SelectionCommittedCommandProperty); set => SetValue(SelectionCommittedCommandProperty, value); }
    public object? SelectionCommittedCommandParameter { get => GetValue(SelectionCommittedCommandParameterProperty); set => SetValue(SelectionCommittedCommandParameterProperty, value); }
    public ICommand? SelectionClearedCommand { get => (ICommand?)GetValue(SelectionClearedCommandProperty); set => SetValue(SelectionClearedCommandProperty, value); }
    public object? SelectionClearedCommandParameter { get => GetValue(SelectionClearedCommandParameterProperty); set => SetValue(SelectionClearedCommandParameterProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public TimeSpan SearchDelay { get => (TimeSpan)GetValue(SearchDelayProperty); set => SetValue(SearchDelayProperty, value); }
    public int MinimumSearchLength { get => (int)GetValue(MinimumSearchLengthProperty); set => SetValue(MinimumSearchLengthProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
}
