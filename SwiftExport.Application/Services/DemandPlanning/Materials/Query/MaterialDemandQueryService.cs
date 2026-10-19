using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Query;

/// <summary>提供物料需求工作台和来源导航的只读查询。</summary>
public sealed class MaterialDemandQueryService(ServiceBaseContext context, IMaterialDemandQueryRepository netDemandRepository,
    ICurrentUser currentUser, IPermissionEvaluationService permissions) : IMaterialDemandQueryService
{
    private readonly ILogger<MaterialDemandQueryService> _logger = context.LoggerFactory.CreateLogger<MaterialDemandQueryService>();

    /// <summary>查询物料需求工作台的物料事实和有效采购关联汇总。</summary>
    public async Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>> SearchManagerAsync(
        MaterialDemandManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new MaterialDemandManagerSearchRequest();
            var canReadPurchase=currentUser.IsAuthenticated && currentUser.UserId is {} actor
                && await permissions.HasPermissionAsync(actor,PermissionKeys.ProcurementOrderRead,cancellationToken);
            if(!canReadPurchase && (request.ActualPurchaseSupplierId.HasValue || !string.IsNullOrWhiteSpace(request.ActualPurchaseSupplierKeyword)
                || !string.IsNullOrWhiteSpace(request.PurchaseOrderNo)))
                return ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Fail(ServiceErrorCodes.Forbidden,"按实际供应商或采购单号筛选需要采购查看权限。");
            var result=await netDemandRepository.SearchManagerAsync(request,cancellationToken,canReadPurchase);
            // 数量及需核实状态属于物料管理摘要；实际采购身份仅向采购读取权限开放。
            if(!canReadPurchase)
                foreach(var row in result.Items){row.PurchaseOrderNos=null!;row.ActualPurchaseSuppliers=null!;}
            return ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询物料需求工作台失败。");
            return ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Fail("查询物料需求失败。");
        }
    }

    /// <summary>查询不含计划状态的物料需求来源导航。</summary>
    public async Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchMaterialSourceGroupsAsync(
        MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new MaterialDemandSourceGroupSearchRequest();
            var groups = await netDemandRepository.SearchMaterialSourceGroupsAsync(request, cancellationToken);
            return ServiceResult<List<NetDemandSourceGroupDto>>.Ok([.. groups]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询物料需求来源导航失败。");
            return ServiceResult<List<NetDemandSourceGroupDto>>.Fail("查询物料需求来源导航失败。");
        }
    }

}
