using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;

/// <summary>接收来源领域认可的实物需求，并在调用方事务内维护来源需求一致性。</summary>
public interface ISourceDemandReceptionService
{
    /// <summary>幂等接收或更新明确提交的来源需求行；未包含的历史行不会被隐式终止。</summary>
    Task<SourceDemandReceptionResult> ReceiveAsync(SourceDemandReceptionCommand command);
    /// <summary>按既有历史安全规则退出来源确认，并返回删除与作废数量。</summary>
    Task<SourceDemandExitResult> CancelConfirmationAsync(string sourceType, Guid sourceUid, IReadOnlyCollection<Guid> sourceLineIds, string reason);
    /// <summary>关闭尚未进入下游执行的活动来源需求。</summary>
    Task<int> ClosePendingAsync(string sourceType, Guid sourceUid, IReadOnlyCollection<Guid> sourceLineIds, string reason);
    /// <summary>判断来源是否仍存在活动需求，用于保护来源草稿删除。</summary>
    Task<bool> HasActiveAsync(string sourceType, Guid sourceUid);
}

