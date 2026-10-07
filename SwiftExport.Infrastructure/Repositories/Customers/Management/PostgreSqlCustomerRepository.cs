using Dapper;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Customers.Management;

/// <summary>客户 PostgreSQL 评审实现，继承单实体 CRUD 并保留各专用查询的软删除、停用和文本处理区别。</summary>
public sealed class PostgreSqlCustomerRepository(RepositoryBaseContext context) : PostgreSqlDapperRepository<Customer>(context), ICustomerRepository
{
    private const string EntityColumns = """
        c."uid" AS "Uid",
        c."version" AS "Version",
        c."is_deleted" AS "IsDeleted",
        c."created_date" AS "CreatedDate",
        c."created_by" AS "CreatedBy",
        c."updated_date" AS "UpdatedDate",
        c."updated_by" AS "UpdatedBy",
        c."is_active" AS "IsActive",
        c."customer_code" AS "CustomerCode",
        c."customer_name" AS "CustomerName",
        c."address" AS "Address",
        c."phone_number" AS "PhoneNumber",
        c."contact_person" AS "ContactPerson",
        c."label_folder_path" AS "LabelFolderPath",
        c."manual_folder_path" AS "ManualFolderPath",
        c."drawing_folder_path" AS "DrawingFolderPath",
        c."linedrawing_folder_path" AS "LinedrawingFolderPath",
        c."barcode_folder_path" AS "BarcodeFolderPath",
        c."spec_sheet_folder_path" AS "SpecSheetFolderPath",
        c."doc_relate_no" AS "DocRelateNo",
        c."incoterm_sort_remark" AS "IncotermSortRemark",
        c."incoterm" AS "Incoterm",
        c."incoterm_location" AS "IncotermLocation",
        c."deposit_rate" AS "DepositRate",
        c."lead_time_days" AS "LeadTimeDays",
        c."payment_method" AS "PaymentMethod",
        c."currency" AS "Currency",
        c."etd_sort_remark" AS "EtdSortRemark",
        c."shipped_by" AS "ShippedBy"
    """;
    private static readonly HashSet<string> DtoProperties = typeof(CustomerDto).GetProperties().Select(property => property.Name).ToHashSet();
    private const string DtoColumns = """
        c."uid" AS "Uid",
        c."version" AS "Version",
        c."is_deleted" AS "IsDeleted",
        c."created_date" AS "CreatedDate",
        c."created_by" AS "CreatedBy",
        c."updated_date" AS "UpdatedDate",
        c."updated_by" AS "UpdatedBy",
        c."customer_code" AS "CustomerCode",
        c."customer_name" AS "CustomerName",
        c."address" AS "Address",
        c."phone_number" AS "PhoneNumber",
        c."contact_person" AS "ContactPerson",
        c."label_folder_path" AS "LabelFolderPath",
        c."manual_folder_path" AS "ManualFolderPath",
        c."drawing_folder_path" AS "DrawingFolderPath",
        c."linedrawing_folder_path" AS "LinedrawingFolderPath",
        c."barcode_folder_path" AS "BarcodeFolderPath",
        c."spec_sheet_folder_path" AS "SpecSheetFolderPath",
        c."doc_relate_no" AS "DocRelateNo",
        c."incoterm_sort_remark" AS "IncotermSortRemark",
        c."incoterm" AS "Incoterm",
        c."incoterm_location" AS "IncotermLocation",
        c."deposit_rate" AS "DepositRate",
        c."lead_time_days" AS "LeadTimeDays",
        c."payment_method" AS "PaymentMethod",
        c."currency" AS "Currency",
        c."etd_sort_remark" AS "EtdSortRemark",
        c."shipped_by" AS "ShippedBy"
    """;
    private const string CodeEquals = "rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci = rtrim(CAST(@CustomerCode AS text),' ') COLLATE public.erp_dictionary_ci";
    private const string CodeInArray = """
        EXISTS (SELECT 1 FROM unnest(@Codes) AS input(code)
                WHERE rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci = rtrim(input.code,' ') COLLATE public.erp_dictionary_ci)
        """;

    /// <summary>普通资料更新不写执行资格；IsActive 仅由其独立命令维护。</summary>
    protected override IReadOnlyList<string> UpdateProperties => [.. base.UpdateProperties
        .Where(property => !property.Equals(nameof(Customer.IsActive), StringComparison.OrdinalIgnoreCase))];

    /// <summary>按编码判断是否已有客户，包含软删除和停用记录，不去掉编码前导空格。</summary>
    public async Task<bool> ExistByCustomerCode(string customerCode)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("CustomerCode", customerCode);
        return (await Connection.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(
            $"SELECT 1 FROM public.customer c WHERE {CodeEquals} LIMIT 1;", parameters, Transaction))).HasValue;
    }

    /// <summary>按编码读取未删除客户实体，允许停用记录，使用元数据投影而不设置全局下划线映射。</summary>
    public Task<Customer?> GetByCustomerCodeAsync(string customerCode)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("CustomerCode", customerCode);
        return Connection.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(
            $"SELECT {EntityColumns} FROM public.customer c WHERE {CodeEquals} AND COALESCE(c.is_deleted,FALSE)=FALSE LIMIT 1;", parameters, Transaction));
    }

    /// <summary>返回本批所有身份之外占用的未删除客户编码；身份排除是全批集合，不按提交项逐对排除。</summary>
    public async Task<List<string>> GetCustomerCodesUsedByOtherCustomersAsync(IEnumerable<(Guid Uid, string CustomerCode)> items)
    {
        var rows = items.ToArray();
        if (rows.Length == 0) return [];
        var parameters = PostgreSqlParameters.Ids(rows.Select(row => row.Uid));
        parameters.AddTexts("Codes", rows.Select(row => row.CustomerCode));
        return (await Connection.QueryAsync<string>(new CommandDefinition($"""
            SELECT c.customer_code AS "CustomerCode" FROM public.customer c
            WHERE {CodeInArray} AND NOT (c.uid=ANY(@Uids)) AND c.is_deleted=FALSE;
            """, parameters, Transaction))).AsList();
    }

    /// <summary>返回命中的原始编码，包含软删除客户；不 Trim 返回值或把数组展开为独立参数。</summary>
    public async Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<string> customerCodes)
    {
        var codes = customerCodes.ToArray();
        if (codes.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", codes);
        return (await Connection.QueryAsync<string>(new CommandDefinition(
            $"SELECT c.customer_code AS \"CustomerCode\" FROM public.customer c WHERE {CodeInArray};", parameters, Transaction))).AsList();
    }

    /// <summary>管理分页排除软删但保留停用客户，默认二十条、上限二百；关键词不预先 Trim。</summary>
    public async Task<PagedResponseBase<CustomerDto>> GetPagedAsync(CustomerSearchRequest request)
    {
        request ??= new CustomerSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var keywordSql = "";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            keywordSql = " AND (c.customer_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR c.customer_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')";
        }
        var filter = "WHERE COALESCE(c.is_deleted,FALSE)=FALSE" + keywordSql;
        var total = await Connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(1) FROM public.customer c {filter};", parameters, Transaction));
        var items = await Connection.QueryAsync<CustomerDto>(new CommandDefinition($"""
            SELECT {DtoColumns} FROM public.customer c {filter}
            ORDER BY c.customer_code COLLATE public.erp_dictionary_ci,c.uid LIMIT @PageSize OFFSET @Offset;
            """, parameters, Transaction));
        return new() { Items = items.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>销售 Lookup 排除软删但不新增停用过滤，空商业字段回退空串或零，使用调用方事务及取消令牌。</summary>
    public async Task<PagedResponseBase<SalesOrderCustomerLookupDto>> SearchSalesOrderCustomersAsync(
        SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Min(request.PageSize <= 0 ? 30 : request.PageSize, 100);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var keywordSql = "";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            keywordSql = """
                 AND (c.customer_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                      OR c.customer_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                      OR c.contact_person COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                      OR c.phone_number COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
                """;
        }
        var filter = "WHERE c.is_deleted=FALSE" + keywordSql;
        var total = await Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM public.customer c {filter};", parameters, Transaction, cancellationToken: cancellationToken));
        var items = await Connection.QueryAsync<SalesOrderCustomerLookupDto>(new CommandDefinition($"""
            SELECT c.uid AS "CustomerId", COALESCE(c.customer_code,'') AS "CustomerCode", COALESCE(c.customer_name,'') AS "CustomerName",
                   COALESCE(c.currency,'') AS "Currency", COALESCE(c.payment_method,'') AS "PaymentMethod",
                   COALESCE(c.shipped_by,'') AS "ShippedBy", COALESCE(c.incoterm,'') AS "Incoterm",
                   COALESCE(c.incoterm_sort_remark,'') AS "IncotermSortRemark", COALESCE(c.incoterm_location,'') AS "IncotermLocation",
                   COALESCE(c.deposit_rate,0) AS "DepositRate", COALESCE(c.lead_time_days,0) AS "LeadTimeDays",
                   COALESCE(c.customer_name,'') AS "ShipToName", COALESCE(c.address,'') AS "ShipToAddress", COALESCE(c.phone_number,'') AS "ShipToPhone"
            FROM public.customer c {filter} ORDER BY c.customer_code COLLATE public.erp_dictionary_ci,c.uid LIMIT @PageSize OFFSET @Offset;
            """, parameters, Transaction, cancellationToken: cancellationToken));
        return new() { Items = items.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>销售编码列表仅去掉首尾普通空格、排除空码并按 ICU 规则去重排序，不改变客户执行资格。</summary>
    public async Task<List<string>> GetSalesOrderCustomerCodesAsync(CancellationToken cancellationToken = default) =>
        (await Connection.QueryAsync<string>(new CommandDefinition("""
            SELECT DISTINCT btrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci AS "CustomerCode"
            FROM public.customer c WHERE c.is_deleted=FALSE AND NULLIF(btrim(c.customer_code,' '),'') IS NOT NULL
            ORDER BY "CustomerCode";
            """, transaction: Transaction, cancellationToken: cancellationToken))).AsList();
}
