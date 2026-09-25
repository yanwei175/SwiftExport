using Dapper;
using System.Data;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Contexts;

using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Query;
namespace SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Query;

/// <summary>保持原售后列表筛选、投影和稳定排序 SQL。</summary>
public sealed class AfterSalesQueryRepository(RepositoryBaseContext context) : IAfterSalesQueryRepository
{
    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;
    private static readonly string[] HeaderColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","AfterSalesNo","CustomerId","CustomerCodeSnapshot","CustomerNameSnapshot","AcceptedDate","OwnerId","DocumentStatus","ConfirmationStatus","DemandConfirmedAt","DemandConfirmedBy","ExpectedCompletionDate","Conclusion","Remark","OriginalSalesOrderLineId","CustomerComplaintNo","LocalFileFolder","ResponsibleSupplierId","IssueProductId","IssueSkuSnapshot","IssueCustomerSkuSnapshot","IssueLinkSkuSnapshot","IssueDescription","QualityCategoryId","CauseAnalysis","RequiredDate","ServiceAmountCny","ReturnedReceivedDate","TreatmentDescription"];
    /// <inheritdoc />
    public async Task<PagedResponseBase<AfterSalesListItemDto>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        const string where = """
            WHERE h.IsDeleted=0
              AND (@CustomerId IS NULL OR h.CustomerId=@CustomerId)
              AND (@DocumentStatus IS NULL OR h.DocumentStatus=@DocumentStatus)
              AND (@ConfirmationStatus IS NULL OR h.ConfirmationStatus=@ConfirmationStatus)
              AND (@DateFrom IS NULL OR h.AcceptedDate>=@DateFrom)
              AND (@DateTo IS NULL OR h.AcceptedDate<=@DateTo)
              AND (@ResponsibleSupplierId IS NULL OR h.ResponsibleSupplierId=@ResponsibleSupplierId)
              AND (@Keyword IS NULL OR h.AfterSalesNo LIKE @Keyword OR h.CustomerComplaintNo LIKE @Keyword OR h.IssueSkuSnapshot LIKE @Keyword OR h.IssueCustomerSkuSnapshot LIKE @Keyword OR h.IssueLinkSkuSnapshot LIKE @Keyword OR EXISTS(SELECT 1 FROM dbo.AfterSalesLine l WHERE l.HeaderId=h.Uid AND l.IsDeleted=0 AND
                (l.SkuSnapshot LIKE @Keyword OR l.CustomerSkuSnapshot LIKE @Keyword OR l.LinkSkuSnapshot LIKE @Keyword)))
            """;
        var sql = $"""
            SELECT COUNT(*) FROM dbo.AfterSalesHeader h {where};
            SELECT {string.Join(",", HeaderColumns.Select(c => "h.[" + c + "]"))},
              COALESCE(u.NickName,u.UserName,N'') AS OwnerName,
              COALESCE(h.ServiceAmountCny,0) AS KnownAmountCny,
              CAST(CASE WHEN h.ServiceAmountCny IS NULL THEN 1 ELSE 0 END AS bit) AS HasUndeterminedAmount,
              COALESCE(NULLIF(h.IssueCustomerSkuSnapshot,N''),h.IssueSkuSnapshot,N'') AS IssueModels,
              COALESCE(s.SupplierName,N'') AS ResponsibleSuppliers
            FROM dbo.AfterSalesHeader h
            LEFT JOIN dbo.SysUser u ON u.Uid=h.OwnerId
            LEFT JOIN dbo.Supplier s ON s.Uid=h.ResponsibleSupplierId
            {where} ORDER BY h.AcceptedDate DESC,h.Uid OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        using var reader = await Connection.QueryMultipleAsync(new CommandDefinition(sql, new { request.CustomerId,request.DocumentStatus,request.ConfirmationStatus,request.ResponsibleSupplierId,DateFrom=request.DateFrom?.Date,DateTo=request.DateTo?.Date,Keyword=Like(request.Keyword),Skip=(page-1)*size,Take=size }, Transaction,cancellationToken:cancellationToken));
        var count = await reader.ReadSingleAsync<int>();
        return new() { PageIndex=page,PageSize=size,TotalCount=count,Items=(await reader.ReadAsync<AfterSalesListItemDto>()).ToList() };
    }

    private static string? Like(string? value)=>string.IsNullOrWhiteSpace(value)?null:"%"+value.Trim()+"%";
}
