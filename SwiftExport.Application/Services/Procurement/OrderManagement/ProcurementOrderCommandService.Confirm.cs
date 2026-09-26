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
            var basis = await confirmation.BuildAsync(facts, ct);
            if (basis.Fingerprint != command.ContextFingerprint)
                throw new ProcurementOperationException(ProcurementErrorCodes.BomBasisChanged, "确认依据已改变，请重新读取采购确认预览。", "ContextFingerprint");
            // 每次确认保留独立批次身份；不再创建 BOM 或消耗仓快照。
            var batchId = GuidSequentialGenerator.NewGuid();
            var now = DateTime.Now;
            head.ActiveConfirmationId = batchId; head.ConfirmedDate = now; head.ConfirmedBy = user.UserName;
            await repository.ConfirmAsync(head, ct);
            head.Version++; head.ConfirmationStatus = ConfirmationStatusEnum.已确认;
            return Result(head, lines);
        }, ct);
    }
}
