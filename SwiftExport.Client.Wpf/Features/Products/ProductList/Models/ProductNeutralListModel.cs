using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>中性产品明细集合，负责详情聚合请求中的行变更集。</summary>
public sealed partial class ProductNeutralListModel : BaseListModel<ProductNeutralLineModel, ProductNeutralDto>
{
    /// <summary>初始化中性产品列表。</summary>
    public ProductNeutralListModel() : base(_ => new ProductNeutralLineModel()) { }

    /// <summary>新增一条中性产品。</summary>
    public ProductNeutralLineModel AddNewNeutral(Guid productMasterId) => AddNew(new ProductNeutralDto
    {
        ProductMasterId = productMasterId
    });

    /// <summary>将指定有效行设为默认主图，并取消当前详情中其他有效行的默认标记。</summary>
    public void SetMainImage(ProductNeutralLineModel line)
    {
        if (!Items.Contains(line) || !line.IsMainImage) return;
        foreach (var other in Items.Where(item => !ReferenceEquals(item, line) && item.IsMainImage))
            other.IsMainImage = false;
    }

    /// <summary>将明细变更填入详情聚合保存请求。</summary>
    public void ApplyChanges(SaveProductMasterDetailRequest request)
    {
        var changes = CreateSaveRequest();
        request.CreateNeutrals = changes.CreateItems;
        request.UpdateNeutrals = changes.UpdateItems;
        request.DeleteNeutrals = changes.DeleteItems;
    }

    /// <summary>构造独立明细保存请求，同时供详情聚合复用相同变更映射。</summary>
    public BatchSaveRequestBase<ProductNeutralWriteDto> CreateSaveRequest()
    {
        var request = new BatchSaveRequestBase<ProductNeutralWriteDto>();
        request.CreateItems = AddedItems.Select(line => new CreateItemRequest<ProductNeutralWriteDto>
        {
            ClientKey = line.Uid == Guid.Empty ? Guid.NewGuid().ToString("N") : line.Uid.ToString("N"),
            RowIndex = line.RowIndex,
            Data = line.ToWriteDto()
        }).ToList();
        request.UpdateItems = ModifiedItems.Select(line => new UpdateItemRequest<ProductNeutralWriteDto>
        {
            Uid = line.Uid,
            Version = line.Version,
            RowIndex = line.RowIndex,
            Data = line.ToWriteDto()
        }).ToList();
        request.DeleteItems = DeletedItems.Where(line => line.Uid != Guid.Empty).Select(line => new DeleteItemRequest
        {
            Uid = line.Uid,
            Version = line.Version,
            RowIndex = line.RowIndex,
            IsSoftDelete = true
        }).ToList();
        return request;
    }
}
