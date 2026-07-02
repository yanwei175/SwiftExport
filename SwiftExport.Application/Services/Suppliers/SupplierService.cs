using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Suppliers;

public partial class SupplierService(ISupplierRepository repository, ServiceBaseContext context)
        : ApplicationServiceBase(context), ISupplierService
{
    private readonly ServiceCrud<Supplier, SupplierDto> _crud = new(repository, context);
    private readonly ISupplierRepository _repository = repository;

    #region 批量保存
    /// <summary>
    /// 批量保存
    /// </summary>
    private async Task<string?> ValidateBeforeAddAsync(List<Supplier> entities)
    {
        var needAddCodes = new List<string>();

        // A. 校验必填项
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.SupplierCode))
            {
                return "供应商代码不能为空"; // 遇到致命错误直接返回提示，基类会中断并回滚
            }
            needAddCodes.Add(item.SupplierCode);
        }

        var existedCodes = await _repository.GetExistingSupplierCodesAsync(needAddCodes);

        // C. 组装特定报错信息
        if (existedCodes.Any())
        {
            return $"新创建的供应商代码在数据库中已存在，拒绝新增。重复代码: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切校验完美通过，放行让基类去写数据库！
        return null;
    }
    #endregion






}

