using System.Data;
using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.AutomaticDocuments;

/// <summary>在原业务会话中持久化自动入出库头行和状态，保留稳定身份、原资格及版本副作用，不接管库存联合锁。</summary>
public sealed class PostgreSqlAutomaticDocumentRepository(RepositoryBaseContext context) : IAutomaticDocumentRepository
{
    /// <summary>透传原事务与取消，不创建连接或附加事务门禁。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: token);

    /// <summary>只读检查全部实体持久化列与原禁止的类型检查、单据产品唯一索引，不自动修改数据库结构。</summary>
    public async Task<bool> IsSchemaReadyAsync(CancellationToken token)
    {
        foreach (var type in new[] { typeof(InboundDocument), typeof(InboundDocumentLine), typeof(OutboundDocument), typeof(OutboundDocumentLine) })
        {
            var metadata = EntityMetadataCache.GetMetadata(type);
            var names = (await context.Session.Connection.QueryAsync<string>(Command("""
                SELECT column_name FROM information_schema.columns WHERE table_schema=@Schema AND table_name=@Table
                """, new { Schema = metadata.SchemaName, Table = metadata.TableName }, token))).ToHashSet(StringComparer.Ordinal);
            if (metadata.Columns.Any(column => !names.Contains(column.ColumnName))) return false;
        }
        // conkey 使用实际依赖列，避免猜测格式化 CHECK 文本；与源相同，类型/建立方式检查存在即拒绝。
        return !await context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint con JOIN pg_class c ON c.oid=con.conrelid
                JOIN pg_namespace n ON n.oid=c.relnamespace
                JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=ANY(con.conkey)
                WHERE n.nspname='public' AND c.relname IN ('inbound_document','outbound_document')
                  AND con.contype='c' AND a.attname IN ('document_type','creation_mode')
            ) OR EXISTS (
                SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid
                JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relname='outbound_document_line' AND i.indisunique AND i.indisvalid
                  AND i.indnkeyatts=2 AND (
                    SELECT count(*) FROM unnest(i.indkey) WITH ORDINALITY k(attnum,position)
                    JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=k.attnum
                    WHERE k.position<=i.indnkeyatts AND a.attname IN ('outbound_document_id','product_id')
                  )=2
            );
            """, null, token));
    }

    /// <summary>按原.NET身份顺序锁现有产品；缺失身份的范围和联合库存协议由外层用例负责。</summary>
    public async Task LockProductsAsync(IReadOnlyList<Guid> productIds, CancellationToken token)
    {
        foreach (var id in productIds.Distinct().Order())
            await context.Session.Connection.QueryAsync<Guid>(Command("SELECT uid FROM public.product WHERE uid=@Id FOR UPDATE", new { Id = id }, token));
    }

    /// <summary>读取未软删采购头行的供应商，不新增确认或产品资格过滤。</summary>
    public Task<Guid?> GetPurchaseSupplierAsync(Guid purchaseLineId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command("""
            SELECT h.supplier_id FROM public.purchase_order_line l
            JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
            WHERE l.uid=@Id AND l.is_deleted=FALSE AND h.is_deleted=FALSE;
            """, new { Id = purchaseLineId }, token));

    /// <summary>在原父事务中保留自动入库头行身份，按原顺序逐项打戳和插入。</summary>
    public async Task InsertInboundAsync(InboundDocument header, IReadOnlyList<InboundDocumentLine> lines, CancellationToken token)
    {
        await AddAsync(header, token);
        foreach (var line in lines) await AddAsync(line, token);
    }

    /// <summary>在原父事务中保留自动出库头行身份，按原顺序逐项打戳和插入。</summary>
    public async Task InsertOutboundAsync(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines, CancellationToken token)
    {
        await AddAsync(header, token);
        foreach (var line in lines) await AddAsync(line, token);
    }

    /// <summary>使用已有实体映射插入原完整持久化属性，不调用会重建 Uid 的通用 Add。</summary>
    private async Task AddAsync<T>(T entity, CancellationToken token) where T : BaseEntity
    {
        token.ThrowIfCancellationRequested();
        if (context.Session.Transaction is null || entity.Uid == Guid.Empty) throw new InvalidOperationException("自动单据插入必须具有事务及稳定身份。");
        context.AuditStamp.StampForInsert(entity);
        entity.IsDeleted = false; entity.Version = Math.Max(1, entity.Version);
        var metadata = EntityMetadataCache.GetMetadata(typeof(T));
        var sql = $"INSERT INTO {Table(metadata)} ({string.Join(',', metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)))}) VALUES ({string.Join(',', metadata.Columns.Select(column => "@" + column.ParameterName))});";
        if (await context.Session.Connection.ExecuteAsync(Command(sql, PostgreSqlParameters.Entity(entity, metadata), token)) != 1)
            throw new DBConcurrencyException("自动单据写入失败。");
    }

    /// <summary>以原采购自动入库资格写状态，保留箱单归属和版本条件。</summary>
    public Task SetInboundStateAsync(InboundDocument header, bool reverse, CancellationToken token) =>
        SetStateAsync(header, "document_type=10 AND creation_mode=1", reverse, token);

    /// <summary>以原正式出货或客供消耗资格写状态，保留箱单归属和版本条件。</summary>
    public Task SetOutboundStateAsync(OutboundDocument header, bool reverse, CancellationToken token) =>
        SetStateAsync(header, "document_type IN(20,21)", reverse, token);

    /// <summary>只翻译固定类型谓词；原状态、版本或箱单不匹配时保持原并发异常，不自行回滚或吞错。</summary>
    private async Task SetStateAsync(BaseEntity entity, string kind, bool reverse, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(entity);
        var metadata = EntityMetadataCache.GetMetadata(entity.GetType());
        var state = reverse
            ? "posting_status=8,reversed_date=@ReversedDate,reversed_by=@ReversedBy,reverse_reason=@ReverseReason"
            : "posting_status=1,posted_date=@PostedDate,posted_by=@PostedBy";
        var changed = await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {Table(metadata)} SET {state},version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy
            WHERE uid=@Uid AND version=@Version AND posting_status={(reverse ? 1 : 0)} AND lifecycle_status=0
              AND is_deleted=FALSE AND {kind} AND trigger_packing_list_id=@TriggerPackingListId AND trigger_packing_list_id IS NOT NULL;
            """, PostgreSqlParameters.Entity(entity, metadata), token));
        if (changed != 1) throw new DBConcurrencyException("自动单据状态、版本或箱单归属已变化。");
        entity.Version++;
    }

    /// <summary>物理表名仅来自已有映射，禁止外部输入参与 SQL 标识符。</summary>
    private static string Table(EntityMetadata metadata) => PostgreSqlNames.Quote(metadata.SchemaName) + "." + PostgreSqlNames.Quote(metadata.TableName);
}
