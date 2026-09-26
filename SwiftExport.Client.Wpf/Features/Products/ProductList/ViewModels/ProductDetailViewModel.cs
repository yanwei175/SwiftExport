using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels
{
    public sealed partial class ProductDetailViewModel : ViewModelBaseEx
    {
        public ProductDetailViewModel()
        {

        }
        public ProductLineModel Product { get; set; }
    }
}
