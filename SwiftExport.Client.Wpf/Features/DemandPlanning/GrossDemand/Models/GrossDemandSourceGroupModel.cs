using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;

public sealed partial class GrossDemandSourceGroupModel
{
    private GrossDemandSourceGroupModel(Guid sourceUid, string sourceType, string sourceNo, int demandCount)
    {
        SourceUid = sourceUid;
        SourceType = sourceType;
        SourceNo = sourceNo;
        DemandCount = demandCount;
    }

    public Guid SourceUid { get; }
    public string SourceType { get; }
    public string SourceNo { get; }
    public int DemandCount { get; }
    public string SourceTypeText => SourceType switch
    {
        "SO" => "销售",
        "MD" => "手工",
        "CR" => "投诉",
        _ => SourceType,
    };
    public string DisplaySourceNo => string.IsNullOrWhiteSpace(SourceNo) ? "未命名来源" : SourceNo;
    public string CountText => $"{DemandCount} 条";

    public static GrossDemandSourceGroupModel FromDto(GrossDemandSourceGroupDto dto)
        => new(dto.SourceUid, dto.SourceType ?? string.Empty, dto.SourceNo ?? string.Empty, dto.DemandCount);
}
