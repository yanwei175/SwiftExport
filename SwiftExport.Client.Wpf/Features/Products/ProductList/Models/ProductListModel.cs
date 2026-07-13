using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

public sealed class ProductListModel : BaseListModel<ProductLineModel, ProductDto>
{
    public ProductListModel() : base(_ => new ProductLineModel()) { }

    public ProductLineModel AddNewProduct() => AddNew(new ProductDto());

    public ProductBatchSaveRequest CreateSaveRequest()
    {
        var changes = GetChangeSet();
        return new ProductBatchSaveRequest
        {
            CreateItems = changes.Added.Select(dto => new CreateItemRequest<ProductDto> { Data = dto }).ToList(),
            UpdateItems = changes.Modified.Select(dto => new UpdateItemRequest<ProductDto> { Uid = dto.Uid, Version = dto.Version, Data = dto }).ToList(),
            DeleteItems = changes.Deleted.Where(dto => dto.Uid != Guid.Empty)
                .Select(dto => new DeleteItemRequest { Uid = dto.Uid, Version = dto.Version, IsSoftDelete = true }).ToList(),
        };
    }
}
