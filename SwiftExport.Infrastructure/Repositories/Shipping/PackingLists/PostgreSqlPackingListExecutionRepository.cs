using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>在原会话中追加箱单执行证据并写状态；保持反向全部历史事实及稳定身份，不承担库存分配或联合锁准备。</summary>
public sealed class PostgreSqlPackingListExecutionRepository(RepositoryBaseContext context) : IPackingListExecutionRepository
{
    /// <summary>只检查原关键表、两列和启用唯一命令索引，不自动迁移或忽略缺失对象。</summary>
    public Task<bool> IsSchemaReadyAsync(CancellationToken token)=>context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
        SELECT to_regclass('public.packing_list_posting_execution') IS NOT NULL
            AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='packing_list_posting_execution' AND column_name='result_json')
            AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='packing_list_posting_execution_line' AND column_name='original_execution_line_id')
            AND EXISTS(SELECT 1 FROM pg_index i JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
                JOIN pg_class idx ON idx.oid=i.indexrelid WHERE n.nspname='public' AND t.relname='packing_list_posting_execution'
                    AND idx.relname=@Index AND i.indisunique AND i.indisvalid);
        """,new { Index=PostgreSqlNames.FromClr("UX_PackingListPostingExecution_Command") },token));

    /// <summary>普通读取命令证据，不在空身份上另加范围锁；正式链复用原外层命令准备，高隔离首次快照另验。</summary>
    public async Task<PackingListPostingExecution?> FindCommandAsync(Guid commandId,CancellationToken token)=>
        (await context.Session.Connection.QueryAsync<PackingListPostingExecution>(Command($"SELECT {Columns<PackingListPostingExecution>()} FROM public.packing_list_posting_execution WHERE command_id=@commandId",new {commandId},token))).SingleOrDefault();

    /// <summary>保留正反执行全部头行及原时间/SqlGuid顺序，不按软删过滤历史。</summary>
    public async Task<(IReadOnlyList<PackingListPostingExecution> Heads,IReadOnlyList<PackingListPostingExecutionLine> Lines)> ReadAsync(Guid headId,CancellationToken token)
    {
        var heads=(await context.Session.Connection.QueryAsync<PackingListPostingExecution>(Command($"SELECT {Columns<PackingListPostingExecution>()} FROM public.packing_list_posting_execution WHERE packing_list_head_id=@headId ORDER BY created_date ASC NULLS FIRST,{UuidOrder("uid")}",new {headId},token))).ToArray();
        var lines=(await context.Session.Connection.QueryAsync<PackingListPostingExecutionLine>(Command($"SELECT {Columns<PackingListPostingExecutionLine>()} FROM public.packing_list_posting_execution_line WHERE execution_id=ANY(@Ids) ORDER BY {UuidOrder("execution_id")},line_number",Ids(heads.Select(x=>x.Uid).ToArray()),token))).ToArray();
        return(heads,lines);
    }

    /// <summary>父事务中按原归属校验和取消检查逐项追加，不改执行身份或自动提交。</summary>
    public async Task AppendAsync(PackingListPostingExecution execution,IReadOnlyList<PackingListPostingExecutionLine> details,CancellationToken token)
    {
        RequireTransaction();
        if(details.Any(x=>x.ExecutionId!=execution.Uid))throw new InvalidOperationException("执行证据头行归属不一致。");
        token.ThrowIfCancellationRequested();await InsertEvidenceAsync(execution,token);
        foreach(var line in details){token.ThrowIfCancellationRequested();await InsertEvidenceAsync(line,token);}
        token.ThrowIfCancellationRequested();
    }

    /// <summary>保留正向/反向各自资格、expectedVersion谓词及对象版本回写，不改生命周期规则。</summary>
    public async Task MarkAsync(PackingListHead head,int expectedVersion,bool reverse,CancellationToken token)
    {
        RequireTransaction();context.AuditStamp.StampForUpdate(head);
        var sql=reverse?"""
            UPDATE public.packing_list_head SET posting_status=8,reverse_command_id=@ReverseCommandId,
                reversed_date=@ReversedDate,reversed_by=@ReversedBy,reverse_reason=@ReverseReason,
                updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
            WHERE uid=@Uid AND version=@ExpectedVersion AND is_deleted=FALSE AND posting_status=1;
            """:"""
            UPDATE public.packing_list_head SET posting_status=1,post_command_id=@PostCommandId,posting_date=@PostingDate,
                posted_date=@PostedDate,posted_by=@PostedBy,updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
            WHERE uid=@Uid AND version=@ExpectedVersion AND is_deleted=FALSE AND lifecycle_status=0 AND posting_status=0;
            """;
        var parameters=PostgreSqlParameters.Entity(head,PostgreSqlEntityMetadata.GetMetadata(typeof(PackingListHead)));parameters.Add("ExpectedVersion",expectedVersion);
        if(await context.Session.Connection.ExecuteAsync(Command(sql,parameters,token))!=1)throw new InvalidOperationException("箱单状态或版本发生变化。");
        head.Version=expectedVersion+1;
    }

    /// <summary>读取原箱单、触发与证据引用的自动头行、正反修正流水和当前余额；保留误关联事实供上层阻断。</summary>
    public async Task<PackingListReverseFacts> ReadReverseFactsAsync(Guid headId,CancellationToken token)
    {
        var history=await ReadAsync(headId,token);var result=new PackingListReverseFacts{Executions=history.Heads,Evidence=history.Lines};
        result.Head=(await ReadRows<PackingListHead>("uid=@headId",new {headId},token)).SingleOrDefault();
        result.PackingLines=await ReadRows<PackingListLine>("packing_list_head_id=@headId",new {headId},token);
        var documents=history.Lines.Select(x=>x.DocumentId).OfType<Guid>().Distinct().ToArray();
        var documentParameters=Ids(documents,"documents");documentParameters.Add("headId",headId);
        result.InboundHeads=await ReadRows<InboundDocument>("trigger_packing_list_id=@headId OR uid=ANY(@documents)",documentParameters,token);
        result.OutboundHeads=await ReadRows<OutboundDocument>("trigger_packing_list_id=@headId OR uid=ANY(@documents)",documentParameters,token);
        var inboundIds=result.InboundHeads.Select(x=>x.Uid).ToArray();var outboundIds=result.OutboundHeads.Select(x=>x.Uid).ToArray();
        result.InboundLines=await ReadRows<InboundDocumentLine>("inbound_document_id=ANY(@Ids)",Ids(inboundIds),token);
        result.OutboundLines=await ReadRows<OutboundDocumentLine>("outbound_document_id=ANY(@Ids)",Ids(outboundIds),token);
        var ledgerParameters=Ids(inboundIds.Concat(outboundIds).Distinct().ToArray());ledgerParameters.AddIds("LedgerIds",history.Lines.Select(x=>x.LedgerId).OfType<Guid>().Distinct().ToArray());
        result.Ledgers=await ReadRows<InventoryLedgerEntry>("source_document_id=ANY(@Ids) OR uid=ANY(@LedgerIds) OR reversal_of_ledger_id=ANY(@LedgerIds) OR replacement_of_ledger_id=ANY(@LedgerIds)",ledgerParameters,token);
        var warehouses=history.Lines.Select(x=>x.WarehouseSiteId).OfType<Guid>().Distinct().ToArray();var products=history.Lines.Select(x=>x.ProductId).OfType<Guid>().Distinct().ToArray();
        result.Warehouses=await ReadRows<WarehouseSite>("uid=ANY(@Ids)",Ids(warehouses),token);
        var balanceParameters=Ids(warehouses,"warehouseIds");balanceParameters.AddIds("productIds",products);
        result.Balances=await ReadRows<InventoryBalance>("warehouse_site_id=ANY(@warehouseIds) AND product_id=ANY(@productIds)",balanceParameters,token);
        return result;
    }

    /// <summary>固定实体映射和谓词不接受客户端标识符，所有历史读保留原SqlGuid顺序。</summary>
    private async Task<IReadOnlyList<T>> ReadRows<T>(string predicate,object parameters,CancellationToken token)=>
        (await context.Session.Connection.QueryAsync<T>(Command($"SELECT {Columns<T>()} FROM {Table<T>()} WHERE {predicate} ORDER BY {UuidOrder("uid")}",parameters,token))).ToArray();

    /// <summary>原执行身份已进入JSON和反向映射，直接插入完整实体持久化列，不调用重建身份的泛型Add。</summary>
    private async Task InsertEvidenceAsync<T>(T entity,CancellationToken token) where T:BaseEntity
    {
        RequireTransaction();if(entity.Uid==Guid.Empty)throw new InvalidOperationException("执行证据缺少稳定身份。");
        context.AuditStamp.StampForInsert(entity);entity.IsDeleted=false;entity.Version=Math.Max(1,entity.Version);
        var map=PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        var sql=$"INSERT INTO {Table<T>()} ({string.Join(',',map.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)))}) VALUES ({string.Join(',',map.Columns.Select(c=>"@"+c.ParameterName))});";
        if(await context.Session.Connection.ExecuteAsync(Command(sql,PostgreSqlParameters.Entity(entity,map),token))!=1)throw new InvalidOperationException("执行证据写入失败。");
    }

    /// <summary>保留源写入事务门禁，不为普通读取新增门禁。</summary>
    private void RequireTransaction(){if(context.Session.Transaction is null)throw new InvalidOperationException("执行证据必须加入外层事务。");}
    /// <summary>原数组以显式uuid[]传输，包括空集合，避免Dapper展开。</summary>
    private static PostgreSqlParameters Ids(Guid[] values,string name="Ids"){var parameters=new PostgreSqlParameters();parameters.AddIds(name,values);return parameters;}
    /// <summary>持久化列按已有映射显式提供CLR别名。</summary>
    private static string Columns<T>()=>string.Join(',',PostgreSqlEntityMetadata.GetMetadata(typeof(T)).Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)+" AS "+PostgreSqlNames.Quote(c.PropertyName)));
    /// <summary>表仅取自编译期实体映射。</summary>
    private static string Table<T>(){var map=PostgreSqlEntityMetadata.GetMetadata(typeof(T));return PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName);}
    /// <summary>保持源uniqueidentifier字节优先级。</summary>
    private static string UuidOrder(string column)=>"ARRAY["+string.Join(',',new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(i=>$"get_byte(uuid_send({column}),{i})"))+"]";
    /// <summary>调用方的同连接、可空事务和取消令牌原样透传。</summary>
    private CommandDefinition Command(string sql,object? parameters,CancellationToken token)=>new(sql,parameters,context.Session.Transaction,cancellationToken:token);
}
