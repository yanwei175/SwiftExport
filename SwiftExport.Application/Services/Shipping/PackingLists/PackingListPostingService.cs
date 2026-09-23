using Mapster;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.Application.Services.Shipping.PackingLists;

/// <summary>处理装箱单过账预览与正式命令；公共管线负责入口授权，公共执行器拥有事务和回执。</summary>
public sealed partial class PackingListPostingService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingPreviewDto>> PreviewAsync(Guid uid, PackingListPostPreviewRequest request, CancellationToken token) =>
        request == null ? Task.FromResult(ServiceResult<PackingListPostingPreviewDto>.Fail("INVALID_COMMAND", "预览请求不能为空。")) :
        PreviewCore(uid, request.ExpectedVersion, request.BusinessDate, token);

    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingCommandResult>> PostAsync(Guid uid, PostPackingListCommand request, CancellationToken token) =>
        ExecutePipelineAsync(request, request?.CommandId, "Shipping.PackingList.Post", PermissionKeys.PackingListPost,
            () => Execute(() => ExecutePostCommandAsync(new(uid, request!.CommandId, request.ExpectedVersion,
                request.BusinessDate, request.PreviewToken), token)), token);

    /// <summary>在正式过账业务方法之前完成细权限和请求结构检查，并记录首次执行与回放耗时。</summary>
    private async Task<ServiceResult<PackingListPostingCommandResult>> ExecutePipelineAsync<TRequest>(TRequest? request,
        Guid? commandId, string commandType, string permission,
        Func<Task<ServiceResult<PackingListPostingCommandResult>>> execute, CancellationToken token)
    {
        try
        {
            return await pipeline.ExecuteAsync<TRequest, PackingListPostingCommandResult>(request, commandId, commandType,
                async ct => !user.IsAuthenticated || user.UserId is not { } actor
                    || !await permissions.HasPermissionAsync(actor, PermissionKeys.ModuleShippingAccess, ct)
                    || !await permissions.HasPermissionAsync(actor, permission, ct)
                    ? ServiceResult<PackingListPostingCommandResult>.Fail(ServiceErrorCodes.Forbidden, "没有当前装箱单操作权限。")
                    : null,
                async _ =>
                {
                    var result = await execute();
                    if (!result.Success)
                        return ServiceResult<CommandExecutionResult<PackingListPostingCommandResult>>.Fail(
                            result.Error?.Code ?? ServiceErrorCodes.Unexpected, result.Message, result.Error?.Field);
                    return ServiceResult<CommandExecutionResult<PackingListPostingCommandResult>>.Ok(
                        new(result.Data!, result.Data!.Status == "AlreadyApplied"));
                }, token);
        }
        catch (CommandValidationException error)
        {
            return ServiceResult<PackingListPostingCommandResult>.Fail("INVALID_COMMAND", error.Message);
        }
    }

    /// <summary>先计算只读过账预览，再补齐展示；任何补读都不会覆盖计算结果或令牌。</summary>
    private async Task<ServiceResult<PackingListPostingPreviewDto>> PreviewCore(Guid uid, int version, DateTime date, CancellationToken token)
    {
        try
        {
            var plan = await PreviewCoreAsync(uid, version, date, token);
            var detail = await packingLists.GetDetailAsync(uid, token);
            if (detail?.Header == null || detail.Header.Version != version)
                return ServiceResult<PackingListPostingPreviewDto>.Fail("VERSION_CONFLICT", "箱单已变化，请刷新后预览。");
            var result = new PackingListPostingPreviewDto { PackingListId = uid, ExpectedVersion = version,
                BusinessDate = date.Date, IsReverse = false, PreviewToken = plan.Token,
                Actions = plan.Actions.Select(x => x.Adapt<PackingListPostingActionDto>(mapping)).ToList(),
                Blockers = plan.Blockers.Select(x => x.Adapt<PackingListPostingBlockerDto>(mapping)).ToList() };
            var display = await factsRepository.ReadAsync(uid, token);
            var warehouses = display.Warehouses;
            foreach (var action in result.Actions)
            {
                var line = detail.Lines.FirstOrDefault(x => x.Uid == action.PackingListLineId);
                action.SourceNo = line?.SourceDocumentNoSnapshot;
                if (line?.ProductId == action.ProductId)
                {
                    action.Sku = line?.SkuSnapshot; action.CustomerSku = line?.CustomerSkuSnapshot; action.ImagePath = line?.ImagePath;
                }
                else if (display?.Products.FirstOrDefault(x => x.Uid == action.ProductId) is { } product)
                {
                    action.Sku = product.Sku; action.ImagePath = product.MainImage;
                }
                var warehouse = warehouses.FirstOrDefault(x => x.Uid == action.WarehouseSiteId);
                action.WarehouseCode = warehouse?.Code; action.WarehouseName = warehouse?.Name;
                var purchase = display?.PurchaseLines.FirstOrDefault(x => x.Uid == action.PurchaseLineId);
                action.PurchaseOrderNo = purchase?.OrderNo; action.PurchaseLineNumber = purchase?.LineNumber;
                action.Explanation = action.ActionKind == "Delivery" ? "售后直发或非标来源：仅登记交付，不产生库存流水。" : null;
            }
            return ServiceResult<PackingListPostingPreviewDto>.Ok(result);
        }
        catch (PackingListPostingFailure error) { return ServiceResult<PackingListPostingPreviewDto>.Fail(error.Code, error.Message); }
        catch (AutomaticPostingException error) { return ServiceResult<PackingListPostingPreviewDto>.Fail(error.Code, error.Message); }
    }

    /// <summary>失败通过结构化错误报告首个阻断，逐来源信息由只读预览提供；未知异常和取消交由统一边界处理，禁止伪造可重发的新命令。</summary>
    private async Task<ServiceResult<PackingListPostingCommandResult>> Execute(Func<Task<PackingListPostOutcome>> action)
    {
        try
        {
            var outcome = await action();
            return ServiceResult<PackingListPostingCommandResult>.Ok(new() { Status = outcome.AlreadyApplied ? "AlreadyApplied" : "Success",
                ExecutionId = outcome.ExecutionId, Detail = outcome.Detail, Documents = outcome.Documents });
        }
        catch (PackingListPostingFailure error)
        {
            return Failure(error.Code, error.Message);
        }
        catch (AutomaticPostingException error) { return Failure(error.Code, error.Message); }
    }

    /// <summary>稳定分类帮助客户端区分版本冲突、预览失效与业务阻断；失败不清空客户端草稿。</summary>
    private static ServiceResult<PackingListPostingCommandResult> Failure(string code, string message)
    {
        return ServiceResult<PackingListPostingCommandResult>.Fail(code, message);
    }
}
