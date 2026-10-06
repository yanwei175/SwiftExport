using System.Data;
using Dapper;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Customers.PoConfigs;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Customers.PoConfigs;

/// <summary>使用调用方 PostgreSQL 会话维护客户采购默认配置，保留原组合校验差异；锁定读取串行配置表写入以保护缺失结果。</summary>
public sealed class PostgreSqlCustomerPoConfigRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<CustomerPoConfig>(context), ICustomerPoConfigRepository
{
    private static readonly string Columns = string.Join(",", Metadata.Columns.Select(column =>
        $"cpc.{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));

    /// <summary>读取双方身份的完整未删除配置；锁定模式须有低隔离事务，先锁单表再查询，事务所有权仍归调用方。</summary>
    public async Task<CustomerPoConfigDto?> GetByPartiesAsync(Guid customerId, Guid supplierId,
        CancellationToken cancellationToken = default, bool withUpdLock = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (withUpdLock)
        {
            var transaction = Transaction ?? throw new InvalidOperationException("锁定采购配置读取需要已有事务。");
            if (transaction.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.ReadUncommitted))
                throw new InvalidOperationException("锁定采购配置读取仅支持 ReadCommitted 或 ReadUncommitted；高隔离快照须由调用链单独验收。");
            // FOR UPDATE 无法保护不存在的组合；单表锁与配置写锁冲突，等待结束后的新语句读取最新已提交结果。
            await Connection.ExecuteAsync(new CommandDefinition(
                "LOCK TABLE public.customer_po_config IN SHARE ROW EXCLUSIVE MODE", transaction: transaction,
                cancellationToken: cancellationToken));
        }
        return await Connection.QuerySingleOrDefaultAsync<CustomerPoConfigDto>(new CommandDefinition($"""
            SELECT {Columns} FROM public.customer_po_config cpc
            WHERE cpc.customer_id=@CustomerId AND cpc.supplier_id=@SupplierId AND cpc.is_deleted=FALSE
            """, new { CustomerId = customerId, SupplierId = supplierId }, Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>优先按非空身份解析，否则按源编码比较；双方均唯一且未删除才整体回填当前编码，不筛停用状态。</summary>
    public async Task<bool> PopulateRelationIdsAsync(CustomerPoConfig entity)
    {
        const string sql = """
            WITH customers AS (
                SELECT uid,customer_code,count(*) OVER () AS matches FROM public.customer
                WHERE COALESCE(is_deleted,FALSE)=FALSE
                  AND ((CAST(@CustomerId AS uuid) IS NOT NULL AND uid=CAST(@CustomerId AS uuid))
                    OR (CAST(@CustomerId AS uuid) IS NULL AND
                        rtrim(customer_code,' ') COLLATE public.erp_dictionary_ci = rtrim(CAST(@CustomerCode AS text),' ') COLLATE public.erp_dictionary_ci))
            ), suppliers AS (
                SELECT uid,supplier_code,count(*) OVER () AS matches FROM public.supplier
                WHERE COALESCE(is_deleted,FALSE)=FALSE
                  AND ((CAST(@SupplierId AS uuid) IS NOT NULL AND uid=CAST(@SupplierId AS uuid))
                    OR (CAST(@SupplierId AS uuid) IS NULL AND
                        rtrim(supplier_code,' ') COLLATE public.erp_dictionary_ci = rtrim(CAST(@SupplierCode AS text),' ') COLLATE public.erp_dictionary_ci))
            )
            SELECT (SELECT uid FROM customers WHERE matches=1) AS "CustomerId",
                   (SELECT customer_code FROM customers WHERE matches=1) AS "CustomerCode",
                   (SELECT uid FROM suppliers WHERE matches=1) AS "SupplierId",
                   (SELECT supplier_code FROM suppliers WHERE matches=1) AS "SupplierCode"
            """;
        var result = await Connection.QuerySingleAsync<RelationIds>(sql,
            new { entity.CustomerId, entity.CustomerCode, entity.SupplierId, entity.SupplierCode }, Transaction);
        if (!result.CustomerId.HasValue || !result.SupplierId.HasValue) return false;
        entity.CustomerId = result.CustomerId;
        entity.CustomerCode = result.CustomerCode;
        entity.SupplierId = result.SupplierId;
        entity.SupplierCode = result.SupplierCode;
        return true;
    }

    /// <summary>候选仅含未删除配置，随后以原 Trim/忽略大小写完整组合匹配；返回数据库原编码。</summary>
    public async Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<PoConfigCheck> items)
    {
        var list = items.ToList();
        var candidates = await CandidatesAsync(list, activeOnly: true, includeUid: false);
        var requested = list.Select(PairKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(item => requested.Contains(PairKey(item)))
            .Select(item => $"{item.CustomerCode} + {item.SupplierCode}")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>保留旧检查包含软删除、大小写敏感且不修剪的内存组合比较，不排除自身身份。</summary>
    public async Task<IReadOnlyList<PoConfigCheck>> GetNonUniqueByPoConfigCheckList(IEnumerable<PoConfigCheck> poConfigCheckList)
    {
        if (poConfigCheckList is null) return [];
        var list = poConfigCheckList.ToList();
        if (list.Count == 0) return [];
        var candidates = await CandidatesAsync(list, activeOnly: false, includeUid: false);
        var requested = list.Select(item => $"{item.CustomerCode}|{item.SupplierCode}").ToHashSet();
        return candidates.Where(item => requested.Contains($"{item.CustomerCode}|{item.SupplierCode}")).ToList();
    }

    /// <summary>按原客户/供应商模糊关键词分页，保留默认及上限、完整字段和稳定降序，不更改请求。</summary>
    public async Task<PagedResponseBase<CustomerPoConfigDto>> GetPagedAsync(CustomerPoConfigSearchRequest request)
    {
        request ??= new CustomerPoConfigSearchRequest();
        var index = Math.Max(1, request.PageIndex);
        var size = Math.Min(request.PageSize <= 0 ? 50 : request.PageSize, 500);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (index - 1) * size);
        parameters.Add("PageSize", size);
        var where = "WHERE COALESCE(cpc.is_deleted,FALSE)=FALSE";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where += " AND (cpc.customer_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR cpc.supplier_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')";
        }
        var total = await Connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM public.customer_po_config cpc {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<CustomerPoConfigDto>($"""
            SELECT {Columns} FROM public.customer_po_config cpc {where}
            ORDER BY cpc.created_date DESC NULLS LAST,cpc.uid DESC LIMIT @PageSize OFFSET @Offset
            """, parameters, Transaction);
        return new PagedResponseBase<CustomerPoConfigDto>
            { Items = rows.ToList(), TotalCount = total, PageIndex = index, PageSize = size };
    }

    /// <summary>按各提交项排除自身，输入 Trim 而数据库原文不 Trim；不改成整批身份排除或数据库完整配对。</summary>
    public async Task<List<string>> GetUsedByCustomerCodeAndSupplierCodeAsync(IEnumerable<PoConfigCheck> items)
    {
        var list = items.Where(item => !string.IsNullOrWhiteSpace(item.CustomerCode) && !string.IsNullOrWhiteSpace(item.SupplierCode))
            .Select(item => new PoConfigCheck { Uid = item.Uid, CustomerCode = item.CustomerCode!.Trim(), SupplierCode = item.SupplierCode!.Trim() }).ToList();
        if (list.Count == 0) return [];
        var candidates = await CandidatesAsync(list, activeOnly: true, includeUid: true);
        return list.Where(item => candidates.Any(row => (item.Uid == Guid.Empty || row.Uid != item.Uid)
                && string.Equals(row.CustomerCode, item.CustomerCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.SupplierCode, item.SupplierCode, StringComparison.OrdinalIgnoreCase)))
            .Select(item => $"{item.CustomerCode} + {item.SupplierCode}").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>仅翻译数据库双方候选筛选，完整配对及各方法差异仍由调用方内存逻辑决定；数组不展开、空集合不查询。</summary>
    private async Task<List<PoConfigCheck>> CandidatesAsync(List<PoConfigCheck> items, bool activeOnly, bool includeUid)
    {
        if (items.Count == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("CustomerCodes", items.Select(item => item.CustomerCode!));
        parameters.AddTexts("SupplierCodes", items.Select(item => item.SupplierCode!));
        var sql = $"""
            SELECT {(includeUid ? "cpc.uid AS \"Uid\"," : string.Empty)}cpc.customer_code AS "CustomerCode",cpc.supplier_code AS "SupplierCode"
            FROM public.customer_po_config cpc WHERE {CodeSet("cpc.customer_code", "CustomerCodes")}
                AND {CodeSet("cpc.supplier_code", "SupplierCodes")}{(activeOnly ? " AND cpc.is_deleted=FALSE" : string.Empty)}
            """;
        return (await Connection.QueryAsync<PoConfigCheck>(sql, parameters, Transaction)).ToList();
    }

    /// <summary>数据库源等值仅去掉尾部 ASCII 空格并应用冻结 ICU，不修改参数或存储文本。</summary>
    private static string CodeSet(string column, string parameter) =>
        $"EXISTS (SELECT 1 FROM unnest(@{parameter}) input(code) WHERE rtrim({column},' ') COLLATE public.erp_dictionary_ci = rtrim(input.code,' ') COLLATE public.erp_dictionary_ci)";

    /// <summary>保留旧存在性检查的全 Trim 组合键，不复用到大小写敏感或不修剪的检查。</summary>
    private static string PairKey(PoConfigCheck item) => $"{item.CustomerCode?.Trim()}|{item.SupplierCode?.Trim()}";

    /// <summary>接收唯一主数据候选，不向调用方暴露部分成功解析结果。</summary>
    private sealed class RelationIds
    {
        public Guid? CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public Guid? SupplierId { get; set; }
        public string? SupplierCode { get; set; }
    }
}
