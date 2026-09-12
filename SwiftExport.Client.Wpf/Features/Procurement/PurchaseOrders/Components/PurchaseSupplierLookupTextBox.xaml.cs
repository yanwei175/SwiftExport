using System.Windows;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExportClient.Resources.Controls;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Components;

/// <summary>采购供应商窄候选 Lookup；可分别承载实际供应商和客供件送达供应商。</summary>
public partial class PurchaseSupplierLookupTextBox : PurchaseLookupTextBoxBase
{
    public PurchaseSupplierLookupTextBox() => InitializeComponent();
    public static readonly DependencyProperty SelectedSupplierProperty = DependencyProperty.Register(nameof(SelectedSupplier), typeof(ProcurementSupplierCandidateDto), typeof(PurchaseSupplierLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty SelectedSupplierIdProperty = DependencyProperty.Register(nameof(SelectedSupplierId), typeof(Guid?), typeof(PurchaseSupplierLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public ProcurementSupplierCandidateDto? SelectedSupplier { get => (ProcurementSupplierCandidateDto?)GetValue(SelectedSupplierProperty); set => SetValue(SelectedSupplierProperty, value); }
    public Guid? SelectedSupplierId { get => (Guid?)GetValue(SelectedSupplierIdProperty); set => SetValue(SelectedSupplierIdProperty, value); }

    /// <summary>把 DataGrid 原生编辑焦点转交给内部搜索输入框，不改变单元格编辑事务。</summary>
    public void FocusEditor() => LookupEditor.FocusEditor();

    /// <summary>先把本次候选强制回写到行模型，再执行外部提交命令，避免命令读取到上一轮绑定值。</summary>
    private void OnLookupSelectionCommitted(object? sender, LookupSelectionCommittedEventArgs e)
    {
        if (e.SelectedItem is not ProcurementSupplierCandidateDto supplier) return;

        SetCurrentValue(SelectedSupplierProperty, supplier);
        SetCurrentValue(SelectedSupplierIdProperty, supplier.Uid);
        GetBindingExpression(SelectedSupplierProperty)?.UpdateSource();
        GetBindingExpression(SelectedSupplierIdProperty)?.UpdateSource();

        var parameter = SelectionCommittedCommandParameter ?? supplier;
        if (SelectionCommittedCommand?.CanExecute(parameter) == true)
            SelectionCommittedCommand.Execute(parameter);
    }
}
