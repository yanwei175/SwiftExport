namespace SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;

/// <summary>在来源用例首次事务快照前准备来源范围与成品需求身份；不做业务校验、写入或重试业务委托。</summary>
public interface ISourceDemandTransactionPreparation
{
    /// <summary>已知来源UID及成品需求UID共同参与准备；竞争预算耗尽返回空，取消原样传播，租约在父事务释放后归还。</summary>
    Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(IReadOnlyCollection<Guid> sourceUids,
        IReadOnlyCollection<Guid> grossDemandIds, CancellationToken cancellationToken = default);
}
