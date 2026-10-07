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
    private const string HeaderProjection = """
        h."uid" AS "Uid",
        h."version" AS "Version",
        h."is_deleted" AS "IsDeleted",
        h."created_date" AS "CreatedDate",
        h."created_by" AS "CreatedBy",
        h."updated_date" AS "UpdatedDate",
        h."updated_by" AS "UpdatedBy",
        h."after_sales_no" AS "AfterSalesNo",
        h."customer_id" AS "CustomerId",
        h."customer_code_snapshot" AS "CustomerCodeSnapshot",
        h."customer_name_snapshot" AS "CustomerNameSnapshot",
        h."accepted_date" AS "AcceptedDate",
        h."owner_id" AS "OwnerId",
        h."document_status" AS "DocumentStatus",
        h."confirmation_status" AS "ConfirmationStatus",
        h."demand_confirmed_at" AS "DemandConfirmedAt",
        h."demand_confirmed_by" AS "DemandConfirmedBy",
        h."expected_completion_date" AS "ExpectedCompletionDate",
        h."conclusion" AS "Conclusion",
        h."remark" AS "Remark",
        h."original_sales_order_line_id" AS "OriginalSalesOrderLineId",
        h."customer_complaint_no" AS "CustomerComplaintNo",
        h."local_file_folder" AS "LocalFileFolder",
        h."responsible_supplier_id" AS "ResponsibleSupplierId",
        h."issue_product_id" AS "IssueProductId",
        h."issue_sku_snapshot" AS "IssueSkuSnapshot",
        h."issue_customer_sku_snapshot" AS "IssueCustomerSkuSnapshot",
        h."issue_link_sku_snapshot" AS "IssueLinkSkuSnapshot",
        h."issue_description" AS "IssueDescription",
        h."quality_category_id" AS "QualityCategoryId",
        h."cause_analysis" AS "CauseAnalysis",
        h."required_date" AS "RequiredDate",
        h."service_amount_cny" AS "ServiceAmountCny",
        h."returned_received_date" AS "ReturnedReceivedDate",
        h."treatment_description" AS "TreatmentDescription"
        """;

    /// <summary>按原客户、状态、受理日期、责任方及头行型号筛选分页；行关键词仅判断存在，不放大单头或金额。</summary>
    public async Task<PagedResponseBase<AfterSalesListItemDto>> SearchAsync(
        AfterSalesSearchRequest request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var where = $"""
            WHERE h."is_deleted"=FALSE
              AND (CAST(@CustomerId AS uuid) IS NULL OR h."customer_id"=CAST(@CustomerId AS uuid))
              AND (CAST(@DocumentStatus AS integer) IS NULL OR h."document_status"=CAST(@DocumentStatus AS integer))
              AND (CAST(@ConfirmationStatus AS integer) IS NULL OR h."confirmation_status"=CAST(@ConfirmationStatus AS integer))
              AND (@DateFrom IS NULL OR h."accepted_date">=@DateFrom)
              AND (@DateTo IS NULL OR h."accepted_date"<=@DateTo)
              AND (CAST(@ResponsibleSupplierId AS uuid) IS NULL OR h."responsible_supplier_id"=CAST(@ResponsibleSupplierId AS uuid))
              AND (CAST(@Keyword AS text) IS NULL OR h."after_sales_no" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '' OR h."customer_complaint_no" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE ''
                OR h."issue_sku_snapshot" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '' OR h."issue_customer_sku_snapshot" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '' OR h."issue_link_sku_snapshot" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE ''
                OR EXISTS(SELECT 1 FROM "public"."after_sales_line" l WHERE l."header_id"=h."uid"
                  AND l."is_deleted"=FALSE AND (l."sku_snapshot" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE ''
                    OR l."customer_sku_snapshot" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '' OR l."link_sku_snapshot" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '')))
            """;
        // SQL Server NULLIF 的空文本比较忽略尾部 ASCII 空格；非空型号仍返回原快照，不裁掉显示文本。
        var issueCustomerSku = "h.\"issue_customer_sku_snapshot\"";
        var sql = $"""
            SELECT COUNT(*) FROM "public"."after_sales_header" h {where};
            SELECT {HeaderProjection},
              COALESCE(u."nick_name",u."user_name",'') AS "OwnerName",
              COALESCE(h."service_amount_cny",0) AS "KnownAmountCny",
              (h."service_amount_cny" IS NULL) AS "HasUndeterminedAmount",
              COALESCE(CASE WHEN rtrim({issueCustomerSku},' ') COLLATE public.erp_dictionary_ci = '' COLLATE public.erp_dictionary_ci
                THEN NULL ELSE {issueCustomerSku} END,h."issue_sku_snapshot",'') AS "IssueModels",
              COALESCE(s."supplier_name",'') AS "ResponsibleSuppliers"
            FROM "public"."after_sales_header" h
            LEFT JOIN "public"."sys_user" u ON u."uid"=h."owner_id"
            LEFT JOIN "public"."supplier" s ON s."uid"=h."responsible_supplier_id"
            {where} ORDER BY h."accepted_date" DESC NULLS LAST,{SqlGuidOrder("h.\"uid\"")} ASC
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
        return new()
        {
            PageIndex = page,
            PageSize = size,
            TotalCount = count,
            Items = (await reader.ReadAsync<AfterSalesListItemDto>()).ToList()
        };
    }

    /// <summary>仅对固定列生成原始 LIKE 条件；保留通配符和反斜杠，使用已冻结的数据库文本比较。</summary>
    private static string Like(string column) => $"{column} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE ''";


    /// <summary>按 SQL Server SqlGuid 字节权重稳定排序相同受理日期的单头，不使用 PG 原生 UUID 顺序。</summary>
    private static string SqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";
}
