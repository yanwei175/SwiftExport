using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Categories.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Categories.Services;

public sealed class ProductCategoryManagerDialogService(
    IServiceProvider serviceProvider,
    IDialogService dialogService) : IProductCategoryManagerDialogService
{
    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        var viewModel = serviceProvider.GetRequiredService<ProductCategoryManagerViewModel>();
        await viewModel.InitializeAsync();
        await dialogService.ShowContentAsync(
            "产品分类管理",
            new ProductCategoryManagerView { DataContext = viewModel },
            "关闭",
            cancellationToken);
    }
}
