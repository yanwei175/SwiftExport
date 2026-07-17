using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Products;

public partial class BomService(
    IBomRepository bomRepo,
    IProductRepository pdRepo,
    ServiceBaseContext context)
    : BaseService<Bom, BomDto>(bomRepo, context), IBomService
{

    private readonly ILogger<BomService> _logger = context.LoggerFactory.CreateLogger<BomService>();

    protected override async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<BomDto>> createItems)
        => await ValidateWriteAsync(createItems.Select(x => (Guid.Empty, x.Data)));

    protected override async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<BomDto>> updateItems)
        => await ValidateWriteAsync(updateItems.Select(x => (x.Uid, x.Data)));

    private async Task<string?> ValidateWriteAsync(IEnumerable<(Guid Uid, BomDto Data)> items)
    {
        foreach (var (_, dto) in items)
        {
            var parent = await pdRepo.GetByBomNoAsync(dto.BomNo);
            if (parent is null) return $"BOM {dto.BomNo} 没有对应的父产品。";
            var parentError = CustomerSuppliedMaterialPolicy.ValidateBomParent(parent);
            if (parentError is not null) return parentError;
            if (dto.SubSkuId == Guid.Empty) return "BOM 子产品不能为空。";
            var child = await pdRepo.GetByIdAsync(dto.SubSkuId);
            if (child is null) return "BOM 子产品不存在或已删除。";
            var line = new Bom
            {
                BomNo = dto.BomNo,
                ParentSkuId = parent.Uid,
                ParentSku = parent.Sku,
                SubSkuId = child.Uid,
                SubSku = child.Sku,
                Numerator = dto.Numerator,
                Denominator = dto.Denominator
            };
            var lineError = CustomerSuppliedMaterialPolicy.ValidateBomLine(line, child);
            if (lineError is not null) return lineError;
            if (CustomerSuppliedMaterialPolicy.IsCustomerSupplied(child))
            {
                var childError = CustomerSuppliedMaterialPolicy.ValidateProduct(
                    child, await bomRepo.HasActiveBomForProductAsync(child.Uid, child.BomNo));
                if (childError is not null) return childError;
            }
        }
        return null;
    }

    #region BOM管理CRUD操作

    public async Task<ServiceResult<int>> DeleteBomByBomNoAsync(string bomNo)
    {

        var rst = await bomRepo.DeleteBomByBomNoAsync(bomNo);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("[{EntityName}] 根据BomNo删除Bom成功！BomNo: {BomNo}, 受影响行数: {AffectedRows}", nameof(Bom), bomNo, rst);
        }
        return ServiceResult<int>.Ok(rst);
    }

    public async Task<ServiceResult<List<BomDto>>> GetBomDtoByBomNoAsync(string bomNo)
    {
        var entities = await bomRepo.GetBomDtoByBomNoAsync(bomNo);
        if (!entities.Any()) return ServiceResult<List<BomDto>>.Fail("未找到数据");

        return ServiceResult<List<BomDto>>.Ok([.. entities]);
    }

    public async Task<ServiceResult<bool>> ValidateBomAsync(string productSku)
    {
        if (string.IsNullOrWhiteSpace(productSku))
        {
            return ServiceResult<bool>.Fail("产品SKU不能为空");
        }

        // 0. 获取产品的 BOM 号
        Product? pd = await pdRepo.GetBySkuAsync(productSku);
        if (pd == null || string.IsNullOrEmpty(pd.BomNo))
            return ServiceResult<bool>.Fail("产品型号不存在或产品没设置BOM");

        string bomNo = pd.BomNo;

        // 1. 查询该产品的 BOM 项
        var bomItems = await bomRepo.GetBomByBomNoAsync(bomNo);
        if (bomItems == null || !bomItems.Any())
            return ServiceResult<bool>.Fail("该Bom 没有 BOM 数据，视为不合法");

        // 2. 数量合法性
        if (bomItems.Any(i => i.Numerator <= 0))
            return ServiceResult<bool>.Fail("子项的用量不能小于等于0");

        // 3. 子件存在性
        foreach (var item in bomItems)
        {
            var exists = item.SubSkuId.HasValue
                ? await pdRepo.ExistsIdAsync(item.SubSkuId.Value)
                : await pdRepo.GetBySkuAsync(item.SubSku) != null;
            if (!exists) return ServiceResult<bool>.Fail($"子项{item.SubSku}不存在");
        }

        // 4. 重复项检查
        if (bomItems.GroupBy(i => i.SubSku).Any(g => g.Count() > 1))
            return ServiceResult<bool>.Fail($"子项中存在重复项");

        // 5. 循环依赖检查（DFS，异步）
        var visited = new HashSet<string>();

        async Task<bool> HasCycleAsync(string currentBomNo)
        {
            if (!visited.Add(currentBomNo)) return true; // 已访问过，说明有环

            var children = await bomRepo.GetBomByBomNoAsync(currentBomNo);
            foreach (var child in children)
            {
                // 递归检查子件的 BOM
                var subPd = await pdRepo.GetBySkuAsync(child.SubSku);
                if (subPd != null && !string.IsNullOrEmpty(subPd.BomNo))
                {
                    if (await HasCycleAsync(subPd.BomNo)) return true;
                }
            }

            visited.Remove(currentBomNo);
            return false;
        }

        if (await HasCycleAsync(bomNo)) return ServiceResult<bool>.Fail("递归依赖检查不通过,Bom链上存在不合理.");

        return ServiceResult<bool>.Ok(true); // 所有检查通过
    }

    public async Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTreeAsync(string productSku)
    {
        if (string.IsNullOrWhiteSpace(productSku))
            return ServiceResult<IEnumerable<BomTreeDto>>.Fail("产品SKU不能为空");

        var pd = await pdRepo.GetBySkuAsync(productSku);
        if (pd == null || string.IsNullOrEmpty(pd.BomNo))
        {
            return ServiceResult<IEnumerable<BomTreeDto>>.Ok([]);
        }

        var visited = new HashSet<string>();
        var result = await BuildBomTreeAsync(pd.BomNo, visited);
        return ServiceResult<IEnumerable<BomTreeDto>>.Ok(result);
    }

    private async Task<List<BomTreeDto>> BuildBomTreeAsync(string bomNo, HashSet<string> visited)
    {
        var resultList = new List<BomTreeDto>();
        if (!visited.Add(bomNo))
        {
            // 发生循环依赖，直接返回当前已构建部分避免死循环
            return resultList;
        }

        var bomItems = await bomRepo.GetBomByBomNoAsync(bomNo);
        foreach (var item in bomItems)
        {
            var dto = item.Adapt<BomTreeDto>();
            var subPd = await pdRepo.GetBySkuAsync(item.SubSku);
            if (subPd != null)
            {
                // 补齐显示用的字段
                dto.Description = subPd.DescriptionEn;
                dto.MainImage = subPd.MainImage;
                // 如果你有 Finish (表面处理/颜色)，这里可以补齐：
                // 这取决于你的底层模型。例如如果需要查询 Color，可在这里处理

                // 判断是否继续递归拆解
                // 条件：实体有BomNo 且 本身的类型不是"止拆"
                if (subPd.BomVersion > 0 && !string.IsNullOrEmpty(subPd.BomNo))
                {
                    dto.Children = await BuildBomTreeAsync(subPd.BomNo, visited);
                }
            }
            else
            {
                // 如果子件在产品表里找不到，仍然添加这个节点，但可以考虑标记一下（例如设置Description为"未知产品"）
                dto.Description = "未知Sku";
            }
            resultList.Add(dto);
        }

        visited.Remove(bomNo);
        return resultList;
    }
    #endregion






    #region 显式基础读写


    public async Task<ServiceResult<IReadOnlyList<BomDto>>> GetAllAsync()
    {
        try
        {
            var entities = await bomRepo.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<BomDto>>.Ok(entities.Adapt<IReadOnlyList<BomDto>>())
                : ServiceResult<IReadOnlyList<BomDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<BomDto>>.Fail(ex.Message);
        }
    }


    public async Task<ServiceResult<BomBatchSaveResponse>> BatchSaveAsync(BomBatchSaveRequest batchReq)
    {
        //_session.Transaction = _session.Connection.BeginTransaction();
        //var result = new BomBatchSaveResponse();

        //var createList = batchReq.CreateItems?.Adapt<List<Bom>>();
        //var updateList = batchReq.UpdateItems?.Adapt<List<Bom>>();
        //var deleteIds = batchReq.DeleteItems;

        //try
        //{
        //    var errorMsg = new StringBuilder();

        //    if (updateList is { Count: > 0 })
        //    {
        //        var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
        //        if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

        //        var needUpdateUids = updateList.Select(x => x.Uid).ToList();
        //        var existingUids = await bomRepo.GetExistingIdListAsync(needUpdateUids);
        //        var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
        //        if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
        //    }

        //    if (deleteIds is { Count: > 0 })
        //    {
        //        var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
        //        if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
        //    }

        //    if (errorMsg.Length > 0)
        //        return ServiceResult<BomBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

        //    if (bomRepo is not IBatchWriteRepository<Bom> batchRepository)
        //        return ServiceResult<BomBatchSaveResponse>.Fail($"{nameof(Bom)} 仓储未实现批量保存");

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
        //        await batchRepository.DeleteRangeAsync(deleteIds);
        //        result.DeleteIdList = deleteIds;
        //    }

        //    _session.Transaction.Commit();
        //    return ServiceResult<BomBatchSaveResponse>.Ok(result);
        //}
        //catch (Exception ex)
        //{
        //    _session.Transaction.Rollback();
        //    return ServiceResult<BomBatchSaveResponse>.Fail(ex.Message);
        //}
        //finally
        //{
        //    _session.Transaction.Dispose();
        //    _session.Transaction = null;
        //}
        throw new NotImplementedException();
    }

    public Task<ServiceResult<PagedResponseBase<BomDto>>> GetPagedListAsync(BomSearchRequest request)
    {
        throw new NotImplementedException();
    }
    #endregion

}
