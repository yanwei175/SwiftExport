using SwiftExport.Contracts.DTOs.Errors;
using Dapper;
using Npgsql;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Document;

/// <summary>在调用方PG会话内持久化销售头及三个状态命令；复用泛型CRUD，不接管事务或扩展服务状态校验。</summary>
public sealed class PostgreSqlSalesOrderHeadRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<SalesOrderHeader>(context), ISalesOrderHeadRepository
{
    private static readonly string HeaderTable = PostgreSqlNames.Quote(Metadata.SchemaName) + "." + PostgreSqlNames.Quote(Metadata.TableName);
    private const string InvoiceIndexName = "ux_sales_order_header_invoice_no";
    private const string DuplicateInvoiceMessage = "发票号已被未删除的销售订单使用，请删除原来的草稿或修改发票号后重新保存。";
    private static readonly string InvoiceLookupSql = $"""
        SELECT EXISTS (SELECT 1 FROM {HeaderTable}
            WHERE is_deleted=FALSE AND uid<>@ExcludeUid
              AND btrim(invoice_no,' ') COLLATE public.erp_dictionary_ci
                = btrim(@InvoiceNo,' ') COLLATE public.erp_dictionary_ci);
        """;

    /// <summary>在调用方事务内按发票号查重，普通空格及大小写与唯一索引一致；空值不占用编号。</summary>
    public Task<bool> InvoiceNoExistsAsync(string invoiceNo, Guid excludeUid, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(invoiceNo)) return Task.FromResult(false);
        return Connection.ExecuteScalarAsync<bool>(new CommandDefinition(InvoiceLookupSql,
            new { InvoiceNo = invoiceNo.Trim(), ExcludeUid = excludeUid }, Transaction, cancellationToken: token));
    }

    /// <summary>新增沿用泛型写入，只把发票唯一索引冲突转换为业务拒绝；失败事务由外层回滚。</summary>
    public override async Task<RepositoryWriteResult> AddAsync(SalesOrderHeader entity)
    {
        try { return await base.AddAsync(entity); }
        catch (PostgresException ex) when (IsInvoiceConflict(ex))
        { return RepositoryWriteResult.Fail(entity.Uid, null, ServiceErrorCodes.Validation, DuplicateInvoiceMessage); }
    }

    /// <summary>更新沿用原版本条件，同号并发冲突返回可读提示，不吞掉其他约束异常。</summary>
    public override async Task<RepositoryWriteResult> UpdateAsync(SalesOrderHeader entity)
    {
        var oldVersion = entity.Version;
        try { return await base.UpdateAsync(entity); }
        catch (PostgresException ex) when (IsInvoiceConflict(ex))
        { return RepositoryWriteResult.Fail(entity.Uid, oldVersion, ServiceErrorCodes.Validation, DuplicateInvoiceMessage); }
    }

    // 仅识别发票号索引，不把订单号、行号或其他唯一性问题伪装成发票重复。
    private static bool IsInvoiceConflict(PostgresException error)
        => error.SqlState == PostgresErrorCodes.UniqueViolation && error.ConstraintName == InvoiceIndexName;

    /// <summary>按原版本与软删除条件更新确认状态；不增加生命周期或原状态限制，返回源并发失败契约。</summary>
    public async Task<RepositoryWriteResult> SetConfirmationStatusAsync(Guid uid, int version, ConfirmationStatusEnum status)
    {
        var parameters = StatusParameters(uid, version);
        parameters.Add("Status", (int)status);
        var updated = await Connection.QuerySingleOrDefaultAsync<int?>($"""
            UPDATE {HeaderTable}
            SET confirmation_status=@Status,updated_date=@Now,updated_by=@UserName,version=version+1
            WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE
            RETURNING version;
            """, parameters, Transaction);
        return ToWriteResult(uid, version, updated, "更新销售订单状态");
    }

    /// <summary>仅关闭指定版本的有效开放订单并保存原原因及同一墙上时间；已关闭或版本变化沿源统一返回并发失败。</summary>
    public async Task<RepositoryWriteResult> CloseAsync(Guid uid, int version, string reason)
    {
        var parameters = StatusParameters(uid, version);
        parameters.Add("Reason", reason);
        parameters.Add("OpenStatus", (int)SalesOrderDocumentStatusEnum.Open);
        parameters.Add("ClosedStatus", (int)SalesOrderDocumentStatusEnum.Closed);
        var updated = await Connection.QuerySingleOrDefaultAsync<int?>($"""
            UPDATE {HeaderTable}
            SET document_status=@ClosedStatus,close_reason=@Reason,closed_date=@Now,closed_by=@UserName,
                updated_date=@Now,updated_by=@UserName,version=version+1
            WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE AND document_status=@OpenStatus
            RETURNING version;
            """, parameters, Transaction);
        return ToWriteResult(uid, version, updated, "关闭销售订单");
    }

    /// <summary>按版本软删除有效开放待确认草稿；不读取或删除明细，行删除仍由原最外层用例编排。</summary>
    public async Task<RepositoryWriteResult> DeleteDraftAsync(Guid uid, int version)
    {
        var parameters = StatusParameters(uid, version);
        parameters.Add("OpenStatus", (int)SalesOrderDocumentStatusEnum.Open);
        parameters.Add("PendingStatus", (int)ConfirmationStatusEnum.待确认);
        var updated = await Connection.QuerySingleOrDefaultAsync<int?>($"""
            UPDATE {HeaderTable}
            SET is_deleted=TRUE,updated_date=@Now,updated_by=@UserName,version=version+1
            WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE
                AND document_status=@OpenStatus AND confirmation_status=@PendingStatus
            RETURNING version;
            """, parameters, Transaction);
        return ToWriteResult(uid, version, updated, "删除销售订单草稿");
    }

    /// <summary>每个命令只取一次原DateTime.Now，并显式绑定无时区时间与当前用户名，不改变本地审计语义。</summary>
    private PostgreSqlParameters StatusParameters(Guid uid, int version)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", uid); parameters.Add("Version", version);
        parameters.AddTimestamp("Now", DateTime.Now); parameters.Add("UserName", CurrentUser.UserName);
        return parameters;
    }

    /// <summary>RETURNING只为成功行提供新版本；未命中不二次查询原因，保留源三命令相同失败码及文案。</summary>
    private static RepositoryWriteResult ToWriteResult(Guid uid, int version, int? updated, string operation)
        => updated.HasValue
            ? RepositoryWriteResult.Ok(uid, version, updated.Value, $"{operation}成功。")
            : RepositoryWriteResult.Fail(uid, version, ServiceErrorCodes.ConcurrencyConflict, $"{operation}失败，订单已被其他用户修改，请刷新后重试。");
}
