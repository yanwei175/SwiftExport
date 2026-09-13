using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Services;

/// <summary>向业务页面提供只读仓库单选弹窗，并以空值表达取消。</summary>
public interface IWarehousePickerService
{
    /// <summary>展示活动仓库选择器；确认返回选择，取消或关闭返回空值。</summary>
    Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default);
}
