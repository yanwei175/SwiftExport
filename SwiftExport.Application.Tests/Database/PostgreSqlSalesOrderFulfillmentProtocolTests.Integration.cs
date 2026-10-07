using System.Data;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.DatabaseSchema;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控履约协议partial复用真实夹具，只提交本例三类随机身份并精确清理，不修改生产或共享测试框架。</summary>
public sealed partial class PostgreSqlSalesOrderFulfillmentMigrationIntegrationTests
{
    /// <summary>冻结交付事实后另一连接原子替换Gross，无事务/RC重选新身份，RR保持原调用方快照；不刷新旧行事实。</summary>
    [TestMethod,DataRow("none"),DataRow("read-committed"),DataRow("repeatable-read")]
    public async Task Protocol_CrossConnectionReselectsGrossAfterFrozenFacts(string mode)
    {
        using var factory=new StageFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());using var session=new DbSession(factory);
        using var seed=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);session.Transaction=seed;
        var fixture=new Fixture(session,seed);fixture.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认;
        var original=fixture.Gross();var replacement=fixture.Gross();replacement.Version=5;
        await using var writer=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());await writer.OpenAsync();
        try
        {
            await fixture.Seed(original);seed.Commit();session.Transaction=null;
            if(mode!="none")session.Transaction=session.Connection.BeginTransaction(mode=="repeatable-read"?IsolationLevel.RepeatableRead:IsolationLevel.ReadCommitted);
            var borrowed=session.Transaction;var pid=await session.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()",transaction:borrowed);
            Assert.AreNotEqual(pid,writer.ProcessID);var changed=false;
            factory.Connection.Stage=3;factory.Connection.Action=async()=>
            {
                await using var transaction=await writer.BeginTransactionAsync();
                Assert.AreEqual(1,await writer.ExecuteAsync("UPDATE public.gross_demand SET is_deleted=TRUE WHERE uid=@Uid",new{original.Uid},transaction));
                var metadata=EntityMetadataCache.GetMetadata(typeof(GrossDemand));
                var columns=string.Join(',',metadata.Columns.Select(column=>PostgreSqlNames.Quote(column.ColumnName)));
                var values=string.Join(',',metadata.Columns.Select(column=>"@"+column.ParameterName));
                Assert.AreEqual(1,await writer.ExecuteAsync($"INSERT INTO public.gross_demand ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(replacement,metadata),transaction));
                await transaction.CommitAsync();changed=true;
            };
            var detail=(await fixture.Repository.GetFulfillmentAsync(fixture.Header.Uid))!;
            Assert.IsTrue(changed);Assert.AreSame(borrowed,session.Transaction);var line=detail.Lines.Single();
            var snapshot=mode=="repeatable-read";Assert.AreEqual(snapshot?original.Uid:replacement.Uid,line.GrossDemandId);
            Assert.AreEqual(snapshot?1:5,line.GrossDemandVersion);Assert.AreEqual("已关联",line.SourceState);
            Assert.AreEqual(0m,line.ShippedQty);Assert.AreEqual(10m,line.UnshippedQty);Assert.AreEqual(10m,line.OrderedQty);
            Assert.HasCount(0,line.Purchases);Assert.AreEqual("未关联",line.PurchaseState);
            Assert.AreEqual(5,await writer.ExecuteScalarAsync<int>("SELECT version FROM public.gross_demand WHERE uid=@Uid",new{replacement.Uid}));
            Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:borrowed));
        }
        finally{await FinishProtocolCase(session,fixture,[original.Uid,replacement.Uid]);}
    }

    /// <summary>真正首条或采购生产SELECT在独立关系锁上等待后取消；观察阻塞PID/查询文本，不以测试pg_sleep代替生产等待。</summary>
    [TestMethod,DataRow(0,false),DataRow(0,true),DataRow(4,false),DataRow(4,true)]
    public async Task Protocol_ProductionQueryLockWaitCancellationReleasesResources(int stage,bool outer)
    {
        using var factory=new StageFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());using var session=new DbSession(factory);
        using var seed=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);session.Transaction=seed;
        var fixture=new Fixture(session,seed);fixture.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认;var gross=fixture.Gross();
        await using var holder=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());await holder.OpenAsync();
        await using var observer=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());await observer.OpenAsync();
        await using var held=await holder.BeginTransactionAsync();using var cancellation=new CancellationTokenSource();Task? pending=null;var holderReleased=false;
        try
        {
            await fixture.Seed(gross);seed.Commit();session.Transaction=null;
            if(outer)session.Transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
            var borrowed=session.Transaction;var pid=await session.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()",transaction:borrowed);
            Assert.AreNotEqual(pid,holder.ProcessID);Assert.AreNotEqual(pid,observer.ProcessID);
            var table=stage==0?"public.sales_order_header":"public.net_demand";
            factory.Connection.Stage=stage;factory.Connection.Action=()=>holder.ExecuteAsync($"LOCK TABLE {table} IN ACCESS EXCLUSIVE MODE",transaction:held);
            pending=fixture.Repository.GetFulfillmentAsync(fixture.Header.Uid,cancellation.Token);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(!await observer.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT @Holder=ANY(pg_blocking_pids(@Pid))",new{Holder=holder.ProcessID,Pid=pid},cancellationToken:deadline.Token)))
                await Task.Delay(25,deadline.Token);
            var query=await observer.ExecuteScalarAsync<string>("SELECT query FROM pg_stat_activity WHERE pid=@Pid",new{Pid=pid})
                ??throw new InvalidOperationException("等待中的履约连接必须可观测。");
            // 实例仅保留前1024字节查询；采购目标表在尾部，须由实际relation等待OID核验，不能要求被截断文本含尾部。
            if(stage==0)StringAssert.Contains(query,"sales_order_header");Assert.IsFalse(query.Contains("pg_sleep",StringComparison.Ordinal));
            Assert.IsTrue(await observer.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Pid AND locktype='relation' AND relation=CAST(@Table AS regclass) AND NOT granted)",new{Pid=pid,Table=table}));
            if(stage==4)StringAssert.Contains(query,"PurchaseOrderId");
            cancellation.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>pending);
            Assert.AreSame(borrowed,session.Transaction);
            if(outer)
            {
                var failure=await Assert.ThrowsAsync<PostgresException>(()=>session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:borrowed));
                Assert.AreEqual("25P02",failure.SqlState);borrowed!.Rollback();borrowed.Dispose();session.Transaction=null;
            }
            await held.RollbackAsync();holderReleased=true;
            Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
            foreach(var target in new[]{pid,holder.ProcessID})
                Assert.AreEqual(0L,await observer.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pg_locks WHERE pid=@Pid AND locktype IN ('relation','tuple','advisory')",new{Pid=target}));
            Assert.AreEqual(0L,await session.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')"));
        }
        finally
        {
            cancellation.Cancel();try{if(pending is not null)await pending;}catch(OperationCanceledException){/*先结束生产命令，再释放事务和夹具。*/}
            finally
            {
                if(session.Transaction is not null){session.Transaction.Rollback();session.Transaction.Dispose();session.Transaction=null;}
                // 已完成事务仍可能保留Connection引用，独立标记避免二次Rollback阻断精确夹具清理。
                try{if(!holderReleased)await held.RollbackAsync();}
                finally{await FinishProtocolCase(session,fixture,[gross.Uid]);}
            }
        }
    }

    /// <summary>先释放读取连接/事务，再事务内逆顺序精确删除本例Gross/行/头，并确认对应UID零残留；不改任何未登记数据。</summary>
    private static async Task FinishProtocolCase(DbSession session,Fixture fixture,Guid[] grossIds)
    {
        if(session.Transaction is not null){session.Transaction.Rollback();session.Transaction.Dispose();session.Transaction=null;}
        session.Dispose();
        await using var connection=new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("DELETE FROM public.gross_demand WHERE uid=ANY(@Uids)",PostgreSqlParameters.Ids(grossIds),transaction);
        await connection.ExecuteAsync("DELETE FROM public.sales_order_line WHERE uid=@Uid",new{fixture.Line.Uid},transaction);
        await connection.ExecuteAsync("DELETE FROM public.sales_order_header WHERE uid=@Uid",new{fixture.Header.Uid},transaction);
        await transaction.CommitAsync();
        Assert.AreEqual(0L,await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.gross_demand WHERE uid=ANY(@Uids)",PostgreSqlParameters.Ids(grossIds)));
        Assert.AreEqual(0L,await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.sales_order_line WHERE uid=@Uid",new{fixture.Line.Uid}));
        Assert.AreEqual(0L,await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.sales_order_header WHERE uid=@Uid",new{fixture.Header.Uid}));
    }
}
