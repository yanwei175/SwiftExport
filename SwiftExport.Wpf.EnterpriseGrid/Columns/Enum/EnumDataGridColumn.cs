using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Enum;

/// <summary>组合原生 <see cref="DataGridComboBoxColumn"/> 的枚举便利列。</summary>
public class EnumDataGridColumn : DataGridComboBoxColumn
{
    /// <summary>枚举类型。</summary>
    public static readonly DependencyProperty EnumTypeProperty = DependencyProperty.Register(
        nameof(EnumType),
        typeof(Type),
        typeof(EnumDataGridColumn),
        new PropertyMetadata(null, OnOptionsChanged),
        value => value is null || value is Type type && type.IsEnum);

    /// <summary>是否加入 null 选项。</summary>
    public static readonly DependencyProperty IncludeNullProperty = DependencyProperty.Register(
        nameof(IncludeNull),
        typeof(bool),
        typeof(EnumDataGridColumn),
        new PropertyMetadata(false, OnOptionsChanged));

    /// <summary>null 选项显示文本。</summary>
    public static readonly DependencyProperty NullDisplayTextProperty = DependencyProperty.Register(
        nameof(NullDisplayText),
        typeof(string),
        typeof(EnumDataGridColumn),
        new PropertyMetadata(string.Empty, OnOptionsChanged));

    public EnumDataGridColumn()
    {
        SelectedValuePath = nameof(EnumDataGridItem.Value);
        DisplayMemberPath = nameof(EnumDataGridItem.DisplayName);
    }

    /// <summary>获取或设置枚举类型。</summary>
    public Type? EnumType
    {
        get => (Type?)GetValue(EnumTypeProperty);
        set => SetValue(EnumTypeProperty, value);
    }

    /// <summary>获取或设置是否加入 null 选项。</summary>
    public bool IncludeNull
    {
        get => (bool)GetValue(IncludeNullProperty);
        set => SetValue(IncludeNullProperty, value);
    }

    /// <summary>获取或设置 null 选项显示文本。</summary>
    public string NullDisplayText
    {
        get => (string)GetValue(NullDisplayTextProperty);
        set => SetValue(NullDisplayTextProperty, value);
    }

    private static void OnOptionsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((EnumDataGridColumn)dependencyObject).RebuildItems();
    }

    private void RebuildItems()
    {
        var items = new List<EnumDataGridItem>();
        if (IncludeNull)
        {
            items.Add(new EnumDataGridItem(null, NullDisplayText));
        }

        if (EnumType is { } enumType)
        {
            foreach (var name in System.Enum.GetNames(enumType))
            {
                var field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static)!;
                if (field.GetCustomAttribute<BrowsableAttribute>() is { Browsable: false })
                {
                    continue;
                }

                var displayName = field.GetCustomAttribute<DisplayAttribute>()?.GetName()
                    ?? field.GetCustomAttribute<DescriptionAttribute>()?.Description
                    ?? name;
                items.Add(new EnumDataGridItem(System.Enum.Parse(enumType, name), displayName));
            }
        }

        ItemsSource = items;
    }
}
