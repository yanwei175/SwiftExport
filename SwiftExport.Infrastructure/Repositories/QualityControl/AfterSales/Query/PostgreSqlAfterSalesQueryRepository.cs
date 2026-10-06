using Dapper;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Query;

/// <summary>实现售后列表的 PostgreSQL 只读分页，保留原筛选、单头金额和快照投影，共享请求级连接及事务。</summary>
public sealed class PostgreSqlAfterSalesQueryRepository(RepositoryBaseContext context) : IAfterSalesQueryRepository
{
    private static readonly string[] HeaderColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","AfterSalesNo","CustomerId","CustomerCodeSnapshot","CustomerNameSnapshot","AcceptedDate","OwnerId","DocumentStatus","ConfirmationStatus","DemandConfirmedAt","DemandConfirmedBy","ExpectedCompletionDate","Conclusion","Remark","OriginalSalesOrderLineId","CustomerComplaintNo","LocalFileFolder","ResponsibleSupplierId","IssueProductId","IssueSkuSnapshot","IssueCustomerSkuSnapshot","IssueLinkSkuSnapshot","IssueDescription","QualityCategoryId","CauseAnalysis","RequiredDate","ServiceAmountCny","ReturnedReceivedDate","TreatmentDescription"];

    /// <summary>按原客户、状态、受理日期、责任方及头行型号筛选分页；行关键词仅判断存在，不放大单头或金额。</summary>
    public async Task<PagedResponseBase<AfterSalesListItemDto>> SearchAsync(
        AfterSalesSearchRequest request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var where = $"""
            WHERE {H("IsDeleted")}=FALSE
              AND (CAST(@CustomerId AS uuid) IS NULL OR {H("CustomerId")}=CAST(@CustomerId AS uuid))
              AND (CAST(@DocumentStatus AS integer) IS NULL OR {H("DocumentStatus")}=CAST(@DocumentStatus AS integer))
              AND (CAST(@ConfirmationStatus AS integer) IS NULL OR {H("ConfirmationStatus")}=CAST(@ConfirmationStatus AS integer))
              AND (@DateFrom IS NULL OR {H("AcceptedDate")}>=@DateFrom)
              AND (@DateTo IS NULL OR {H("AcceptedDate")}<=@DateTo)
              AND (CAST(@ResponsibleSupplierId AS uuid) IS NULL OR {H("ResponsibleSupplierId")}=CAST(@ResponsibleSupplierId AS uuid))
              AND (CAST(@Keyword AS text) IS NULL OR {Like(H("AfterSalesNo"))} OR {Like(H("CustomerComplaintNo"))}
                OR {Like(H("IssueSkuSnapshot"))} OR {Like(H("IssueCustomerSkuSnapshot"))} OR {Like(H("IssueLinkSkuSnapshot"))}
                OR EXISTS(SELECT 1 FROM {Table<AfterSalesLine>()} l WHERE {C<AfterSalesLine>("l", "HeaderId")}={H("Uid")}
                  AND {C<AfterSalesLine>("l", "IsDeleted")}=FALSE AND ({Like(C<AfterSalesLine>("l", "SkuSnapshot"))}
                    OR {Like(C<AfterSalesLine>("l", "CustomerSkuSnapshot"))} OR {Like(C<AfterSalesLine>("l", "LinkSkuSnapshot"))})))
            """;
        // SQL Server NULLIF 的空文本比较忽略尾部 ASCII 空格；非空型号仍返回原快照，不裁掉显示文本。
        var issueCustomerSku = H("IssueCustomerSkuSnapshot");
        var sql = $"""
            SELECT COUNT(*) FROM {Table<AfterSalesHeader>()} h {where};
            SELECT {string.Join(',', HeaderColumns.Select(property => H(property) + " AS " + PostgreSqlNames.Quote(property)))},
              COALESCE({C<SysUser>("u", "NickName")},{C<SysUser>("u", "UserName")},'') AS "OwnerName",
              COALESCE({H("ServiceAmountCny")},0) AS "KnownAmountCny",
              ({H("ServiceAmountCny")} IS NULL) AS "HasUndeterminedAmount",
              COALESCE(CASE WHEN rtrim({issueCustomerSku},' ') COLLATE public.erp_dictionary_ci = '' COLLATE public.erp_dictionary_ci
                THEN NULL ELSE {issueCustomerSku} END,{H("IssueSkuSnapshot")},'') AS "IssueModels",
              COALESCE({C<Supplier>("s", "SupplierName")},'') AS "ResponsibleSuppliers"
            FROM {Table<AfterSalesHeader>()} h
            LEFT JOIN {Table<SysUser>()} u ON {C<SysUser>("u", "Uid")}={H("OwnerId")}
            LEFT JOIN {Table<Supplier>()} s ON {C<Supplier>("s", "Uid")}={H("ResponsibleSupplierId")}
            {where} ORDER BY {H("AcceptedDate")} DESC NULLS LAST,{SqlGuidOrder(H("Uid"))} ASC
            LIMIT @Take OFFSET @Skip;
            """;
        var parameters = new PostgreSqlParameters();
        parameters.Add("CustomerId", request.CustomerId);
        parameters.Add("DocumentStatus", request.DocumentStatus.HasValue ? (int)request.DocumentStatus.Value : null);
        parameters.Add("ConfirmationStatus", request.ConfirmationStatus.HasValue ? (int)request.ConfirmationStatus.Value : null);
        parameters.Add("ResponsibleSupplierId", request.ResponsibleSupplierId);
        // 两端仍截到当天零点并包含边界；不擅自把结束日期扩展为整日范围。
        parameters.AddTimestamp("DateFrom", request.DateFrom?.Date);
        parameters.AddTimestamp("DateTo", request.DateTo?.Date);
        parameters.Add("Keyword", string.IsNullOrWhiteSpace(request.Keyword) ? null : "%" + request.Keyword.Trim() + "%");
        parameters.Add("Skip", (page - 1) * size);
        parameters.Add("Take", size);
        using var reader = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var count = await reader.ReadSingleAsync<int>();
        return new() { PageIndex = page, PageSize = size, TotalCount = count,
            Items = (await reader.ReadAsync<AfterSalesListItemDto>()).ToList() };
    }

    /// <summary>仅对固定列生成原始 LIKE 条件；保留通配符和反斜杠，使用已冻结的数据库文本比较。</summary>
    private static string Like(string column) => $"{column} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE ''";

    /// <summary>单头属性通过共享物理映射引用，参数和 DTO 别名仍使用 CLR 名称。</summary>
    private static string H(string property) => C<AfterSalesHeader>("h", property);

    /// <summary>固定实体属性映射为引用列名，不接收外部 SQL 标识符。</summary>
    private static string C<T>(string alias, string property) => alias + "." +
        PostgreSqlNames.Quote(PostgreSqlEntityMetadata.GetMetadata(typeof(T)).GetProperty(property).ColumnName);

    /// <summary>固定实体使用已有 public 名称映射，不新增提供者切换或数据库连接。</summary>
    private static string Table<T>()
    {
        var map = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        return PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
    }

    /// <summary>按 SQL Server SqlGuid 字节权重稳定排序相同受理日期的单头，不使用 PG 原生 UUID 顺序。</summary>
    private static string SqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";
}
