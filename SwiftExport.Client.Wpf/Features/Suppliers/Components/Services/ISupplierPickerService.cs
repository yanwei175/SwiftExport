using SwiftExport.Contracts.DTOs.Suppliers;

namespace SwiftExportClient.Features.Suppliers.Components.Services;

/// <summary>向业务页面提供基于共享缓存的供应商单选弹窗。</summary>
public interface ISupplierPickerService
{
    /// <summary>展示供应商选择器；确认返回独立 DTO，取消或关闭返回空值。</summary>
    Task<SupplierDto?> PickAsync(CancellationToken cancellationToken = default);
}
