using System.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Confirmation;
using SwiftExport.Application.Services.QualityControl.AfterSales.Exceptions;
using SwiftExport.Application.Services.QualityControl.AfterSales.Internal;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using static SwiftExport.Application.Services.QualityControl.AfterSales.Internal.AfterSalesRules;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Confirmation;

/// <summary>在公共回执事务中撤回售后实物需求确认，来源退出参与同一事务。</summary>
public sealed class AfterSalesCancellationService(ServiceBaseContext context,
    IAfterSalesDocumentRepository documents, IBusinessOperationLogRepository logs,
    IUserPermissionService permissions, ICurrentUser user, ISourceDemandReceptionService reception,
    CommandPipeline pipeline, BusinessCommandExecutor executor) : IAfterSalesCancellationService
{
    private const string CommandType = "QualityControl.AfterSales.CancelDemandConfirmation";
    private readonly AfterSalesCommandAudit audit = new(documents, logs, user);

    /// <summary>先授权和验证，再查询公共回执或首次执行取消。</summary>
    public Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid,
        CancelAfterSalesDemandConfirmationCommand command, CancellationToken cancellationToken = default)
        => RunAsync(() => pipeline.ExecuteAsync<CancelAfterSalesDemandConfirmationCommand, AfterSalesCommandResult>(
            command, command?.CommandId, CommandType, AuthorizeAsync,
            token => ExecuteCancellationCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>路由目标、原版本和原因进入指纹；状态变化留到首次事务内检查。</summary>
    private async Task<ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>> ExecuteCancellationCommandAsync(
        Guid uid, CancelAfterSalesDemandConfirmationCommand command, CancellationToken token)
    {
        if (uid == Guid.Empty)
            return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Fail(
                ServiceErrorCodes.Validation, "单据身份不能为空。", "Uid");
        var result = await executor.ExecuteAsync(command.CommandId, CommandType, uid,
            new { command.Version, command.Reason },
            ct => CancelInTransactionAsync(uid, command, ct), token, IsolationLevel.Serializable);
        result.Value.AlreadyApplied = result.IsReplay;
        return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Ok(result);
    }

    /// <summary>锁头行并验证原版本、状态和来源安全，再写状态及前后审计。</summary>
    private async Task<AfterSalesCommandResult> CancelInTransactionAsync(Guid uid,
        CancelAfterSalesDemandConfirmationCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = await audit.SnapshotAsync(uid);
        var head = await documents.GetHeaderAsync(uid, true)
            ?? throw new AfterSalesException("NOT_FOUND", "售后单不存在。");
        RequireVersion(head, command.Version);
        var lines = (await documents.GetLinesAsync(uid, true)).Where(x => !x.IsDeleted).ToList();
        RequireOpen(head);
        RequireReason(command.Reason);
        if (head.ConfirmationStatus != ConfirmationStatusEnum.已确认)
            Fail("BUSINESS_BLOCKED", "当前未确认实物需求。");
        await reception.CancelConfirmationAsync("CR", uid,
            lines.Where(x => x.Quantity > 0).Select(x => x.Uid).ToList(), command.Reason);
        head.ConfirmationStatus = ConfirmationStatusEnum.待确认;
        head.DemandConfirmedAt = null;
        head.DemandConfirmedBy = null;
        if (!await documents.WriteHeaderAsync(head, false)) Fail("VERSION_CONFLICT", "售后单状态已变化。");
        var result = Result(head, lines.Any(x => x.Quantity > 0));
        var after = await audit.SnapshotAsync(uid);
        await audit.WriteAsync(uid, command.CommandId, "CancelConfirmation", command.Reason, result, before, after);
        return result;
    }

    /// <summary>取消确认权限独立于模块和正向确认权限。</summary>
    private async Task<ServiceResult<AfterSalesCommandResult>?> AuthorizeAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor)
            return ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Unauthenticated,
                "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.AfterSalesCancelConfirmation, token)
            ? null : ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Forbidden,
                "当前账号没有售后取消确认权限。");
    }

    /// <summary>保留售后业务错误，公共命令冲突及结果不明直接透传。</summary>
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
            context.LoggerFactory.CreateLogger<AfterSalesCancellationService>()
                .LogError(ex, "售后取消确认结果需按原 CommandId 核实。");
            return ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Unexpected,
                "售后取消确认失败，请核实命令结果。");
        }
    }
}
