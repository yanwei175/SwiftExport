using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.SupplierSuggestion;

/// <summary>按物料版本局部维护推荐采购供应商，不修改采购和交付事实。</summary>
public sealed class MaterialDemandSupplierSuggestionService(
    ServiceBaseContext context,
    ISupplierRepository supplierRepository,
    INetDemandRepository netDemandRepository) : IMaterialDemandSupplierSuggestionService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<MaterialDemandSupplierSuggestionService> _logger =
        context.LoggerFactory.CreateLogger<MaterialDemandSupplierSuggestionService>();

    /// <summary>
    /// 以物料需求版本为并发边界局部保存推荐采购供应商；该建议不修改任何既有采购单或送达事实。
    /// </summary>
    public async Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
        UpdateSuggestedPurchaseSupplierCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null || command.NetDemandId == Guid.Empty || command.Version <= 0)
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail("INVALID_COMMAND", "物料需求和版本不能为空。");
        if (command.SuggestedPurchaseSupplierId == Guid.Empty)
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail("INVALID_SUPPLIER", "供应商 Uid 无效；清空建议请提交 null。");
        if (_session.Transaction is not null)
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail("TRANSACTION_ALREADY_ACTIVE", "当前请求已存在未完成事务。");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var family = await netDemandRepository.GetActiveFamilyForUpdateAsync(command.NetDemandId);
            var demand = family.SingleOrDefault(item => item.Uid == command.NetDemandId)
                ?? throw new SuggestedSupplierException("MATERIAL_DEMAND_NOT_ACTIVE", "物料需求不存在、已回退或已作废。");
            if (demand.Version != command.Version)
                throw new SuggestedSupplierException("CONCURRENCY_CONFLICT", "物料需求已被其他用户修改，请刷新后重试。");

            Supplier? supplier = null;
            if (command.SuggestedPurchaseSupplierId.HasValue)
            {
                supplier = await supplierRepository.GetByIdAsync(command.SuggestedPurchaseSupplierId.Value, withUpdLock: true)
                    ?? throw new SuggestedSupplierException("INVALID_SUPPLIER", "推荐采购供应商不存在或已停用。");
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (demand.SuggestedPurchaseSupplierId == command.SuggestedPurchaseSupplierId)
            {
                _session.Transaction.Commit();
                return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Ok(new UpdateSuggestedPurchaseSupplierResult
                {
                    NetDemandId = demand.Uid,
                    Version = demand.Version,
                    SuggestedPurchaseSupplierId = supplier?.Uid,
                    SuggestedPurchaseSupplierCode = supplier?.SupplierCode ?? string.Empty,
                    SuggestedPurchaseSupplierName = supplier?.SupplierName ?? string.Empty
                });
            }

            var affected = await netDemandRepository.UpdateSuggestedPurchaseSupplierAsync(
                demand.Uid, demand.Version, command.SuggestedPurchaseSupplierId);
            if (affected != 1)
                throw new SuggestedSupplierException("CONCURRENCY_CONFLICT", "物料需求已被其他用户修改，请刷新后重试。");
            cancellationToken.ThrowIfCancellationRequested();

            _session.Transaction.Commit();
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Ok(new UpdateSuggestedPurchaseSupplierResult
            {
                NetDemandId = demand.Uid,
                Version = demand.Version + 1,
                SuggestedPurchaseSupplierId = supplier?.Uid,
                SuggestedPurchaseSupplierCode = supplier?.SupplierCode ?? string.Empty,
                SuggestedPurchaseSupplierName = supplier?.SupplierName ?? string.Empty
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _session.Transaction.Rollback();
            throw;
        }
        catch (SuggestedSupplierException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "更新物料需求 {NetDemandId} 的推荐采购供应商失败。", command.NetDemandId);
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail("更新推荐采购供应商失败。");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>表示推荐供应商局部更新可预期的业务失败。</summary>
    private sealed class SuggestedSupplierException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

}
