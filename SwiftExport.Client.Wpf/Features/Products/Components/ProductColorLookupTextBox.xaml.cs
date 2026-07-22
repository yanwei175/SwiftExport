using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.Products;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 使用产品颜色缓存执行本地搜索的选择控件。
/// </summary>
public partial class ProductColorLookupTextBox : UserControl
{
    public ProductColorLookupTextBox()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TextProperty = RegisterTwoWay(nameof(Text), typeof(string), string.Empty);
    public static readonly DependencyProperty ItemsSourceProperty = Register(nameof(ItemsSource), typeof(IEnumerable));
    public static readonly DependencyProperty SelectedColorProperty = RegisterTwoWay(nameof(SelectedColor), typeof(ProductColorDto));
    public static readonly DependencyProperty SelectedColorIdProperty = RegisterTwoWay(nameof(SelectedColorId), typeof(Guid?));
    public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(nameof(SelectionCommittedCommand), typeof(ICommand));
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(nameof(SelectionCommittedCommandParameter), typeof(object));
    public static readonly DependencyProperty SelectionClearedCommandProperty = Register(nameof(SelectionClearedCommand), typeof(ICommand));
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(nameof(SelectionClearedCommandParameter), typeof(object));
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "搜索颜色代码或名称");

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ProductColorDto? SelectedColor
    {
        get => (ProductColorDto?)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public Guid? SelectedColorId
    {
        get => (Guid?)GetValue(SelectedColorIdProperty);
        set => SetValue(SelectedColorIdProperty, value);
    }

    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    public ICommand? SelectionClearedCommand
    {
        get => (ICommand?)GetValue(SelectionClearedCommandProperty);
        set => SetValue(SelectionClearedCommandProperty, value);
    }

    public object? SelectionClearedCommandParameter
    {
        get => GetValue(SelectionClearedCommandParameterProperty);
        set => SetValue(SelectionClearedCommandParameterProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public void FocusEditor() => LookupEditor.FocusEditor();

    private static DependencyProperty Register(string name, Type type, object? defaultValue = null) =>
        DependencyProperty.Register(name, type, typeof(ProductColorLookupTextBox), new PropertyMetadata(defaultValue));

    private static DependencyProperty RegisterTwoWay(string name, Type type, object? defaultValue = null) =>
        DependencyProperty.Register(
            name,
            type,
            typeof(ProductColorLookupTextBox),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
