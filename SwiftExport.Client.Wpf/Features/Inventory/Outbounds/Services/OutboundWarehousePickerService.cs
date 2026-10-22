using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>在出库查看权限下读取完整活动仓库候选，复用原选择界面，不访问仓库管理缓存。</summary>
public sealed class OutboundWarehousePickerService(IOutboundManagerApi api, IApiCallRunner runner, IDialogService dialogs) : IWarehousePickerService
{
    /// <inheritdoc/>
    public Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default)
        => new WarehousePickerService(() => new WarehousePickerViewModel(LoadOptionsAsync), dialogs).PickAsync(cancellationToken);

    /// <summary>候选逐页完整读取后交付本次弹窗；失败或身份重复不交付部分候选。</summary>
    public async Task<ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>> LoadOptionsAsync(CancellationToken token)
    {
        var items = new List<WarehouseSitePickerDto>();
        for (var page = 1; ; page++)
        {
            var request = new PagedRequestBase { PageIndex = page, PageSize = OutboundQueryLimits.PageSizeMax };
            var result = await runner.RunAsync(() => api.GetWarehouseOptionsAsync(request, token), token, "加载出库仓库候选失败");
            token.ThrowIfCancellationRequested();
            if (!result.Success || result.Data?.Items is null)
                return ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>.Fail(result.Error?.Code ?? ServiceErrorCodes.Unexpected, result.Message);
            var batch = result.Data.Items;
            if (batch.Any(x => x.Uid == Guid.Empty || !x.IsActive) || batch.Select(x => x.Uid).Distinct().Count() != batch.Count
                || batch.Any(x => items.Any(old => old.Uid == x.Uid)) || batch.Count == 0 && items.Count < result.Data.TotalCount)
                return ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>.Fail(ServiceErrorCodes.Unexpected, "出库仓库候选不完整或发生变化，请重试。");
            items.AddRange(batch);
            if (items.Count >= result.Data.TotalCount) return ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>.Ok(items);
        }
    }
}
