using Dapper;
using System.Data;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.QualityControl;

/// <summary>售后专用仓储，保留稳定身份，所有写入共享调用方事务。</summary>
public sealed class AfterSalesRepository(RepositoryBaseContext context) : IAfterSalesRepository
{
    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;
    private static readonly string[] HeaderColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","AfterSalesNo","CustomerId","CustomerCodeSnapshot","CustomerNameSnapshot","AcceptedDate","OwnerId","DocumentStatus","ConfirmationStatus","DemandConfirmedAt","DemandConfirmedBy","ExpectedCompletionDate","Conclusion","Remark","OriginalSalesOrderLineId","CustomerComplaintNo","ResponsibleSupplierId","IssueProductId","IssueSkuSnapshot","IssueCustomerSkuSnapshot","IssueLinkSkuSnapshot","IssueDescription","QualityCategoryId","CauseAnalysis","RequiredDate","ServiceAmountCny","ReturnedReceivedDate","TreatmentDescription"];
    private static readonly string[] LineColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","HeaderId","LineNumber","ProductId","IsNonStandard","SkuSnapshot","CustomerSkuSnapshot","LinkSkuSnapshot","DescriptionSnapshot","ColorId","ColorSnapshot","SpecificationSnapshot","Quantity","UnitSnapshot","Remark"];

    /// <inheritdoc />
    public Task<AfterSalesHeader?> GetHeaderAsync(Guid uid, bool forUpdate = false)
        => Connection.QuerySingleOrDefaultAsync<AfterSalesHeader>($"SELECT {Columns(HeaderColumns)} FROM dbo.AfterSalesHeader {(forUpdate ? "WITH(UPDLOCK,HOLDLOCK)" : "")} WHERE Uid=@uid AND IsDeleted=0", new { uid }, Transaction);
    /// <inheritdoc />
    public async Task<IReadOnlyList<AfterSalesLine>> GetLinesAsync(Guid uid, bool forUpdate = false)
        => (await Connection.QueryAsync<AfterSalesLine>($"SELECT {Columns(LineColumns)} FROM dbo.AfterSalesLine {(forUpdate ? "WITH(UPDLOCK,HOLDLOCK)" : "")} WHERE HeaderId=@uid ORDER BY LineNumber,Uid", new { uid }, Transaction)).ToList();
    /// <inheritdoc />
    public Task<bool> WriteHeaderAsync(AfterSalesHeader head, bool insert) => WriteAsync("AfterSalesHeader", HeaderColumns, head, insert);
    /// <inheritdoc />
    public Task<bool> WriteLineAsync(AfterSalesLine line, bool insert) => WriteAsync("AfterSalesLine", LineColumns, line, insert);

    // 列名来自本类固定清单，不能由客户端输入；新增不调用会重写 Uid 的泛型 AddAsync。
    private async Task<bool> WriteAsync(string table, string[] columns, BaseEntity entity, bool insert)
    {
        if (Transaction is null) throw new InvalidOperationException("售后写入必须处于业务事务。");
        string sql;
        if (insert)
        {
            context.AuditStamp.StampForInsert(entity);
            entity.Version = 1;
            sql = $"INSERT dbo.[{table}] ({Columns(columns)}) VALUES ({string.Join(",", columns.Select(c => "@" + c))})";
        }
        else
        {
            context.AuditStamp.StampForUpdate(entity);
            var writable = columns.Except(["Uid","Version","CreatedDate","CreatedBy"]);
            sql = $"UPDATE dbo.[{table}] SET {string.Join(",", writable.Select(c => $"[{c}]=@{c}"))},Version=Version+1 WHERE Uid=@Uid AND Version=@Version";
        }
        var count = await Connection.ExecuteAsync(sql, entity, Transaction);
        if (count == 1 && !insert) entity.Version++;
        return count == 1;
    }
    /// <inheritdoc />
    public async Task LockCommandAsync(Guid commandId)
    {
        if (Transaction is null) throw new InvalidOperationException("命令锁要求已有事务。");
        var result = await Connection.ExecuteScalarAsync<int>("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @result;", new { resource = "AfterSales:" + commandId.ToString("N") }, Transaction);
        if (result < 0) throw new InvalidOperationException("售后命令锁获取失败，请重试。");
    }

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

    /// <inheritdoc />
    public async Task<PagedResponseBase<AfterSalesLookupDto>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken)
    {
        var source = ReferenceSql(kind, true);
        var page=Math.Max(1,request.PageIndex);
        var size=Math.Clamp(request.PageSize<=0?50:request.PageSize,1,200);
        var sql=$"SELECT COUNT(*) FROM ({source}) q WHERE (@Keyword IS NULL OR q.Code LIKE @Keyword OR q.Name LIKE @Keyword); SELECT {LookupColumns(kind)} FROM ({source}) q WHERE (@Keyword IS NULL OR q.Code LIKE @Keyword OR q.Name LIKE @Keyword) ORDER BY q.Code,q.Uid OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";
        using var reader=await Connection.QueryMultipleAsync(new CommandDefinition(sql,new {request.CustomerId,Keyword=Like(request.Keyword),Skip=(page-1)*size,Take=size},Transaction,cancellationToken:cancellationToken));
        var count=await reader.ReadSingleAsync<int>();
        return new() {PageIndex=page,PageSize=size,TotalCount=count,Items=(await reader.ReadAsync<AfterSalesLookupDto>()).ToList()};
    }
    /// <inheritdoc />
    public Task<AfterSalesLookupDto?> ReferenceAsync(string kind, Guid uid, Guid? customerId, bool requireActive)
        => Connection.QuerySingleOrDefaultAsync<AfterSalesLookupDto>($"SELECT {LookupColumns(kind)} FROM ({ReferenceSql(kind,requireActive)}) q WHERE q.Uid=@uid",new {uid,CustomerId=customerId},Transaction);

    // 候选仅投影契约需要的字段，产品额外返回用于选择后填充的快照。
    private static string LookupColumns(string kind) => "q.Uid,q.Code,q.Name" +
        (kind=="products"?",q.CustomerSku,q.LinkSku,q.Unit,q.ColorId,q.ColorName,q.Specification":"");

    // 固定来源白名单；活动过滤仅适用于新引用，既有合法身份可以保留快照。
    private static string ReferenceSql(string kind,bool active)
    {
        var enabled=active?" AND IsActive=1":"";
        return kind switch
        {
            "customers" => $"SELECT Uid,CustomerCode AS Code,CustomerName AS Name FROM dbo.Customer WHERE IsDeleted=0{enabled}",
            "suppliers" => $"SELECT Uid,SupplierCode AS Code,SupplierName AS Name FROM dbo.Supplier WHERE IsDeleted=0{enabled}",
            "owners" => $"SELECT Uid,UserName AS Code,COALESCE(NickName,UserName) AS Name FROM dbo.SysUser WHERE IsDeleted=0{enabled}",
            "colors" => "SELECT Uid,ColorCode AS Code,COALESCE(ColorNameEn,ColorNameZh) AS Name FROM dbo.ProductColor WHERE IsDeleted=0",
            "quality-categories" => $"SELECT Uid,COALESCE(DataNameEn,DataNameZh) AS Code,COALESCE(DataNameZh,DataNameEn) AS Name FROM dbo.SysDictData WHERE IsDeleted=0 AND DictCode=N'AfterSalesQualityCategory'{(active?" AND IsEnabled=1":"")}",
            "sales-order-lines" => "SELECT l.Uid,CONCAT(h.OrderNo,N'/',l.LineNumber) AS Code,l.Sku AS Name FROM dbo.SalesOrderLine l INNER JOIN dbo.SalesOrderHeader h ON h.Uid=l.SalesOrderId WHERE l.IsDeleted=0 AND h.IsDeleted=0 AND h.CustomerId=@CustomerId",
            "products" => $"""
                SELECT p.Uid,p.Sku AS Code,COALESCE(p.DescriptionZh,p.DescriptionEn,N'') AS Name,p.LinkSku,
                    p.Unit,p.ColorId,COALESCE(c.ColorNameEn,c.ColorNameZh) AS ColorName,
                    COALESCE(p.FeaturesZh,p.FeaturesEn) AS Specification,cp.CustomerSku
                FROM dbo.Product p LEFT JOIN dbo.ProductColor c ON c.Uid=p.ColorId
                OUTER APPLY(SELECT TOP(1) r.CustomerSku FROM dbo.CustomerProduct r WHERE r.SkuId=p.Uid AND r.CustomerId=@CustomerId AND r.IsDeleted=0 AND r.IsActive=1 ORDER BY r.Uid) cp
                WHERE p.IsDeleted=0{(active?" AND p.IsActive=1":"")}
                """,
            _ => throw new ArgumentException("不支持的售后候选类型。",nameof(kind))
        };
    }
    private static string Columns(IEnumerable<string> columns)=>string.Join(",",columns.Select(c=>"["+c+"]"));
    private static string? Like(string? value)=>string.IsNullOrWhiteSpace(value)?null:"%"+value.Trim()+"%";
}
