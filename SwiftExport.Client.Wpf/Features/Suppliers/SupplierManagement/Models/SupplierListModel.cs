using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Suppliers.SupplierManagement.Models;

public sealed class SupplierListModel : BaseListModel<SupplierLineModel, SupplierDto>
{
    public SupplierListModel() : base(_ => new SupplierLineModel()) { }

    public SupplierLineModel AddNewSupplier() => AddNew(new SupplierDto { Currency = "CNY" });

    public SupplierBatchSaveRequest CreateSaveRequest() => new()
    {
        CreateItems = AddedItems.Select(line => new CreateItemRequest<SupplierDto>
        {
            ClientKey = line.ClientKey, RowIndex = line.RowIndex, Data = line.ToDto(),
        }).ToList(),
        UpdateItems = ModifiedItems.Select(line => new UpdateItemRequest<SupplierDto>
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

    public void ApplySaveResponse(SupplierBatchSaveResponse response)
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
