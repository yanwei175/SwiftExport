using Dapper;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Core.Interfaces.ForExcel;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.ForExcel;

/// <summary>参数化批量导入查询，共享 request DbSession，不写库、不使用脏读。</summary>
public sealed class ForExcelSalesOrderRepository(RepositoryBaseContext context) : IForExcelSalesOrderRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesOrderImportCustomerDto>> FindCustomersAsync(string customerCode, CancellationToken token)
        => (await context.Session.Connection.QueryAsync<SalesOrderImportCustomerDto>(new CommandDefinition(
            "SELECT Uid,CustomerCode,CustomerName,IsActive FROM dbo.Customer WHERE IsDeleted=0 AND UPPER(LTRIM(RTRIM(CustomerCode)))=@Code ORDER BY Uid",
            new { Code = customerCode.Trim().ToUpperInvariant() }, context.Session.Transaction, cancellationToken: token))).AsList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesOrderImportProductCandidateDto>> MatchProductsAsync(Guid customerId, string[] customerSkus, CancellationToken token)
    {
        if (customerSkus.Length == 0) return [];
        return (await context.Session.Connection.QueryAsync<SalesOrderImportProductCandidateDto>(new CommandDefinition("""
            SELECT cp.Uid AS CustomerProductId,p.Uid AS ProductId,cp.CustomerSku AS CustomerSku,
                   p.Sku AS Sku,COALESCE(NULLIF(LTRIM(RTRIM(cp.LinkSku)),''),p.LinkSku) AS LinkSku,
                   p.Unit AS Unit,p.MainImage AS MainImage,
                   CAST(CASE WHEN cp.IsActive=1 AND p.IsActive=1 AND NULLIF(LTRIM(RTRIM(p.Unit)),'') IS NOT NULL
                        THEN 1 ELSE 0 END AS bit) AS IsUsable
            FROM dbo.CustomerProduct cp
            LEFT JOIN dbo.Product p ON p.Uid=cp.SkuId AND p.IsDeleted=0
            WHERE cp.CustomerId=@CustomerId AND cp.IsDeleted=0
              AND UPPER(LTRIM(RTRIM(cp.CustomerSku))) IN @Skus
            ORDER BY cp.CustomerSku,cp.Uid
            """, new { CustomerId = customerId, Skus = customerSkus }, context.Session.Transaction, cancellationToken: token))).AsList();
    }
    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesOrderImportExistingOrderDto>> FindExistingOrdersAsync(Guid customerId, string invoiceNo, string customerPoNo, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(invoiceNo) && string.IsNullOrWhiteSpace(customerPoNo)) return [];
        return (await context.Session.Connection.QueryAsync<SalesOrderImportExistingOrderDto>(new CommandDefinition("""
            SELECT Uid,OrderNo,InvoiceNo,CustomerPoNo,DocumentStatus,ConfirmationStatus
            FROM dbo.SalesOrderHeader
            WHERE IsDeleted=0 AND CustomerId=@CustomerId
              AND ((@InvoiceNo<>'' AND UPPER(LTRIM(RTRIM(InvoiceNo)))=@InvoiceNo)
                OR (@CustomerPoNo<>'' AND UPPER(LTRIM(RTRIM(CustomerPoNo)))=@CustomerPoNo))
            ORDER BY OrderNo,Uid
            """, new { CustomerId = customerId, InvoiceNo = invoiceNo?.Trim().ToUpperInvariant() ?? "", CustomerPoNo = customerPoNo?.Trim().ToUpperInvariant() ?? "" },
            context.Session.Transaction, cancellationToken: token))).AsList();
    }
}
