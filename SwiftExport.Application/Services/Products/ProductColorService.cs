using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products;

public class ProductColorService(IProductColorRepository repository, ServiceBaseContext context) : BaseService<ProductColor, ProductColorDto>(repository, context), IProductColorService
{
    #region 批量保存
    protected override bool IsSoftDelete => false;
    protected override async Task<string?> ValidateBeforeAddAsync(List<ProductColor> entities)
    {
        var codes = new List<string>();
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.ColorCode))
                return "颜色代码不能为空";
            codes.Add(item.ColorCode);
        }

        // 调用子仓储专属方法检查重复
        var repositoryImpl = (IProductColorRepository)_repository;
        var existedCodes = await repositoryImpl.GetExistingColorCodesAsync(codes);
        if (existedCodes.Any())
        {
            return $"以下颜色代码已存在，拒绝新增: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切正常，放行！
        return null;
    }
    #endregion
}
