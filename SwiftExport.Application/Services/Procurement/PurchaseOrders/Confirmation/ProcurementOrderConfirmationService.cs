using System.Data;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Confirmation;

/// <summary>采购确认用例；保持来源锁序、版本和确认依据校验及可串行化事务。</summary>
public sealed class ProcurementOrderConfirmationService(
    IProcurementOrderDocumentRepository repository, IProcurementOrderFactLockRepository reversalFacts,
    ProcurementConfirmationBuilder confirmation, ProcurementOrderReadSupport reads, ICurrentUser user,
    BusinessCommandExecutor executor, CommandPipeline pipeline, ProcurementOrderCommandSupport support)
    : IProcurementOrderConfirmationService
{
    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementConfirmationContextDto>> GetConfirmationContextAsync(Guid uid, CancellationToken ct = default)
        => reads.ReadConsistentAsync("Read", async () =>
        {
            var facts = ProcurementOrderCommandSupport.RequireLockedFacts(await reversalFacts.LockAsync(uid, ct));
            ProcurementConfirmationBuilder.RequireSourceReferences(facts);
            _ = facts.Head ?? throw new InvalidOperationException("采购单不存在。");
            return await confirmation.BuildAsync(facts, ct);
        }, ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, ConfirmProcurementOrderCommand command, CancellationToken ct = default) =>
        pipeline.ExecuteAsync<ConfirmProcurementOrderCommand, ProcurementOrderCommandResult>(command, command?.CommandId,
            "Procurement.Order.Confirm", token => support.AuthorizeAsync<ProcurementOrderCommandResult>("Confirm", token),
            token => ExecuteConfirmCommandAsync(uid, command!, token), ct);

    /// <summary>先校验路由身份，再把版本和确认依据纳入公共回执指纹。</summary>
    private Task<ServiceResult<CommandExecutionResult<ProcurementOrderCommandResult>>> ExecuteConfirmCommandAsync(
        Guid uid, ConfirmProcurementOrderCommand command, CancellationToken ct) => support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw ProcurementOperationException.Validation("采购身份不能为空。", "Uid");
            return await executor.ExecuteAsync(command.CommandId, "Procurement.Order.Confirm", uid,
                new { command.ExpectedVersion, command.ContextFingerprint },
                token => ConfirmInTransactionAsync(uid, command, token), ct, IsolationLevel.Serializable);
        }, ct);

    /// <summary>在公共事务内依既有锁序读取来源，核对版本和确认依据后确认并写业务审计。</summary>
    private async Task<ProcurementOrderCommandResult> ConfirmInTransactionAsync(Guid uid, ConfirmProcurementOrderCommand command, CancellationToken ct)
    {
        // 与解除引用保持同一锁序，防止确认过程中来源已回退或贡献集合改变。
        var facts = ProcurementOrderCommandSupport.RequireLockedFacts(await reversalFacts.LockAsync(uid, ct));
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
        ProcurementOrderCommandSupport.EnsureWriteSucceeded(await repository.ConfirmAsync(head, ct));
        head.Version++; head.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        var result = ProcurementOrderCommandSupport.Result(head, lines);
        await support.RecordSuccessAsync(command.CommandId, "Confirm", result, ct);
        return result;
    }
}
