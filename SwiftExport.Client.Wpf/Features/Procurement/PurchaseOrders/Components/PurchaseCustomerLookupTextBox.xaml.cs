using System.Windows;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Components;

/// <summary>采购客户窄候选 Lookup；业务搜索与提交由采购详情 ViewModel 负责。</summary>
public partial class PurchaseCustomerLookupTextBox : PurchaseLookupTextBoxBase
{
    public PurchaseCustomerLookupTextBox() => InitializeComponent();
    public static readonly DependencyProperty SelectedCustomerProperty = DependencyProperty.Register(nameof(SelectedCustomer), typeof(ProcurementCustomerCandidateDto), typeof(PurchaseCustomerLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty SelectedCustomerIdProperty = DependencyProperty.Register(nameof(SelectedCustomerId), typeof(Guid?), typeof(PurchaseCustomerLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public ProcurementCustomerCandidateDto? SelectedCustomer { get => (ProcurementCustomerCandidateDto?)GetValue(SelectedCustomerProperty); set => SetValue(SelectedCustomerProperty, value); }
    public Guid? SelectedCustomerId { get => (Guid?)GetValue(SelectedCustomerIdProperty); set => SetValue(SelectedCustomerIdProperty, value); }
}
