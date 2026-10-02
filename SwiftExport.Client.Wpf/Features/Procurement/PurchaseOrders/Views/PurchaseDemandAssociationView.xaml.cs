using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>关联工作区视图；只负责选择事件桥接和原生 WPF 编辑事务，不调用业务 API。</summary>
public partial class PurchaseDemandAssociationView : UserControl
{
    /// <summary>初始化工作区视觉树，数据上下文由 Shell 页签提供。</summary>
    public PurchaseDemandAssociationView() => InitializeComponent();
    /// <summary>显式用户选择采购行后调用查询命令；初始化或集合清空不触发查询。</summary>
    private async void OnLineSelected(object sender, SelectionChangedEventArgs args)
    {
        if (DataContext is PurchaseDemandAssociationViewModel vm && args.AddedItems.OfType<ProcurementDemandAssociationLineDto>().FirstOrDefault() is { } line
            && vm.SelectLineCommand.CanExecute(line)) await vm.SelectLineCommand.ExecuteAsync(line);
    }
    /// <summary>点击或键盘激活前提交网格编辑，转换错误不能被旧绑定值绕过。</summary>
    private async void OnPrepareClicked(object sender, RoutedEventArgs args)
    {
        if (DataContext is not PurchaseDemandAssociationViewModel vm) return;
        if (!PlanGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !PlanGrid.CommitEdit(DataGridEditingUnit.Row, true) || HasEditorError(PlanGrid)
            || (ReferenceEquals(sender, AddButton) && Validation.GetHasError(AllocationInput)))
        { vm.ReportEditorError(); return; }
        IRelayCommand command = ReferenceEquals(sender, AddButton) ? vm.AddAllocationCommand
            : ReferenceEquals(sender, PreviewButton) ? vm.PreviewCommand : vm.SubmitCommand;
        if (!command.CanExecute(null)) return;
        if (command is IAsyncRelayCommand asyncCommand) await asyncCommand.ExecuteAsync(null);
        else command.Execute(null);
    }
    /// <summary>只检查视觉树中的 WPF 转换错误，领域验证仍在 ViewModel 与服务端。</summary>
    private static bool HasEditorError(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (HasEditorError(VisualTreeHelper.GetChild(element, index))) return true;
        return false;
    }
}
