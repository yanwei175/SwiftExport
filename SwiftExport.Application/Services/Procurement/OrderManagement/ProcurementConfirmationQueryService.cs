using Mapster;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;

using SwiftExport.Core.Interfaces.Procurement;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;
/// <summary>独立装配已确认快照，不把大树附加到管理详情，也不复用当前上下文响应。</summary>
public sealed class ProcurementConfirmationQueryService(ProcurementOrderTransaction transaction,
    IProcurementOrderRepository orders, IProcurementConfirmationRepository snapshots, TypeAdapterConfig mappings)
    : IProcurementConfirmationQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementConfirmationSnapshotDto>> GetSnapshotAsync(Guid uid, CancellationToken ct = default)
        => transaction.ExecuteAsync("Read", async () =>
        {
            var head = await orders.GetHeadAsync(uid, ct) ?? throw new InvalidOperationException("独立采购单不存在。");
            var batch = await snapshots.GetLatestBatchAsync(uid, ct);
            if (!batch.HasValue) return new ProcurementConfirmationSnapshotDto { PurchaseOrderId = uid };
            var lines = await snapshots.GetLinesAsync(uid, batch.Value, ct);
            if (lines.Count == 0) throw new InvalidOperationException("已确认依据缺失，不能按当前 BOM 补造。");
            var nodes = (await snapshots.GetNodesAsync(uid, batch.Value, ct)).ToLookup(x => x.ConfirmationLineId);
            var result = new ProcurementConfirmationSnapshotDto { PurchaseOrderId = uid, ConfirmationId = batch.Value };
            foreach (var line in lines)
            {
                var item = line.Adapt<ProcurementConfirmedLineDto>(mappings);
                item.Nodes = nodes[line.Uid].Select(x => x.Adapt<ProcurementConfirmedNodeDto>(mappings)).ToList();
                result.Lines.Add(item);
            }
            return result;
        }, ct);
}
