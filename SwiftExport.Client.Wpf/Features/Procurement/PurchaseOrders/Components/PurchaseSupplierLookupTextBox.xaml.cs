using System.Windows;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Components;

/// <summary>采购供应商窄候选 Lookup；可分别承载实际供应商和客供件送达供应商。</summary>
public partial class PurchaseSupplierLookupTextBox : PurchaseLookupTextBoxBase
{
    public PurchaseSupplierLookupTextBox() => InitializeComponent();
    public static readonly DependencyProperty SelectedSupplierProperty = DependencyProperty.Register(nameof(SelectedSupplier), typeof(ProcurementSupplierCandidateDto), typeof(PurchaseSupplierLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty SelectedSupplierIdProperty = DependencyProperty.Register(nameof(SelectedSupplierId), typeof(Guid?), typeof(PurchaseSupplierLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public ProcurementSupplierCandidateDto? SelectedSupplier { get => (ProcurementSupplierCandidateDto?)GetValue(SelectedSupplierProperty); set => SetValue(SelectedSupplierProperty, value); }
    public Guid? SelectedSupplierId { get => (Guid?)GetValue(SelectedSupplierIdProperty); set => SetValue(SelectedSupplierIdProperty, value); }
}
