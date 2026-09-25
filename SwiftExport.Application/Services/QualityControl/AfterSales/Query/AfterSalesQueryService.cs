using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.QualityControl;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl;

using SwiftExport.Application.Services.QualityControl.AfterSales.Policies;
using SwiftExport.Application.Services.QualityControl.AfterSales.Exceptions;
using SwiftExport.Application.Services.QualityControl.AfterSales.Internal;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
using static SwiftExport.Application.Services.QualityControl.AfterSales.Internal.AfterSalesRules;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Query;
namespace SwiftExport.Application.Services.QualityControl.AfterSales.Query;

/// <summary>提供售后列表、候选和一致性详情读取，保持原查询权限与 RepeatableRead 边界。</summary>
public sealed class AfterSalesQueryService(ServiceBaseContext context, IAfterSalesQueryRepository queries, IAfterSalesLookupRepository lookups, IAfterSalesDocumentRepository documents, IUserPermissionService permissions, ICurrentUser user) : IAfterSalesQueryService
{
    /// <inheritdoc />
    public Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken = default)
        => ReadAsync(() => queries.SearchAsync(request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken = default)
        => ReadAsync(() => lookups.LookupAsync(kind, request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default)
        => ReadAsync(async () =>
        {
            // 保持头行在同一读取版本，避免并发保存导致编辑页拿到混合快照。
            if(context.Session.Transaction is not null) Fail("BUSINESS_BLOCKED","不能接管外部事务。");
            using var tx=context.Session.Connection.BeginTransaction(IsolationLevel.RepeatableRead);
            context.Session.Transaction=tx;
            try
            {
                var head = await RequireHeaderAsync(uid);
                var lines = (await documents.GetLinesAsync(uid)).Where(x=>!x.IsDeleted).ToList();
                var result = new AfterSalesDetailDto { Header=head.Adapt<AfterSalesHeaderDto>(),
                    Lines=lines.Adapt<List<AfterSalesLineDto>>(),
                    Capabilities=AfterSalesPolicy.Capabilities(head,lines.Any(x=>x.Quantity>0)) };
                tx.Commit();
                return result;
            }
            finally {context.Session.Transaction=null;}
        }, cancellationToken);

    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<T>> action,CancellationToken token)
    {
        if(!await AuthorizedAsync(token)) return ServiceResult<T>.Fail("FORBIDDEN","当前账号无品质管理权限。");
        try { token.ThrowIfCancellationRequested(); return ServiceResult<T>.Ok(await action()); }
        catch(AfterSalesException ex) {return ServiceResult<T>.Fail(ex.Code,ex.Message,ex.Field);}
        catch(ArgumentException) {return ServiceResult<T>.Fail("VALIDATION","查询条件或候选类型无效。");}
    }

    private Task<bool> AuthorizedAsync(CancellationToken token)
        => user.IsAuthenticated && user.UserId.HasValue
            ? permissions.HasPermissionAsync(user.UserId.Value,PermissionKeys.ModuleQualityControlAccess,token)
            : Task.FromResult(false);
    private async Task<AfterSalesHeader> RequireHeaderAsync(Guid uid,bool locked=false)
        => await documents.GetHeaderAsync(uid,locked) ?? throw new AfterSalesException("NOT_FOUND","售后单不存在。");

}
