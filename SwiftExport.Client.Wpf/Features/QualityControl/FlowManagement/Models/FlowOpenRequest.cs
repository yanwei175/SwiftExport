namespace SwiftExportClient.Features.QualityControl.FlowManagement.Models;

/// <summary>打开一个流量测试组；空身份表示新建，同一持久化身份只激活已有页签。</summary>
public sealed class FlowOpenRequest
{
    public Guid? HeaderId { get; init; }
}

