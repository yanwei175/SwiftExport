using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>隔离真实弹窗，默认确认但不授权资料覆盖；测试可控制等待、取消及选择。</summary>
internal sealed class ProductMasterSyncDialogStub : IProductMasterSyncDialogService
{
    public int Calls { get; private set; }
    public Func<Task<ProductMasterSyncDialogResult>> Respond { get; set; } = () =>
        Task.FromResult(new ProductMasterSyncDialogResult { Confirmed = true });

    /// <inheritdoc/>
    public Task<ProductMasterSyncDialogResult> ConfirmSaveAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Respond();
    }
}
