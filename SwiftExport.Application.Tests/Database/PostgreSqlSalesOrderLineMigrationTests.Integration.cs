using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>销售行真实PG用例只在获批故障库的随机RC事务内新增夹具并finally回滚，主控串行执行。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlSalesOrderLineMigrationIntegrationTests
{
    /// <summary>销售行Add保留请求UID及非标身份，Version/审计边界和普通/锁定完整读取。</summary>
    [TestMethod,DataRow(false,7),DataRow(true,0)]
    public Task RealPg_AddIdentityVersionAndAudit(bool empty,int version)=>Rollback(async f=>
    {
        await f.Insert(f.Head); var line=f.Line(1); var requested=line.Uid; line.Uid=empty?Guid.Empty:requested; line.Version=version; line.IsDeleted=true;
        line.CreatedDate=f.Day; line.CreatedBy="显式建立"; var write=await f.Repository.AddAsync(line);
        Assert.IsTrue(write.Success); Assert.AreEqual(empty?line.Uid:requested,write.Uid); Assert.AreNotEqual(Guid.Empty,line.Uid); Assert.AreEqual(version<=0?1:version,write.NewVersion);
        var actual=(await f.Repository.GetLinesByOrderIdAsync(f.Head.Uid,true)).Single(); Assert.AreEqual(line.Uid,actual.Uid); Assert.IsFalse(actual.IsDeleted);
        Assert.AreEqual(f.Day,actual.CreatedDate); Assert.AreEqual("显式建立",actual.CreatedBy); Assert.AreEqual(UserName,actual.UpdatedBy);
        Assert.AreEqual(line.Qty,actual.Qty); Assert.AreEqual(line.UnitSnapshot,actual.UnitSnapshot); Assert.AreEqual(line.ExpectedDeliveryDate,actual.ExpectedDeliveryDate);
        Assert.AreEqual(DateTimeKind.Unspecified,actual.UpdatedDate!.Value.Kind); Assert.AreEqual(1,await f.Scalar());
    });

    /// <summary>两行交换、三行循环及删除新增在正号安全区恢复，活动唯一索引和CHECK不放宽。</summary>
    [TestMethod,DataRow("swap"),DataRow("cycle"),DataRow("delete-add")]
    public Task RealPg_ReorderWithValidUniqueKeys(string mode)=>Rollback(async f=>
    {
        await f.Insert(f.Head); var originals=new[]{f.Line(1),f.Line(2),f.Line(3)}; foreach(var line in originals)await f.Repository.AddAsync(line);
        var before=(await f.Repository.GetLinesByOrderIdAsync(f.Head.Uid)).ToDictionary(l=>l.Uid);
        Assert.AreEqual(3,await f.Repository.StageLineNumbersAsync(f.Head.Uid,3)); var staged=(await f.Repository.GetLinesByOrderIdAsync(f.Head.Uid)).ToDictionary(l=>l.Uid);
        foreach(var original in originals) { Assert.AreEqual(original.LineNumber+7,staged[original.Uid].LineNumber); Assert.AreEqual(original.Version,staged[original.Uid].Version);
            Assert.AreEqual(before[original.Uid].UpdatedDate,staged[original.Uid].UpdatedDate); Assert.AreEqual(before[original.Uid].UpdatedBy,staged[original.Uid].UpdatedBy); }
        if(mode=="delete-add")
        {
            Assert.IsTrue((await f.Repository.DeleteLinesAsync([new() { Uid=originals[1].Uid,Version=1 }])).Single().Success);
            originals[0].LineNumber=2; originals[2].LineNumber=1; Assert.IsTrue((await f.Repository.UpdateLinesAsync([originals[0],originals[2]])).All(r=>r.Success));
            var added=f.Line(3); var addedUid=added.Uid; Assert.IsTrue((await f.Repository.AddLinesAsync([added])).Single().Success); Assert.AreEqual(addedUid,added.Uid);
            var history=await f.Raw(originals[1].Uid); Assert.IsTrue(history.IsDeleted); Assert.AreEqual(2,history.Version);
        }
        else
        {
            originals[0].LineNumber=2; originals[1].LineNumber=mode=="swap"?1:3; originals[2].LineNumber=mode=="swap"?3:1;
            Assert.IsTrue((await f.Repository.UpdateLinesAsync(originals)).All(r=>r.Success));
        }
        var actual=await f.Repository.GetLinesByOrderIdAsync(f.Head.Uid,true); CollectionAssert.AreEqual(new[]{1,2,3},actual.Select(l=>l.LineNumber).ToArray()); Assert.IsTrue(actual.All(l=>l.LineNumber>0));
        Assert.AreEqual(1,await f.Scalar());
    });

    /// <summary>上界相等/超1、空集合最大N门禁和软删高号；本地异常前无UPDATE，事务不会自行aborted。</summary>
    [TestMethod,DataRow(1073741822,2,false),DataRow(1073741822,3,true),DataRow(1073741823,2,true),
     DataRow(0,2,false),DataRow(0,int.MaxValue,true),DataRow(2,2,false)]
    public Task RealPg_StageMaximumBoundary(int maximum,int count,bool overflow)=>Rollback(async f=>
    {
        await f.Insert(f.Head); SalesOrderLine? active=null;
        if(maximum>0) { active=f.Line(maximum); await f.Repository.AddAsync(active); }
        // 比较Stage前实际落库审计值，避免把.NET 100ns与PG微秒存储差异误报为Stage审计修改。
        var before=active is null?null:await f.Raw(active.Uid);
        var deleted=f.Line(int.MaxValue); deleted.IsDeleted=true; await f.Insert(deleted);
        if(overflow) { var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.StageLineNumbersAsync(f.Head.Uid,count)); Assert.AreEqual("销售订单行号暂存超出 int 范围。",failure.Message); }
        else Assert.AreEqual(maximum==0?0:1,await f.Repository.StageLineNumbersAsync(f.Head.Uid,count));
        if(active is not null) { var actual=await f.Raw(active.Uid); Assert.AreEqual(overflow?maximum:(int)(2L*maximum+count+1),actual.LineNumber); Assert.AreEqual(1,actual.Version); Assert.AreEqual(before!.UpdatedDate,actual.UpdatedDate); Assert.AreEqual(before.UpdatedBy,actual.UpdatedBy); }
        Assert.AreEqual(int.MaxValue,(await f.Raw(deleted.Uid)).LineNumber); Assert.AreEqual(1,await f.Scalar());
    });

    /// <summary>按原版本更新与软删、继承Delete不推进版本的历史差异；旧版本/软删/缺失仍原并发返回。</summary>
    [TestMethod,DataRow("match"),DataRow("old"),DataRow("deleted"),DataRow("missing")]
    public Task RealPg_UpdateAndBatchDeletePredicates(string mode)=>Rollback(async f=>
    {
        await f.Insert(f.Head); var line=f.Line(1); line.Version=4; if(mode=="deleted")line.IsDeleted=true; if(mode!="missing")await f.Insert(line);
        var command=f.Line(1); command.Uid=line.Uid; command.Version=mode=="old"?3:4;
        var update=await f.Repository.UpdateAsync(command); Assert.AreEqual(mode=="match",update.Success); Assert.AreEqual(mode=="old"?4:5,command.Version); Assert.AreEqual(command.Version,update.OldVersion);
        var delete=(await f.Repository.DeleteLinesAsync([new() { Uid=line.Uid,Version=mode=="match"?5:4 }])).Single(); Assert.AreEqual(mode is "match" or "old",delete.Success);
        if(delete.Success) { var actual=await f.Raw(line.Uid); Assert.IsTrue(actual.IsDeleted); Assert.AreEqual(mode=="match"?6:5,actual.Version); Assert.AreEqual(UserName,actual.UpdatedBy); }
        else Assert.AreEqual("CONCURRENCY_CONFLICT",delete.ErrorCode);
        var other=f.Line(2); await f.Repository.AddAsync(other); var inherited=await f.Repository.DeleteAsync(other.Uid,1); Assert.IsTrue(inherited.Success); Assert.AreEqual(1,(await f.Raw(other.Uid)).Version);
    });

    /// <summary>整单软删只改当前有效行，返回0/数量并推进版本；软删历史不会再次修改。</summary>
    [TestMethod,DataRow(0),DataRow(3)]
    public Task RealPg_DeleteByOrderRetainsHistory(int count)=>Rollback(async f=>
    {
        await f.Insert(f.Head); var lines=new List<SalesOrderLine>(); for(var i=0;i<count;i++) { var line=f.Line(i+1); await f.Repository.AddAsync(line); lines.Add(line); }
        var deleted=f.Line(99); deleted.IsDeleted=true; deleted.Version=8; await f.Insert(deleted);
        Assert.AreEqual(count,await f.Repository.DeleteByOrderIdAsync(f.Head.Uid)); Assert.HasCount(0,await f.Repository.GetLinesByOrderIdAsync(f.Head.Uid));
        foreach(var line in lines) { var actual=await f.Raw(line.Uid); Assert.IsTrue(actual.IsDeleted); Assert.AreEqual(2,actual.Version); Assert.AreEqual(UserName,actual.UpdatedBy); }
        Assert.AreEqual(8,(await f.Raw(deleted.Uid)).Version); Assert.AreEqual(0,await f.Repository.DeleteByOrderIdAsync(f.Head.Uid));
    });

    /// <summary>调用者回滚单次头行变更后恢复既有号/版本/审计与删除状态；技术唯一失败也由调用者恢复事务。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public Task RealPg_HeadAndAllLineChangesRollback(bool uniqueFailure)=>Rollback(async f=>
    {
        await f.Insert(f.Head); var lines=new[]{f.Line(1),f.Line(2)}; foreach(var line in lines)await f.Repository.AddAsync(line);
        var original=await f.Raw(lines[0].Uid); var tx=(DbTransaction)f.Transaction!; tx.Save("before_command");
        var header=await f.Heads.GetByIdAsync(f.Head.Uid,true); header!.Remark="本轮修改"; Assert.IsTrue((await f.Heads.UpdateAsync(header)).Success);
        Assert.AreEqual(2,await f.Repository.StageLineNumbersAsync(f.Head.Uid,2)); Assert.IsTrue((await f.Repository.DeleteLinesAsync([new() { Uid=lines[1].Uid,Version=1 }])).Single().Success);
        lines[0].LineNumber=1; Assert.IsTrue((await f.Repository.UpdateLinesAsync([lines[0]])).Single().Success); var added=f.Line(2); await f.Repository.AddAsync(added);
        if(uniqueFailure) { var conflict=f.Line(1); var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Repository.AddAsync(conflict)); Assert.AreEqual("23505",error.SqlState); }
        else { var stale=f.Line(1); stale.Uid=lines[0].Uid; stale.Version=1; Assert.IsFalse((await f.Repository.UpdateAsync(stale)).Success); }
        // 测试调用者的保存点用于保留随机基线并检验恢复；最外层finally仍全事务回滚，不声称服务执行器已验收。
        tx.Rollback("before_command"); var restored=await f.Raw(lines[0].Uid); Assert.AreEqual(original.LineNumber,restored.LineNumber); Assert.AreEqual(original.Version,restored.Version);
        Assert.AreEqual(original.UpdatedDate,restored.UpdatedDate); Assert.AreEqual(original.UpdatedBy,restored.UpdatedBy);
        Assert.IsFalse((await f.Raw(lines[1].Uid)).IsDeleted); Assert.AreEqual(1,(await f.Heads.GetByIdAsync(f.Head.Uid))!.Version);
        Assert.IsNull(await f.Repository.GetByIdAsync(added.Uid)); Assert.AreEqual(1,await f.Scalar()); Assert.AreSame(f.Transaction,f.Session.Transaction);
    });

    /// <summary>真实MAX命令后精确同事务变更，Offset不刷新而UPDATE读取当前范围，增加/减少影响行数可观察。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task RealPg_StageUsesFrozenOffsetAndCurrentRange(bool remove)
    {
        using var factory=new StageFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()); using var session=new DbSession(factory);
        using var transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction=transaction;
        try {
            var f=new Fixture(session,transaction); await f.Insert(f.Head); var first=f.Line(1); var second=f.Line(2); await f.Repository.AddAsync(first); await f.Repository.AddAsync(second); var added=f.Line(3);
            factory.Connection.Stage=1; factory.Connection.Action=async()=> { if(remove)await f.Execute("UPDATE public.sales_order_line SET is_deleted=TRUE WHERE uid=@Uid",second.Uid); else await f.Repository.AddAsync(added); };
            Assert.AreEqual(remove?1:3,await f.Repository.StageLineNumbersAsync(f.Head.Uid,2)); Assert.AreEqual(6,(await f.Raw(first.Uid)).LineNumber);
            Assert.AreEqual(remove?2:7,(await f.Raw(second.Uid)).LineNumber); if(!remove)Assert.AreEqual(8,(await f.Raw(added.Uid)).LineNumber);
            Assert.AreSame(transaction,session.Transaction); Assert.AreEqual(1,await f.Scalar());
        }
        finally { try { transaction.Rollback(); } finally { session.Transaction=null; } }
    }

    /// <summary>无外层事务的只读/空Stage/空软删可连续调用；更新锁无事务拒绝，仓储不留下临时表或内部事务。</summary>
    [TestMethod]
    public async Task RealPg_NoOuterTransactionKeepsConnectionReusable()
    {
        using var session=Open(); var f=new Fixture(session,null); var missing=Guid.NewGuid(); Assert.HasCount(0,await f.Repository.GetLinesByOrderIdAsync(missing));
        Assert.AreEqual(0,await f.Repository.StageLineNumbersAsync(missing,2)); Assert.AreEqual(0,await f.Repository.DeleteByOrderIdAsync(missing));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetLinesByOrderIdAsync(missing,true)); Assert.IsNull(session.Transaction); Assert.AreEqual(1,await f.Scalar());
        Assert.AreEqual(0L,await session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')"));
    }

    private const string UserName="__pgtest_sales_line_real__";
    /// <summary>严格既有故障库门禁，不提供默认凭据。</summary>
    private static DbSession Open()=>new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
    /// <summary>所有随机夹具写入在调用者RC事务内，finally只回滚本例。</summary>
    private static async Task Rollback(Func<Fixture,Task> action)
    {
        using var session=Open(); using var tx=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction=tx;
        try { await action(new(session,tx)); Assert.AreSame(tx,session.Transaction); }
        finally { try { tx.Rollback(); } finally { session.Transaction=null; } }
    }
    /// <summary>本例实体/元数据及两仓储共享同会话，不依赖已有数据或主控销售头新文件同步。</summary>
    private sealed class Fixture
    {
        public DbSession Session { get; } public IDbTransaction? Transaction { get; } public PostgreSqlSalesOrderLineRepository Repository { get; }
        public PostgreSqlDapperRepository<SalesOrderHeader> Heads { get; } public DateTime Day { get; }=new(2026,10,5,9,10,11);
        public SalesOrderHeader Head { get; }=new() { OrderNo="PGLINE"+Guid.NewGuid().ToString("N"),CustomerId=Guid.NewGuid() };
        /// <summary>构造现有上下文，仓储不拥有本事务。</summary>
        public Fixture(DbSession session,IDbTransaction? transaction)
        { Session=session;Transaction=transaction; var user=new User(); var context=new RepositoryBaseContext(session,NullLoggerFactory.Instance,new AuditStampService(user),user); Repository=new(context);Heads=new(context); }
        /// <summary>正行号与合法业务数量，随机UID只用于本例。</summary>
        public SalesOrderLine Line(int number)=>new() { SalesOrderId=Head.Uid,OrderNo=Head.OrderNo,LineNumber=number,IsNonStandard=true,Sku="PGLINE"+Guid.NewGuid().ToString("N"),Qty=12.3456m,UnitSnapshot="PCS",ExpectedDeliveryDate=Day };
        /// <summary>按现有元数据插入合法历史软删值，不绕约束或触发器。</summary>
        public async Task Insert<T>(T entity) where T:BaseEntity
        {
            var metadata=EntityMetadataCache.GetMetadata(typeof(T)); var table=PostgreSqlNames.Quote(metadata.SchemaName)+"."+PostgreSqlNames.Quote(metadata.TableName);
            var columns=string.Join(',',metadata.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName))); var values=string.Join(',',metadata.Columns.Select(c=>"@"+c.ParameterName));
            Assert.AreEqual(1,await Session.Connection.ExecuteAsync($"INSERT INTO {table} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(entity,metadata),transaction:Transaction));
        }
        /// <summary>原始历史实体读取，包括软删状态；只限定本例UID。</summary>
        public Task<SalesOrderLine> Raw(Guid uid)
        {
            var metadata=EntityMetadataCache.GetMetadata(typeof(SalesOrderLine)); var projection=string.Join(',',metadata.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)+" AS "+PostgreSqlNames.Quote(c.PropertyName)));
            return Session.Connection.QuerySingleAsync<SalesOrderLine>($"SELECT {projection} FROM public.sales_order_line WHERE uid=@Uid",new{Uid=uid},transaction:Transaction);
        }
        /// <summary>只执行本例固定语句及随机UID。</summary>
        public Task<int> Execute(string sql,Guid uid)=>Session.Connection.ExecuteAsync(sql,new{Uid=uid},transaction:Transaction);
        /// <summary>确认调用者事务/连接仍可用。</summary>
        public Task<int> Scalar()=>Session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:Session.Transaction);
    }
    /// <summary>仅用于随机夹具审计。</summary>
    private sealed class User:ICurrentUser { public Guid? UserId=>null; public string UserName=>PostgreSqlSalesOrderLineMigrationIntegrationTests.UserName; public bool IsAuthenticated=>false; }
    /// <summary>测试专用真实连接包装器只在命令执行边界加只读等待，不改业务SQL、参数或事务，不用于运行接线。</summary>
    private sealed class StageFactory : IDbConnectionFactory, IDisposable
    {
        public NpgsqlConnection Connection { get; }
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        /// <summary>仅由严格门禁提供连接串。</summary>
        public StageFactory(string connectionString) => Connection = new(connectionString);
        /// <summary>返回既有包装连接，真实连接由DbSession打开。</summary>
        public IDbConnection CreateConnection() => Connection;
        /// <summary>释放包装器的真实连接。</summary>
        public void Dispose() => Connection.Dispose();

        /// <summary>类型名使Dapper识别Npgsql数组能力；所有实际操作仍委托真实驱动。</summary>
        public sealed class NpgsqlConnection(string connectionString) : DbConnection
        {
            private readonly Npgsql.NpgsqlConnection inner = new(connectionString);
            public int? Stage { get; set; }
            public Func<Task>? Action { get; set; }
            public int ReadCommands { get; private set; }
            [AllowNull] public override string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value; }
            public override string Database => inner.Database;
            public override string DataSource => inner.DataSource;
            public override string ServerVersion => inner.ServerVersion;
            public override ConnectionState State => inner.State;
            /// <summary>委托打开，不另建连接。</summary>
            public override void Open() => inner.Open();
            /// <summary>仅生命周期关闭。</summary>
            public override void Close() => inner.Close();
            /// <summary>禁止测试切库。</summary>
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            /// <summary>返回调用者拥有的真实事务。</summary>
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => inner.BeginTransaction(isolationLevel);
            /// <summary>仅包装真实命令的执行边界。</summary>
            protected override DbCommand CreateDbCommand() => new StageCommand(this, inner.CreateCommand());
            /// <summary>精确Stage边界先关闭观察再同事务自写；不改业务SQL或模拟查询结果。</summary>
            private async Task Before()
            {
                if (!Stage.HasValue) return;
                if (ReadCommands++ != Stage.Value) return;
                Stage = null;
                if (Action is not null) await Action();
            }
            /// <summary>只释放真实连接，保留调用者事务恢复策略。</summary>
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }

            /// <summary>真实参数/SQL/事务完全委托，只在命令边界施加本例同事务变更。</summary>
            private sealed class StageCommand(NpgsqlConnection owner, DbCommand innerCommand) : DbCommand
            {
                [AllowNull] public override string CommandText { get => innerCommand.CommandText; set => innerCommand.CommandText = value; }
                public override int CommandTimeout { get => innerCommand.CommandTimeout; set => innerCommand.CommandTimeout = value; }
                public override CommandType CommandType { get => innerCommand.CommandType; set => innerCommand.CommandType = value; }
                public override bool DesignTimeVisible { get => innerCommand.DesignTimeVisible; set => innerCommand.DesignTimeVisible = value; }
                public override UpdateRowSource UpdatedRowSource { get => innerCommand.UpdatedRowSource; set => innerCommand.UpdatedRowSource = value; }
                protected override DbConnection? DbConnection { get => owner; set { if (value != owner) throw new InvalidOperationException("测试不能切换连接"); } }
                protected override DbTransaction? DbTransaction { get => innerCommand.Transaction; set => innerCommand.Transaction = value; }
                protected override DbParameterCollection DbParameterCollection => innerCommand.Parameters;
                protected override DbParameter CreateDbParameter() => innerCommand.CreateParameter();
                /// <summary>取消交给真实命令。</summary>
                public override void Cancel() => innerCommand.Cancel();
                /// <summary>准备交给真实命令。</summary>
                public override void Prepare() => innerCommand.Prepare();
                /// <summary>夹具写入交给真实命令。</summary>
                public override int ExecuteNonQuery() => innerCommand.ExecuteNonQuery();
                /// <summary>标量交给真实命令。</summary>
                public override object? ExecuteScalar() => innerCommand.ExecuteScalar();
                /// <summary>同步读交给真实命令。</summary>
                protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => innerCommand.ExecuteReader(behavior);
                /// <summary>阶段启用时观察真实写入边界；回调先关闭阶段再写本例夹具。</summary>
                public override async Task<int> ExecuteNonQueryAsync(CancellationToken token)
                { await owner.Before(); return await innerCommand.ExecuteNonQueryAsync(token); }
                /// <summary>标量读取使用真实驱动与相同令牌。</summary>
                public override async Task<object?> ExecuteScalarAsync(CancellationToken token)
                { await owner.Before(); return await innerCommand.ExecuteScalarAsync(token); }
                /// <summary>集合读取使用真实驱动，不重写业务SQL。</summary>
                protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
                { await owner.Before(); return await innerCommand.ExecuteReaderAsync(behavior, token); }
                /// <summary>只释放当前命令。</summary>
                protected override void Dispose(bool disposing) { if (disposing) innerCommand.Dispose(); base.Dispose(disposing); }
            }
        }
    }
}
