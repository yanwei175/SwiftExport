using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Suppliers.SupplierProducts.Models;

public sealed class SupplierProductListModel : BaseListModel<SupplierProductLineModel, SupplierProductDto>
{
    public SupplierProductListModel() : base(_ => new SupplierProductLineModel()) { }

    public SupplierProductLineModel AddNewItem(Guid supplierId, string supplierCode, string supplierName, string currency, decimal? taxRate) => AddNew(new SupplierProductDto
    {
        SupplierId = supplierId,
        SupplierCode = supplierCode,
        SupplierName = supplierName,
        Currency = string.IsNullOrWhiteSpace(currency) ? "CNY" : currency,
        TaxRate = taxRate,
    });

    public SupplierProductBatchSaveRequest CreateSaveRequest() => new()
    {
        CreateItems = AddedItems.Select(line => new CreateItemRequest<SupplierProductDto>
        {
            ClientKey = line.ClientKey, RowIndex = line.RowIndex, Data = line.ToDto(),
        }).ToList(),
        UpdateItems = ModifiedItems.Select(line => new UpdateItemRequest<SupplierProductDto>
        {
            ClientKey = line.ClientKey, RowIndex = line.RowIndex,
            Uid = line.Uid, Version = line.Version, Data = line.ToDto(),
        }).ToList(),
        DeleteItems = DeletedItems.Where(line => line.Uid != Guid.Empty).Select(line => new DeleteItemRequest
        {
            ClientKey = line.ClientKey, RowIndex = line.RowIndex,
            Uid = line.Uid, Version = line.Version, IsSoftDelete = true,
        }).ToList(),
    };

    public void ApplySaveResponse(SupplierProductBatchSaveResponse response)
    {
        var byKey = AllLines.ToDictionary(line => line.ClientKey, StringComparer.Ordinal);
        foreach (var result in response.Items.Where(item => item.Success && item.Operation != SaveOperation.Delete))
        {
            if (!string.IsNullOrWhiteSpace(result.ClientKey) && byKey.TryGetValue(result.ClientKey, out var line))
            {
                using var _ = line.SuppressChangeTracking();
                if (result.Uid.HasValue) line.Uid = result.Uid.Value;
                if (result.NewVersion.HasValue) line.Version = result.NewVersion.Value;
            }
        }

        AcceptChanges();
    }
}
