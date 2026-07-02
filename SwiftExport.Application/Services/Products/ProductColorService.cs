using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products;

public partial class ProductColorService(IProductColorRepository repository, ServiceBaseContext context)
    : ApplicationServiceBase(context), IProductColorService
{
    private readonly ServiceCrud<ProductColor, ProductColorDto> _crud = new(repository, context);
    private readonly IProductColorRepository _repository = repository;

    #region 批量保存
    private async Task<string?> ValidateBeforeAddAsync(List<ProductColor> entities)
    {
        var codes = new List<string>();
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.ColorCode))
                return "颜色代码不能为空";
            codes.Add(item.ColorCode);
        }

        var existedCodes = await _repository.GetExistingColorCodesAsync(codes);
        if (existedCodes.Any())
        {
            return $"以下颜色代码已存在，拒绝新增: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切正常，放行！
        return null;
    }
    #endregion
}
