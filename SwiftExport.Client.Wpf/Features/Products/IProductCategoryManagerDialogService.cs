namespace SwiftExportClient.Features.Products;

public interface IProductCategoryManagerDialogService
{
    Task ShowAsync(CancellationToken cancellationToken = default);
}
