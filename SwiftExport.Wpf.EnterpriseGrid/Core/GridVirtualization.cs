using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExport.Wpf.EnterpriseGrid.Core;

/// <summary>为原生 DataGrid 启用可恢复的行列 Recycling 虚拟化配置。</summary>
public static class GridVirtualization
{
    /// <summary>标识是否应用 Enterprise Grid 推荐虚拟化配置。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(GridVirtualization),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty OriginalStateProperty = DependencyProperty.RegisterAttached(
        "OriginalState",
        typeof(VirtualizationState),
        typeof(GridVirtualization),
        new PropertyMetadata(null));

    /// <summary>获取是否应用推荐虚拟化配置。</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>设置是否应用推荐虚拟化配置。</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DataGrid dataGrid)
        {
            throw new InvalidOperationException("GridVirtualization.IsEnabled 只能应用于原生 WPF DataGrid。");
        }

        if ((bool)e.NewValue)
        {
            if (dataGrid.GetValue(OriginalStateProperty) is VirtualizationState)
            {
                return;
            }

            dataGrid.SetValue(OriginalStateProperty, VirtualizationState.Capture(dataGrid));
            dataGrid.SetCurrentValue(DataGrid.EnableRowVirtualizationProperty, true);
            dataGrid.SetCurrentValue(DataGrid.EnableColumnVirtualizationProperty, true);
            VirtualizingPanel.SetIsVirtualizing(dataGrid, true);
            VirtualizingPanel.SetVirtualizationMode(dataGrid, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(dataGrid, true);
            return;
        }

        if (dataGrid.GetValue(OriginalStateProperty) is VirtualizationState original)
        {
            original.Restore(dataGrid);
            dataGrid.ClearValue(OriginalStateProperty);
        }
    }

    private sealed record VirtualizationState(
        PropertyValueState RowVirtualization,
        PropertyValueState ColumnVirtualization,
        PropertyValueState IsVirtualizing,
        PropertyValueState VirtualizationMode,
        PropertyValueState CanContentScroll)
    {
        public static VirtualizationState Capture(DataGrid dataGrid) => new(
            PropertyValueState.Capture(dataGrid, DataGrid.EnableRowVirtualizationProperty),
            PropertyValueState.Capture(dataGrid, DataGrid.EnableColumnVirtualizationProperty),
            PropertyValueState.Capture(dataGrid, VirtualizingPanel.IsVirtualizingProperty),
            PropertyValueState.Capture(dataGrid, VirtualizingPanel.VirtualizationModeProperty),
            PropertyValueState.Capture(dataGrid, ScrollViewer.CanContentScrollProperty));

        public void Restore(DataGrid dataGrid)
        {
            RowVirtualization.Restore(dataGrid, DataGrid.EnableRowVirtualizationProperty);
            ColumnVirtualization.Restore(dataGrid, DataGrid.EnableColumnVirtualizationProperty);
            IsVirtualizing.Restore(dataGrid, VirtualizingPanel.IsVirtualizingProperty);
            VirtualizationMode.Restore(dataGrid, VirtualizingPanel.VirtualizationModeProperty);
            CanContentScroll.Restore(dataGrid, ScrollViewer.CanContentScrollProperty);
        }
    }

    private sealed record PropertyValueState(object LocalValue, BindingBase? Binding)
    {
        public static PropertyValueState Capture(DependencyObject target, DependencyProperty property) =>
            new(target.ReadLocalValue(property), BindingOperations.GetBindingBase(target, property));

        public void Restore(DependencyObject target, DependencyProperty property)
        {
            if (Binding is not null)
            {
                BindingOperations.SetBinding(target, property, Binding);
            }
            else if (ReferenceEquals(LocalValue, DependencyProperty.UnsetValue))
            {
                target.ClearValue(property);
            }
            else
            {
                target.SetValue(property, LocalValue);
            }
        }
    }
}
