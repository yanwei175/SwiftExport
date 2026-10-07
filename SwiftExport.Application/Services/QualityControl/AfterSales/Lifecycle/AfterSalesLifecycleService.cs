using SwiftExport.Contracts.DTOs.QualityControl;
using System.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Lifecycle;
using SwiftExport.Application.Services.QualityControl.AfterSales.Exceptions;
using SwiftExport.Application.Services.QualityControl.AfterSales.Internal;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using static SwiftExport.Application.Services.QualityControl.AfterSales.Internal.AfterSalesRules;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Lifecycle;

/// <summary>分别执行售后结案、重开和作废，保留原状态与来源阻断规则。</summary>
public sealed class AfterSalesLifecycleService(ServiceBaseContext context,
    IAfterSalesDocumentRepository documents, IBusinessOperationLogRepository logs,
    IPermissionEvaluationService permissions, ICurrentUser user, ISourceDemandReceptionService reception,
    CommandPipeline pipeline, BusinessCommandExecutor executor) : IAfterSalesLifecycleService
{
    private const string CloseType = "QualityControl.AfterSales.Close";
    private const string ReopenType = "QualityControl.AfterSales.Reopen";
    private const string VoidType = "QualityControl.AfterSales.Void";
    private readonly AfterSalesCommandAudit audit = new(documents, logs, user);

    /// <summary>结案入口先授权与验证；正数量单据继续明确阻断。</summary>
    public Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid,
        CloseAfterSalesCommand command, CancellationToken cancellationToken = default)
        => RunAsync(() => pipeline.ExecuteAsync<CloseAfterSalesCommand, AfterSalesCommandResult>(
            command, command?.CommandId, CloseType, AuthorizeAsync,
            token => ExecuteCloseCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>回执读取早于业务版本和状态检查。</summary>
    private async Task<ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>> ExecuteCloseCommandAsync(
        Guid uid, CloseAfterSalesCommand command, CancellationToken token)
    {
        if (uid == Guid.Empty) return InvalidTarget();
        var result = await executor.ExecuteAsync(command.CommandId, CloseType, uid,
            new { command.Version, command.Reason }, ct => CloseInTransactionAsync(uid, command, ct),
            token, IsolationLevel.Serializable);
        result.Value.AlreadyApplied = result.IsReplay;
        return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Ok(result);
    }

    /// <summary>锁头行并复核交付限制后写入结案状态及审计。</summary>
    private async Task<AfterSalesCommandResult> CloseInTransactionAsync(Guid uid,
        CloseAfterSalesCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var facts = await LoadForChangeAsync(uid, command.Version);
        RequireOpen(facts.Head);
        RequireReason(command.Reason);
        var positive = facts.Lines.Where(x => x.Quantity > 0).ToList();
        if (positive.Count > 0 && facts.Head.ConfirmationStatus != ConfirmationStatusEnum.已确认)
            Fail(ServiceErrorCodes.BusinessBlocked, "存在尚未确认的实物要求。");
        if (positive.Count > 0)
            Fail(AfterSalesErrorCodes.AftersalesDeliveryQueryUnavailable,
                "售后补货交付查询尚未接入，当前无法核验对应售后来源的 PackingList 是否已生效，不能结案。");
        facts.Head.DocumentStatus = DocumentStatusEnum.Closed;
        facts.Head.Conclusion = command.Reason;
        return await FinishChangeAsync(uid, command.CommandId, "Close", command.Reason, facts);
    }

    /// <summary>重开入口使用独立命令身份，仍要求原 Closed 状态。</summary>
    public Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid,
        ReopenAfterSalesCommand command, CancellationToken cancellationToken = default)
        => RunAsync(() => pipeline.ExecuteAsync<ReopenAfterSalesCommand, AfterSalesCommandResult>(
            command, command?.CommandId, ReopenType, AuthorizeAsync,
            token => ExecuteReopenCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>原请求版本和原因参与重开指纹。</summary>
    private async Task<ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>> ExecuteReopenCommandAsync(
        Guid uid, ReopenAfterSalesCommand command, CancellationToken token)
    {
        if (uid == Guid.Empty) return InvalidTarget();
        var result = await executor.ExecuteAsync(command.CommandId, ReopenType, uid,
            new { command.Version, command.Reason }, ct => ReopenInTransactionAsync(uid, command, ct),
            token, IsolationLevel.Serializable);
        result.Value.AlreadyApplied = result.IsReplay;
        return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Ok(result);
    }

    /// <summary>锁定原单，保持仅已结案单据可重开的规则。</summary>
    private async Task<AfterSalesCommandResult> ReopenInTransactionAsync(Guid uid,
        ReopenAfterSalesCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var facts = await LoadForChangeAsync(uid, command.Version);
        RequireReason(command.Reason);
        if (facts.Head.DocumentStatus != DocumentStatusEnum.Closed)
            Fail(ServiceErrorCodes.BusinessBlocked, "只有已结案售后单可以重开。");
        facts.Head.DocumentStatus = DocumentStatusEnum.Open;
        return await FinishChangeAsync(uid, command.CommandId, "Reopen", command.Reason, facts);
    }

    /// <summary>作废入口先检查独立生命周期权限，再查公共回执。</summary>
    public Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid,
        VoidAfterSalesCommand command, CancellationToken cancellationToken = default)
        => RunAsync(() => pipeline.ExecuteAsync<VoidAfterSalesCommand, AfterSalesCommandResult>(
            command, command?.CommandId, VoidType, AuthorizeAsync,
            token => ExecuteVoidCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>原请求版本和原因参与作废指纹。</summary>
    private async Task<ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>> ExecuteVoidCommandAsync(
        Guid uid, VoidAfterSalesCommand command, CancellationToken token)
    {
        if (uid == Guid.Empty) return InvalidTarget();
        var result = await executor.ExecuteAsync(command.CommandId, VoidType, uid,
            new { command.Version, command.Reason }, ct => VoidInTransactionAsync(uid, command, ct),
            token, IsolationLevel.Serializable, beforeTransaction: reception is SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document.ISourceDemandTransactionPreparation preparation
                ? ct => preparation.AcquireBeforeTransactionAsync([uid], [], ct) : null);
        result.Value.AlreadyApplied = result.IsReplay;
        return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Ok(result);
    }

    /// <summary>确认及活动来源仍会阻止售后作废。</summary>
    private async Task<AfterSalesCommandResult> VoidInTransactionAsync(Guid uid,
        VoidAfterSalesCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var facts = await LoadForChangeAsync(uid, command.Version);
        RequireOpen(facts.Head);
        RequireReason(command.Reason);
        if (facts.Head.ConfirmationStatus == ConfirmationStatusEnum.已确认 || await reception.HasActiveAsync("CR", uid))
            Fail(ServiceErrorCodes.BusinessBlocked, "必须先成功取消确认并处置来源要求，才能作废。");
        facts.Head.DocumentStatus = DocumentStatusEnum.Voided;
        return await FinishChangeAsync(uid, command.CommandId, "Void", command.Reason, facts);
    }

    /// <summary>锁定头行并取得原审计前快照；不为每种生命周期动作复制读取步骤。</summary>
    private async Task<ChangeFacts> LoadForChangeAsync(Guid uid, int version)
    {
        var before = await audit.SnapshotAsync(uid);
        var head = await documents.GetHeaderAsync(uid, true)
            ?? throw new AfterSalesException(ServiceErrorCodes.NotFound, "售后单不存在。");
        RequireVersion(head, version);
        var lines = (await documents.GetLinesAsync(uid, true)).Where(x => !x.IsDeleted).ToList();
        return new ChangeFacts(head, lines, before);
    }

    /// <summary>状态写入、业务审计及完整回执由同一个执行器事务完成。</summary>
    private async Task<AfterSalesCommandResult> FinishChangeAsync(Guid uid, Guid commandId,
        string operation, string? reason, ChangeFacts facts)
    {
        if (!await documents.WriteHeaderAsync(facts.Head, false))
            Fail(ServiceErrorCodes.VersionConflict, "售后单状态已变化。");
        var result = Result(facts.Head, facts.Lines.Any(x => x.Quantity > 0));
        var after = await audit.SnapshotAsync(uid);
        await audit.WriteAsync(uid, commandId, operation, reason, result, facts.Before, after);
        return result;
    }

    /// <summary>路由目标为空时在回执事务前拒绝。</summary>
    private static ServiceResult<CommandExecutionResult<AfterSalesCommandResult>> InvalidTarget()
        => ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Fail(
            ServiceErrorCodes.Validation, "单据身份不能为空。", "Uid");

    /// <summary>模块访问不授予售后生命周期写权限。</summary>
    private async Task<ServiceResult<AfterSalesCommandResult>?> AuthorizeAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor)
            return ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Unauthenticated,
                "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.AfterSalesLifecycle, token)
            ? null : ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Forbidden,
                "当前账号没有售后生命周期权限。");
    }

    /// <summary>保留售后业务码并透传公共命令的冲突与结果不明。</summary>
    private async Task<ServiceResult<AfterSalesCommandResult>> RunAsync(
        Func<Task<ServiceResult<AfterSalesCommandResult>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex)
        { return ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex)
        { return ServiceResult<AfterSalesCommandResult>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (AfterSalesException ex)
        { return ServiceResult<AfterSalesCommandResult>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (SourceDemandReceptionException ex)
        { return ServiceResult<AfterSalesCommandResult>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<AfterSalesLifecycleService>()
                .LogError(ex, "售后生命周期命令结果需按原 CommandId 核实。");
            return ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Unexpected,
                "售后生命周期命令失败，请核实命令结果。");
        }
    }

    /// <summary>生命周期命令共用的锁定事实和审计前快照。</summary>
    private sealed record ChangeFacts(AfterSalesHeader Head, List<AfterSalesLine> Lines, AfterSalesDetailDto? Before);
}
