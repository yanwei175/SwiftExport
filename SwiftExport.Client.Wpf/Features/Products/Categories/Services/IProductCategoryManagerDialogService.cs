namespace SwiftExportClient.Features.Products.Categories.Services;

public interface IProductCategoryManagerDialogService
{
    Task ShowAsync(CancellationToken cancellationToken = default);
}
