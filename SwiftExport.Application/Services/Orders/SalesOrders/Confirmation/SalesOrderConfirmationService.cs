using SwiftExport.Core.Interfaces.Orders.SalesOrders.Document;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.Orders.SalesOrders.Confirmation;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Services.Orders.SalesOrders.Confirmation;

/// <summary>拥有销售确认的公共事务和完整回执；来源接收只参与本次事务。</summary>
public sealed class SalesOrderConfirmationService(
    ServiceBaseContext context,
    ISalesOrderHeadRepository heads,
    ISalesOrderLineRepository lines,
    IProductRepository products,
    ISourceDemandReceptionService reception,
    IBusinessOperationLogRepository logs,
    ICorrelationContext correlation,
    ICurrentUser user,
    IUserPermissionService permissions,
    CommandPipeline pipeline,
    BusinessCommandExecutor executor) : ISalesOrderConfirmationService
{
    private const string CommandType = "Sales.Order.Confirm";

    /// <summary>先授权和验证请求，再交由公共执行器取得回执或执行首次确认。</summary>
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(
        Guid uid, ConfirmSalesOrderCommand request, CancellationToken cancellationToken = default)
        => RunAsync(() => pipeline.ExecuteAsync<ConfirmSalesOrderCommand, SalesOrderConfirmationResultDto>(
            request, request?.CommandId, CommandType, AuthorizeAsync,
            token => ExecuteConfirmCommandAsync(uid, request!, token), cancellationToken));

    /// <summary>用路由目标及完整业务请求确定指纹，回执查找早于可变化的订单校验。</summary>
    private async Task<ServiceResult<CommandExecutionResult<SalesOrderConfirmationResultDto>>> ExecuteConfirmCommandAsync(
        Guid uid, ConfirmSalesOrderCommand request, CancellationToken token)
    {
        if (uid == Guid.Empty || uid != request.Uid)
            return ServiceResult<CommandExecutionResult<SalesOrderConfirmationResultDto>>.Fail(
                ServiceErrorCodes.Validation, "路由 Uid 与请求体 Uid 不一致。", "Uid");
        var result = await executor.ExecuteAsync(request.CommandId, CommandType, uid,
            new { request.Uid, request.Version },
            ct => ConfirmInTransactionAsync(uid, request, ct), token);
        return ServiceResult<CommandExecutionResult<SalesOrderConfirmationResultDto>>.Ok(result);
    }

    /// <summary>按订单头、明细、产品和来源的原有顺序锁定并写入确认事实及两域审计。</summary>
    private async Task<SalesOrderConfirmationResultDto> ConfirmInTransactionAsync(
        Guid uid, ConfirmSalesOrderCommand request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var header = await heads.GetByIdAsync(uid, true)
            ?? throw new SalesConfirmationException("销售订单不存在或已删除。");
        if (header.DocumentStatus != SalesOrderDocumentStatusEnum.Open)
            throw new SalesConfirmationException("已关闭的销售订单不能确认。");
        var alreadyConfirmed = header.ConfirmationStatus == ConfirmationStatusEnum.已确认;
        if (!alreadyConfirmed && header.Version != request.Version)
            throw new SalesConfirmationException("销售订单已被其他用户修改，请刷新后重试。");

        var orderLines = await lines.GetLinesByOrderIdAsync(uid, true);
        if (orderLines.Count == 0)
            throw new SalesConfirmationException("销售订单没有有效明细，不能确认。");

        var version = header.Version;
        if (!alreadyConfirmed)
        {
            var write = await heads.SetConfirmationStatusAsync(uid, request.Version, ConfirmationStatusEnum.已确认);
            if (!write.Success) throw new SalesConfirmationException(write.Message ?? "数据库写入失败。");
            version = write.NewVersion ?? request.Version + 1;
        }

        var productIds = orderLines.Where(line => line.ProductId.HasValue)
            .Select(line => line.ProductId!.Value).Distinct().ToArray();
        var productMap = (await products.GetByIdListAsync(productIds, true))
            .ToDictionary(product => product.Uid);
        SourceDemandReceptionResult received;
        try
        {
            received = await reception.ReceiveAsync(CreateSourceDemandCommand(
                header, orderLines, productMap, version, request.CommandId));
        }
        catch (SourceDemandReceptionException ex)
        {
            throw new SalesConfirmationException($"{ex.Code}：{ex.Message}");
        }

        var result = new SalesOrderConfirmationResultDto
        {
            Uid = uid, Version = version,
            ConfirmationStatus = ConfirmationStatusEnum.已确认,
            GrossDemandCount = received.TotalCount
        };
        await WriteAuditAsync(header, request, alreadyConfirmed, result);
        return result;
    }

    /// <summary>把确认后的销售事实交给来源领域，不在销售服务直接构造毛需求实体。</summary>
    private static SourceDemandReceptionCommand CreateSourceDemandCommand(
        SalesOrderHeader header, IReadOnlyList<SalesOrderLine> orderLines,
        IReadOnlyDictionary<Guid, Product> productMap, int sourceVersion, Guid commandId)
        => new()
        {
            CommandId = commandId,
            SourceType = DocumentTypeStaticsEnum.SalesOrder,
            SourceUid = header.Uid,
            SourceNo = header.OrderNo,
            SourceVersion = sourceVersion,
            CustomerId = header.CustomerId ?? Guid.Empty,
            RejectUnsubmittedActiveLines = true,
            Items = orderLines.Select(line =>
            {
                productMap.TryGetValue(line.ProductId ?? Guid.Empty, out var product);
                return new SourceDemandReceptionItem
                {
                    SourceLineId = line.Uid,
                    SourceLineNumber = line.LineNumber,
                    ProductId = line.ProductId,
                    IsNonStandard = line.IsNonStandard,
                    SkuSnapshot = line.Sku,
                    LinkSkuSnapshot = line.LinkSku,
                    CustomerSkuSnapshot = line.CustomerSku,
                    DescriptionSnapshot = line.DescriptionEn,
                    ColorSnapshot = line.ColorNameEn,
                    SpecificationSnapshot = line.FeaturesEn,
                    ImagePathSnapshot = line.MainImage,
                    UnitSnapshot = product?.Unit,
                    DemandQty = line.Qty,
                    RequiredDate = line.ExpectedDeliveryDate ?? header.DeliveryDate,
                    Remark = line.SubNote
                };
            }).ToArray()
        };

    /// <summary>只记录业务前后版本、接收数量、操作者和链路；机器回执由执行器持久化。</summary>
    private async Task WriteAuditAsync(SalesOrderHeader header, ConfirmSalesOrderCommand request,
        bool alreadyConfirmed, SalesOrderConfirmationResultDto result)
    {
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = request.CommandId,
            CorrelationId = string.IsNullOrWhiteSpace(correlation.CorrelationId)
                ? request.CommandId.ToString("N") : correlation.CorrelationId[..Math.Min(64, correlation.CorrelationId.Length)],
            Module = "SalesOrder", AggregateType = "SalesOrder", AggregateUid = header.Uid,
            AggregateNoSnapshot = header.OrderNo, OperationType = "Confirm", OperationName = "确认销售订单",
            Summary = $"确认销售订单，接收 {result.GrossDemandCount} 条来源需求。",
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                BeforeVersion = request.Version, AfterVersion = result.Version,
                AlreadyConfirmed = alreadyConfirmed, result.GrossDemandCount
            }),
            RelatedObjectsJson = JsonSerializer.Serialize(new { SourceUid = header.Uid }),
            UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
        };
        if (await logs.InsertAsync(log) != 1)
            throw new SalesConfirmationException("销售确认审计写入失败。");
    }

    /// <summary>有效登录和现有销售模块权限均在回执查找及事务之前检查。</summary>
    private async Task<ServiceResult<SalesOrderConfirmationResultDto>?> AuthorizeAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor)
            return ServiceResult<SalesOrderConfirmationResultDto>.Fail(
                ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.SalesOrderConfirm, token)
            ? null : ServiceResult<SalesOrderConfirmationResultDto>.Fail(
                ServiceErrorCodes.Forbidden, "当前账号没有销售模块权限。");
    }

    /// <summary>将已知业务和公共命令错误转为结构化失败，保留取消和结果不明语义。</summary>
    private async Task<ServiceResult<SalesOrderConfirmationResultDto>> RunAsync(
        Func<Task<ServiceResult<SalesOrderConfirmationResultDto>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex)
        { return ServiceResult<SalesOrderConfirmationResultDto>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex)
        { return ServiceResult<SalesOrderConfirmationResultDto>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (SalesConfirmationException ex)
        { return ServiceResult<SalesOrderConfirmationResultDto>.Fail(ServiceErrorCodes.BusinessBlocked, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<SalesOrderConfirmationService>()
                .LogError(ex, "确认销售订单失败，结果需按 CommandId 核实。");
            return ServiceResult<SalesOrderConfirmationResultDto>.Fail(
                ServiceErrorCodes.Unexpected, "确认销售订单失败，请核实命令结果。");
        }
    }

    private sealed class SalesConfirmationException(string message) : Exception(message);
}
