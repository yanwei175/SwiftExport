using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

public class IndependentDemandService(
    IIndependentDemandRepository repository,
    IBomService bomService,
    IDependentDemandRepository dependentDemandRepository,
    IDependentDemandService dependentDemandService,
    ServiceBaseContext context) : BaseService<IndependentDemand, IndependentDemandDto>(repository, context), IIndependentDemandServices
{
    private readonly IIndependentDemandRepository _independentRepo = repository;
    private readonly IBomService _bomService = bomService;
    private readonly IDependentDemandService _dependentService = dependentDemandService;
    private readonly IDependentDemandRepository _dependentRepository = dependentDemandRepository;

    public async Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedListAsync(InDependSearchReq search)
    {

        search ??= new InDependSearchReq();

        var criteria = BuildCriteria(search);
        int pageIndex = search.PageIndex > 0 ? search.PageIndex : 1;
        int pageSize = search.PageSize > 0 ? search.PageSize : 20;

        var paged = await _independentRepo.GetPagedListAsync(criteria, pageIndex, pageSize, "Id DESC");

        return ServiceResult<PagedResultDto<IndependentDemandDto>>.Ok(new PagedResultDto<IndependentDemandDto>
        {
            Items = paged.Items.Adapt<List<IndependentDemandDto>>(),
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }
    /// <summary>
    /// 构建分页查询条件。
    /// </summary>
    private static QueryCriteria BuildCriteria(InDependSearchReq search)
    {
        var criteria = new QueryCriteria();

        if (!string.IsNullOrWhiteSpace(search.SourceNo))
            criteria.Add("SourceNo", 查询操作符.模糊匹配, search.SourceNo.Trim());

        if (!string.IsNullOrWhiteSpace(search.SourceType))
            criteria.Add("SourceType", 查询操作符.等于, search.SourceType.Trim());

        if (!string.IsNullOrWhiteSpace(search.Sku))
            criteria.Add("Sku", 查询操作符.模糊匹配, search.Sku.Trim());

        if (!string.IsNullOrWhiteSpace(search.SupplyType))
            criteria.Add("SupplyType", 查询操作符.等于, search.SupplyType.Trim());

        if (search.BomLevel.HasValue)
        {
            // 中文注释：层级过滤支持“成品/子件”工作台筛选。
            if (search.BomLevel.Value == 1)
                criteria.Add("BomLevel", 查询操作符.大于等于, 1);
            else
                criteria.Add("BomLevel", 查询操作符.等于, search.BomLevel.Value);
        }

        if (search.StatusFlag < 0)
            criteria.Add("StatusFlag", 查询操作符.等于, IndependentDemandStatusEnum.草稿);

        if (search.IsKitted.HasValue)
            criteria.Add("IsKitted", 查询操作符.等于, search.IsKitted.Value);

        if (!string.IsNullOrWhiteSpace(search.SupplierKeyword))
        {
            var supplierKeyword = search.SupplierKeyword.Trim();
            criteria.AddOrGroup(
                ("SupplierCode", 查询操作符.模糊匹配, supplierKeyword),
                ("SupplierName", 查询操作符.模糊匹配, supplierKeyword));
        }

        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            criteria.AddOrGroup(
                ("SourceNo", 查询操作符.模糊匹配, keyword),
                ("Sku", 查询操作符.模糊匹配, keyword),
                ("TargetNo", 查询操作符.模糊匹配, keyword));
        }

        return criteria;
    }
    /// <summary>
    /// 更新已到货数量的业务逻辑：
    /// </summary>
    /// <param name="id"></param>
    /// <param name="addQty"></param>
    /// <returns></returns>
    public async Task<ServiceResult<bool>> UpdateFilledQtyAsync(int id, decimal addQty)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return ServiceResult<bool>.Fail("不存在的行");

            entity.FilledQty += addQty;
            entity.UpdatedDate = DateTime.Now;

            // 中文注释：状态闭环规则1：FilledQty 达到 OriginalQty 视为“已到货”。
            if (entity.StatusFlag != IndependentDemandStatusEnum.已完成)
            {
                if (entity.FilledQty >= entity.OriginalQty)
                    entity.StatusFlag = IndependentDemandStatusEnum.已到货;
                else if (entity.FilledQty > 0 && entity.FilledQty < entity.OriginalQty)
                    entity.StatusFlag = IndependentDemandStatusEnum.部分到货;
            }
            // 中文注释：状态闭环规则2：仅当到货后并且发货数量达到原始数量，才视为“已完成”。
            if (entity.FilledQty >= entity.OriginalQty && entity.ShippedQty >= entity.OriginalQty)
                entity.StatusFlag = IndependentDemandStatusEnum.已完成;

            var rst = await _repository.UpdateAsync(entity);
            if (rst > 0) return ServiceResult<bool>.Ok(true);
            return ServiceResult<bool>.Fail("更新失败");
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail(ex.Message);
        }

    }

    /// <summary>
    /// 中文注释：由销售需求（毛需求）驱动执行 BOM 拆解。
    /// 规则：
    /// 1）仅允许已审批后的需求（状态=待处理）参与拆解；
    /// 2）仅拆解有效 SKU 且 OriginalQty > 0 的行；
    /// 3）拆解成功后将需求状态置为“采购中”，表示已进入执行阶段。
    /// </summary>
    public async Task<ServiceResult<List<IndependentDemandDto>>> 根据来源单据拆解至净需求Async(string sourceType, string sourceNo)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
            return ServiceResult<List<IndependentDemandDto>>.Fail("来源类型和来源单号不能为空");

        //根据来源单据类型和单据号查询原始需求行
        var 原需求行List = await _independentRepo.GetListBySourceTypeAndSourceNoAsync(sourceType, sourceNo);
        if (原需求行List == null || 原需求行List.Count == 0)
            return ServiceResult<List<IndependentDemandDto>>.Fail("未找到可拆解的销售需求");

        // 中文注释：拆解防重校验——同一来源若存在已拆解标记，则禁止重复拆解。
        var 原需求行是否已拆解 = 原需求行List.Where(x => x.IsDecomposed).ToList();
        if (原需求行是否已拆解.Count > 0)
        {
            var decomposedLineIdsText = string.Join(",", 原需求行是否已拆解.Select(x => x.SourceLineId));
            return ServiceResult<List<IndependentDemandDto>>.Fail($"需求已拆解，禁止重复执行。来源行：{decomposedLineIdsText}");
        }

        var 原需求行是否为待处理 = 原需求行List.FirstOrDefault(x => x.StatusFlag != IndependentDemandStatusEnum.已确认);
        if (原需求行是否为待处理 != null)
            return ServiceResult<List<IndependentDemandDto>>.Fail($"需求行[{原需求行是否为待处理.SourceLineId}]状态为[{原需求行是否为待处理.StatusFlag}]，仅“已确认”状态可执行拆解");

        var 真实可拆原需求 = 原需求行List
            .Where(x => x.OriginalQty > 0 && !string.IsNullOrWhiteSpace(x.Sku))
            .ToList();

        if (真实可拆原需求.Count == 0)
            return ServiceResult<List<IndependentDemandDto>>.Fail("无可拆解需求：请检查需求数量，且至少维护SKU或非标描述信息");

        var lineIds = 真实可拆原需求.Select(x => (int)x.SourceLineId).Distinct().ToList();
        var 拆解时间 = DateTime.Now;

        // 调用需求头仓库先标记为已拆解，防止重复拆解。若标记失败（返回值小于实际行数），则说明存在并发操作导致部分行已被标记，需禁止本次拆解并提示用户。
        var markedCount = await _independentRepo.TryMarkDecomposedAsync(sourceType, sourceNo, lineIds, 拆解时间);
        if (markedCount < lineIds.Count)
            return ServiceResult<List<IndependentDemandDto>>.Fail("需求已被其他操作锁定或拆解，禁止重复执行");

        //开始拆解需求，整个过程需要事务保障原子性：
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 中文注释：统一走“按毛需求集合拆解”的核心入口，调用方负责提供标准化毛需求集合。
            var calculated = await _bomService.根据毛需求集合拆解成净需求(真实可拆原需求);

            // 中文注释：写入依赖需求池（持久化层）
            var saveRst = await _dependentRepository.AddRangeAsync(calculated);

            if (saveRst.Count > 0)
            {
                _session.Transaction.Rollback();
                // 拆解失败要把之前标记的已拆解状态回滚掉，释放锁
                await _independentRepo.RollbackDecomposedMarkAsync(sourceType, sourceNo, lineIds);
                return ServiceResult<List<IndependentDemandDto>>.Fail("写入依赖需求池失败");
            }

            foreach (var demand in 真实可拆原需求)
            {
                demand.StatusFlag = IndependentDemandStatusEnum.采购中;
                demand.IsDecomposed = true;
                demand.DecomposedTime = 拆解时间;
                demand.UpdatedDate = DateTime.Now;
            }
            // 中文注释：更新原始需求行状态和拆解标记。
            await _independentRepo.UpdateRangeAsync(真实可拆原需求);
            _session.Transaction.Commit();
            return ServiceResult<List<IndependentDemandDto>>.Ok(calculated.Adapt<List<IndependentDemandDto>>());
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            await _independentRepo.RollbackDecomposedMarkAsync(sourceType, sourceNo, lineIds);
            return ServiceResult<List<IndependentDemandDto>>.Fail($"执行拆解失败：{ex.Message}");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>
    /// 由毛需求行ID集合驱动执行 BOM 拆解。规则同上一个方法。
    /// </summary>
    /// <param name="ids">毛需求行ID集合</param>
    /// <returns>净需求Dto集合</returns>
    public async Task<ServiceResult<List<IndependentDemandDto>>> 根据选定毛需求拆解至净需求Async(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return ServiceResult<List<IndependentDemandDto>>.Fail("请选择要拆解的毛需求行");
        var 原毛需求行List = await _independentRepo.GetByIdListAsync(ids);
        if (原毛需求行List == null || 原毛需求行List.Count == 0)
            return ServiceResult<List<IndependentDemandDto>>.Fail("未找到可拆解的销售需求");


        // 中文注释：拆解防重校验——同一来源若存在已拆解标记，则禁止重复拆解。
        var 原需求行是否已拆解 = 原毛需求行List.Where(x => x.IsDecomposed).ToList();
        if (原需求行是否已拆解.Count > 0)
        {
            var decomposedLineIdsText = string.Join(",", 原需求行是否已拆解.Select(x => x.SourceLineId));
            return ServiceResult<List<IndependentDemandDto>>.Fail($"需求已拆解，禁止重复执行。来源行：{decomposedLineIdsText}");
        }

        var 原需求行是否为待处理 = 原毛需求行List.FirstOrDefault(x => x.StatusFlag != IndependentDemandStatusEnum.已确认);
        if (原需求行是否为待处理 != null)
            return ServiceResult<List<IndependentDemandDto>>.Fail($"需求行[{原需求行是否为待处理.SourceLineId}]状态为[{原需求行是否为待处理.StatusFlag}]，仅“已确认”状态可执行拆解");

        var 真实可拆原需求 = 原毛需求行List
            .Where(x => x.OriginalQty > 0             //要求数量必须大于0
                        && (!string.IsNullOrWhiteSpace(x.Sku)  //要求有Sku,无Sku就是非标需求，必须维护描述信息（描述、特征、颜色至少一项）才能拆解
                            || !string.IsNullOrWhiteSpace(x.Description)
                            || !string.IsNullOrWhiteSpace(x.Features)
                            || !string.IsNullOrWhiteSpace(x.ColorName)))
            .ToList();

        if (真实可拆原需求.Count == 0)
            return ServiceResult<List<IndependentDemandDto>>.Fail("无可拆解需求：请检查需求数量，且至少维护SKU或非标描述信息");

        // 中文注释：按来源单据分组做并发防重标记。因为“选定毛需求”可能跨多个来源单据，不能用单一 sourceType/sourceNo 调用标记接口。
        var 来源分组 = 真实可拆原需求
            .GroupBy(x => new { x.SourceType, x.SourceNo })
            .Select(g => new
            {
                g.Key.SourceType,
                g.Key.SourceNo,
                LineIds = g.Select(x => x.SourceLineId).Distinct().ToList()
            })
            .ToList();

        var 拆解时间 = DateTime.Now;
        var 已成功标记分组 = new List<(string SourceType, string SourceNo, List<int> LineIds)>();

        async Task 回滚已成功标记分组Async()
        {
            foreach (var (SourceType, SourceNo, LineIds) in 已成功标记分组)
            {
                await _independentRepo.RollbackDecomposedMarkAsync(SourceType, SourceNo, LineIds);
            }
        }

        // 中文注释：调用需求头仓库先标记为已拆解，防止重复拆解。
        // 若任一来源分组标记行数不足，说明存在并发操作，立即回滚已标记分组并返回失败。
        foreach (var group in 来源分组)
        {
            if (string.IsNullOrWhiteSpace(group.SourceType) || string.IsNullOrWhiteSpace(group.SourceNo))
                return ServiceResult<List<IndependentDemandDto>>.Fail("选定毛需求中存在缺少来源信息的行，无法执行并发锁定");

            var markedCount = await _independentRepo.TryMarkDecomposedAsync(group.SourceType, group.SourceNo, group.LineIds, 拆解时间);
            if (markedCount < group.LineIds.Count)
            {
                await 回滚已成功标记分组Async();
                return ServiceResult<List<IndependentDemandDto>>.Fail("需求已被其他操作锁定或拆解，禁止重复执行");
            }

            已成功标记分组.Add((group.SourceType, group.SourceNo, group.LineIds));
        }

        //开始拆解需求，整个过程需要事务保障原子性：
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 中文注释：统一走“按毛需求集合拆解”的核心入口，支持跨来源单据混合拆解。
            var calculated = await _bomService.根据毛需求集合拆解成净需求(真实可拆原需求);

            // 中文注释：写入依赖需求池（持久化层）
            var saveRst = await _dependentRepository.AddRangeAsync(calculated);

            if (saveRst.Count > 0)
            {
                _session.Transaction.Rollback();
                // 拆解失败要把之前标记的已拆解状态回滚掉，释放锁
                await 回滚已成功标记分组Async();
                return ServiceResult<List<IndependentDemandDto>>.Fail("写入依赖需求池失败");
            }

            foreach (var demand in 真实可拆原需求)
            {
                demand.StatusFlag = IndependentDemandStatusEnum.采购中;
                demand.IsDecomposed = true;
                demand.DecomposedTime = 拆解时间;
                demand.UpdatedDate = DateTime.Now;
            }
            // 中文注释：更新原始需求行状态和拆解标记。
            await _independentRepo.UpdateRangeAsync(真实可拆原需求);
            _session.Transaction.Commit();
            return ServiceResult<List<IndependentDemandDto>>.Ok(calculated.Adapt<List<IndependentDemandDto>>());
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            await 回滚已成功标记分组Async();
            return ServiceResult<List<IndependentDemandDto>>.Fail($"执行拆解失败：{ex.Message}");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    public async Task<ServiceResult<List<IndependentDemandDto>>> 批量回退草稿(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return ServiceResult<List<IndependentDemandDto>>.Fail("Id列表不能为空");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 1. 获取并锁定数据
            List<IndependentDemand> items = [.. await _independentRepo.GetByIdListAsync(ids)];
            if (items == null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<List<IndependentDemandDto>>.Fail("不存在的");                
            }

            //校验状态
            foreach (var item in items)
            {
                item.ApprovalStatus = ApprovalStatusEnum.已批准;
                if (item.StatusFlag != IndependentDemandStatusEnum.已确认 
                    || item.FilledQty>0
                    || item.ShippedQty >0
                    || item.IsDecomposed)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<List<IndependentDemandDto>>.Fail($"已经产生下游流程");
                }
                item.StatusFlag = IndependentDemandStatusEnum.草稿;
            }
            //校验净需求允许回退不
            //var allowRollback = await _dependentService.CanCancelAsync(items.Select(x => x.SourceNo).ToList());


            //// 3. 同步该订单对应销售需求状态为“待处理”（审批通过后由 IndependentDemand 驱动后续拆解）。
            //var updateDemandRst = await UpdateDemandStatusBySourceNoAsync(OrderTypesEnum.销售订单, items.OrderNo ?? string.Empty, IndependentDemandStatusEnum.已确认);
            //if (!updateDemandRst.Success)
            //{
            //    _session.Transaction.Rollback();
            //    return ServiceResult<bool>.Fail(updateDemandRst.Message);
            //}

            // 4. 更新订单审批状态。
  
            await _independentRepo.UpdateRangeAsync(items);

            _session.Transaction.Commit();
            var rst= await _independentRepo.GetByIdListAsync(items.Select(x=>x.Id).ToList());
            return ServiceResult<List<IndependentDemandDto>>.Ok(rst.Adapt<List<IndependentDemandDto>>());
        }
        catch (Exception)
        {
            _session.Transaction.Rollback();
            return ServiceResult<List<IndependentDemandDto>>.Fail("审批订单失败");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    private async Task<bool> CanCancelAsync(List<int> ids)
    {
        
        return await _independentRepo.CanCanceAsync(ids);
    }

}
