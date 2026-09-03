using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购详情行集合；统一负责加载、新增、选择、变更集和整表校验。</summary>
public sealed class PurchaseOrderLineListModel
    : BaseListModel<PurchaseOrderLineEditModel, PurchaseOrderLineDetailDto>
{
    public PurchaseOrderLineListModel() : base(_ => new PurchaseOrderLineEditModel())
    {
    }
}
