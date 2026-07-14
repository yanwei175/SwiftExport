using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Operations;

/// <summary>为原生 DataGrid 提供可撤销的单元格操作、Excel 式复制粘贴和向下填充。</summary>
public static class GridOperations
{
    /// <summary>启用单元格提交历史、Ctrl+Z、Ctrl+C、Ctrl+V、Ctrl+D 和 Delete 清空。默认关闭。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(GridOperations),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyPropertyKey CanUndoPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "CanUndo",
        typeof(bool),
        typeof(GridOperations),
        new PropertyMetadata(false));

    /// <summary>当前 Grid 是否存在可撤销的已提交操作。</summary>
    public static readonly DependencyProperty CanUndoProperty = CanUndoPropertyKey.DependencyProperty;

    /// <summary>将矩形选区每列的首行值填充到该列下方，默认快捷键 Ctrl+D。</summary>
    public static RoutedUICommand FillDownCommand { get; } = new(
        "向下填充",
        nameof(FillDownCommand),
        typeof(GridOperations),
        new InputGestureCollection { new KeyGesture(Key.D, ModifierKeys.Control) });

    /// <summary>获取是否启用 Excel 式操作。</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>设置是否启用 Excel 式操作。</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>获取当前是否可以撤销。</summary>
    public static bool GetCanUndo(DependencyObject element) => (bool)element.GetValue(CanUndoProperty);

    internal static void SetCanUndoFromView(DataGrid dataGrid, bool value) =>
        dataGrid.SetValue(CanUndoPropertyKey, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGridCore.GetCoordinator(dataGrid)?.Operations.RefreshEnabledState();
        }
    }
}
