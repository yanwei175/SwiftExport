using System.Data;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用已验真实连接包装器，只在测试partial安排跨连接提交和精确UID清理，不改变生产读取或加锁。</summary>
public sealed partial class PostgreSqlMaterialDemandTraceMigrationIntegrationTests
{
    /// <summary>覆盖结果已冻结后独立连接提交收货8；RC展示新8仍保留旧覆盖6/1，RR沿调用方快照看到7。</summary>
    [TestMethod]
    [DataRow("none")] [DataRow("read-committed")] [DataRow("repeatable-read")]
    public async Task CrossConnection_CoverageFreezeAndCurrentReceiptsRespectCallerIsolation(string mode)
    {
        using var fixture=new Fixture(); var rows=new CommittedTraceRows();
        await using var writer=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()); await writer.OpenAsync();
        try
        {
            var gross=fixture.Gross(); await AddCommittedSeed(fixture,rows,gross);
            var first=fixture.Net(gross,1); var second=fixture.Net(gross,2);
            await AddCommittedSeed(fixture,rows,first); await AddCommittedSeed(fixture,rows,second);
            var target=await Target(fixture,gross,20m);
            rows.RegisterTable("public.supplier",target.Head.SupplierId);
            rows.Register(target.Head); rows.Register(target.Line);
            var firstLink=fixture.Link(first,target.Head.Uid,target.Line.Uid,6m,0m); firstLink.DemandRequiredDate=new(2026,10,5);
            var secondLink=fixture.Link(second,target.Head.Uid,target.Line.Uid,6m,0m); secondLink.DemandRequiredDate=new(2026,10,6);
            await AddCommittedSeed(fixture,rows,firstLink); await AddCommittedSeed(fixture,rows,secondLink);
            var receipt=await Receipt(fixture,target.Line.Uid,7m); rows.Register(receipt.Head); rows.Register(receipt.Line);
            BeginCommittedReader(fixture,mode);
            var readerPid=await fixture.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT pg_backend_pid()",transaction:fixture.Session.Transaction));
            Assert.AreNotEqual(writer.ProcessID,readerPid,"修改来自不同物理连接，不能当作同事务自写。");
            var hook=(HookConnection)fixture.Session.Connection; hook.Commands.Clear(); var changed=false;
            hook.Before=async(sql,token)=>
            {
                if(!sql.Contains("WITH coverage AS",StringComparison.Ordinal)) return;
                hook.Before=null;
                Assert.AreEqual(1,await writer.ExecuteAsync(new CommandDefinition("UPDATE public.inbound_document_line SET quantity=8 WHERE uid=@Uid",new { Uid=receipt.Line.Uid },cancellationToken:token)));
                changed=true;
            };
            var borrowed=fixture.Session.Transaction;
            var initial=(await fixture.Repository.GetTraceAsync(second.Uid))!;
            Assert.IsTrue(changed); Assert.AreSame(borrowed,fixture.Session.Transaction); Assert.HasCount(6,hook.Commands);
            Assert.AreEqual(6m,initial.AssociatedPurchaseQty); Assert.AreEqual(1m,initial.CurrentReceiptCoverageQty);
            var snapshot=mode=="repeatable-read";
            Assert.AreEqual(snapshot?7m:8m,initial.Purchases.Single().PurchaseLineReceivedQty);
            Assert.AreEqual(snapshot?0m:1m,initial.Purchases.Single().ReceiptOutsideCurrentCoverageQty);
            Assert.AreEqual(8m,await writer.ExecuteScalarAsync<decimal>("SELECT quantity FROM public.inbound_document_line WHERE uid=@Uid",new { Uid=receipt.Line.Uid }));
            var next=(await fixture.Repository.GetTraceAsync(second.Uid))!;
            Assert.AreEqual(snapshot?1m:2m,next.CurrentReceiptCoverageQty); Assert.AreEqual(0m,next.Purchases.Single().ReceiptOutsideCurrentCoverageQty);
            Assert.AreSame(borrowed,fixture.Session.Transaction);
        }
        finally { await EndCommittedCase(fixture,rows); }
    }

    /// <summary>Guard后独立连接建立指向坏历史头的新Link；RC后续可见而不重新Guard，下一次调用才拒绝；RR仍原scope。</summary>
    [TestMethod]
    [DataRow("none")] [DataRow("read-committed")] [DataRow("repeatable-read")]
    public async Task CrossConnection_InitialGuardScopeAndLaterLinksRemainIndependent(string mode)
    {
        using var fixture=new Fixture(); var rows=new CommittedTraceRows();
        await using var writer=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()); await writer.OpenAsync();
        try
        {
            var gross=fixture.Gross(); gross.UnitSnapshot="KG"; await AddCommittedSeed(fixture,rows,gross);
            var net=fixture.Net(gross,1); await AddCommittedSeed(fixture,rows,net);
            var original=await Target(fixture,gross,20m); rows.RegisterTable("public.supplier",original.Head.SupplierId); rows.Register(original.Head); rows.Register(original.Line);
            var later=await Target(fixture,gross,20m); rows.RegisterTable("public.supplier",later.Head.SupplierId); rows.Register(later.Head); rows.Register(later.Line);
            var link=fixture.Link(net,original.Head.Uid,original.Line.Uid,2m,0m); await AddCommittedSeed(fixture,rows,link);
            var originalReceipt=await Receipt(fixture,original.Line.Uid,1m); rows.Register(originalReceipt.Head); rows.Register(originalReceipt.Line);
            // 合法结构下旧修正引用使Guard不可信；该头开始时无任何范围Link，不能提前扩大scope。
            var badHead=new InboundDocument { Uid=Guid.NewGuid(),DocumentNo=fixture.Prefix+"LATER",DocumentType=(InboundDocumentTypeEnum)10,
                PostingStatus=(InboundPostingStatusEnum)1,ReceiptDate=new(2026,10,5),PostedDate=new(2026,10,5),WarehouseSiteId=Guid.NewGuid() };
            var badLine=new InboundDocumentLine { Uid=Guid.NewGuid(),InboundDocumentId=badHead.Uid,LineNumber=1,ProductId=Guid.NewGuid(),
                SourceLineId=later.Line.Uid,OriginalLineId=Guid.NewGuid(),Quantity=2m,UnitSnapshot="KG" };
            await AddCommittedSeed(fixture,rows,badHead); await AddCommittedSeed(fixture,rows,badLine);
            var newLink=fixture.Link(net,later.Head.Uid,later.Line.Uid,3m,0m); rows.Register(newLink);
            BeginCommittedReader(fixture,mode);
            Assert.AreNotEqual(writer.ProcessID,await fixture.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT pg_backend_pid()",transaction:fixture.Session.Transaction)));
            var hook=(HookConnection)fixture.Session.Connection; hook.Commands.Clear(); var changed=false;
            hook.Before=async(sql,token)=>
            {
                var header=sql.Contains("SELECT n.uid AS \"NetDemandId\"",StringComparison.Ordinal);
                if(!header) return; hook.Before=null;
                await InsertCommittedWriter(fixture,writer,newLink,token); changed=true;
            };
            var borrowed=fixture.Session.Transaction; var expectedNew=mode!="repeatable-read";
            {
                var result=(await fixture.Repository.GetTraceAsync(net.Uid))!;
                Assert.HasCount(expectedNew?2:1,result.Purchases); Assert.AreEqual(expectedNew?5m:2m,result.AssociatedPurchaseQty);
                Assert.AreEqual(expectedNew?3m:1m,result.CurrentReceiptCoverageQty);
            }
            Assert.IsTrue(changed); Assert.AreSame(borrowed,fixture.Session.Transaction);
            Assert.AreEqual(1,hook.Commands.Count(sql=>sql.Contains("SELECT EXISTS(SELECT 1 FROM public.inbound_document h",StringComparison.Ordinal)));
            if(expectedNew)
            {
                await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>fixture.Repository.GetTraceAsync(net.Uid));
                Assert.AreEqual(1,await fixture.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:borrowed)),"业务Guard信号不能中止调用方事务。");
            }
            else
            {
                Assert.HasCount(1,(await fixture.Repository.GetTraceAsync(net.Uid))!.Purchases);
            }
            Assert.AreSame(borrowed,fixture.Session.Transaction);
        }
        finally { await EndCommittedCase(fixture,rows); }
    }

    /// <summary>真实生产scope SQL在关系锁等待中取消，明确观察pg_blocking_pids和查询文本，而非测试pg_sleep。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task CrossConnection_ProductionScopeWaitCancellationReleasesResources(bool outer)
    {
        using var reader=new Fixture(outer);
        await using var holder=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()); await holder.OpenAsync();
        await using var observer=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()); await observer.OpenAsync();
        await using var held=await holder.BeginTransactionAsync(); using var cancellation=new CancellationTokenSource();
        var pid=await reader.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT pg_backend_pid()",transaction:reader.Session.Transaction));
        var borrowed=reader.Session.Transaction;
        await holder.ExecuteAsync("LOCK TABLE public.demand_execution_link IN ACCESS EXCLUSIVE MODE",transaction:held);
        Task pending=reader.Repository.GetTraceAsync(Guid.NewGuid(),cancellation.Token);
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(!await observer.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT cardinality(pg_blocking_pids(@Pid))>0",new { Pid=pid },cancellationToken:deadline.Token)))
                await Task.Delay(25,deadline.Token);
            var query=await observer.ExecuteScalarAsync<string>("SELECT query FROM pg_stat_activity WHERE pid=@Pid",new { Pid=pid });
            StringAssert.Contains(query,"target_line_id"); StringAssert.Contains(query,"public.demand_execution_link");
            cancellation.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(()=>pending); Assert.AreSame(borrowed,reader.Session.Transaction);
            if(outer)
            {
                var failure=await Assert.ThrowsAsync<PostgresException>(()=>reader.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:borrowed)));
                Assert.AreEqual("25P02",failure.SqlState);
            }
        }
        finally
        {
            cancellation.Cancel();
            try { await pending; } catch(OperationCanceledException) { /*先结束驱动任务再回滚持锁和读取事务。*/ }
            finally
            {
                try { if(borrowed is not null) { borrowed.Rollback(); borrowed.Dispose(); reader.Session.Transaction=null; } }
                finally { await held.RollbackAsync(); }
            }
        }
        Assert.AreEqual(1,await reader.Session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        foreach(var target in new[] { pid,holder.ProcessID })
            Assert.AreEqual(0L,await observer.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype IN ('relation','tuple','advisory')",new { Pid=target }));
        Assert.AreEqual(0L,await reader.Session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')"));
    }

    /// <summary>提交当前随机种子后由测试调用者选择读取隔离，仓储始终借用而不擅自创建/结束事务。</summary>
    private static void BeginCommittedReader(Fixture fixture,string mode)
    {
        var seed=fixture.Session.Transaction!; seed.Commit(); seed.Dispose(); fixture.Session.Transaction=null;
        if(mode!="none") fixture.Session.Transaction=fixture.Session.Connection.BeginTransaction(
            mode=="repeatable-read"?IsolationLevel.RepeatableRead:IsolationLevel.ReadCommitted);
    }

    /// <summary>在种子事务新增前先登记UID，种子若失败仍由finally回滚并只精确清理登记目标。</summary>
    private static async Task AddCommittedSeed<T>(Fixture fixture,CommittedTraceRows rows,T row) where T:BaseEntity
    { rows.Register(row); await fixture.InsertAsync(row); }

    /// <summary>另一物理连接直接按现有元数据提交随机Link；不经服务、序列、审计日志或公共写入协议。</summary>
    private static async Task InsertCommittedWriter<T>(Fixture fixture,NpgsqlConnection writer,T row,CancellationToken token) where T:BaseEntity
    {
        fixture.Context.AuditStamp.StampForInsert(row); var map=EntityMetadataCache.GetMetadata(typeof(T));
        var table=PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName);
        var columns=string.Join(',',map.Columns.Select(column=>PostgreSqlNames.Quote(column.ColumnName)));
        var values=string.Join(',',map.Columns.Select(column=>"@"+column.ParameterName));
        Assert.AreEqual(1,await writer.ExecuteAsync(new CommandDefinition($"INSERT INTO {table} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(row,map),cancellationToken:token)));
    }

    /// <summary>先结束读事务及连接，再精确删除本例提交夹具，避免快照/活动读取阻塞清理；不清理任何业务键范围。</summary>
    private static async Task EndCommittedCase(Fixture fixture,CommittedTraceRows rows)
    { try { fixture.Dispose(); } finally { await rows.RemoveAsync(); } }

    /// <summary>只保存本例实际实体表/UID和采购目标的随机供应商身份，按相反插入顺序事务删除，不做清库或DDL。</summary>
    private sealed class CommittedTraceRows
    {
        private readonly Dictionary<string,List<Guid>> _rows=new(StringComparer.Ordinal);

        /// <summary>从可信CLR元数据登记单实体精确身份；不从输入字符串猜表或删除条件。</summary>
        public void Register<T>(T row) where T:BaseEntity
        { var map=EntityMetadataCache.GetMetadata(typeof(T)); RegisterTable(PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName),row.Uid); }

        /// <summary>供应商由既有Target创建函数返回头SupplierId登记，调用方仅传内部固定表和本例随机身份。</summary>
        public void RegisterTable(string table,Guid id)
        { if(!_rows.TryGetValue(table,out var values)) _rows.Add(table,values=[]); values.Add(id); }

        /// <summary>新连接事务内只按登记uid删除，提交后逐表确认这些uid为零；现有任何未登记行均不匹配。</summary>
        public async Task RemoveAsync()
        {
            await using var connection=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()); await connection.OpenAsync();
            await using var transaction=await connection.BeginTransactionAsync();
            foreach(var entry in _rows.Reverse())
            {
                var parameters=PostgreSqlParameters.Ids(entry.Value.Distinct());
                await connection.ExecuteAsync($"DELETE FROM {entry.Key} WHERE uid=ANY(@Uids)",parameters,transaction:transaction);
            }
            await transaction.CommitAsync();
            foreach(var entry in _rows)
                Assert.AreEqual(0L,await connection.ExecuteScalarAsync<long>($"SELECT count(*) FROM {entry.Key} WHERE uid=ANY(@Uids)",PostgreSqlParameters.Ids(entry.Value.Distinct())));
        }
    }
}
