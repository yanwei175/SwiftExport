using Mapster;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

/// <summary>表示成品需求选择器左侧的一张待建立来源单。</summary>
public sealed class SourceDemandCandidateGroupModel
{
    /// <summary>来源类型，与来源 Uid 共同构成后端明细查询边界。</summary>
    public string SourceType { get; init; } = string.Empty;

    /// <summary>来源业务单据的稳定标识。</summary>
    public Guid SourceUid { get; init; }

    /// <summary>供用户识别的来源单号快照。</summary>
    public string SourceNo { get; init; } = string.Empty;

    /// <summary>当前尚可建立物料需求的来源行数量。</summary>
    public int CandidateCount { get; init; }

    /// <summary>来源单号为空时提供明确占位，避免列表出现不可选择的空白项。</summary>
    public string DisplaySourceNo => string.IsNullOrWhiteSpace(SourceNo) ? "未命名来源" : SourceNo;

    /// <summary>返回来源类型的业务显示名称。</summary>
    public string SourceTypeText => SourceType switch
    {
        "SO" => "销售订单",
        "MD" => "手工需求",
        "CR" => "投诉补货",
        _ => SourceType,
    };

    /// <summary>返回当前待建立行数文本。</summary>
    public string CountText => $"{CandidateCount} 条";

    /// <summary>把专用查询 DTO 转换为不包含可变选择状态的前端分组模型。</summary>
    public static SourceDemandCandidateGroupModel FromDto(SourceDemandCandidateGroupDto dto)
        => dto.Adapt<SourceDemandCandidateGroupModel>();
}
