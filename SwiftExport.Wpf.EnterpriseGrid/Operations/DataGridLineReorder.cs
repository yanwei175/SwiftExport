using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Operations;

/// <summary>
/// 为原生 <see cref="DataGrid"/> 注入紧凑的单据行上移、下移命令列。
/// 控件只负责命令转发，不读取或修改业务行号和集合。
/// </summary>
public static class DataGridLineReorder
{
    /// <summary>是否启用行排序操作列。默认关闭。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DataGridLineReorder),
        new PropertyMetadata(false, OnConfigurationChanged));

    /// <summary>接收当前行对象的上移命令。</summary>
    public static readonly DependencyProperty MoveUpCommandProperty = DependencyProperty.RegisterAttached(
        "MoveUpCommand",
        typeof(ICommand),
        typeof(DataGridLineReorder),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>接收当前行对象的下移命令。</summary>
    public static readonly DependencyProperty MoveDownCommandProperty = DependencyProperty.RegisterAttached(
        "MoveDownCommand",
        typeof(ICommand),
        typeof(DataGridLineReorder),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>操作列列头。默认为空。</summary>
    public static readonly DependencyProperty ColumnHeaderProperty = DependencyProperty.RegisterAttached(
        "ColumnHeader",
        typeof(object),
        typeof(DataGridLineReorder),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>操作列宽度。默认 56 DIP。</summary>
    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.RegisterAttached(
        "ColumnWidth",
        typeof(DataGridLength),
        typeof(DataGridLineReorder),
        new PropertyMetadata(new DataGridLength(56d), OnConfigurationChanged));

    /// <summary>操作列显示位置。默认位于第一列。</summary>
    public static readonly DependencyProperty ColumnDisplayIndexProperty = DependencyProperty.RegisterAttached(
        "ColumnDisplayIndex",
        typeof(int),
        typeof(DataGridLineReorder),
        new PropertyMetadata(0, OnConfigurationChanged));

    /// <summary>DataGrid 只读时是否仍显示禁用的操作列。默认隐藏。</summary>
    public static readonly DependencyProperty ShowWhenReadOnlyProperty = DependencyProperty.RegisterAttached(
        "ShowWhenReadOnly",
        typeof(bool),
        typeof(DataGridLineReorder),
        new PropertyMetadata(false, OnConfigurationChanged));

    private static readonly DependencyPropertyKey CanReorderPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "CanReorder",
            typeof(bool),
            typeof(DataGridLineReorder),
            new PropertyMetadata(false));

    internal static readonly DependencyProperty CanReorderProperty = CanReorderPropertyKey.DependencyProperty;

    /// <summary>获取是否启用行排序操作列。</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>设置是否启用行排序操作列。</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>获取上移命令。</summary>
    public static ICommand? GetMoveUpCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(MoveUpCommandProperty);

    /// <summary>设置上移命令。</summary>
    public static void SetMoveUpCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(MoveUpCommandProperty, value);

    /// <summary>获取下移命令。</summary>
    public static ICommand? GetMoveDownCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(MoveDownCommandProperty);

    /// <summary>设置下移命令。</summary>
    public static void SetMoveDownCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(MoveDownCommandProperty, value);

    /// <summary>获取操作列列头。</summary>
    public static object? GetColumnHeader(DependencyObject element) => element.GetValue(ColumnHeaderProperty);

    /// <summary>设置操作列列头。</summary>
    public static void SetColumnHeader(DependencyObject element, object? value) =>
        element.SetValue(ColumnHeaderProperty, value);

    /// <summary>获取操作列宽度。</summary>
    public static DataGridLength GetColumnWidth(DependencyObject element) =>
        (DataGridLength)element.GetValue(ColumnWidthProperty);

    /// <summary>设置操作列宽度。</summary>
    public static void SetColumnWidth(DependencyObject element, DataGridLength value) =>
        element.SetValue(ColumnWidthProperty, value);

    /// <summary>获取操作列显示位置。</summary>
    public static int GetColumnDisplayIndex(DependencyObject element) =>
        (int)element.GetValue(ColumnDisplayIndexProperty);

    /// <summary>设置操作列显示位置。</summary>
    public static void SetColumnDisplayIndex(DependencyObject element, int value) =>
        element.SetValue(ColumnDisplayIndexProperty, value);

    /// <summary>获取只读时是否显示操作列。</summary>
    public static bool GetShowWhenReadOnly(DependencyObject element) =>
        (bool)element.GetValue(ShowWhenReadOnlyProperty);

    /// <summary>设置只读时是否显示操作列。</summary>
    public static void SetShowWhenReadOnly(DependencyObject element, bool value) =>
        element.SetValue(ShowWhenReadOnlyProperty, value);

    internal static void SetCanReorderFromView(DataGrid dataGrid, bool value) =>
        dataGrid.SetValue(CanReorderPropertyKey, value);

    private static void OnConfigurationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DataGrid dataGrid)
        {
            throw new InvalidOperationException("DataGridLineReorder 附加属性只能应用于原生 WPF DataGrid。");
        }

        EnterpriseGridCore.GetCoordinator(dataGrid)?.LineReorder.ApplyConfiguration();
    }
}
