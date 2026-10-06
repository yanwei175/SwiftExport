using Dapper;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Core.Interfaces.ForExcel;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.ForExcel;

/// <summary>以调用方 PostgreSQL 会话执行销售及装箱导入的三类只读查询；保留停用、断链与歧义供服务解释。</summary>
public sealed class PostgreSqlForExcelSalesOrderRepository(RepositoryBaseContext context) : IForExcelSalesOrderRepository
{
    /// <summary>按输入全 Trim/大写、数据库仅 ASCII 空格修剪后的客户编码查询；返回全部未删候选，不判断启用资格。</summary>
    public Task<IReadOnlyList<SalesOrderImportCustomerDto>> FindCustomersAsync(string customerCode, CancellationToken token)
        => ReadAsync<SalesOrderImportCustomerDto>("""
            SELECT uid AS "Uid",customer_code AS "CustomerCode",customer_name AS "CustomerName",is_active AS "IsActive"
            FROM public.customer
            WHERE is_deleted=FALSE AND upper(btrim(customer_code,' ')) COLLATE public.erp_dictionary_ci = @Code
            ORDER BY uid
            """, new { Code = customerCode.Trim().ToUpperInvariant() }, token);

    /// <summary>批量匹配调用方已规范化的型号；产品 LEFT JOIN 保留断链，关系启用、产品启用及非 ASCII 空白单位只影响 IsUsable。</summary>
    public Task<IReadOnlyList<SalesOrderImportProductCandidateDto>> MatchProductsAsync(Guid customerId, string[] customerSkus, CancellationToken token)
    {
        // 空数组沿用源短路，包括已取消请求；不访问会话，也不再次 Trim/大写/去重调用方数组。
        if (customerSkus.Length == 0) return Task.FromResult<IReadOnlyList<SalesOrderImportProductCandidateDto>>([]);
        var parameters = new PostgreSqlParameters();
        parameters.Add("CustomerId", customerId); parameters.AddTexts("Skus", customerSkus);
        // SQL Server 等值比较忽略参数尾部 ASCII 空格；仅 SQL 内 rtrim 右侧数组项，不能全 Trim 或改写调用方数组。
        return ReadAsync<SalesOrderImportProductCandidateDto>("""
            SELECT cp.uid AS "CustomerProductId",p.uid AS "ProductId",cp.customer_sku AS "CustomerSku",
                   p.sku AS "Sku",COALESCE(NULLIF(btrim(cp.link_sku,' '),''),p.link_sku) AS "LinkSku",
                   p.unit AS "Unit",p.main_image AS "MainImage",
                   CASE WHEN cp.is_active=TRUE AND p.is_active=TRUE AND NULLIF(btrim(p.unit,' '),'') IS NOT NULL
                        THEN TRUE ELSE FALSE END AS "IsUsable"
            FROM public.customer_product cp
            LEFT JOIN public.product p ON p.uid=cp.sku_id AND p.is_deleted=FALSE
            WHERE cp.customer_id=@CustomerId AND cp.is_deleted=FALSE
              AND upper(btrim(cp.customer_sku,' ')) COLLATE public.erp_dictionary_ci = ANY(
                  SELECT rtrim(input.sku,' ') FROM unnest(@Skus) input(sku))
            ORDER BY cp.customer_sku COLLATE public.erp_dictionary_ci,cp.uid
            """, parameters, token);
    }

    /// <summary>同客户匹配任一非空商业编号并返回全部提示，不限定状态或假定编号唯一；双空编号沿用零查询短路。</summary>
    public Task<IReadOnlyList<SalesOrderImportExistingOrderDto>> FindExistingOrdersAsync(Guid customerId, string invoiceNo, string customerPoNo, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(invoiceNo) && string.IsNullOrWhiteSpace(customerPoNo))
            return Task.FromResult<IReadOnlyList<SalesOrderImportExistingOrderDto>>([]);
        return ReadAsync<SalesOrderImportExistingOrderDto>("""
            SELECT uid AS "Uid",order_no AS "OrderNo",invoice_no AS "InvoiceNo",customer_po_no AS "CustomerPoNo",
                   document_status AS "DocumentStatus",confirmation_status AS "ConfirmationStatus"
            FROM public.sales_order_header
            WHERE is_deleted=FALSE AND customer_id=@CustomerId
              AND ((@InvoiceNo<>'' AND upper(btrim(invoice_no,' ')) COLLATE public.erp_dictionary_ci = @InvoiceNo)
                OR (@CustomerPoNo<>'' AND upper(btrim(customer_po_no,' ')) COLLATE public.erp_dictionary_ci = @CustomerPoNo))
            ORDER BY order_no COLLATE public.erp_dictionary_ci,uid
            """, new { CustomerId = customerId, InvoiceNo = invoiceNo?.Trim().ToUpperInvariant() ?? "",
                CustomerPoNo = customerPoNo?.Trim().ToUpperInvariant() ?? "" }, token);
    }

    /// <summary>透传可空外层事务及取消令牌，查询后再检查取消；只返回 DTO，不开始或结束调用方事务。</summary>
    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, object parameters, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var rows = (await context.Session.Connection.QueryAsync<T>(new CommandDefinition(
            sql, parameters, context.Session.Transaction, cancellationToken: token))).AsList();
        token.ThrowIfCancellationRequested();
        return rows;
    }
}
