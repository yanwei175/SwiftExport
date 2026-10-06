using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过真实驱动命令边界验证冻结关系及调用方事务；包装器只放在测试，不注入生产钩子。</summary>
public sealed partial class PostgreSqlSalesOrderDetailMigrationIntegrationTests
{
    /// <summary>每一阶段真实执行前改动本例事实，验证候选/交付冻结和商业头行重新观察，SQL仍在真实PG执行。</summary>
    [TestMethod]
    public async Task StagedReads_PreserveFrozenEvidenceAndObserveCurrentCommercialRows()
    {
        using var connection = new ObservedConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        using var session = new DbSession(new ObservedFactory(connection));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
        var fixture = new Fixture(session,transaction); fixture.Header.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        var gross = fixture.Gross(); var head = fixture.PackingHead(); var packing = fixture.PackingLine(head,gross);
        try
        {
            await fixture.Seed(gross); await fixture.Add(head); await fixture.Add(packing);
            connection.Observe(async stage =>
            {
                if (stage == 2)
                    await connection.Native.ExecuteAsync("UPDATE public.sales_order_header SET order_no=@OrderNo,version=2,confirmation_status=0 WHERE uid=@Uid",
                        new { Uid=fixture.Header.Uid,OrderNo="阶段后单号" },transaction:transaction);
                if (stage == 3)
                    await connection.Native.ExecuteAsync("UPDATE public.packing_list_line SET qty=8 WHERE uid=@Uid",new { Uid=packing.Uid },transaction:transaction);
                if (stage == 4)
                    await connection.Native.ExecuteAsync("UPDATE public.sales_order_line SET qty=8,sku='阶段后SKU' WHERE uid=@Uid",new { Uid=fixture.Line.Uid },transaction:transaction);
            });
            var result = (await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!;
            Assert.AreEqual(4,connection.Executions); Assert.AreEqual("阶段后单号",result.Header.OrderNo);
            Assert.AreEqual(2,result.Header.Version); Assert.AreEqual(ConfirmationStatusEnum.待确认,result.Header.ConfirmationStatus);
            var line = result.Lines.Single(); Assert.AreEqual("阶段后SKU",line.Sku); Assert.AreEqual(8m,line.Qty);
            Assert.AreEqual(4m,line.ShippedQty); Assert.AreEqual(6m,line.UnshippedQty); Assert.IsNull(line.ReviewReason);
            Assert.AreSame(transaction,session.Transaction); Assert.AreEqual(1,connection.Begins);
            Assert.AreEqual(8m,await connection.Native.ExecuteScalarAsync<decimal>("SELECT qty FROM public.packing_list_line WHERE uid=@Uid",new { Uid=packing.Uid },transaction:transaction));
        }
        finally { transaction.Rollback(); session.Transaction=null; }
    }

    /// <summary>事实后删除头，第三阶段为空但第四阶段仍完整执行；返回null不使调用方事务失效。</summary>
    [TestMethod]
    public async Task HeaderBecomesDeleted_AfterFactsStillBuffersLinesAndKeepsOuterTransaction()
    {
        using var connection = new ObservedConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        using var session = new DbSession(new ObservedFactory(connection));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction=transaction;
        var fixture=new Fixture(session,transaction);
        try
        {
            await fixture.Add(fixture.Header); await fixture.Add(fixture.Line);
            connection.Observe(async stage =>
            {
                if (stage==3) await connection.Native.ExecuteAsync("UPDATE public.sales_order_header SET is_deleted=TRUE WHERE uid=@Uid",new { Uid=fixture.Header.Uid },transaction:transaction);
            });
            Assert.IsNull(await fixture.Repository.GetDetailAsync(fixture.Header.Uid));
            Assert.AreEqual(4,connection.Executions); Assert.AreSame(transaction,session.Transaction);
            Assert.AreEqual(1,await connection.Native.ExecuteScalarAsync<int>("SELECT 1",transaction:transaction));
            Assert.AreEqual(0L,await connection.Native.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')",transaction:transaction));
        }
        finally { transaction.Rollback(); session.Transaction=null; }
    }

    /// <summary>后续阶段命令开始前取消，Driver必须返回取消而不清理调用方事务；该命令未在服务器执行。</summary>
    [TestMethod]
    [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public async Task CancellationBeforeLaterStage_ReturnsCancellationWithUsableOuterTransaction(int stage)
    {
        using var connection=new ObservedConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        using var session=new DbSession(new ObservedFactory(connection));
        using var transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction=transaction;
        using var cancellation=new CancellationTokenSource(); var fixture=new Fixture(session,transaction);
        try
        {
            await fixture.Add(fixture.Header); await fixture.Add(fixture.Line);
            connection.Observe(index => { if(index==stage) cancellation.Cancel(); return Task.CompletedTask; });
            await Assert.ThrowsAsync<OperationCanceledException>(()=>fixture.Repository.GetDetailAsync(fixture.Header.Uid,cancellation.Token));
            Assert.AreEqual(stage,connection.Executions); Assert.AreSame(transaction,session.Transaction);
            Assert.AreEqual(1,await connection.Native.ExecuteScalarAsync<int>("SELECT 1",transaction:transaction));
        }
        finally { transaction.Rollback(); session.Transaction=null; }
    }

    /// <summary>真实服务器等待中取消；无事务连接复用，已有PG事务由调用者回滚，仓储不得暗自恢复/降隔离。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task CancellationDuringRealWait_LeavesTransactionOwnershipToCaller(bool outer)
    {
        using var reader=Open(); using var holder=Open(); using var observer=Open();
        using var held=holder.Connection.BeginTransaction(IsolationLevel.ReadCommitted); holder.Transaction=held;
        using var borrowed=outer?reader.Connection.BeginTransaction(IsolationLevel.ReadCommitted):null; reader.Transaction=borrowed;
        using var cancellation=new CancellationTokenSource();
        await holder.Connection.ExecuteAsync("LOCK TABLE public.sales_order_line IN ACCESS EXCLUSIVE MODE",transaction:held);
        var pending=new Fixture(reader,borrowed).Repository.GetDetailAsync(Guid.NewGuid(),cancellation.Token);
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var pid=((NpgsqlConnection)reader.Connection).ProcessID;
            while(!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT cardinality(pg_blocking_pids(@Pid))>0",new { Pid=pid },cancellationToken:deadline.Token)))
                await Task.Delay(25,deadline.Token);
            cancellation.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(()=>pending);
            Assert.AreSame(borrowed,reader.Transaction);
            if(outer)
            {
                var exception=await Assert.ThrowsAsync<PostgresException>(()=>reader.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:borrowed));
                Assert.AreEqual("25P02",exception.SqlState);
            }
        }
        finally
        {
            cancellation.Cancel();
            try { await pending; } catch(OperationCanceledException) { /*先收完真实驱动任务，再释放调用方和持锁方事务。*/ }
            finally
            {
                try { borrowed?.Rollback(); } finally { reader.Transaction=null; held.Rollback(); holder.Transaction=null; }
            }
        }
        Assert.AreEqual(1,await reader.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        Assert.AreEqual(0L,await reader.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')"));
        foreach(var pid in new[] { ((NpgsqlConnection)reader.Connection).ProcessID,((NpgsqlConnection)holder.Connection).ProcessID })
            Assert.AreEqual(0L,await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype IN ('relation','tuple','advisory')",new { Pid=pid }));
    }

    /// <summary>生产会话仍使用真实PG分类器；只把创建连接替换为命令边界观察包装器。</summary>
    private sealed class ObservedFactory(ObservedConnection connection):IDbConnectionFactory
    {
        public IDbConnection CreateConnection()=>connection;
        public IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;
    }

    /// <summary>代理真实Npgsql连接，阶段动作在上一读取已完整缓冲释放后、下一命令执行前运行。</summary>
    private sealed class ObservedConnection(string connectionString):DbConnection
    {
        public NpgsqlConnection Native { get; }=new(connectionString);
        public int Executions { get; private set; }
        public int Begins { get; private set; }
        private Func<int,Task>? _before;
        [AllowNull] public override string ConnectionString { get=>Native.ConnectionString; set=>Native.ConnectionString=value!; }
        public override string Database=>Native.Database;
        public override string DataSource=>Native.DataSource;
        public override string ServerVersion=>Native.ServerVersion;
        public override ConnectionState State=>Native.State;
        public override void Open()=>Native.Open();
        public override void Close()=>Native.Close();
        public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException("协议夹具不切换数据库。");

        /// <summary>种子执行完成后重置阶段编号，只记录真实公开方法产生的命令。</summary>
        public void Observe(Func<int,Task> before) { Executions=0; _before=before; }

        /// <summary>由测试调用方创建的真实事务原样返回，仓储新增事务会反映在计数。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return Native.BeginTransaction(isolationLevel); }
        protected override DbCommand CreateDbCommand()=>new ObservedCommand(this,Native.CreateCommand());
        protected override void Dispose(bool disposing) { if(disposing) Native.Dispose(); base.Dispose(disposing); }

        /// <summary>只串行执行测试动作，不改变SQL、参数、取消令牌或提供者结果。</summary>
        private Task BeforeAsync() => _before is null?Task.CompletedTask:_before(++Executions);

        /// <summary>代理真实命令所有参数和事务，ExecuteReader前的明确屏障不使用延时猜测。</summary>
        private sealed class ObservedCommand(ObservedConnection connection,DbCommand native):DbCommand
        {
            [AllowNull] public override string CommandText { get=>native.CommandText; set=>native.CommandText=value!; }
            public override int CommandTimeout { get=>native.CommandTimeout; set=>native.CommandTimeout=value; }
            public override CommandType CommandType { get=>native.CommandType; set=>native.CommandType=value; }
            public override bool DesignTimeVisible { get=>native.DesignTimeVisible; set=>native.DesignTimeVisible=value; }
            public override UpdateRowSource UpdatedRowSource { get=>native.UpdatedRowSource; set=>native.UpdatedRowSource=value; }
            protected override DbConnection? DbConnection { get=>connection; set { if(value is not null && !ReferenceEquals(value,connection)) throw new InvalidOperationException("夹具不得换连接。"); } }
            protected override DbTransaction? DbTransaction { get=>native.Transaction; set=>native.Transaction=value; }
            protected override DbParameterCollection DbParameterCollection=>native.Parameters;
            protected override DbParameter CreateDbParameter()=>native.CreateParameter();
            public override void Cancel()=>native.Cancel();
            public override void Prepare()=>native.Prepare();
            public override int ExecuteNonQuery()=>native.ExecuteNonQuery();
            public override object? ExecuteScalar()=>native.ExecuteScalar();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>native.ExecuteReader(behavior);
            public override Task<int> ExecuteNonQueryAsync(CancellationToken token)=>native.ExecuteNonQueryAsync(token);
            public override Task<object?> ExecuteScalarAsync(CancellationToken token)=>native.ExecuteScalarAsync(token);

            /// <summary>每个阶段都仍返回真实驱动读取器；释放、类型映射和SQL失败由Npgsql/Dapper处理。</summary>
            protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token)
            { await connection.BeforeAsync(); return await native.ExecuteReaderAsync(behavior,token); }
            protected override void Dispose(bool disposing) { if(disposing) native.Dispose(); base.Dispose(disposing); }
        }
    }
}
