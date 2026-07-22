using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.Components;

public sealed partial class ProductCategoryLookupNode : ObservableObject
{
    internal ProductCategoryLookupNode(ProductCategoryCacheNode source)
    {
        Source = source;
    }

    public ProductCategoryCacheNode Source { get; }

    public ObservableCollection<ProductCategoryLookupNode> Children { get; } = [];

    public Guid Uid => Source.Uid;

    public string CategoryName => Source.CategoryName;

    public string CategoryCode => Source.CategoryCode;

    public string FakeName => Source.FakeName;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
