namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;

/// <summary>成品需求接收命令；SourceVersion 是来源单据头的事实版本。</summary>
public sealed class SourceDemandReceptionCommand
{
    public Guid CommandId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceUid { get; set; }
    public string SourceNo { get; set; } = string.Empty;
    public int SourceVersion { get; set; }
    public Guid CustomerId { get; set; }
    /// <summary>要求当前活动需求与本次提交行完全一致；销售确认等全量事实提交使用此保护。</summary>
    public bool RejectUnsubmittedActiveLines { get; set; }
    public IReadOnlyList<SourceDemandReceptionItem> Items { get; set; } = [];
}
