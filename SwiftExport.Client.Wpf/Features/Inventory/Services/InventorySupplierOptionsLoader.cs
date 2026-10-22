using Mapster;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Inventory.Services;

/// <summary>按所属业务窄接口完整读取供应商筛选候选，适配既有选择控件，不访问全量基础资料缓存。</summary>
internal static class InventorySupplierOptionsLoader
{
    /// <summary>逐页读取全部身份后一次接纳；中途失败、无效身份或重复分页不返回残缺候选。</summary>
    public static async Task<ServiceResult<List<SupplierDto>>> LoadAsync(
        Func<PagedRequestBase, CancellationToken, Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>>> query,
        IApiCallRunner runner, int pageSize, CancellationToken token)
    {
        var rows = new List<InventorySupplierLookupDto>();
        for (var page = 1; ; page++)
        {
            var request = new PagedRequestBase { PageIndex = page, PageSize = pageSize };
            var result = await runner.RunAsync(() => query(request, token), token, "加载供应商筛选候选失败");
            token.ThrowIfCancellationRequested();
            if (!result.Success || result.Data?.Items is null)
                return ServiceResult<List<SupplierDto>>.Fail(result.Error?.Code ?? ServiceErrorCodes.Unexpected, result.Message);
            var items = result.Data.Items;
            if (items.Any(x => x.Uid == Guid.Empty) || items.Select(x => x.Uid).Distinct().Count() != items.Count
                || items.Any(x => rows.Any(old => old.Uid == x.Uid)) || items.Count == 0 && rows.Count < result.Data.TotalCount)
                return ServiceResult<List<SupplierDto>>.Fail(ServiceErrorCodes.Unexpected, "供应商筛选候选不完整或发生变化，请重新打开页面。");
            rows.AddRange(items);
            if (rows.Count >= result.Data.TotalCount) return ServiceResult<List<SupplierDto>>.Ok(rows.Adapt<List<SupplierDto>>());
        }
    }
}
