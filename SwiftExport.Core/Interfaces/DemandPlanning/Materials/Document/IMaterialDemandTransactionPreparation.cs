namespace SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;

/// <summary>物料建议、采购创建及关联用例在首次事务快照前发现需求绑定并准备全局命令项与来源资源；不执行业务校验或写入。</summary>
public interface IMaterialDemandTransactionPreparation
{
    /// <summary>已知来源、成品需求、净需求及命令项整组准备；命令项含空Guid，超时返回空，取消原样传播，父事务释放后归还租约。</summary>
    Task<IAsyncDisposable?> AcquireMaterialBeforeTransactionAsync(IReadOnlyCollection<Guid> sourceUids,
        IReadOnlyCollection<Guid> grossDemandIds, IReadOnlyCollection<Guid> netDemandIds,
        IReadOnlyCollection<Guid> commandItemIds, CancellationToken cancellationToken = default);
}
