using Mapster;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Application.Mappings;

/// <summary>独立采购商业输入的字段名适配；产品身份和生命周期由命令服务显式建立。</summary>
public sealed class IndependentPurchaseOrderMapping : IRegister
{
    /// <summary>注册单头业务名称到历史物理字段的映射，不接收审计或来源身份。</summary>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CreateIndependentPurchaseOrderCommand,PurchaseOrderHead>()
            .Ignore(x => x.Uid).Ignore(x => x.ExecutionRequestId)
            .Map(x => x.SoPerson,x => x.SalesPerson)
            .Map(x => x.PoPerson,x => x.PurchasePerson)
            .Map(x => x.InletrHoseType,x => x.InletHoseType)
            .Map(x => x.IMType,x => x.InstructionManualType)
            .Map(x => x.ZheKouBeiZhu,x => x.DiscountRemark)
            .AddDestinationTransform((string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
}
