using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products;

public sealed class ProductsViewModel : ViewModelBaseEx
{
    public ProductsViewModel()
    {
        Title = "产品";
        Description = "产品管理页面将在后续阶段接入产品资料、BOM 和 DataGrid 编辑流程。";
    }

    public string Description { get; }
}
