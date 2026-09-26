using Dapper;
using System.Data;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Contexts;

using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
namespace SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Lookup;

/// <summary>保持原售后候选白名单及引用核验 SQL，共用请求级 DbSession。</summary>
public sealed class AfterSalesLookupRepository(RepositoryBaseContext context) : IAfterSalesLookupRepository
{
    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;
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
