using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Customers;

public partial class CustomerPoConfigService(ICustomerPoConfigRepository repository, ServiceBaseContext context)
    : ApplicationServiceBase(context), ICustomerPoConfigService
{
    private readonly ServiceCrud<CustomerPoConfig, CustomerPoConfigDto> _crud = new(repository, context);
    private readonly ICustomerPoConfigRepository _repository = repository;

    #region 批量保存
    private async Task<string?> ValidateBeforeAddAsync(List<CustomerPoConfig> entities)
    {
        // A. 自身列表内的查重 (Self-Duplicates)
        var selfDuplicates = entities
            .GroupBy(x => new { x.CustomerCode, x.SupplierCode })
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.CustomerCode}|{g.Key.SupplierCode}")
            .ToList();

        if (selfDuplicates.Count > 0)
        {
            // 如果自身都有重复，这批新增数据直接打回
            return "批量保存失败：新增列表中存在重复的 客户+供应商 组合！";
        }

        var needAdd = new List<PoConfigCheck>();

        // B. 必填项空值检查
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.CustomerCode)) return "客户代码不能为空";
            if (string.IsNullOrWhiteSpace(item.SupplierCode)) return "供应商代码不能为空";

            needAdd.Add(new PoConfigCheck { CustomerCode = item.CustomerCode, SupplierCode = item.SupplierCode });
        }

        var nonUniquePoConfig = await _repository.GetNonUniqueByPoConfigCheckList(needAdd);

        if (nonUniquePoConfig.Count > 0)
        {
            var wrongAdds = nonUniquePoConfig.Select(x => $"{x.CustomerCode}|{x.SupplierCode}");
            return $"新创建的在数据库中已存在，拒绝新增。重复组合: {string.Join(", ", wrongAdds)}";
        }

        // 一切正常，放行给基类写入！
        return null;
    }
    #endregion

}
