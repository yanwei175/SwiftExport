using SwiftExportClient.Features.Products.ProductList.Models;

namespace SwiftExportClient.Features.Products.ProductList.Services;

/// <summary>为主型号两种保存入口采集本次同步选择，不持有页面或 API 状态。</summary>
public interface IProductMasterSyncDialogService
{
    /// <summary>显示本次保存确认，每次默认不勾选；取消不授权保存。</summary>
    Task<ProductMasterSyncDialogResult> ConfirmSaveAsync(CancellationToken cancellationToken = default);
}
