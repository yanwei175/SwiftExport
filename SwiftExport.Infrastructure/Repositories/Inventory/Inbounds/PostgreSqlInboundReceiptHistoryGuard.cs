using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

/// <summary>复用源入库反向证明条件，供PG全局收货查询和限定采购行的Trace共用；不创建事务或数据库对象。</summary>
public static class PostgreSqlInboundReceiptHistoryGuard
{
    /// <summary>在同一调用方会话检查全部采购正式入库历史，明确不可信时保留收货未知类别。</summary>
    public static Task ValidateGlobalAsync(DbSession session, CancellationToken cancellationToken = default)
        => ValidateAsync(session, false, [], cancellationToken);

    /// <summary>按既定采购行集合定位相关入库头，检查整头全部有效行；软删的范围行也参与定位。</summary>
    public static Task ValidateScopedAsync(DbSession session, IReadOnlyCollection<Guid> purchaseLineIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(purchaseLineIds);
        return ValidateAsync(session, true, purchaseLineIds.ToArray(), cancellationToken);
    }

    /// <summary>单次只读EXISTS只转换已证明的业务门禁命中；数据库异常及取消原样传播，外层事务原样借用。</summary>
    private static async Task ValidateAsync(DbSession session, bool scoped, Guid[] purchaseLineIds, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        // 显式绑定uuid[]，避免连接装饰器使Dapper误判提供者并把ANY参数展开为多标量；空scope仍是空数组。
        var parameters = new PostgreSqlParameters();
        parameters.Add("Scoped", scoped);
        parameters.AddIds("PurchaseLineIds", purchaseLineIds);
        var invalid = await session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            ValidationSql, parameters, session.Transaction, cancellationToken: token));
        if (invalid) throw new UnverifiedReceiptHistoryException();
    }

    // 条件与源Guard逐项对应：整头有效行、反向头身份/时间/原因、恰好成对流水及无外部修正引用。
    // 全量流水计数不排软删；scopeLine也不排软删，两者都是源证明边界，不能借迁移“修正”。
    private const string ValidationSql = """
        SELECT EXISTS(SELECT 1 FROM public.inbound_document h
            WHERE h.is_deleted=FALSE AND h.posting_status IN(1,8) AND h.document_type=10
              AND (NOT @Scoped OR EXISTS(
                  SELECT 1 FROM public.inbound_document_line scope_line
                  WHERE scope_line.inbound_document_id=h.uid AND scope_line.source_line_id=ANY(@PurchaseLineIds)))
              AND (
                  NOT EXISTS(SELECT 1 FROM public.inbound_document_line l WHERE l.inbound_document_id=h.uid AND l.is_deleted=FALSE)
                  OR EXISTS(SELECT 1 FROM public.inbound_document_line l WHERE l.inbound_document_id=h.uid AND l.is_deleted=FALSE
                      AND (l.quantity<=0 OR l.original_line_id IS NOT NULL))
                  OR (h.posting_status=8 AND (
                      NOT((h.creation_mode=0 AND h.trigger_packing_list_id IS NULL) OR (h.creation_mode=1 AND h.trigger_packing_list_id IS NOT NULL))
                      OR h.reversed_date IS NULL OR NULLIF(btrim(h.reverse_reason,' '),'') IS NULL
                      OR (SELECT COUNT(*) FROM public.inventory_ledger_entry e WHERE e.source_document_id=h.uid)
                          <>2*(SELECT COUNT(*) FROM public.inbound_document_line l WHERE l.inbound_document_id=h.uid AND l.is_deleted=FALSE)
                      OR EXISTS(SELECT 1 FROM public.inbound_document_line l WHERE l.inbound_document_id=h.uid AND l.is_deleted=FALSE
                          AND (SELECT COUNT(*) FROM public.inventory_ledger_entry o
                              JOIN public.inventory_ledger_entry r ON r.reversal_of_ledger_id=o.uid
                              WHERE o.source_document_id=h.uid AND o.source_line_id=l.uid AND o.command_item_id=l.uid
                                AND rtrim(o.source_document_type,' ') COLLATE public.erp_dictionary_ci='InboundDocument'
                                AND ((h.creation_mode=0 AND o.transaction_type IN(10,11)) OR (h.creation_mode=1 AND o.transaction_type=12))
                                AND o.warehouse_site_id=h.warehouse_site_id AND o.product_id=l.product_id AND o.quantity_delta=l.quantity
                                AND rtrim(o.unit_snapshot,' ') COLLATE public.erp_dictionary_ci=rtrim(l.unit_snapshot,' ') COLLATE public.erp_dictionary_ci
                                AND o.is_deleted=FALSE AND o.reversal_of_ledger_id IS NULL
                                AND o.replacement_of_ledger_id IS NULL AND o.correction_group_id IS NULL
                                AND r.source_document_id=h.uid AND r.source_line_id=l.uid
                                AND rtrim(r.source_document_type,' ') COLLATE public.erp_dictionary_ci='InboundDocument'
                                AND r.transaction_type=900 AND r.quantity_delta=-o.quantity_delta AND r.product_id=o.product_id
                                AND r.warehouse_site_id=o.warehouse_site_id
                                AND rtrim(r.unit_snapshot,' ') COLLATE public.erp_dictionary_ci=rtrim(o.unit_snapshot,' ') COLLATE public.erp_dictionary_ci
                                AND r.is_deleted=FALSE AND r.replacement_of_ledger_id IS NULL AND r.correction_group_id IS NULL)<>1)
                      OR EXISTS(SELECT 1 FROM public.inventory_ledger_entry x JOIN public.inventory_ledger_entry o
                          ON x.replacement_of_ledger_id=o.uid OR x.reversal_of_ledger_id=o.uid
                          WHERE o.source_document_id=h.uid AND (x.source_document_id<>h.uid OR x.source_document_id IS NULL))
                  ))
              ));
        """;
}
