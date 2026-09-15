using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;

public sealed partial class ProcurementOrderCommandService
{
    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, ConfirmProcurementOrderCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Validation, "确认命令不能为空。", "command"));
        return ExecuteAsync(uid, command.CommandId, "Confirm", command, async () =>
        {
            // 与解除引用保持同一锁序，防止确认过程中来源已回退或贡献集合改变。
            var facts = await reversalFacts.LockAsync(uid, ct);
            ProcurementConfirmationBuilder.RequireSourceReferences(facts);
            var head = facts.Head ?? throw new InvalidOperationException("采购单不存在。");
            ProcurementOrderPolicy.RequireDraft(head, command.ExpectedVersion);
            var lines = facts.Lines.Where(x => !x.IsDeleted).ToList();
            foreach (var line in lines)
            {
                if (!line.TaxPrice.HasValue) throw new InvalidOperationException("确认前必须明确含税单价；零价允许。");
                if (line.TaxAmount != ProcurementOrderPolicy.Amount(line.Quantity, line.TaxPrice)
                    || line.UnitAmount != ProcurementOrderPolicy.Amount(line.Quantity, line.UnitPrice))
                    throw new InvalidOperationException("采购金额与数量单价不一致。");
            }
            var basis = await confirmation.BuildAsync(head, lines, ct);
            if (basis.Fingerprint != command.ContextFingerprint)
                throw new ProcurementOperationException(ProcurementErrorCodes.BomBasisChanged, "确认依据已改变，请重新读取 BOM 上下文。", "ContextFingerprint");
            var bindings = command.ConsumptionBindings;
            if (bindings is null || bindings.Any(x => x is null || string.IsNullOrWhiteSpace(x.Path))
                || bindings.Select(x => (x.PurchaseOrderLineId, x.Path)).Distinct().Count() != bindings.Count)
                throw new InvalidOperationException("客供件消耗目标存在空值或重复路径。");
            var required = basis.Lines.SelectMany(l => l.Bom.Nodes.Where(n => n.IsCustomerSupplied).Select(n => (l.PurchaseOrderLineId, n.Path))).ToHashSet();
            if (!required.SetEquals(bindings.Select(x => (x.PurchaseOrderLineId, x.Path))))
                throw new InvalidOperationException("必须且只能为每个客供件完整路径指定消耗仓库。");
            var batchId = GuidSequentialGenerator.NewGuid();
            var snapshots = new List<PurchaseConfirmationLine>();
            var nodes = new List<PurchaseConfirmationNode>();
            var now = DateTime.Now;
            foreach (var item in basis.Lines)
            {
                var snapshot = new PurchaseConfirmationLine
                {
                    Uid = GuidSequentialGenerator.NewGuid(), ConfirmationId = batchId, PurchaseOrderId = uid,
                    PurchaseOrderLineId = item.PurchaseOrderLineId, LineVersion = item.LineVersion,
                    CommandId = command.CommandId, RootProductId = item.Bom.RootProductId, HasBom = item.Bom.HasBom,
                    Fingerprint = item.Bom.Fingerprint, ConfirmedAt = now
                };
                snapshots.Add(snapshot);
                foreach (var original in item.Bom.Nodes)
                {
                    var node = original.Adapt<PurchaseConfirmationNode>(mappings);
                    node.Uid = GuidSequentialGenerator.NewGuid(); node.ConfirmationLineId = snapshot.Uid;
                    if (node.IsCustomerSupplied)
                    {
                        var binding = bindings.Single(x => x.PurchaseOrderLineId == item.PurchaseOrderLineId && x.Path == node.Path);
                        var target = await confirmation.RequireWarehouseAsync(binding.WarehouseSiteId, ct);
                        node.ConsumptionWarehouseSiteId = target.Site.Uid;
                        node.WarehouseCodeSnapshot = target.Site.Code; node.WarehouseNameSnapshot = target.Site.Name;
                        node.ConsumptionSupplierId = target.Supplier.Uid;
                        node.SupplierCodeSnapshot = target.Supplier.SupplierCode; node.SupplierNameSnapshot = target.Supplier.SupplierName;
                    }
                    nodes.Add(node);
                }
            }
            head.ActiveConfirmationId = batchId; head.ConfirmedDate = now; head.ConfirmedBy = user.UserName;
            await repository.ConfirmAsync(head, snapshots, nodes, ct);
            head.Version++; head.ConfirmationStatus = ConfirmationStatusEnum.已确认;
            return Result(head, lines);
        }, ct);
    }
}
