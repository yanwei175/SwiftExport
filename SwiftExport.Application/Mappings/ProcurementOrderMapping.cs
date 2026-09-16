using Mapster;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Products.Execution;
using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Application.Mappings;
/// <summary>新采购限定结构映射；固定身份和确认事务由业务服务负责。</summary>
public sealed class ProcurementOrderMapping : IRegister
{
    /// <summary>注册商务创建输入与旧确认历史的只读映射，新确认不生成结构节点。</summary>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CreateIndependentPurchaseOrderLineCommand, PurchaseOrderLine>()
            .IgnoreNonMapped(true)
            .Map(d => d.UnitPrice, s => s.UnitPrice).Map(d => d.TaxPrice, s => s.TaxPrice)
            .Map(d => d.PlannedDeliveryDate, s => s.PlannedDeliveryDate);
        config.NewConfig<PurchaseConfirmationNode, BomExecutionNodeDto>();
        config.NewConfig<PurchaseConfirmationLine, ProcurementConfirmedLineDto>();
        config.NewConfig<PurchaseConfirmationNode, ProcurementConfirmedNodeDto>()
            .Map(d => d.Structure, s => s);
    }
}
