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

public partial class ProductCategoryService(IProductCategoryRepository repository, ServiceBaseContext context)
    : BaseService<ProductCategory, ProductCategoryDto>(repository, context), IProductCategoryService
{
    private readonly ILogger<ProductCategoryService> _logger = context.LoggerFactory.CreateLogger<ProductCategoryService>();


    #region 显式基础读写
    public async Task<ServiceResult<IReadOnlyList<ProductCategoryDto>>> GetAllAsync()
    {
        try
        {
            var entities = await repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<ProductCategoryDto>>.Ok(entities.Adapt<IReadOnlyList<ProductCategoryDto>>())
                : ServiceResult<IReadOnlyList<ProductCategoryDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<ProductCategoryDto>>.Fail(ex.Message);
        }
    }



    public async Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync(ProductCategoryBatchSaveRequest batchReq)
    {
        //_session.Transaction = _session.Connection.BeginTransaction();
        //var result = new ProductCategoryBatchSaveResponse();

        //var createList = batchReq.CreateItems?.Adapt<List<ProductCategory>>();
        //var updateList = batchReq.UpdateItems?.Adapt<List<ProductCategory>>();
        //var deleteIds = batchReq.DeleteItems;

        //try
        //{
        //    var errorMsg = new StringBuilder();

        //    if (updateList is { Count: > 0 })
        //    {
        //        var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
        //        if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

        //        var needUpdateUids = updateList.Select(x => x.Uid).ToList();
        //        var existingUids = await repository.GetExistingIdListAsync(needUpdateUids);
        //        var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
        //        if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
        //    }

        //    if (deleteIds is { Count: > 0 })
        //    {
        //        var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
        //        if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
        //    }

        //    if (errorMsg.Length > 0)
        //        return ServiceResult<ProductCategoryBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

        //    if (repository is not IBatchWriteRepository<ProductCategory> batchRepository)
        //        return ServiceResult<ProductCategoryBatchSaveResponse>.Fail($"{nameof(ProductCategory)} 仓储未实现批量保存");

        //    if (createList is { Count: > 0 })
        //    {
        //        result.AddIdList = [.. await batchRepository.AddRangeAsync(createList)];
        //    }

        //    if (updateList is { Count: > 0 })
        //    {
        //        await batchRepository.UpdateRangeAsync(updateList);
        //        result.UpdateIdList = [.. updateList.Select(x => x.Uid)];
        //    }

        //    if (deleteIds is { Count: > 0 })
        //    {
        //        await batchRepository.DeleteRangeSoftAsync(deleteIds);
        //        result.DeleteIdList = deleteIds;
        //    }

        //    _session.Transaction.Commit();
        //    return ServiceResult<ProductCategoryBatchSaveResponse>.Ok(result);
        //}
        //catch (Exception ex)
        //{
        //    _session.Transaction.Rollback();
        //    return ServiceResult<ProductCategoryBatchSaveResponse>.Fail(ex.Message);
        //}
        //finally
        //{
        //    _session.Transaction.Dispose();
        //    _session.Transaction = null;
        //}
        throw new NotImplementedException();
    }

    public Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>> GetPagedAsync(ProductCategorySearchRequest request)
    {
        throw new NotImplementedException();
    }
    #endregion

}
