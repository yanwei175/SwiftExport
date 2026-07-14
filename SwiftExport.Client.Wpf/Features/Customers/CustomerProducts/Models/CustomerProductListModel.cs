using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Customers.CustomerProducts.Models;

public sealed class CustomerProductListModel : BaseListModel<CustomerProductLineModel, CustomerProductDto>
{
    public CustomerProductListModel() : base(_ => new CustomerProductLineModel()) { }

    public CustomerProductLineModel AddNewItem(Guid customerId, string customerCode) => AddNew(new CustomerProductDto
    {
        CustomerId = customerId,
        CustomerCode = customerCode,
        Currency = "USD",
        IsActive = true,
    });

    public CustomerProductBatchSaveRequest CreateSaveRequest() => new()
    {
        CreateItems = AddedItems.Select(line => new CreateItemRequest<CustomerProductDto>
        {
            ClientKey = line.ClientKey, RowIndex = line.RowIndex, Data = line.ToDto(),
        }).ToList(),
        UpdateItems = ModifiedItems.Select(line => new UpdateItemRequest<CustomerProductDto>
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

    public void ApplySaveResponse(CustomerProductBatchSaveResponse response)
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
