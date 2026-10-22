using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Inbounds.Services;

/// <summary>在入库查看权限下完整读取活动仓库候选，复用原选择器布局与确认协议。</summary>
public sealed class InboundWarehousePickerService(IInboundManagerApi api, IApiCallRunner runner, IDialogService dialogs) : IWarehousePickerService
{
    /// <inheritdoc/>
    public Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default)
        => new WarehousePickerService(() => new WarehousePickerViewModel(LoadOptionsAsync), dialogs).PickAsync(cancellationToken);

    /// <summary>按共享分页上限取完候选后交给弹窗本地筛选；中途失败不交付残缺快照。</summary>
    public async Task<ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>> LoadOptionsAsync(CancellationToken token)
    {
        var items = new List<WarehouseSitePickerDto>();
        for (var page = 1; ; page++)
        {
            var request = new PagedRequestBase { PageIndex = page, PageSize = InboundQueryLimits.MaxPageSize };
            var result = await runner.RunAsync(() => api.GetWarehouseOptionsAsync(request, token), token, "加载入库仓库候选失败");
            token.ThrowIfCancellationRequested();
            if (!result.Success || result.Data?.Items is null)
                return ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>.Fail(result.Error?.Code ?? ServiceErrorCodes.Unexpected, result.Message);
            var batch = result.Data.Items;
            if (batch.Any(x => x.Uid == Guid.Empty || !x.IsActive) || batch.Select(x => x.Uid).Distinct().Count() != batch.Count
                || batch.Any(x => items.Any(old => old.Uid == x.Uid)) || batch.Count == 0 && items.Count < result.Data.TotalCount)
                return ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>.Fail(ServiceErrorCodes.Unexpected, "仓库候选不完整或发生变化，请重新打开选择器。");
            items.AddRange(batch);
            if (items.Count >= result.Data.TotalCount) return ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>.Ok(items);
        }
    }
}
