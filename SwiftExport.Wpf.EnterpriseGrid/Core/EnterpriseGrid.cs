using System.Windows;
using System.Windows.Controls;
using SwiftExport.Wpf.EnterpriseGrid.Core.Internal;

namespace SwiftExport.Wpf.EnterpriseGrid.Core;

/// <summary>
/// 为原生 <see cref="DataGrid"/> 启用 Enterprise Grid 协调能力。
/// </summary>
public static class EnterpriseGrid
{
    /// <summary>
    /// 标识是否为当前 DataGrid 创建并启用内部协调器。
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(EnterpriseGrid),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty CoordinatorProperty = DependencyProperty.RegisterAttached(
        "Coordinator",
        typeof(GridCoordinator),
        typeof(EnterpriseGrid),
        new PropertyMetadata(null));

    /// <summary>
    /// 获取是否启用 Enterprise Grid。
    /// </summary>
    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    /// <summary>
    /// 设置是否启用 Enterprise Grid。
    /// </summary>
    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    internal static GridCoordinator? GetCoordinator(DataGrid dataGrid)
    {
        return (GridCoordinator?)dataGrid.GetValue(CoordinatorProperty);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DataGrid dataGrid)
        {
            throw new InvalidOperationException("EnterpriseGrid.IsEnabled 只能应用于原生 WPF DataGrid。");
        }

        if ((bool)e.NewValue)
        {
            if (GetCoordinator(dataGrid) is not null)
            {
                return;
            }

            var coordinator = new GridCoordinator(dataGrid);
            dataGrid.SetValue(CoordinatorProperty, coordinator);
            coordinator.Attach();
            return;
        }

        if (GetCoordinator(dataGrid) is { } existingCoordinator)
        {
            existingCoordinator.Dispose();
            dataGrid.ClearValue(CoordinatorProperty);
        }
    }
}
