using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>主型号列表集合，负责批量变更集和保存请求构造。</summary>
public sealed class ProductMasterListModel : BaseListModel<ProductMasterLineModel, ProductMasterListItemDto>
{
    /// <summary>初始化主型号列表。</summary>
    public ProductMasterListModel() : base(_ => new ProductMasterLineModel()) { }

    /// <summary>新增一条未保存主型号。</summary>
    public ProductMasterLineModel AddNewMaster() => AddNew(new ProductMasterListItemDto());

    /// <summary>按照当前行状态构造批量保存请求。</summary>
    public ProductMasterBatchSaveRequest CreateSaveRequest()
    {
        return new ProductMasterBatchSaveRequest
        {
            CreateItems = AddedItems.Select(line => new CreateItemRequest<ProductMasterWriteDto>
            {
                ClientKey = line.ClientKey,
                RowIndex = line.RowIndex,
                Data = line.ToWriteDto()
            }).ToList(),
            UpdateItems = ModifiedItems.Select(line => new UpdateItemRequest<ProductMasterWriteDto>
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
    }

    /// <summary>使用服务端逐行结果回填标识和版本，并在全部成功后接纳变更。</summary>
    public void ApplySaveResponse(ProductMasterBatchSaveResponse response)
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
