using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>包装关系编辑列表，复用行基线、删除池和撤销；请求只输出显式变更。</summary>
public sealed partial class ProductPackageListModel : BaseListModel<ProductPackageLineModel, ProductPackageDto>
{
    /// <summary>为每条传输快照创建独立编辑行。</summary>
    public ProductPackageListModel() : base(_ => new ProductPackageLineModel()) { }
    /// <summary>冻结包装增删改，省略未编辑的关系；Owner 不进入可写 DTO。</summary>
    public ProductPackageChangesRequest? CreateChangesRequest()
    {
        if (!HasChanges) return null;
        var changes = GetChangeSet();
        return new()
        {
            CreateItems = changes.Added.Select(row => new CreateItemRequest<ProductPackageWriteDto> { Data = row.Data }).ToList(),
            UpdateItems = changes.Modified.Select(row => new UpdateItemRequest<ProductPackageWriteDto> { Uid = row.Uid, Version = row.Version, Data = row.Data }).ToList(),
            DeleteItems = changes.Deleted.Select(row => new DeleteItemRequest { Uid = row.Uid, Version = row.Version, IsSoftDelete = true }).ToList()
        };
    }
}
