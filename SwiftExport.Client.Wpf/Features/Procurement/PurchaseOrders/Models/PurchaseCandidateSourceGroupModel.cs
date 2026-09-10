using Mapster;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>表示采购选择器左侧的全部入口或一张待采购来源单。</summary>
public sealed class PurchaseCandidateSourceGroupModel
{
    /// <summary>是否为置顶的全部待采购物料入口。</summary>
    public bool IsAllSources { get; init; }
    /// <summary>来源类型；与来源 Uid 共同构成单来源查询身份。</summary>
    public string SourceType { get; init; } = string.Empty;
    /// <summary>来源业务单据 Uid；全部入口为空。</summary>
    public Guid? SourceUid { get; init; }
    /// <summary>来源业务单号快照。</summary>
    public string SourceNo { get; init; } = string.Empty;
    /// <summary>当前仍有剩余量的采购候选数量。</summary>
    public int CandidateCount { get; init; }

    /// <summary>返回来源入口主标题。</summary>
    public string DisplaySourceNo => IsAllSources
        ? "全部待采购物料"
        : string.IsNullOrWhiteSpace(SourceNo) ? "未命名来源" : SourceNo;

    /// <summary>返回来源类型的业务显示名称。</summary>
    public string SourceTypeText => IsAllSources
        ? "所有来源"
        : SourceType switch
        {
            "SO" => "销售订单",
            "MD" => "手工需求",
            "CR" => "投诉补货",
            _ => SourceType,
        };

    /// <summary>返回候选数量文本。</summary>
    public string CountText => $"{CandidateCount} 条";

    /// <summary>创建固定的全部待采购物料入口。</summary>
    public static PurchaseCandidateSourceGroupModel CreateAll(int candidateCount) => new()
    {
        IsAllSources = true,
        CandidateCount = candidateCount,
    };

    /// <summary>把采购候选来源契约映射为页面分组模型。</summary>
    public static PurchaseCandidateSourceGroupModel FromDto(PurchaseCandidateSourceGroupDto dto)
        => dto.Adapt<PurchaseCandidateSourceGroupModel>();
}
