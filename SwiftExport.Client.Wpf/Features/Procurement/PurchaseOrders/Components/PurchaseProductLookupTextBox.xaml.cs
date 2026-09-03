using System.Windows;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Components;

/// <summary>采购产品窄候选 Lookup；只负责产品选择，不信任或提交显示快照。</summary>
public partial class PurchaseProductLookupTextBox : PurchaseLookupTextBoxBase
{
    public PurchaseProductLookupTextBox() => InitializeComponent();
    public static readonly DependencyProperty SelectedProductProperty = DependencyProperty.Register(nameof(SelectedProduct), typeof(ProcurementProductCandidateDto), typeof(PurchaseProductLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty SelectedProductIdProperty = DependencyProperty.Register(nameof(SelectedProductId), typeof(Guid?), typeof(PurchaseProductLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public ProcurementProductCandidateDto? SelectedProduct { get => (ProcurementProductCandidateDto?)GetValue(SelectedProductProperty); set => SetValue(SelectedProductProperty, value); }
    public Guid? SelectedProductId { get => (Guid?)GetValue(SelectedProductIdProperty); set => SetValue(SelectedProductIdProperty, value); }
}
