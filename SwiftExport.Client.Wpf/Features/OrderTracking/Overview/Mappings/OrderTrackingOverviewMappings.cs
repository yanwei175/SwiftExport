using Mapster;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
namespace SwiftExportClient.Features.OrderTracking.Overview.Mappings;
/// <summary>只复制基础行结构；摘要、权限和读取状态由独立加载器明确接纳。</summary>
public sealed class OrderTrackingOverviewMappings : IRegister
{
    /// <summary>注册来源和需求只读行映射，禁止把UI读取状态从基础DTO推断出来。</summary>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<OrderTrackingSourceGroupDto, OrderTrackingSourceRowModel>()
            .Ignore(x => x.Summary).Ignore(x => x.SummaryState).Ignore(x => x.Access).Ignore(x => x.BusinessDate).Ignore(x => x.ReadAt);
        config.NewConfig<OrderTrackingListItemDto, OrderTrackingDemandRowModel>().Ignore(x => x.QueryMatched).Ignore(x => x.MatchState)
            .Ignore(x => x.Summary).Ignore(x => x.SummaryState).Ignore(x => x.Access).Ignore(x => x.BusinessDate).Ignore(x => x.ReadAt);
    }
}
