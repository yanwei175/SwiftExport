using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using static Dapper.SqlMapper;

namespace SwiftExport.Application.Services.Products;

public class BomService(IBomRepository bomRepo, IProductRepository pdRepo, ServiceBaseContext context) : BaseService<Bom, BomDto>(bomRepo, context), IBomService
{
    private readonly IBomRepository _bomRepo = bomRepo;
    private readonly IProductRepository _pdRepo = pdRepo;

    protected override bool IsSoftDelete => false;

    public override async Task<ServiceResult<int>> AddAsync(BomDto dto)
    {
        var entity = dto.Adapt<Bom>();

        //检查这条BOM是否在数据库中对应BomNo和ProductSku已经存在了，如果存在了就不允许新增
        //根据这条bom的BomNo和ProductSku去数据库中查询，如果查询到了数据就说明已经存在了，就不允许新增了
        var rst = await _bomRepo.IsBomItemExistAsync(entity.BomNo, entity.SubSku); 

        if (rst)
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning("尝试新增 {EntityName} 数据失败，原因：BomNo和ProductSku的组合已经存在！BomNo: {BomNo}, ProductSku: {ProductSku}", EntityName, entity.BomNo, entity.SubSku);
            }
            return ServiceResult<int>.Fail("相同BomNo和ProductSku的记录已存在，无法新增！");
        }


        entity.CreatedDate = DateTime.Now;
        entity.IsDeleted = false;

        var id = await _repository.AddAsync(entity);

        // Information 级别
        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation("成功新增 {EntityName} 数据，生成的 ID: {Id}", EntityName, id);
        }

        return ServiceResult<int>.Ok(id);
    }


    public async Task<ServiceResult<int>> DeleteBomByBomNoAsync(string bomNo)
    {

        var rst = await _bomRepo.DeleteBomByBomNoAsync(bomNo);

        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation("[{EntityName}] 根据BomNo删除Bom成功！BomNo: {BomNo}, 受影响行数: {AffectedRows}", EntityName, bomNo, rst);
        }
        return ServiceResult<int>.Ok(rst);
    }

    public async Task<ServiceResult<IEnumerable<Bom>>> GetBomByBomNoAsync(string bomNo)
    {
        return ServiceResult<IEnumerable<Bom>>.Ok(await _bomRepo.GetBomByBomNoAsync(bomNo));
    }

    public async Task<ServiceResult<bool>> ValidateBomAsync(string productSku)
    {
        if (string.IsNullOrWhiteSpace(productSku))
        {
            return ServiceResult<bool>.Fail("产品SKU不能为空");
        }

        // 0. 获取产品的 BOM 号
        Product pd = await _pdRepo.GetBySkuAsync(productSku);
        if (pd == null || string.IsNullOrEmpty(pd.BomNo))
            return ServiceResult<bool>.Fail("产品型号不存在或产品没设置BOM");

        string bomNo = pd.BomNo;

        // 1. 查询该产品的 BOM 项
        var bomItems = await _bomRepo.GetBomByBomNoAsync(bomNo);
        if (bomItems == null || !bomItems.Any())
            return ServiceResult<bool>.Fail("该Bom 没有 BOM 数据，视为不合法");

        // 2. 数量合法性
        if (bomItems.Any(i => i.Numerator <= 0))
            return ServiceResult<bool>.Fail("子项的用量不能小于等于0");

        // 3. 子件存在性
        foreach (var item in bomItems)
        {
            var exists = await _pdRepo.ExistsIdAsync(Convert.ToInt32(item.SubSkuId));
            if (!exists) return ServiceResult<bool>.Fail($"子项{item.SubSku}的Id不存在");
        }

        // 4. 重复项检查
        if (bomItems.GroupBy(i => i.SubSku).Any(g => g.Count() > 1))
            return ServiceResult<bool>.Fail($"子项中存在重复项");

        // 5. 循环依赖检查（DFS，异步）
        var visited = new HashSet<string>();

        async Task<bool> HasCycleAsync(string currentBomNo)
        {
            if (!visited.Add(currentBomNo)) return true; // 已访问过，说明有环

            var children = await _bomRepo.GetBomByBomNoAsync(currentBomNo);
            foreach (var child in children)
            {
                // 递归检查子件的 BOM
                var subPd = await _pdRepo.GetBySkuAsync(child.SubSku);
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

        var pd = await _pdRepo.GetBySkuAsync(productSku);
        if (pd == null || string.IsNullOrEmpty(pd.BomNo))
        {
            return ServiceResult<IEnumerable<BomTreeDto>>.Ok(new List<BomTreeDto>());
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

        var bomItems = await _bomRepo.GetBomByBomNoAsync(bomNo);
        foreach (var item in bomItems)
        {
            var dto = item.Adapt<BomTreeDto>();
            var subPd = await _pdRepo.GetBySkuAsync(item.SubSku);
            if (subPd != null)
            {
                // 补齐显示用的字段
                dto.Description = subPd.Description;
                dto.MainImage = subPd.MainImage;
                // 如果你有 Finish (表面处理/颜色)，这里可以补齐：
                // 这取决于你的底层模型。例如如果需要查询 Color，可在这里处理
                
                // 判断是否继续递归拆解
                // 条件：实体有BomNo 且 本身的类型不是"止拆"
                if (string.Equals(subPd.SupplyType, SupplyType.按BOM拆分, StringComparison.OrdinalIgnoreCase) && 
                    !string.IsNullOrEmpty(subPd.BomNo) && 
                    !string.Equals(item.BomSubType, BomSubType.止拆, StringComparison.OrdinalIgnoreCase))
                {
                    dto.Children = await BuildBomTreeAsync(subPd.BomNo, visited);
                }
            }else
            {
                // 如果子件在产品表里找不到，仍然添加这个节点，但可以考虑标记一下（例如设置Description为"未知产品"）
                dto.Description = "未知Sku";
            }
            resultList.Add(dto);
        }

        visited.Remove(bomNo);
        return resultList;
    }

    //public async Task<ServiceResult<int>> SyncBomItemsAsync(IEnumerable<Bom> items)
    //{
    //    // 1. 开启事务
    //    _session.Transaction = _session.Connection.BeginTransaction();
    //    try
    //    {

    //        var affectedRows = await _bomRepo.SyncBomItemsAsync(items);

    //        // 提交事务
    //        _session.Transaction.Commit();

    //        if (Logger.IsEnabled(LogLevel.Information))
    //        {
    //            Logger.LogInformation("[{EntityName}] 同步失败！", EntityName);
    //        }

    //        return ServiceResult<int>.Ok(affectedRows);

    //    }
    //    catch (Exception ex)
    //    {
    //        _session.Transaction.Rollback();
    //        if (Logger.IsEnabled(LogLevel.Error)) Logger.LogError(ex, "[{EntityName}] 同步发生致命异常，事务已回滚！", EntityName);
    //        throw;
    //    }
    //    finally
    //    {
    //        _session.Transaction.Dispose();
    //        _session.Transaction = null;
    //    }


    //}
}
