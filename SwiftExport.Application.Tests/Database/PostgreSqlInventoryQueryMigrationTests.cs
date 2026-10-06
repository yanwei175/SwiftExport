using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Inventory.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>五个实际Dapper入口的离线映射、阶段差异、参数及生命周期验证，不解释库存SQL算法。</summary>
[TestClass]
public sealed partial class PostgreSqlInventoryQueryMigrationTests
{
    /// <summary>所有DTO字段真实Dapper还原，分页不依据页行重新计算count，父事务可空且不结束。</summary>
    [TestMethod,DataRow(0,false),DataRow(0,true),DataRow(1,false),DataRow(1,true),DataRow(2,false),DataRow(2,true),DataRow(3,false),DataRow(3,true),DataRow(4,false),DataRow(4,true)]
    public async Task Offline_FiveMethodsMapAndBorrow(int method,bool transactional)
    {
        using var f=new ReadFixture(transactional);var expected=method is 0 or 2?(object)Sample<InventoryBalanceDto>():method is 1 or 3?Sample<InventoryLedgerItemDto>():Sample<InventoryReconciliationDto>();
        f.Connection.Rows=Rows(expected);using var cts=new CancellationTokenSource();var uid=Guid.NewGuid();object? actual=method switch
        {0=>(await f.Repository.SearchBalancesAsync(new(),cts.Token)).Items.Single(),1=>(await f.Repository.SearchLedgerAsync(new(),cts.Token)).Items.Single(),2=>await f.Repository.GetBalanceDtoAsync(uid,cts.Token),3=>(await f.Repository.GetLedgerByBalanceAsync(uid,1,20,cts.Token)).Items.Single(),_=>await f.Repository.GetReconciliationAsync(uid,cts.Token)};
        Assert.IsNotNull(actual);foreach(var p in expected.GetType().GetProperties()){Assert.AreEqual(p.GetValue(expected),p.GetValue(actual),p.Name);StringAssert.Contains(f.Connection.Sql,"AS \""+p.Name+"\"");}
        Assert.AreEqual(method is 0 or 1 or 3?2:1,f.Connection.Commands);Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreSame(f.Transaction,f.Connection.LastTransaction);Assert.AreEqual(cts.Token,f.Connection.Token);
        Assert.AreEqual(0,f.Connection.Closes);Assert.AreEqual(0,f.Connection.EndTransactions);Assert.IsTrue(f.Connection.Reader!.IsClosed);
        if(method is 0 or 1 or 3){Assert.AreEqual(default,f.Connection.Tokens[0]);Assert.AreEqual(cts.Token,f.Connection.Tokens[1]);}
        foreach(var sql in f.Connection.Sqls)foreach(var forbidden in new[]{"FOR UPDATE","CREATE ","DROP ","UPDATE ","INSERT ","DELETE "})Assert.IsFalse(sql.Contains(forbidden,StringComparison.Ordinal));
        if(method is 1 or 3)
        {
            Assert.IsFalse(f.Connection.Sql.Contains("is_deleted",StringComparison.Ordinal));
            StringAssert.Contains(f.Connection.Sql,"get_byte(uuid_send(t.uid),10)");
            Assert.IsFalse(f.Connection.Sqls[0].Contains("rv.",StringComparison.Ordinal));Assert.IsFalse(f.Connection.Sqls[0].Contains("rp.",StringComparison.Ordinal));
        }
        if(method==0){StringAssert.Contains(f.Connection.Sql,"get_byte(uuid_send(i.uid),10)");StringAssert.Contains(f.Connection.Sql,"ASC NULLS FIRST");}
        if(method==3)Assert.IsFalse(f.Connection.Sql.Contains("i.is_deleted",StringComparison.Ordinal));
    }

    /// <summary>默认页大小仅两个搜索回落50，按余额流水直接clamp到1；index至少1且上限200。</summary>
    [TestMethod,DataRow(0,0,50),DataRow(1,-3,50),DataRow(2,0,1),DataRow(2,-3,1),DataRow(0,999,200),DataRow(1,999,200),DataRow(2,999,200),DataRow(0,1,1)]
    public async Task Offline_PagingDifferences(int method,int inputSize,int expectedSize)
    {
        using var f=new ReadFixture(false);if(method==0){var result=await f.Repository.SearchBalancesAsync(new(){PageIndex=-1,PageSize=inputSize});Assert.AreEqual(37,result.TotalCount);Assert.AreEqual(expectedSize,result.PageSize);Assert.AreEqual(1,result.PageIndex);}
        else if(method==1){var result=await f.Repository.SearchLedgerAsync(new(){PageIndex=-1,PageSize=inputSize});Assert.AreEqual(expectedSize,result.PageSize);Assert.AreEqual(37,result.TotalCount);}
        else{var result=await f.Repository.GetLedgerByBalanceAsync(Guid.Empty,-1,inputSize);Assert.AreEqual(expectedSize,result.PageSize);Assert.AreEqual(37,result.TotalCount);}
        Assert.AreEqual(expectedSize,f.Connection.Values["PageSize"]);Assert.AreEqual(0,f.Connection.Values["Offset"]);Assert.AreEqual(2,f.Connection.Commands);
    }

    /// <summary>count后数据库结果变化可使总数与页行不同，源不是冻结工作集；仓储不C#截页。</summary>
    [TestMethod,DataRow(0),DataRow(1),DataRow(3)]
    public async Task Offline_CountThenChangedPage(int method)
    {
        using var f=new ReadFixture(true);f.Connection.Before=_=>{if(f.Connection.Commands==2)f.Connection.Rows=method==0?Rows(Sample<InventoryBalanceDto>()):Rows(Sample<InventoryLedgerItemDto>());return Task.CompletedTask;};
        if(method==0){var result=await f.Repository.SearchBalancesAsync(new(){PageIndex=8,PageSize=2});Assert.AreEqual(37,result.TotalCount);Assert.HasCount(1,result.Items);}
        else if(method==1){var result=await f.Repository.SearchLedgerAsync(new(){PageIndex=8,PageSize=2});Assert.AreEqual(37,result.TotalCount);Assert.HasCount(1,result.Items);}
        else{var result=await f.Repository.GetLedgerByBalanceAsync(Guid.NewGuid(),8,2);Assert.AreEqual(37,result.TotalCount);Assert.HasCount(1,result.Items);}
        Assert.AreEqual(14,f.Connection.Values["Offset"]);Assert.AreEqual(2,f.Connection.Commands);Assert.AreEqual(0,f.Connection.EndTransactions);
    }

    /// <summary>完整字面反斜线筛选和墙上时间，保留源上界SQL加一天/原含时分，不转自然日或CLR加日。</summary>
    [TestMethod]
    public async Task Offline_FilterTypesAndBounds()
    {
        using var f=new ReadFixture(false);var date=new DateTime(2026,10,5,13,14,15,DateTimeKind.Utc);var warehouse=Guid.NewGuid();var supplier=Guid.NewGuid();
        await f.Repository.SearchLedgerAsync(new(){SupplierId=supplier,WarehouseSiteId=warehouse,TransactionType=(InventoryTransactionTypeEnum)900,Keyword="  a\\%_[  ",SourceDocumentNo="  %  ",OccurredDateFrom=date,OccurredDateTo=DateTime.MaxValue});
        Assert.AreEqual("%a\\\\\\%\\_\\[%",f.Connection.Values["Keyword"]);Assert.AreEqual("%\\%%",f.Connection.Values["SourceDocumentNo"]);
        Assert.AreEqual(supplier,f.Connection.Values["SupplierId"]);Assert.AreEqual(warehouse,f.Connection.Values["WarehouseSiteId"]);Assert.AreEqual(900,f.Connection.Values["TransactionType"]);
        Assert.AreEqual(DateTime.SpecifyKind(date,DateTimeKind.Unspecified),f.Connection.Values["OccurredDateFrom"]);Assert.AreEqual(DateTime.MaxValue,f.Connection.Values["OccurredDateTo"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp,f.Connection.Types["OccurredDateTo"]);Assert.AreEqual(NpgsqlDbType.Uuid,f.Connection.Types["SupplierId"]);Assert.AreEqual(NpgsqlDbType.Integer,f.Connection.Types["TransactionType"]);
        StringAssert.Contains(f.Connection.Sql,"t.occurred_date<@OccurredDateTo + INTERVAL '1 day'");StringAssert.Contains(f.Connection.Sql,"ESCAPE E'\\\\'");
        StringAssert.Contains(f.Connection.Sql,"rv.reversal_of_ledger_id=t.uid");StringAssert.Contains(f.Connection.Sql,"rp.replacement_of_ledger_id=t.uid");Assert.IsFalse(f.Connection.Sql.Contains("DISTINCT",StringComparison.Ordinal));
    }

    /// <summary>nullablebool空值不限制，余额只排自身软删，主数据不擅加过滤或产品名称回落。</summary>
    [TestMethod,DataRow(null),DataRow(false),DataRow(true)]
    public async Task Offline_BalanceNullableFilter(bool? nonzero)
    {
        using var f=new ReadFixture(false);await f.Repository.SearchBalancesAsync(new(){NonZeroOnly=nonzero,Keyword=" \t "});
        Assert.AreEqual(nonzero.HasValue?(object)nonzero.Value:DBNull.Value,f.Connection.Values["NonZeroOnly"]);Assert.AreEqual(DBNull.Value,f.Connection.Values["Keyword"]);Assert.AreEqual(NpgsqlDbType.Boolean,f.Connection.Types["NonZeroOnly"]);
        foreach(var forbidden in new[]{"w.is_deleted","w.is_active","s.is_deleted","s.is_active","p.sku","p.description","COALESCE(i.sku"})Assert.IsFalse(f.Connection.Sql.Contains(forbidden,StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Sql,"i.on_hand_qty AS \"AvailableQty\"");StringAssert.Contains(f.Connection.Sql,"p.is_deleted=FALSE");
    }

    /// <summary>可空名称/单位/引用保持NULL，不补空字符串/Guid；缺余额与对账为NULL，缺Trace行为空但count仍独立。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task Offline_NullFieldsAndMissing(bool ledger)
    {
        using var f=new ReadFixture(false);object expected=ledger?Sample<InventoryLedgerItemDto>():Sample<InventoryBalanceDto>();foreach(var p in expected.GetType().GetProperties())if(p.PropertyType==typeof(string)||Nullable.GetUnderlyingType(p.PropertyType)!=null)p.SetValue(expected,null);
        f.Connection.Rows=Rows(expected);object? actual=ledger?(await f.Repository.SearchLedgerAsync(new())).Items.Single():await f.Repository.GetBalanceDtoAsync(Guid.Empty);
        foreach(var p in expected.GetType().GetProperties())Assert.AreEqual(p.GetValue(expected),p.GetValue(actual),p.Name);
        f.Connection.Rows=new();Assert.IsNull(await f.Repository.GetBalanceDtoAsync(Guid.NewGuid()));Assert.IsNull(await f.Repository.GetReconciliationAsync(Guid.NewGuid()));
    }

    /// <summary>预取消分页仍执行不带token的count，再取消页；单对象查询不执行，准确保持源差异。</summary>
    [TestMethod,DataRow(0),DataRow(1),DataRow(2),DataRow(3),DataRow(4)]
    public async Task Offline_PreCancellationBoundary(int method)
    {
        using var f=new ReadFixture(true);await Assert.ThrowsAsync<OperationCanceledException>(()=>Read(f,method,new CancellationToken(true)));
        Assert.AreEqual(method is 0 or 1 or 3?1:0,f.Connection.Commands);Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreEqual(0,f.Connection.EndTransactions);
    }

    /// <summary>仅行阶段异步等待可被调用令牌取消，技术失败原样抛；这是ADO证据，不是服务器锁等待。</summary>
    [TestMethod,DataRow(0),DataRow(1),DataRow(2),DataRow(3),DataRow(4)]
    public async Task Offline_ExecutingCancellationAndFailure(int method)
    {
        using var f=new ReadFixture(false);using var cts=new CancellationTokenSource();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var count=method is 0 or 1 or 3?2:1;
        f.Connection.Before=async token=>{if(f.Connection.Commands!=count)return;entered.SetResult();await Task.Delay(Timeout.Infinite,token);};var pending=Read(f,method,cts.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cts.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>pending);
        using var broken=new ReadFixture(true);var error=new InvalidOperationException("查询故障");broken.Connection.Before=_=>throw error;Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>Read(broken,method,default)));
    }

    /// <summary>统一测试调用形状，保持真实方法的参数/返回，不模拟计算。</summary>
    private static Task Read(ReadFixture f,int method,CancellationToken token)=>method switch{0=>f.Repository.SearchBalancesAsync(new(),token),1=>f.Repository.SearchLedgerAsync(new(),token),2=>f.Repository.GetBalanceDtoAsync(Guid.NewGuid(),token),3=>f.Repository.GetLedgerByBalanceAsync(Guid.NewGuid(),1,20,token),_=>f.Repository.GetReconciliationAsync(Guid.NewGuid(),token)};
    /// <summary>丰富原始字段样本，数值刻意不协调，验证仓储不重新计算对账或Available。</summary>
    private static T Sample<T>() where T:new()
    {
        var item=new T();foreach(var p in typeof(T).GetProperties()){var t=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;object value=t==typeof(Guid)?Guid.NewGuid():t==typeof(string)?" 原快照 ":t==typeof(DateTime)?new DateTime(2026,10,5,12,34,56):t==typeof(decimal)?12.3456m:t==typeof(long)?1234567890123L:t==typeof(bool)?true:t.IsEnum?Enum.ToObject(t,900):7;p.SetValue(item,value);}return item;
    }
    /// <summary>提供真正ADO列结构，不解释SQL或业务CASE。</summary>
    private static DataTable Rows(object row)
    {
        var table=new DataTable();var props=row.GetType().GetProperties();foreach(var p in props){var t=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;table.Columns.Add(p.Name,t.IsEnum?typeof(int):t);}table.Rows.Add(props.Select(p=>{var v=p.GetValue(row);return v is Enum?Convert.ToInt32(v):v??DBNull.Value;}).ToArray());return table;
    }
    /// <summary>独占无网络夹具，不修改或共享旧测试的可变结果集。</summary>
    private sealed class ReadFixture : IDisposable
    {
        public ReadConnection Connection { get; } = new();
        public DbSession Session { get; }
        public DbTransaction? Transaction { get; }
        public PostgreSqlInventoryQueryRepository Repository { get; }
        /// <summary>构建既有上下文和调用者可空事务。</summary>
        public ReadFixture(bool transactional)
        {
            Session = new(new ReadFactory(Connection)); if (transactional) Session.Transaction = Transaction = Connection.BeginTransaction();
            var user = new WorkspaceUser(); Repository = new(new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        /// <summary>夹具结束才释放会话。</summary>
        public void Dispose() => Session.Dispose();
    }

    /// <summary>记录实际命令/参数，脚本结果不解释SQL。</summary>
    private sealed class ReadConnection : DbConnection
    {
        public List<string> Sqls=[]; public List<CancellationToken> Tokens=[]; public string Sql = ""; public DataTable Rows = new(); public DbDataReader? Reader;
        public Dictionary<string, object?> Values = []; public Dictionary<string, NpgsqlDbType> Types = [];
        public DbTransaction? LastTransaction; public CancellationToken Token;
        public int Commands; public int Begins; public int EndTransactions; public int Closes;
        public Func<CancellationToken, Task>? Before;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline";
        public override string DataSource => "none";
        public override string ServerVersion => "script";
        public override ConnectionState State => ConnectionState.Open;
        /// <summary>没有网络打开。</summary>
        public override void Open() { }
        /// <summary>记录越权关闭。</summary>
        public override void Close() => Closes++;
        /// <summary>禁止切库。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>记录调用者事务。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new ReadTransaction(this); }
        /// <summary>建立真实Dapper可执行的命令。</summary>
        protected override DbCommand CreateDbCommand() => new ReadCommand(this);
    }

    /// <summary>仅维护事务身份及结束计数。</summary>
    private sealed class ReadTransaction(ReadConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        /// <summary>记录提交。</summary>
        public override void Commit() => connection.EndTransactions++;
        /// <summary>记录回滚。</summary>
        public override void Rollback() => connection.EndTransactions++;
        /// <summary>记录释放。</summary>
        protected override void Dispose(bool disposing) { if (disposing) connection.EndTransactions++; base.Dispose(disposing); }
    }

    /// <summary>参数使用真实Npgsql容器；只读执行由本例脚本提供。</summary>
    private sealed class ReadCommand(ReadConnection connection) : DbCommand
    {
        private readonly NpgsqlCommand parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        /// <summary>取消由传入令牌控制。</summary>
        public override void Cancel() { }
        /// <summary>无需提供者准备。</summary>
        public override void Prepare() { }
        /// <summary>禁止写入。</summary>
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        /// <summary>本切片没有标量调用。</summary>
        public override object? ExecuteScalar() => throw new NotSupportedException();
        /// <summary>真实count重载记录无令牌阶段，返回独立总数，不解释SQL。</summary>
        public override async Task<object?> ExecuteScalarAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); connection.Commands++; connection.Sql=CommandText; connection.Sqls.Add(CommandText); connection.Token=token; connection.Tokens.Add(token); connection.LastTransaction=DbTransaction; connection.Values=parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>(object?)p.Value); connection.Types=parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>p.NpgsqlDbType); if(connection.Before is not null)await connection.Before(token); return 37; }
        /// <summary>禁止丢失令牌的同步reader路径。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        /// <summary>捕获参数和令牌，等待/异常仅注入测试命令边界。</summary>
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); connection.Commands++; connection.Sql = CommandText; connection.Sqls.Add(CommandText); connection.Tokens.Add(token); connection.Token = token; connection.LastTransaction = DbTransaction;
            connection.Values = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value);
            connection.Types = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => p.NpgsqlDbType);
            if (connection.Before is not null) await connection.Before(token);
            return connection.Reader = connection.Rows.CreateDataReader();
        }
        /// <summary>只释放当前参数容器。</summary>
        protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>不读取配置或连接网络，错误分类沿现有PG实现。</summary>
    private sealed class ReadFactory(ReadConnection connection) : IDbConnectionFactory
    {
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        /// <summary>返回本例连接。</summary>
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>夹具审计身份。</summary>
    private sealed class WorkspaceUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_gross_workspace_detail__";
        public bool IsAuthenticated => false;
    }
}

