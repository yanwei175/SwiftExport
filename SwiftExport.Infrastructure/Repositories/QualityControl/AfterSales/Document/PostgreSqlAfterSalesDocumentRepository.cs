using System.Data;
using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Document;

/// <summary>借用当前请求会话完成 PostgreSQL 售后头行读写；保留固定持久化列、审计和版本，不接管事务。</summary>
public sealed class PostgreSqlAfterSalesDocumentRepository(RepositoryBaseContext context) : IAfterSalesDocumentRepository
{
    /// <summary>本仓储固定持久化列及参数，保留原写入范围。</summary>
    private const string LineUpdateSql = """
        UPDATE "public"."after_sales_line" SET "is_deleted"=@IsDeleted,
        "updated_date"=@UpdatedDate,
        "updated_by"=@UpdatedBy,
        "header_id"=@HeaderId,
        "line_number"=@LineNumber,
        "product_id"=@ProductId,
        "is_non_standard"=@IsNonStandard,
        "sku_snapshot"=@SkuSnapshot,
        "customer_sku_snapshot"=@CustomerSkuSnapshot,
        "link_sku_snapshot"=@LinkSkuSnapshot,
        "description_snapshot"=@DescriptionSnapshot,
        "color_id"=@ColorId,
        "color_snapshot"=@ColorSnapshot,
        "specification_snapshot"=@SpecificationSnapshot,
        "quantity"=@Quantity,
        "unit_snapshot"=@UnitSnapshot,
        "remark"=@Remark,
        "version"="version"+1 WHERE "uid"=@Uid AND "version"=@Version
    """;

    /// <summary>本仓储固定持久化列及参数，保留原写入范围。</summary>
    private const string LineInsertSql = """
        INSERT INTO "public"."after_sales_line" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "header_id",
        "line_number",
        "product_id",
        "is_non_standard",
        "sku_snapshot",
        "customer_sku_snapshot",
        "link_sku_snapshot",
        "description_snapshot",
        "color_id",
        "color_snapshot",
        "specification_snapshot",
        "quantity",
        "unit_snapshot",
        "remark") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @HeaderId,
        @LineNumber,
        @ProductId,
        @IsNonStandard,
        @SkuSnapshot,
        @CustomerSkuSnapshot,
        @LinkSkuSnapshot,
        @DescriptionSnapshot,
        @ColorId,
        @ColorSnapshot,
        @SpecificationSnapshot,
        @Quantity,
        @UnitSnapshot,
        @Remark)
    """;

    /// <summary>本仓储固定持久化列及参数，保留原写入范围。</summary>
    private const string HeaderUpdateSql = """
        UPDATE "public"."after_sales_header" SET "is_deleted"=@IsDeleted,
        "updated_date"=@UpdatedDate,
        "updated_by"=@UpdatedBy,
        "after_sales_no"=@AfterSalesNo,
        "customer_id"=@CustomerId,
        "customer_code_snapshot"=@CustomerCodeSnapshot,
        "customer_name_snapshot"=@CustomerNameSnapshot,
        "accepted_date"=@AcceptedDate,
        "owner_id"=@OwnerId,
        "document_status"=@DocumentStatus,
        "confirmation_status"=@ConfirmationStatus,
        "demand_confirmed_at"=@DemandConfirmedAt,
        "demand_confirmed_by"=@DemandConfirmedBy,
        "expected_completion_date"=@ExpectedCompletionDate,
        "conclusion"=@Conclusion,
        "remark"=@Remark,
        "original_sales_order_line_id"=@OriginalSalesOrderLineId,
        "customer_complaint_no"=@CustomerComplaintNo,
        "local_file_folder"=@LocalFileFolder,
        "responsible_supplier_id"=@ResponsibleSupplierId,
        "issue_product_id"=@IssueProductId,
        "issue_sku_snapshot"=@IssueSkuSnapshot,
        "issue_customer_sku_snapshot"=@IssueCustomerSkuSnapshot,
        "issue_link_sku_snapshot"=@IssueLinkSkuSnapshot,
        "issue_description"=@IssueDescription,
        "quality_category_id"=@QualityCategoryId,
        "cause_analysis"=@CauseAnalysis,
        "required_date"=@RequiredDate,
        "service_amount_cny"=@ServiceAmountCny,
        "returned_received_date"=@ReturnedReceivedDate,
        "treatment_description"=@TreatmentDescription,
        "version"="version"+1 WHERE "uid"=@Uid AND "version"=@Version
    """;

    /// <summary>本仓储固定持久化列及参数，保留原写入范围。</summary>
    private const string HeaderInsertSql = """
        INSERT INTO "public"."after_sales_header" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "after_sales_no",
        "customer_id",
        "customer_code_snapshot",
        "customer_name_snapshot",
        "accepted_date",
        "owner_id",
        "document_status",
        "confirmation_status",
        "demand_confirmed_at",
        "demand_confirmed_by",
        "expected_completion_date",
        "conclusion",
        "remark",
        "original_sales_order_line_id",
        "customer_complaint_no",
        "local_file_folder",
        "responsible_supplier_id",
        "issue_product_id",
        "issue_sku_snapshot",
        "issue_customer_sku_snapshot",
        "issue_link_sku_snapshot",
        "issue_description",
        "quality_category_id",
        "cause_analysis",
        "required_date",
        "service_amount_cny",
        "returned_received_date",
        "treatment_description") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @AfterSalesNo,
        @CustomerId,
        @CustomerCodeSnapshot,
        @CustomerNameSnapshot,
        @AcceptedDate,
        @OwnerId,
        @DocumentStatus,
        @ConfirmationStatus,
        @DemandConfirmedAt,
        @DemandConfirmedBy,
        @ExpectedCompletionDate,
        @Conclusion,
        @Remark,
        @OriginalSalesOrderLineId,
        @CustomerComplaintNo,
        @LocalFileFolder,
        @ResponsibleSupplierId,
        @IssueProductId,
        @IssueSkuSnapshot,
        @IssueCustomerSkuSnapshot,
        @IssueLinkSkuSnapshot,
        @IssueDescription,
        @QualityCategoryId,
        @CauseAnalysis,
        @RequiredDate,
        @ServiceAmountCny,
        @ReturnedReceivedDate,
        @TreatmentDescription)
    """;

    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;
    private static readonly EntityMetadata HeaderMetadata = EntityMetadataCache.GetMetadata(typeof(AfterSalesHeader));
    private static readonly EntityMetadata LineMetadata = EntityMetadataCache.GetMetadata(typeof(AfterSalesLine));

    /// <summary>读取未软删单头；指定锁定时锁实际返回行，锁寿命由调用方的业务事务决定，缺失返回 null。</summary>
    public Task<AfterSalesHeader?> GetHeaderAsync(Guid uid, bool forUpdate = false)
        => Connection.QuerySingleOrDefaultAsync<AfterSalesHeader>(
            $"""
                SELECT "uid" AS "Uid",
                "version" AS "Version",
                "is_deleted" AS "IsDeleted",
                "created_date" AS "CreatedDate",
                "created_by" AS "CreatedBy",
                "updated_date" AS "UpdatedDate",
                "updated_by" AS "UpdatedBy",
                "after_sales_no" AS "AfterSalesNo",
                "customer_id" AS "CustomerId",
                "customer_code_snapshot" AS "CustomerCodeSnapshot",
                "customer_name_snapshot" AS "CustomerNameSnapshot",
                "accepted_date" AS "AcceptedDate",
                "owner_id" AS "OwnerId",
                "document_status" AS "DocumentStatus",
                "confirmation_status" AS "ConfirmationStatus",
                "demand_confirmed_at" AS "DemandConfirmedAt",
                "demand_confirmed_by" AS "DemandConfirmedBy",
                "expected_completion_date" AS "ExpectedCompletionDate",
                "conclusion" AS "Conclusion",
                "remark" AS "Remark",
                "original_sales_order_line_id" AS "OriginalSalesOrderLineId",
                "customer_complaint_no" AS "CustomerComplaintNo",
                "local_file_folder" AS "LocalFileFolder",
                "responsible_supplier_id" AS "ResponsibleSupplierId",
                "issue_product_id" AS "IssueProductId",
                "issue_sku_snapshot" AS "IssueSkuSnapshot",
                "issue_customer_sku_snapshot" AS "IssueCustomerSkuSnapshot",
                "issue_link_sku_snapshot" AS "IssueLinkSkuSnapshot",
                "issue_description" AS "IssueDescription",
                "quality_category_id" AS "QualityCategoryId",
                "cause_analysis" AS "CauseAnalysis",
                "required_date"::timestamp without time zone AS "RequiredDate",
                "service_amount_cny" AS "ServiceAmountCny",
                "returned_received_date"::timestamp without time zone AS "ReturnedReceivedDate",
                "treatment_description" AS "TreatmentDescription" FROM "public"."after_sales_header" WHERE "uid"=@uid AND "is_deleted"=FALSE{(forUpdate ? " FOR UPDATE" : "")}
            """,
            new { uid }, Transaction);

    /// <summary>按原稳定顺序读取全部历史行；锁定模式先锁实际父头再锁行，在同一外层事务内为已有父头的空行集合提供锚点。</summary>
    public async Task<IReadOnlyList<AfterSalesLine>> GetLinesAsync(Guid uid, bool forUpdate = false)
    {
        if (forUpdate)
        {
            // 只锁已有父头（含软删头），不改变历史行返回资格；缺失父头没有可锁锚点，不虚构间隙保护。
            await Connection.QuerySingleOrDefaultAsync<Guid?>(
                $"SELECT \"uid\" FROM \"public\".\"after_sales_header\" WHERE \"uid\"=@uid FOR UPDATE",
                new { uid }, Transaction);
        }
        var uidColumn = "\"uid\"";
        return (await Connection.QueryAsync<AfterSalesLine>(
            $"""
                SELECT "uid" AS "Uid",
                "version" AS "Version",
                "is_deleted" AS "IsDeleted",
                "created_date" AS "CreatedDate",
                "created_by" AS "CreatedBy",
                "updated_date" AS "UpdatedDate",
                "updated_by" AS "UpdatedBy",
                "header_id" AS "HeaderId",
                "line_number" AS "LineNumber",
                "product_id" AS "ProductId",
                "is_non_standard" AS "IsNonStandard",
                "sku_snapshot" AS "SkuSnapshot",
                "customer_sku_snapshot" AS "CustomerSkuSnapshot",
                "link_sku_snapshot" AS "LinkSkuSnapshot",
                "description_snapshot" AS "DescriptionSnapshot",
                "color_id" AS "ColorId",
                "color_snapshot" AS "ColorSnapshot",
                "specification_snapshot" AS "SpecificationSnapshot",
                "quantity" AS "Quantity",
                "unit_snapshot" AS "UnitSnapshot",
                "remark" AS "Remark" FROM "public"."after_sales_line" WHERE "header_id"=@uid ORDER BY "line_number" ASC NULLS FIRST,
                {SqlGuidOrder(uidColumn)} ASC{(forUpdate ? " FOR UPDATE" : "")}
            """,
            new { uid }, Transaction)).ToList();
    }

    /// <summary>在已有业务事务内按原单头固定列插入或乐观更新；返回是否恰好影响一行，并保留审计和实体版本副作用。</summary>
    public Task<bool> WriteHeaderAsync(AfterSalesHeader head, bool insert)
        => WriteAsync(HeaderMetadata, head, insert, HeaderInsertSql, HeaderUpdateSql);

    /// <summary>在已有业务事务内按原行固定列插入或乐观更新；生产调用方负责先锁或写父头，再写行。</summary>
    public Task<bool> WriteLineAsync(AfterSalesLine line, bool insert)
        => WriteAsync(LineMetadata, line, insert, LineInsertSql, LineUpdateSql);

    /// <summary>仅翻译固定列 SQL；新增保留调用方 Uid，更新不增加软删过滤，失败也保留原审计打戳时机。</summary>
    private async Task<bool> WriteAsync(EntityMetadata metadata, BaseEntity entity, bool insert, string insertSql, string updateSql)
    {
        if (Transaction is null) throw new InvalidOperationException("售后写入必须处于业务事务。");
        string sql;
        if (insert)
        {
            context.AuditStamp.StampForInsert(entity);
            entity.Version = 1;
            sql = insertSql;
        }
        else
        {
            context.AuditStamp.StampForUpdate(entity);
            sql = updateSql;
        }
        var count = await Connection.ExecuteAsync(sql, PostgreSqlParameters.Entity(entity, metadata), Transaction);
        if (count == 1 && !insert) entity.Version++;
        return count == 1;
    }

    /// <summary>用 SQL Server uniqueidentifier 的字节比较顺序保留同行号时的原稳定排序。</summary>
    private static string SqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";
}
