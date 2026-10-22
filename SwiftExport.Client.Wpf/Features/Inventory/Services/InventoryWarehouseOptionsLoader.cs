using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Inventory.Services;

/// <summary>两个库存查询页共用的候选读取；只使用库存查看接口，完整分页后一次接纳，不修改共享仓库缓存权限。</summary>
internal static class InventoryWarehouseOptionsLoader
{
    /// <summary>逐页获取全部活动仓库；失败或分页不完整时不把部分候选误报为完整结果。</summary>
    public static async Task<ServiceResult<List<WarehouseSitePickerDto>>> LoadAsync(IInventoryApi api, IApiCallRunner runner, CancellationToken token)
    {
        var items = new List<WarehouseSitePickerDto>();
        for (var page = 1; ; page++)
        {
            var response = await runner.RunAsync(() => api.GetWarehouseOptionsAsync(new PagedRequestBase
                { PageIndex = page, PageSize = InventoryQueryLimits.MaxPageSize }, token), token, "加载库存仓库候选失败");
            token.ThrowIfCancellationRequested();
            if (!response.Success || response.Data is null) return ServiceResult<List<WarehouseSitePickerDto>>.Fail(response.Error?.Code ?? ServiceErrorCodes.Unexpected, response.Message);
            var data = response.Data;
            if (data.Items.Count == 0 && items.Count < data.TotalCount)
                return ServiceResult<List<WarehouseSitePickerDto>>.Fail(ServiceErrorCodes.Unexpected, "仓库候选分页不完整，请重新加载。");
            items.AddRange(data.Items);
            if (items.Count >= data.TotalCount) return ServiceResult<List<WarehouseSitePickerDto>>.Ok(items.DistinctBy(x => x.Uid).ToList());
        }
    }
}
