using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
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
    public async Task<ServiceResult<List<DependentDemandDto>>> 根据来源单据拆解至净需求Async(string sourceType, string sourceNo)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
            return ServiceResult<List<DependentDemandDto>>.Fail("来源类型和来源单号不能为空");

        //根据来源单据类型和单据号查询原始需求行
        var 原需求行List = await _independentRepo.GetListBySourceTypeAndSourceNoAsync(sourceType, sourceNo);
        if (原需求行List == null || 原需求行List.Count == 0)
            return ServiceResult<List<DependentDemandDto>>.Fail("未找到可拆解的销售需求");

        // 中文注释：拆解防重校验——同一来源若存在已拆解标记，则禁止重复拆解。
        var 原需求行是否已拆解 = 原需求行List.Where(x => x.IsDecomposed).ToList();
        if (原需求行是否已拆解.Count > 0)
        {
            var decomposedLineIdsText = string.Join(",", 原需求行是否已拆解.Select(x => x.SourceLineId));
            return ServiceResult<List<DependentDemandDto>>.Fail($"需求已拆解，禁止重复执行。来源行：{decomposedLineIdsText}");
        }

        var 原需求行是否为待处理 = 原需求行List.FirstOrDefault(x => x.StatusFlag != IndependentDemandStatusEnum.已确认);
        if (原需求行是否为待处理 != null)
            return ServiceResult<List<DependentDemandDto>>.Fail($"需求行[{原需求行是否为待处理.SourceLineId}]状态为[{原需求行是否为待处理.StatusFlag}]，仅“已确认”状态可执行拆解");

        var 真实可拆原需求 = 原需求行List
            .Where(x => x.OriginalQty > 0             //要求数量必须大于0
                        && (!string.IsNullOrWhiteSpace(x.Sku)  //要求有Sku,无Sku就是非标需求，必须维护描述信息（描述、特征、颜色至少一项）才能拆解
                            || !string.IsNullOrWhiteSpace(x.Description)
                            || !string.IsNullOrWhiteSpace(x.Features)
                            || !string.IsNullOrWhiteSpace(x.ColorName)))
            .ToList();

        if (真实可拆原需求.Count == 0)
            return ServiceResult<List<DependentDemandDto>>.Fail("无可拆解需求：请检查需求数量，且至少维护SKU或非标描述信息");

        var lineIds = 真实可拆原需求.Select(x => (int)x.SourceLineId).Distinct().ToList();
        var 拆解时间 = DateTime.Now;

        // 调用需求头仓库先标记为已拆解，防止重复拆解。若标记失败（返回值小于实际行数），则说明存在并发操作导致部分行已被标记，需禁止本次拆解并提示用户。
        var markedCount = await _independentRepo.TryMarkDecomposedAsync(sourceType, sourceNo, lineIds, 拆解时间);
        if (markedCount < lineIds.Count)
            return ServiceResult<List<DependentDemandDto>>.Fail("需求已被其他操作锁定或拆解，禁止重复执行");

        //开始拆解需求，整个过程需要事务保障原子性：
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 中文注释：统一走“按毛需求集合拆解”的核心入口，调用方负责提供标准化毛需求集合。
            var calculated = await _bomService.根据毛需求集合拆解成净需求(真实可拆原需求);

            // 中文注释：写入依赖需求池（持久化层）
            var saveRst = await _dependentRepository.AddRangeAsync(calculated);

            if (saveRst.Count>0)
            {
                _session.Transaction.Rollback();
                // 拆解失败要把之前标记的已拆解状态回滚掉，释放锁
                await _independentRepo.RollbackDecomposedMarkAsync(sourceType, sourceNo, lineIds);
                return ServiceResult<List<DependentDemandDto>>.Fail("写入依赖需求池失败");
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
            return ServiceResult<List<DependentDemandDto>>.Ok(calculated.Adapt<List<DependentDemandDto>>());
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            await _independentRepo.RollbackDecomposedMarkAsync(sourceType, sourceNo, lineIds);
            return ServiceResult<List<DependentDemandDto>>.Fail($"执行拆解失败：{ex.Message}");
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
    /// <param name="independentDemandIds">毛需求行ID集合</param>
    /// <returns>净需求Dto集合</returns>
    public async Task<ServiceResult<List<DependentDemandDto>>> 根据选定毛需求拆解至净需求Async(List<int> independentDemandIds)
    {
        if (independentDemandIds == null || independentDemandIds.Count == 0)
            return ServiceResult<List<DependentDemandDto>>.Fail("请选择要拆解的毛需求行");
        var 原毛需求行List = await _independentRepo.GetByIdListAsync(independentDemandIds);
        if (原毛需求行List == null || 原毛需求行List.Count == 0)
            return ServiceResult<List<DependentDemandDto>>.Fail("未找到可拆解的销售需求");


        // 中文注释：拆解防重校验——同一来源若存在已拆解标记，则禁止重复拆解。
        var 原需求行是否已拆解 = 原毛需求行List.Where(x => x.IsDecomposed).ToList();
        if (原需求行是否已拆解.Count > 0)
        {
            var decomposedLineIdsText = string.Join(",", 原需求行是否已拆解.Select(x => x.SourceLineId));
            return ServiceResult<List<DependentDemandDto>>.Fail($"需求已拆解，禁止重复执行。来源行：{decomposedLineIdsText}");
        }

        var 原需求行是否为待处理 = 原毛需求行List.FirstOrDefault(x => x.StatusFlag != IndependentDemandStatusEnum.已确认);
        if (原需求行是否为待处理 != null)
            return ServiceResult<List<DependentDemandDto>>.Fail($"需求行[{原需求行是否为待处理.SourceLineId}]状态为[{原需求行是否为待处理.StatusFlag}]，仅“已确认”状态可执行拆解");

        var 真实可拆原需求 = 原毛需求行List
            .Where(x => x.OriginalQty > 0             //要求数量必须大于0
                        && (!string.IsNullOrWhiteSpace(x.Sku)  //要求有Sku,无Sku就是非标需求，必须维护描述信息（描述、特征、颜色至少一项）才能拆解
                            || !string.IsNullOrWhiteSpace(x.Description)
                            || !string.IsNullOrWhiteSpace(x.Features)
                            || !string.IsNullOrWhiteSpace(x.ColorName)))
            .ToList();

        if (真实可拆原需求.Count == 0)
            return ServiceResult<List<DependentDemandDto>>.Fail("无可拆解需求：请检查需求数量，且至少维护SKU或非标描述信息");

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
                return ServiceResult<List<DependentDemandDto>>.Fail("选定毛需求中存在缺少来源信息的行，无法执行并发锁定");

            var markedCount = await _independentRepo.TryMarkDecomposedAsync(group.SourceType, group.SourceNo, group.LineIds, 拆解时间);
            if (markedCount < group.LineIds.Count)
            {
                await 回滚已成功标记分组Async();
                return ServiceResult<List<DependentDemandDto>>.Fail("需求已被其他操作锁定或拆解，禁止重复执行");
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
                return ServiceResult<List<DependentDemandDto>>.Fail("写入依赖需求池失败");
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
            return ServiceResult<List<DependentDemandDto>>.Ok(calculated.Adapt<List<DependentDemandDto>>());
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            await 回滚已成功标记分组Async();
            return ServiceResult<List<DependentDemandDto>>.Fail($"执行拆解失败：{ex.Message}");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }





}
