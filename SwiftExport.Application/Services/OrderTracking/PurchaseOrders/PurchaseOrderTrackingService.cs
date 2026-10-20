using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.OrderTracking.PurchaseOrders;

namespace SwiftExport.Application.Services.OrderTracking.PurchaseOrders;

/// <summary>采购单跟踪权限与查询编排；不发写命令、不记录回执、不接管调用方事务。</summary>
public sealed class PurchaseOrderTrackingService(ServiceBaseContext context,ICurrentUser user,
    IPermissionEvaluationService permissions,IPurchaseOrderTrackingRepository repository) : IPurchaseOrderTrackingService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>> SearchPurchaseOrdersAsync(
        PurchaseOrderTrackingSearchRequest request,CancellationToken cancellationToken=default)
        =>ReadAsync<PagedResponseBase<PurchaseOrderTrackingItemDto>>(null,async(progress,receipts)=>
        {
            Validate(request);
            if((request.ReadyStatuses.Count>0||request.DueView!=PurchaseDueViewEnum.All||request.ExpectedCompletionDateFrom.HasValue||request.ExpectedCompletionDateTo.HasValue)&&!progress
                ||request.ReceiptStatuses.Count>0&&!receipts)
                return ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>.Fail(ServiceErrorCodes.Forbidden,"没有所选进度筛选维度的查看权限。");
            return ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>.Ok(await repository.SearchAsync(request,progress,receipts,cancellationToken));
        },cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<List<PurchaseOrderTrackingLineDto>>> GetPurchaseOrderLinesAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default)
        =>ReadAsync<List<PurchaseOrderTrackingLineDto>>(null,async(progress,receipts)=>
        {
            await RequireOrderAsync(purchaseOrderId,cancellationToken);
            return ServiceResult<List<PurchaseOrderTrackingLineDto>>.Ok((await repository.GetLinesAsync(purchaseOrderId,progress,receipts,cancellationToken)).ToList());
        },cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseOrderTrackingReceiptsDto>> GetPurchaseOrderReceiptsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default)
        =>ReadAsync<PurchaseOrderTrackingReceiptsDto>(PermissionKeys.InventoryInboundView,async(_,_)=>
        {
            await RequireOrderAsync(purchaseOrderId,cancellationToken);
            return ServiceResult<PurchaseOrderTrackingReceiptsDto>.Ok(await repository.GetReceiptsAsync(purchaseOrderId,cancellationToken));
        },cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<List<SupplierProgressReportDto>>> GetPurchaseOrderReportsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default)
        =>ReadAsync<List<SupplierProgressReportDto>>(PermissionKeys.ProcurementProgressView,async(_,_)=>
        {
            await RequireOrderAsync(purchaseOrderId,cancellationToken);
            return ServiceResult<List<SupplierProgressReportDto>>.Ok(await repository.GetReportsAsync(purchaseOrderId,cancellationToken));
        },cancellationToken);

    /// <summary>权限拒绝发生于任何仓储读取之前；技术失败、取消及业务无记录保持不同语义。</summary>
    private async Task<ServiceResult<T>> ReadAsync<T>(string? dimension,Func<bool,bool,Task<ServiceResult<T>>> action,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(!user.IsAuthenticated||!user.UserId.HasValue||user.UserId==Guid.Empty)
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated,"登录状态无效，请重新登录。");
        try
        {
            foreach(var key in new[]{PermissionKeys.OrderTrackingView,PermissionKeys.ProcurementOrderRead})
                if(!await permissions.HasPermissionAsync(user.UserId.Value,key,ct))
                    return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden,"当前账号缺少采购跟踪读取权限。");
            var progress=await permissions.HasPermissionAsync(user.UserId.Value,PermissionKeys.ProcurementProgressView,ct);
            var receipts=await permissions.HasPermissionAsync(user.UserId.Value,PermissionKeys.InventoryInboundView,ct);
            if(dimension==PermissionKeys.ProcurementProgressView&&!progress||dimension==PermissionKeys.InventoryInboundView&&!receipts)
                return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden,"当前账号缺少此跟踪维度的查看权限。");
            return await action(progress,receipts);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(ArgumentException ex){return ServiceResult<T>.Fail(ServiceErrorCodes.Validation,ex.Message);}
        catch(KeyNotFoundException){return ServiceResult<T>.Fail(ServiceErrorCodes.NotFound,"未找到采购单。");}
        catch(Exception ex)
        {
            context.LoggerFactory.CreateLogger<PurchaseOrderTrackingService>().LogError(ex,"读取采购单跟踪失败。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.ServiceUnavailable,"读取采购跟踪失败，请稍后重试。");
        }
    }

    /// <summary>先验证稳定身份与存在性，空结果不能掩盖不存在或已删除的采购单。</summary>
    private async Task RequireOrderAsync(Guid id,CancellationToken ct)
    {
        if(id==Guid.Empty)throw new ArgumentException("采购单身份不能为空。");
        if(!await repository.ExistsAsync(id,ct))throw new KeyNotFoundException();
    }

    /// <summary>限制分页、多选、文本与日期区间；无效枚举不能穿透SQL。</summary>
    private static void Validate(PurchaseOrderTrackingSearchRequest request)
    {
        if(request is null||request.PageIndex<1||request.PageSize is <1 or >200||(long)(request.PageIndex-1)*request.PageSize>int.MaxValue
            ||request.Keyword?.Length>100||request.CustomerCodes is null||request.SupplierIds is null
            ||request.CustomerCodes.Count>500||request.SupplierIds.Count>500
            ||request.CustomerCodes.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>100)||request.SupplierIds.Any(x=>x==Guid.Empty)
            ||request.OrderDateFrom?.Date>request.OrderDateTo?.Date||request.PlannedDeliveryDateFrom>request.PlannedDeliveryDateTo
            ||request.ExpectedCompletionDateFrom>request.ExpectedCompletionDateTo
            ||!Valid(request.ConfirmationStatuses)||!Valid(request.LifecycleStatuses)||!Valid(request.ReadyStatuses)||!Valid(request.ReceiptStatuses)
            ||!Enum.IsDefined(request.DueView))throw new ArgumentException("采购跟踪筛选参数无效。");
    }

    /// <summary>只接受已声明的有限状态集合，重复值不扩大查询范围。</summary>
    private static bool Valid<T>(List<T>? values) where T:struct,Enum=>values is not null&&values.Count<=20&&values.All(Enum.IsDefined);
}
