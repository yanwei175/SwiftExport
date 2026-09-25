using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
using SwiftExport.Application.Services.QualityControl.AfterSales.Policies;
using SwiftExport.Application.Services.QualityControl.AfterSales.Exceptions;
using System.Data;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Confirmation;
using SwiftExport.Application.Services.QualityControl;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Confirmation;

/// <summary>售后确认的外层事务所有者；公共命令锁之后按头行和来源顺序写事实及完整回执。</summary>
public sealed class AfterSalesConfirmationService(
    ServiceBaseContext context,
    IAfterSalesDocumentRepository repository,
    IAfterSalesLookupRepository lookups,
    ISourceDemandReceptionService reception,
    IBusinessOperationLogRepository logs,
    IPermissionEvaluationService permissions,
    ICurrentUser user,
    CommandPipeline pipeline,
    BusinessCommandExecutor executor) : IAfterSalesConfirmationService
{
    private const string CommandType = "QualityControl.AfterSales.ConfirmDemand";

    /// <summary>先校验有效登录和品质模块权限，再验证命令并查找公共回执。</summary>
    public Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid,
        ConfirmAfterSalesDemandCommand command, CancellationToken cancellationToken = default)
        => RunAsync(() => pipeline.ExecuteAsync<ConfirmAfterSalesDemandCommand, AfterSalesCommandResult>(
            command, command?.CommandId, CommandType, AuthorizeAsync,
            token => ExecuteConfirmCommandAsync(uid, command!, token), cancellationToken));

    /// <summary>完整请求版本参与指纹；沿用售后原有 Serializable 隔离级别。</summary>
    private async Task<ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>> ExecuteConfirmCommandAsync(
        Guid uid, ConfirmAfterSalesDemandCommand command, CancellationToken token)
    {
        if (uid == Guid.Empty)
            return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Fail(
                ServiceErrorCodes.Validation, "单据身份不能为空。", "Uid");
        var result = await executor.ExecuteAsync(command.CommandId, CommandType, uid,
            new { command.Version }, ct => ConfirmInTransactionAsync(uid, command, ct),
            token, IsolationLevel.Serializable);
        // 回执保存首次权威结果；重放标记只修饰本次响应，不改已持久化结果。
        result.Value.AlreadyApplied = result.IsReplay;
        return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Ok(result);
    }

    /// <summary>公共回执锁之后锁头行、重验并接收来源及写审计。</summary>
    private async Task<AfterSalesCommandResult> ConfirmInTransactionAsync(Guid uid,
        ConfirmAfterSalesDemandCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = await SnapshotAsync(uid);
        var head = await repository.GetHeaderAsync(uid, true)
            ?? throw new AfterSalesException("NOT_FOUND", "售后单不存在。");
        if (head.Version != command.Version)
            throw new AfterSalesException("VERSION_CONFLICT", "售后单已被其他操作更新，请刷新。");
        if (head.DocumentStatus != DocumentStatusEnum.Open)
            throw new AfterSalesException("BUSINESS_BLOCKED", "售后单已关闭或作废。");
        if (head.ConfirmationStatus == ConfirmationStatusEnum.已确认)
            throw new AfterSalesException("BUSINESS_BLOCKED", "实物需求已经确认。");

        var lines = (await repository.GetLinesAsync(uid, true)).Where(line => !line.IsDeleted).ToList();
        var positive = lines.Where(line => line.Quantity > 0).ToList();
        if (positive.Count > 0)
        {
            AfterSalesPolicy.ValidateHeader(head, true);
            await ValidateReferencesAsync(head, positive);
            // 原头版本加一作为来源版本；来源服务参与本事务但不拥有回执或提交权。
            await reception.ReceiveAsync(CreateSourceCommand(uid, head, positive, command.CommandId));
            head.ConfirmationStatus = ConfirmationStatusEnum.已确认;
            head.DemandConfirmedAt = DateTime.Now;
            head.DemandConfirmedBy = user.UserName;
        }
        if (!await repository.WriteHeaderAsync(head, false))
            throw new AfterSalesException("VERSION_CONFLICT", "售后单状态已变化。");
        var result = new AfterSalesCommandResult
        {
            Uid = head.Uid, AfterSalesNo = head.AfterSalesNo, Version = head.Version,
            DocumentStatus = head.DocumentStatus, ConfirmationStatus = head.ConfirmationStatus,
            Capabilities = AfterSalesPolicy.Capabilities(head, positive.Count > 0)
        };
        var after = await SnapshotAsync(uid);
        await WriteAuditAsync(uid, command.CommandId, result, before, after);
        return result;
    }

    /// <summary>保留原有供应商、问题产品、原销售行、客户和正数量补件引用资格。</summary>
    private async Task ValidateReferencesAsync(AfterSalesHeader head, IReadOnlyList<AfterSalesLine> positive)
    {
        if (head.ResponsibleSupplierId.HasValue)
            await ReferenceAsync("suppliers", head.ResponsibleSupplierId.Value, null, true,
                "Header.ResponsibleSupplierId");
        if (head.IssueProductId.HasValue)
            await ReferenceAsync("products", head.IssueProductId.Value, head.CustomerId, false,
                "Header.IssueProductId");
        if (head.OriginalSalesOrderLineId.HasValue)
            await ReferenceAsync("sales-order-lines", head.OriginalSalesOrderLineId.Value, head.CustomerId,
                false, "Header.OriginalSalesOrderLineId");
        await ReferenceAsync("customers", head.CustomerId, head.CustomerId, true, "Header.CustomerId");
        foreach (var line in positive)
        {
            AfterSalesPolicy.ValidateLine(line, true);
            if (line.ProductId.HasValue)
                await ReferenceAsync("products", line.ProductId.Value, head.CustomerId, true,
                    $"Lines[{line.Uid}].ProductId");
            if (line.ColorId.HasValue)
                await ReferenceAsync("colors", line.ColorId.Value, null, false,
                    $"Lines[{line.Uid}].ColorId");
        }
    }

    /// <summary>将售后主数据不可用转换为原有稳定字段错误。</summary>
    private async Task ReferenceAsync(string kind, Guid uid, Guid? customerId, bool active, string field)
    {
        if (await lookups.ReferenceAsync(kind, uid, customerId, active) is null)
            throw new AfterSalesException("VALIDATION", "引用对象不存在、不适用或已停用。", field);
    }

    /// <summary>按正数量行建立 CR 来源命令；纯投诉不会调用此方法。</summary>
    private static SourceDemandReceptionCommand CreateSourceCommand(Guid uid, AfterSalesHeader head,
        IReadOnlyList<AfterSalesLine> positive, Guid commandId) => new()
    {
        CommandId = commandId, SourceType = DocumentTypeStaticsEnum.ComplaintReplenishment,
        SourceUid = uid, SourceNo = head.AfterSalesNo, SourceVersion = head.Version + 1,
        CustomerId = head.CustomerId, RejectUnsubmittedActiveLines = true,
        Items = positive.Select(line => new SourceDemandReceptionItem
        {
            SourceLineId = line.Uid, SourceLineNumber = line.LineNumber, ProductId = line.ProductId,
            IsNonStandard = line.IsNonStandard, SkuSnapshot = line.SkuSnapshot!,
            CustomerSkuSnapshot = line.CustomerSkuSnapshot, LinkSkuSnapshot = line.LinkSkuSnapshot,
            DescriptionSnapshot = line.DescriptionSnapshot, ColorSnapshot = line.ColorSnapshot,
            SpecificationSnapshot = line.SpecificationSnapshot, UnitSnapshot = line.UnitSnapshot,
            DemandQty = line.Quantity!.Value, RequiredDate = head.RequiredDate, Remark = line.Remark
        }).ToList()
    };

    /// <summary>在现有事务读取审计前后快照；不把快照或审计日志当作机器回执。</summary>
    private async Task<AfterSalesDetailDto?> SnapshotAsync(Guid uid)
    {
        var head = await repository.GetHeaderAsync(uid, true);
        if (head is null) return null;
        return new AfterSalesDetailDto
        {
            Header = head.Adapt<AfterSalesHeaderDto>(),
            Lines = (await repository.GetLinesAsync(uid, true)).Where(line => !line.IsDeleted)
                .Adapt<List<AfterSalesLineDto>>()
        };
    }

    /// <summary>保留售后操作的前后业务快照，首次命令审计和回执由同一事务提交。</summary>
    private async Task WriteAuditAsync(Guid uid, Guid commandId, AfterSalesCommandResult result,
        AfterSalesDetailDto? before, AfterSalesDetailDto? after)
    {
        var affected = await logs.InsertAsync(new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId,
            Module = "AfterSales", AggregateType = "AfterSales", AggregateUid = uid,
            AggregateNoSnapshot = result.AfterSalesNo, OperationType = "Command",
            OperationName = "Confirm", Summary = "售后确认实物需求",
            ChangedFieldsJson = JsonSerializer.Serialize(new { Before = before, After = after }),
            RelatedObjectsJson = JsonSerializer.Serialize(new { SourceUid = uid }),
            UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
        });
        if (affected != 1) throw new AfterSalesException("LOG_WRITE_FAILED", "售后确认审计写入失败。");
    }

    /// <summary>未登录与品质模块拒绝均先于回执和事务返回。</summary>
    private async Task<ServiceResult<AfterSalesCommandResult>?> AuthorizeAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor)
            return ServiceResult<AfterSalesCommandResult>.Fail(
                ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.AfterSalesConfirm, token)
            ? null : ServiceResult<AfterSalesCommandResult>.Fail(
                ServiceErrorCodes.Forbidden, "当前账号无品质管理权限。");
    }

    /// <summary>保持原售后业务错误码，并让公共执行器的未知结果原样穿过响应边界。</summary>
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
            context.LoggerFactory.CreateLogger<AfterSalesConfirmationService>()
                .LogError(ex, "售后确认执行失败；须用原命令标识核实结果。");
            return ServiceResult<AfterSalesCommandResult>.Fail(
                ServiceErrorCodes.Unexpected, "售后确认失败，请核实命令结果。");
        }
    }
}
