using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

public class IndependentDemandService(
    IIndependentDemandRepository repository,
    IBomService bomService,
    IDependentDemandService dependentDemandService,
    ServiceBaseContext context) : BaseService<IndependentDemand, IndependentDemandDto>(repository, context), IIndependentDemandServices
{
    private readonly IIndependentDemandRepository _independentRepo = repository;
    private readonly IBomService _bomService = bomService;
    private readonly IDependentDemandService _dependentService = dependentDemandService;
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
            if (entity.StatusFlag != RequirementStatusEnums.已完成)
            {
                if (entity.FilledQty >= entity.OriginalQty)
                    entity.StatusFlag = RequirementStatusEnums.已到货;
                else if (entity.FilledQty > 0 && entity.FilledQty < entity.OriginalQty)
                    entity.StatusFlag = RequirementStatusEnums.部分到货;
            }
            // 中文注释：状态闭环规则2：仅当到货后并且发货数量达到原始数量，才视为“已完成”。
            if (entity.FilledQty >= entity.OriginalQty && entity.ShippedQty >= entity.OriginalQty)
                entity.StatusFlag = RequirementStatusEnums.已完成;

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
    public async Task<ServiceResult<List<DependentDemandDto>>> ExecuteDecomposeBySourceAsync(string sourceType, string sourceNo)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
            return ServiceResult<List<DependentDemandDto>>.Fail("来源类型和来源单号不能为空");

        var demands = await _independentRepo.GetListBySourceTypeAndSourceNoAsync(sourceType, sourceNo);
        if (demands == null || demands.Count == 0)
            return ServiceResult<List<DependentDemandDto>>.Fail("未找到可拆解的销售需求");

        // 中文注释：拆解防重校验——同一来源若存在已拆解标记，则禁止重复拆解。
        var decomposedDemands = demands.Where(x => x.IsDecomposed).ToList();
        if (decomposedDemands.Count > 0)
        {
            var decomposedLineIdsText = string.Join(",", decomposedDemands.Select(x => x.SourceLineId));
            return ServiceResult<List<DependentDemandDto>>.Fail($"需求已拆解，禁止重复执行。来源行：{decomposedLineIdsText}");
        }

        var invalidStatusDemand = demands.FirstOrDefault(x => x.StatusFlag != RequirementStatusEnums.待处理);
        if (invalidStatusDemand != null)
            return ServiceResult<List<DependentDemandDto>>.Fail($"需求行[{invalidStatusDemand.SourceLineId}]状态为[{invalidStatusDemand.StatusFlag}]，仅“待处理”状态可执行拆解");

        var validDemands = demands
            .Where(x => x.OriginalQty > 0
                        && (!string.IsNullOrWhiteSpace(x.Sku)
                            || !string.IsNullOrWhiteSpace(x.Description)
                            || !string.IsNullOrWhiteSpace(x.Features)
                            || !string.IsNullOrWhiteSpace(x.ColorName)))
            .ToList();

        if (validDemands.Count == 0)
            return ServiceResult<List<DependentDemandDto>>.Fail("无可拆解需求：请检查需求数量，且至少维护SKU或非标描述信息");

        var lineIds = validDemands.Select(x => (int)x.SourceLineId).Distinct().ToList();
        var decomposeTime = DateTime.Now;

        // 中文注释：并发窗口收口 —— 先原子标记未拆解行，只有一个并发请求可成功拿到全部行。
        var markedCount = await _independentRepo.TryMarkDecomposedAsync(sourceType, sourceNo, lineIds, decomposeTime);
        if (markedCount < lineIds.Count)
            return ServiceResult<List<DependentDemandDto>>.Fail("需求已被其他操作锁定或拆解，禁止重复执行");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 中文注释：构建 BOM 拆解请求对象。
            var decomposeRequest = new DecomposeRequestDto
            {
                SourceType = sourceType,
                SourceNo = sourceNo,
                Lines = [.. validDemands.Select(x => new DecomposeLineRequestDto
                {
                    SourceLineId = x.SourceLineId,
                    Sku = x.Sku ?? string.Empty,
                    Description = x.Description,
                    ColorName = x.ColorName,
                    Features = x.Features,
                    Quantity = x.OriginalQty,
                    RequiredDate = x.HopefulDate
                })]
            };

            // 中文注释：计算拆解（核心引擎）
            var calculated = await _bomService.CalculateDecomposeAsync(decomposeRequest);

            // 中文注释：写入依赖需求池（持久化层）
            var saveRst = await _dependentService.SaveToPoolAsync(calculated);
            if (!saveRst.Success)
            {
                _session.Transaction.Rollback();
                await _independentRepo.RollbackDecomposedMarkAsync(sourceType, sourceNo, lineIds);
                return ServiceResult<List<DependentDemandDto>>.Fail(saveRst.Message);
            }

            foreach (var demand in validDemands)
            {
                demand.StatusFlag = RequirementStatusEnums.采购中;
                demand.IsDecomposed = true;
                demand.DecomposedTime = decomposeTime;
                demand.UpdatedDate = DateTime.Now;
            }

            await _independentRepo.UpdateRangeAsync(validDemands);
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



}
