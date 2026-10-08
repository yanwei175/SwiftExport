namespace SwiftExportClient.Features.QualityControl.FlowManagement.Models;

/// <summary>字典选择项；候选显示当前名称，历史显示保留在行快照中。</summary>
public sealed class FlowDictionaryOption
{
    public Guid Uid { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;
}

