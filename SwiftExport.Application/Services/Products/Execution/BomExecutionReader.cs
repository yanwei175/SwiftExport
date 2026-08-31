using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products.Execution;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products.Execution;

/// <summary>在调用方事务内按稳定身份完整读取当前图，交给纯构建器生成执行依据。</summary>
public sealed class BomExecutionReader(ServiceBaseContext context, IBomExecutionRepository repository) : IBomExecutionReader
{
    /// <summary>读取所有当前可达产品及父组成范围；异常或取消时绝不返回部分图。</summary>
    public async Task<BomExecutionSnapshotDto> ReadAsync(Guid rootProductId, CancellationToken cancellationToken = default)
    {
        if (rootProductId == Guid.Empty) throw new InvalidOperationException("BOM 根产品 Uid 不能为空。");
        if (context.Session.Transaction is null)
            throw new InvalidOperationException("BOM 执行依据必须在调用方事务内读取。");

        var units = await repository.GetUnitsAsync(cancellationToken);
        var products = new Dictionary<Guid, Product>();
        var lines = new List<Bom>();
        var pending = new Queue<Guid>();
        var scheduled = new HashSet<Guid> { rootProductId };
        pending.Enqueue(rootProductId);
        while (pending.TryDequeue(out var productId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (products.Count >= BomExecutionSnapshotBuilder.MaxNodes)
                throw new InvalidOperationException("BOM 可达产品超过安全读取上限，不能生成完整执行依据。");
            var product = await repository.GetProductAsync(productId, cancellationToken)
                ?? throw new InvalidOperationException($"BOM 产品 {productId} 不存在，不能生成执行依据。");
            products.Add(productId, product);
            var parentLines = await repository.GetLinesAsync(productId, cancellationToken);
            if (lines.Count + parentLines.Count > BomExecutionSnapshotBuilder.MaxNodes)
                throw new InvalidOperationException("BOM 组成读取超过安全节点上限，不能生成部分执行依据。");
            lines.AddRange(parentLines);
            // 只沿当前版本展开；旧版本行仍保留给构建器检查终端物料是否存在非法组成。
            foreach (var line in parentLines.Where(x => !x.IsDeleted && x.BomVersion == product.BomVersion
                         && string.Equals(x.BomNo, product.BomNo, StringComparison.Ordinal)))
            {
                if (line.ComponentProductId is Guid childId && childId != Guid.Empty && scheduled.Add(childId))
                    pending.Enqueue(childId);
            }
        }
        return BomExecutionSnapshotBuilder.Build(rootProductId, products, lines, units, cancellationToken);
    }
}
