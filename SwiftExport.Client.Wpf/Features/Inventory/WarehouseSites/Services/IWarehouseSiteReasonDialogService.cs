using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Services;

/// <summary>采集虚拟仓停用或删除的必填原因，并在确认前显示稳定目标。</summary>
public interface IWarehouseSiteReasonDialogService
{
    /// <summary>显示目标代码和名称；取消返回 null，确认返回清理后的真实原因。</summary>
    Task<string?> RequestAsync(string action, WarehouseSiteDto warehouse, CancellationToken cancellationToken = default);
}
