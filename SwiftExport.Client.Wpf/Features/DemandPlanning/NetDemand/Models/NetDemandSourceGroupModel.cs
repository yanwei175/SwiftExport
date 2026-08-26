using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

/// <summary>
/// 表示净需求左侧来源导航中的一张来源单及其匹配净需求数量。
/// </summary>
public sealed class NetDemandSourceGroupModel
{
    private NetDemandSourceGroupModel(Guid sourceUid, string sourceType, string sourceNo, int demandCount)
    {
        SourceUid = sourceUid;
        SourceType = sourceType;
        SourceNo = sourceNo;
        DemandCount = demandCount;
    }

    /// <summary>
    /// 来源业务单据的权威标识。
    /// </summary>
    public Guid SourceUid { get; }

    /// <summary>
    /// 来源类型代码。
    /// </summary>
    public string SourceType { get; }

    /// <summary>
    /// 来源单号快照。
    /// </summary>
    public string SourceNo { get; }

    /// <summary>
    /// 当前来源导航筛选下的净需求条数。
    /// </summary>
    public int DemandCount { get; }

    /// <summary>
    /// 返回适合导航列表显示的来源类型名称。
    /// </summary>
    public string SourceTypeText => SourceType switch
    {
        "SO" => "销售订单",
        "MD" => "手工需求",
        "CR" => "投诉补货",
        _ => SourceType,
    };

    /// <summary>
    /// 返回来源单号；来源快照异常为空时提供明确占位文本。
    /// </summary>
    public string DisplaySourceNo => string.IsNullOrWhiteSpace(SourceNo) ? "未命名来源" : SourceNo;

    /// <summary>
    /// 返回来源单包含的匹配净需求数量文本。
    /// </summary>
    public string CountText => $"{DemandCount} 条";

    /// <summary>
    /// 将后端来源分组传输对象转换为前端只读模型。
    /// </summary>
    public static NetDemandSourceGroupModel FromDto(NetDemandSourceGroupDto dto)
        => new(dto.SourceUid, dto.SourceType ?? string.Empty, dto.SourceNo ?? string.Empty, dto.DemandCount);
}
