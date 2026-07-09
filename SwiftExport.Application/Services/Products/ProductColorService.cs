using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Products;

public partial class ProductColorService(IProductColorRepository repository, ServiceBaseContext context)
    : BaseService<ProductColor, ProductColorDto>(repository, context), IProductColorService
{
    private readonly ILogger<ProductColorService> _logger = context.LoggerFactory.CreateLogger<ProductColorService>();

    #region 批量保存
    public async Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync(ProductColorBatchSaveRequest batchReq)
    {
        throw new NotImplementedException();
    }
    #endregion


    #region 显式基础读写


    public async Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAllAsync()
    {
        try
        {
            var entities = await repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<ProductColorDto>>.Ok(entities.Adapt<IReadOnlyList<ProductColorDto>>())
                : ServiceResult<IReadOnlyList<ProductColorDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<ProductColorDto>>.Fail(ex.Message);
        }
    }


    public async Task<ServiceResult<PagedResponseBase<ProductColorDto>>> GetPagedAsync(ProductColorSearchRequest request)
    {
        try
        {
            var paged = await repository.GetProductColorPagedAsync(request);
            return ServiceResult<PagedResponseBase<ProductColorDto>>.Ok(new PagedResponseBase<ProductColorDto>
            {
                Items = paged.Items.Adapt<List<ProductColorDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResponseBase<ProductColorDto>>.Fail(ex.Message);
        }
    }




    #endregion

}
