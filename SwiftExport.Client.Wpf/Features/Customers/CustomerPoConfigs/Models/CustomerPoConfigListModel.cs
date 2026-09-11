using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Customers.CustomerPoConfigs.Models;

/// <summary>管理客户采购默认配置的列表变更集、组合校验和保存结果接纳。</summary>
public sealed class CustomerPoConfigListModel : BaseListModel<CustomerPoConfigLineModel, CustomerPoConfigDto>
{
    /// <summary>初始化列表行工厂。</summary>
    public CustomerPoConfigListModel() : base(_ => new CustomerPoConfigLineModel()) { }
    /// <summary>新增空白配置行。</summary>
    public CustomerPoConfigLineModel AddNewConfig() => AddNew(new CustomerPoConfigDto { Currency = CurrencyEnum.CNY });

    /// <summary>校验行字段及本页客户与供应商组合唯一性。</summary>
    public async Task<string?> ValidateListAsync()
    {
        var error = await ValidateAllAsync();
        if (!string.IsNullOrWhiteSpace(error)) return error;
        var duplicate = Items
            .GroupBy(line => $"{line.CustomerCode.Trim()}|{line.SupplierCode.Trim()}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        return duplicate is null ? null : $"客户+供应商组合重复：{duplicate.Key.Replace('|', '+')}。";
    }

    /// <summary>按当前变更状态构造原子批量保存请求。</summary>
    public CustomerPoConfigBatchSaveRequest CreateSaveRequest() => new()
    {
        CreateItems = AddedItems.Select(line => new CreateItemRequest<CustomerPoConfigDto> { ClientKey = line.ClientKey, RowIndex = line.RowIndex, Data = line.ToDto() }).ToList(),
        UpdateItems = ModifiedItems.Select(line => new UpdateItemRequest<CustomerPoConfigDto> { ClientKey = line.ClientKey, RowIndex = line.RowIndex, Uid = line.Uid, Version = line.Version, Data = line.ToDto() }).ToList(),
        DeleteItems = DeletedItems.Where(line => line.Uid != Guid.Empty).Select(line => new DeleteItemRequest { ClientKey = line.ClientKey, RowIndex = line.RowIndex, Uid = line.Uid, Version = line.Version, IsSoftDelete = true }).ToList()
    };

    /// <summary>回填服务端标识和版本，并在整批成功后接纳变更。</summary>
    public void ApplySaveResponse(CustomerPoConfigBatchSaveResponse response)
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
