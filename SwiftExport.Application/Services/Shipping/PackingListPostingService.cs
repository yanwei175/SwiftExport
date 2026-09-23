using Mapster;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Shipping;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>转换过账用例契约和稳定失败结果；事务、库存及权限仍由协调器负责。</summary>
public sealed class PackingListPostingService(IPackingListPostingCoordinator coordinator,
    IPackingListRepository packingLists, IPackingListPostingFactsRepository factsRepository,
    IPackingListExecutionRepository executions, TypeAdapterConfig mapping) : IPackingListPostingService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingPreviewDto>> PreviewAsync(Guid uid, PackingListPostPreviewRequest request, CancellationToken token) =>
        request == null ? Task.FromResult(ServiceResult<PackingListPostingPreviewDto>.Fail("INVALID_COMMAND", "预览请求不能为空。")) :
        PreviewCore(uid, request.ExpectedVersion, request.BusinessDate, null, token);

    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingPreviewDto>> ReversePreviewAsync(Guid uid, PackingListReversePreviewRequest request, CancellationToken token) =>
        request == null ? Task.FromResult(ServiceResult<PackingListPostingPreviewDto>.Fail("INVALID_COMMAND", "反向预览请求不能为空。")) :
        PreviewCore(uid, request.ExpectedVersion, request.BusinessDate, request.Reason ?? "", token);

    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingCommandResult>> PostAsync(Guid uid, PostPackingListCommand request, CancellationToken token) =>
        Execute(() => request == null ? throw new PackingListPostingFailure("INVALID_COMMAND", "过账请求不能为空。") :
            coordinator.PostAsync(new(uid, request.CommandId, request.ExpectedVersion, request.BusinessDate, request.PreviewToken), token));

    /// <inheritdoc/>
    public Task<ServiceResult<PackingListPostingCommandResult>> ReverseAsync(Guid uid, ReversePackingListCommand request, CancellationToken token) =>
        Execute(() => request == null ? throw new PackingListPostingFailure("INVALID_COMMAND", "反向请求不能为空。") :
            coordinator.ReverseAsync(new(uid, request.CommandId, request.ExpectedVersion, request.BusinessDate, request.Reason, request.PreviewToken), token));

    /// <summary>先经过协调器授权及计算，随后只补展示；任何补读都不会覆盖计算结果或令牌。</summary>
    private async Task<ServiceResult<PackingListPostingPreviewDto>> PreviewCore(Guid uid, int version, DateTime date, string? reason, CancellationToken token)
    {
        try
        {
            var reverse = reason != null;
            var plan = reverse ? await coordinator.ReversePreviewAsync(uid, version, date, reason!, token)
                : await coordinator.PreviewAsync(uid, version, date, token);
            var detail = await packingLists.GetDetailAsync(uid, token);
            if (detail?.Header == null || detail.Header.Version != version)
                return ServiceResult<PackingListPostingPreviewDto>.Fail("VERSION_CONFLICT", "箱单已变化，请刷新后预览。");
            var result = new PackingListPostingPreviewDto { PackingListId = uid, ExpectedVersion = version,
                BusinessDate = date.Date, IsReverse = reverse, PreviewToken = plan.Token,
                Actions = plan.Actions.Select(x => x.Adapt<PackingListPostingActionDto>(mapping)).ToList(),
                Blockers = plan.Blockers.Select(x => x.Adapt<PackingListPostingBlockerDto>(mapping)).ToList() };
            // 反向仅使用原执行关联仓，不解析当前需求、Link、采购资格或 BOM。
            var display = reverse ? null : await factsRepository.ReadAsync(uid, token);
            var warehouses = reverse ? (await executions.ReadReverseFactsAsync(uid, token)).Warehouses : display!.Warehouses;
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

    /// <summary>失败保留逐来源信息；未知异常和取消交由统一边界处理，禁止伪造可重发的新命令。</summary>
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
            return Failure(error.Code, error.Message, error.Blockers.Select(x => x.Adapt<PackingListPostingBlockerDto>(mapping)).ToList());
        }
        catch (AutomaticPostingException error) { return Failure(error.Code, error.Message, []); }
    }

    /// <summary>稳定分类帮助客户端区分版本冲突、预览失效与业务阻断；失败不清空客户端草稿。</summary>
    private static ServiceResult<PackingListPostingCommandResult> Failure(string code, string message, List<PackingListPostingBlockerDto> blockers)
    {
        if (blockers.Count == 0) blockers.Add(new() { Code = code, Message = message });
        var result = ServiceResult<PackingListPostingCommandResult>.Fail(code, message);
        result.Data = new() { ErrorCode = code, Status = code == "VERSION_CONFLICT" ? "VersionConflict" : code == "PREVIEW_STALE" ? "PreviewExpired" : "Blocked", Blockers = blockers };
        return result;
    }
}
