using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Controls.Primitives;
using SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup.Internal;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup;

/// <summary>
/// 基于原生 <see cref="DataGridTemplateColumn"/> 的 Lookup 便利列；本地集合与远程 Provider 均需显式提供。
/// </summary>
[ContentProperty(nameof(SelectedValueBinding))]
public class LookupDataGridColumn : DataGridTemplateColumn
{
    /// <summary>候选集合；DataGridColumn 不继承 DataContext，因此必须显式设置或使用 ItemsSourceBinding。</summary>
    public IEnumerable? ItemsSource { get; set; }

    /// <summary>按当前行或显式 RelativeSource 获取候选集合的 Binding。</summary>
    public BindingBase? ItemsSourceBinding { get; set; }

    /// <summary>选中值 Binding；通常使用 TwoWay。</summary>
    public BindingBase? SelectedValueBinding { get; set; }

    /// <summary>显示文本 Binding；设置后显示态不执行 Lookup 解析。</summary>
    public BindingBase? DisplayBinding { get; set; }

    /// <summary>按当前行或显式 RelativeSource 获取 Provider 的 Binding。</summary>
    public BindingBase? ProviderBinding { get; set; }

    /// <summary>固定远程 Provider。</summary>
    public IGridLookupProvider? Provider { get; set; }

    /// <summary>候选项的值路径。</summary>
    public string? SelectedValuePath { get; set; }

    /// <summary>候选项的显示路径。</summary>
    public string? DisplayMemberPath { get; set; }

    /// <summary>远程搜索去抖时长。</summary>
    public TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>远程搜索最少字符数。</summary>
    public int MinimumSearchLength { get; set; } = 1;

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        if (DisplayBinding is not null)
        {
            var textBlock = new TextBlock();
            BindingOperations.SetBinding(textBlock, TextBlock.TextProperty, DisplayBinding);
            return textBlock;
        }

        var display = new GridLookupDisplayTextBlock
        {
            LookupItemsSource = ItemsSource,
            Provider = Provider,
            SelectedValuePath = SelectedValuePath,
            DisplayMemberPath = DisplayMemberPath
        };
        ApplyBinding(display, GridLookupDisplayTextBlock.SelectedValueProperty, SelectedValueBinding);
        ApplyBinding(display, GridLookupDisplayTextBlock.LookupItemsSourceProperty, ItemsSourceBinding);
        ApplyBinding(display, GridLookupDisplayTextBlock.ProviderProperty, ProviderBinding);
        return display;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var editor = new GridLookupEditor
        {
            ItemsSource = ItemsSource,
            Provider = Provider,
            SelectedValuePath = SelectedValuePath,
            DisplayMemberPath = DisplayMemberPath,
            SearchDelay = SearchDelay,
            MinimumSearchLength = MinimumSearchLength
        };
        ApplyBinding(editor, GridLookupEditor.LookupValueProperty, SelectedValueBinding);
        ApplyBinding(editor, ItemsControl.ItemsSourceProperty, ItemsSourceBinding);
        ApplyBinding(editor, GridLookupEditor.ProviderProperty, ProviderBinding);
        return editor;
    }

    private static void ApplyBinding(DependencyObject target, DependencyProperty property, BindingBase? binding)
    {
        if (binding is not null)
        {
            BindingOperations.SetBinding(target, property, binding);
        }
    }
}
