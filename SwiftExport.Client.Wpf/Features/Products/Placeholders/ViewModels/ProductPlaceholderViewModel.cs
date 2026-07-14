using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Placeholders.ViewModels;

public abstract class ProductPlaceholderViewModel : ViewModelBaseEx
{
    protected ProductPlaceholderViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Description { get; }
}

public sealed class ProductBomViewModel : ProductPlaceholderViewModel
{
    public ProductBomViewModel()
        : base("BOM", "BOM 页面将在后续阶段接入。")
    {
    }
}

public sealed class SupplierProductsViewModel : ProductPlaceholderViewModel
{
    public SupplierProductsViewModel()
        : base("供应商产品", "供应商产品页面将在后续阶段接入。")
    {
    }
}
