using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>中性产品管理页集合，负责跨主型号变更集和保存结果接纳。</summary>
public sealed class ProductNeutralManagerListModel
    : BaseListModel<ProductNeutralManagerLineModel, ProductNeutralManagerListItemDto>
{
    /// <summary>初始化中性产品管理集合。</summary>
    public ProductNeutralManagerListModel() : base(_ => new ProductNeutralManagerLineModel()) { }

    /// <summary>新增一条尚未选择主型号和颜色的中性产品。</summary>
    public ProductNeutralManagerLineModel AddNewNeutral() => AddNew(new ProductNeutralManagerListItemDto());

    /// <summary>按照当前行状态构造 Manager 专用批量保存请求。</summary>
    public ProductNeutralManagerBatchSaveRequest CreateSaveRequest() => new()
    {
        CreateItems = AddedItems.Select(line => new CreateItemRequest<ProductNeutralManagerWriteDto>
        {
            ClientKey = line.ClientKey,
            RowIndex = line.RowIndex,
            Data = line.ToWriteDto()
        }).ToList(),
        UpdateItems = ModifiedItems.Select(line => new UpdateItemRequest<ProductNeutralManagerWriteDto>
        {
            Uid = line.Uid,
            Version = line.Version,
            ClientKey = line.ClientKey,
            RowIndex = line.RowIndex,
            Data = line.ToWriteDto()
        }).ToList(),
        DeleteItems = DeletedItems.Where(line => line.Uid != Guid.Empty).Select(line => new DeleteItemRequest
        {
            Uid = line.Uid,
            Version = line.Version,
            ClientKey = line.ClientKey,
            RowIndex = line.RowIndex,
            IsSoftDelete = true
        }).ToList()
    };

    /// <summary>回填成功行的标识与版本，并在整批成功后接纳当前变更。</summary>
    public void ApplySaveResponse(ProductNeutralManagerBatchSaveResponse response)
    {
        var byKey = AllLines.ToDictionary(line => line.ClientKey, StringComparer.Ordinal);
        foreach (var result in response.Items.Where(item => item.Success && item.Operation != SaveOperation.Delete))
        {
            if (string.IsNullOrWhiteSpace(result.ClientKey) || !byKey.TryGetValue(result.ClientKey, out var line)) continue;
            using var _ = line.SuppressChangeTracking();
            if (result.Uid.HasValue) line.Uid = result.Uid.Value;
            if (result.NewVersion.HasValue) line.Version = result.NewVersion.Value;
        }
        AcceptChanges();
    }
}
