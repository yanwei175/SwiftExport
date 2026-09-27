using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.ProductList.Services;

/// <summary>打开独立上传视图，复用 Shell 公共弹窗宿主；文件选择由视图内操作触发。</summary>
public sealed class ProductImageUploadDialogService(
    IDialogService dialogs, ICurrentUserService currentUser,
    ProductImageUploadViewModel viewModel) : IProductImageUploadDialogService
{
    /// <inheritdoc />
    public async Task ShowAsync()
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsAdmin)
        {
            await dialogs.ShowErrorAsync("批量上传主图", "只有管理员可以上传产品图片。");
            return;
        }
        viewModel.Prepare([]);
        await dialogs.ShowAsync(new ProductImageUploadView { DataContext = viewModel },
            new DialogOptions("批量上传产品主图") { Width = 720, CloseButtonText = "关闭" }, viewModel);
    }
}
