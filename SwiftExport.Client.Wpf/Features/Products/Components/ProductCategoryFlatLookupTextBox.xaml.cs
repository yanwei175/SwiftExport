using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 使用扁平分类缓存执行本地搜索的产品分类选择控件，适用于 DataGrid 编辑单元格。
/// </summary>
public partial class ProductCategoryFlatLookupTextBox : UserControl
{
    public ProductCategoryFlatLookupTextBox()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TextProperty = RegisterTwoWay(nameof(Text), typeof(string), string.Empty);
    public static readonly DependencyProperty ItemsSourceProperty = Register(nameof(ItemsSource), typeof(IEnumerable));
    public static readonly DependencyProperty SelectedCategoryProperty = RegisterTwoWay(nameof(SelectedCategory), typeof(ProductCategoryCacheNode));
    public static readonly DependencyProperty SelectedCategoryIdProperty = RegisterTwoWay(nameof(SelectedCategoryId), typeof(Guid?));
    public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(nameof(SelectionCommittedCommand), typeof(ICommand));
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(nameof(SelectionCommittedCommandParameter), typeof(object));
    public static readonly DependencyProperty SelectionClearedCommandProperty = Register(nameof(SelectionClearedCommand), typeof(ICommand));
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(nameof(SelectionClearedCommandParameter), typeof(object));
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "搜索产品分类");

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

    public ProductCategoryCacheNode? SelectedCategory
    {
        get => (ProductCategoryCacheNode?)GetValue(SelectedCategoryProperty);
        set => SetValue(SelectedCategoryProperty, value);
    }

    public Guid? SelectedCategoryId
    {
        get => (Guid?)GetValue(SelectedCategoryIdProperty);
        set => SetValue(SelectedCategoryIdProperty, value);
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
        DependencyProperty.Register(name, type, typeof(ProductCategoryFlatLookupTextBox), new PropertyMetadata(defaultValue));

    private static DependencyProperty RegisterTwoWay(string name, Type type, object? defaultValue = null) =>
        DependencyProperty.Register(
            name,
            type,
            typeof(ProductCategoryFlatLookupTextBox),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
