using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Contracts.DTOs.Suppliers;

namespace SwiftExportClient.Features.Suppliers.Components;

/// <summary>
/// 供应商远程搜索编辑器。网络请求由 ViewModel 命令负责，控件只封装供应商选择语义和候选视觉。
/// </summary>
public partial class SupplierLookupTextBox : UserControl
{
    public SupplierLookupTextBox()
    {
        InitializeComponent();
        Loaded += SupplierLookupTextBox_Loaded;
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SupplierLookupTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SupplierLookupTextBox), new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedSupplierProperty = DependencyProperty.Register(
        nameof(SelectedSupplier), typeof(SupplierDto), typeof(SupplierLookupTextBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public SupplierDto? SelectedSupplier
    {
        get => (SupplierDto?)GetValue(SelectedSupplierProperty);
        set => SetValue(SelectedSupplierProperty, value);
    }

    public static readonly DependencyProperty SelectedSupplierIdProperty = DependencyProperty.Register(
        nameof(SelectedSupplierId), typeof(Guid?), typeof(SupplierLookupTextBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public Guid? SelectedSupplierId
    {
        get => (Guid?)GetValue(SelectedSupplierIdProperty);
        set => SetValue(SelectedSupplierIdProperty, value);
    }

    public static readonly DependencyProperty SearchCommandProperty = DependencyProperty.Register(
        nameof(SearchCommand), typeof(ICommand), typeof(SupplierLookupTextBox), new PropertyMetadata(null));

    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public static readonly DependencyProperty SelectionCommittedCommandProperty = DependencyProperty.Register(
        nameof(SelectionCommittedCommand), typeof(ICommand), typeof(SupplierLookupTextBox), new PropertyMetadata(null));

    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = DependencyProperty.Register(
        nameof(SelectionCommittedCommandParameter), typeof(object), typeof(SupplierLookupTextBox), new PropertyMetadata(null));

    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    public static readonly DependencyProperty SelectionClearedCommandProperty = DependencyProperty.Register(
        nameof(SelectionClearedCommand), typeof(ICommand), typeof(SupplierLookupTextBox), new PropertyMetadata(null));

    public ICommand? SelectionClearedCommand
    {
        get => (ICommand?)GetValue(SelectionClearedCommandProperty);
        set => SetValue(SelectionClearedCommandProperty, value);
    }

    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = DependencyProperty.Register(
        nameof(SelectionClearedCommandParameter), typeof(object), typeof(SupplierLookupTextBox), new PropertyMetadata(null));

    public object? SelectionClearedCommandParameter
    {
        get => GetValue(SelectionClearedCommandParameterProperty);
        set => SetValue(SelectionClearedCommandParameterProperty, value);
    }

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(SupplierLookupTextBox), new PropertyMetadata(false));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public static readonly DependencyProperty SearchDelayProperty = DependencyProperty.Register(
        nameof(SearchDelay), typeof(TimeSpan), typeof(SupplierLookupTextBox),
        new PropertyMetadata(TimeSpan.FromMilliseconds(300)),
        value => value is TimeSpan delay && delay >= TimeSpan.Zero);

    public TimeSpan SearchDelay
    {
        get => (TimeSpan)GetValue(SearchDelayProperty);
        set => SetValue(SearchDelayProperty, value);
    }

    public static readonly DependencyProperty MinimumSearchLengthProperty = DependencyProperty.Register(
        nameof(MinimumSearchLength), typeof(int), typeof(SupplierLookupTextBox),
        new PropertyMetadata(1, null, static (_, value) => Math.Max(0, (int)value)));

    public int MinimumSearchLength
    {
        get => (int)GetValue(MinimumSearchLengthProperty);
        set => SetValue(MinimumSearchLengthProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SupplierLookupTextBox),
        new PropertyMetadata("搜索供应商代码或名称"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public void FocusEditor() => LookupEditor.FocusEditor();

    private void SupplierLookupTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        var cell = FindVisualAncestor<DataGridCell>(this);
        if (cell?.IsEditing != true)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                if (IsLoaded && cell.IsEditing)
                {
                    LookupEditor.FocusEditor();
                }
            },
            DispatcherPriority.Input);
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T ancestor)
            {
                return ancestor;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
