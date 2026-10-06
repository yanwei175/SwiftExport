using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实公开入口/Dapper离线验证分组参数、完整详情投影及事务令牌；不模拟PG聚合算法。</summary>
[TestClass]
public sealed partial class PostgreSqlGrossDemandWorkspaceDetailMigrationTests
{
    /// <summary>生命周期去重顺序保留、unknown枚举不擅自过滤，明确integer[]绑定不依赖Dapper提供者猜测。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task Offline_GroupsBindDistinctTypedArrayAndMap(bool transactional)
    {
        using var f = new ReadFixture(transactional); var source = Guid.NewGuid();
        f.Connection.Rows = GroupRows(source, "SO", " RAW ", 3);
        var statuses = new List<GrossDemandLifecycleStatusEnum> { (GrossDemandLifecycleStatusEnum)8, (GrossDemandLifecycleStatusEnum)0,
            (GrossDemandLifecycleStatusEnum)8, (GrossDemandLifecycleStatusEnum)777 };
        using var cts = new CancellationTokenSource();
        var rows = await f.Repository.SearchSourceGroupsAsync(new() { LifecycleStatuses = statuses }, cts.Token);
        CollectionAssert.AreEqual(new[] { 8, 0, 777 }, (int[])f.Connection.Values["SourceLifecycleStatuses"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Integer, f.Connection.Types["SourceLifecycleStatuses"]);
        Assert.HasCount(4, statuses); var row = rows.Single(); Assert.AreEqual(source, row.SourceUid);
        Assert.AreEqual("SO", row.SourceType); Assert.AreEqual(" RAW ", row.SourceNo); Assert.AreEqual(3, row.DemandCount);
        StringAssert.Contains(f.Connection.Sql, "COUNT(1)::integer"); StringAssert.Contains(f.Connection.Sql, "GROUP BY d.source_uid");
        StringAssert.Contains(f.Connection.Sql, "MAX(d.source_type COLLATE public.erp_dictionary_ci)");
        StringAssert.Contains(f.Connection.Sql, "MAX(d.source_no COLLATE public.erp_dictionary_ci)");
        Assert.IsFalse(f.Connection.Sql.Contains("JOIN", StringComparison.Ordinal)); Assert.IsFalse(f.Connection.Sql.Contains("ROW_NUMBER", StringComparison.Ordinal));
        AssertBorrowed(f, cts.Token);
    }

    /// <summary>源空/null列表不限制状态，查询仍执行；空组集合正常返回，不转换为预取消短路。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task Offline_EmptyStatusListDoesNotRestrict(bool nullList)
    {
        using var f = new ReadFixture(false);
        var rows = await f.Repository.SearchSourceGroupsAsync(new() { LifecycleStatuses = nullList ? null! : [] });
        Assert.HasCount(0, rows); Assert.HasCount(0, f.Connection.Values);
        Assert.IsFalse(f.Connection.Sql.Contains("lifecycle_status=", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Sql, "d.is_deleted=FALSE"); AssertBorrowed(f, default);
    }

    /// <summary>NULL请求先抛参数异常，即使已取消，不发查询；这是源方法的入口优先级。</summary>
    [TestMethod]
    public async Task Offline_NullRequestKeepsSourcePriority()
    {
        using var f = new ReadFixture(false);
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Repository.SearchSourceGroupsAsync(null!, new CancellationToken(true)));
        Assert.AreEqual(0, f.Connection.Commands);
    }

    /// <summary>完整DTO的所有字段经真实Dapper映射，包含nullable版本/客户/产品、业务快照及墙上时间；SQL不修剪原值。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task Offline_DetailMapsEveryFieldAndBorrowsTransaction(bool transactional)
    {
        using var f = new ReadFixture(transactional); var expected = DetailSample(); f.Connection.Rows = DetailRows(expected);
        using var cts = new CancellationTokenSource(); var result = await f.Repository.GetWorkspaceDetailAsync(expected.Uid, cts.Token);
        Assert.IsNotNull(result);
        foreach (var property in typeof(GrossDemandDetailDto).GetProperties())
            Assert.AreEqual(property.GetValue(expected), property.GetValue(result), property.Name);
        Assert.AreEqual(expected.Uid, f.Connection.Values["Uid"]); Assert.HasCount(1, f.Connection.Values);
        Assert.IsFalse(f.Connection.Sql.Contains("trim(", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(f.Connection.Sql.Contains("COALESCE", StringComparison.Ordinal)); Assert.IsFalse(f.Connection.Sql.Contains("JOIN", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Sql.Contains("lifecycle_status=", StringComparison.Ordinal)); AssertBorrowed(f, cts.Token);
        Assert.AreEqual(DateTimeKind.Unspecified, result.CreatedDate!.Value.Kind);
    }

    /// <summary>NULL字段和原单位空字符串/ASCII边界/Unicode空白不被补值或规范化。</summary>
    [TestMethod, DataRow(null), DataRow(""), DataRow(" PCS "), DataRow("\t\u3000")]
    public async Task Offline_DetailPreservesNullsAndRawUnit(string? unit)
    {
        using var f = new ReadFixture(false); var expected = DetailSample(); expected.Unit = unit!;
        expected.SourceLineNumber = null; expected.SourceVersion = null; expected.CustomerId = null; expected.ProductId = null;
        expected.LinkSku = null!; expected.CustomerSku = null!; expected.Description = null!; expected.Color = null!;
        expected.Specification = null!; expected.ImagePath = null!; expected.Remark = null!;
        expected.RequiredDate = null; expected.LifecycleChangedAt = null; expected.LifecycleChangedBy = null!; expected.LifecycleChangedReason = null!;
        expected.CreatedDate = null; expected.CreatedBy = null!; expected.UpdatedDate = null; expected.UpdatedBy = null!;
        f.Connection.Rows = DetailRows(expected); var result = await f.Repository.GetWorkspaceDetailAsync(expected.Uid);
        foreach (var property in typeof(GrossDemandDetailDto).GetProperties()) Assert.AreEqual(property.GetValue(expected), property.GetValue(result), property.Name);
    }

    /// <summary>缺失详情/空Uid由数据库结果决定，仓储不增加业务早返，单次读结束后reader释放。</summary>
    [TestMethod]
    public async Task Offline_MissingDetailAndEmptyUidStillExecute()
    {
        using var f = new ReadFixture(true);
        Assert.IsNull(await f.Repository.GetWorkspaceDetailAsync(Guid.Empty));
        Assert.AreEqual(Guid.Empty, f.Connection.Values["Uid"]); AssertBorrowed(f, default);
    }

    /// <summary>两个入口预取消均传播并不访问ADO执行，不开始/结束事务。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task Offline_PreCancellation(bool transactional)
    {
        using var f = new ReadFixture(transactional); var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Repository.SearchSourceGroupsAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Repository.GetWorkspaceDetailAsync(Guid.NewGuid(), token));
        Assert.AreEqual(0, f.Connection.Commands); Assert.AreEqual(0, f.Connection.EndTransactions);
    }

    /// <summary>等待取消与技术失败由实际异步ADO执行传播，不伪装为空/业务未知；借用事务保持不变。</summary>
    [TestMethod, DataRow(false, false), DataRow(false, true), DataRow(true, false), DataRow(true, true)]
    public async Task Offline_ExecutingCancellationOrFailure(bool groups, bool cancellation)
    {
        using var f = new ReadFixture(true); using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("执行失败");
        f.Connection.Before = async token => { if (!cancellation) throw error; entered.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        Task read = groups ? f.Repository.SearchSourceGroupsAsync(new(), cts.Token) : f.Repository.GetWorkspaceDetailAsync(Guid.NewGuid(), cts.Token);
        if (cancellation)
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cts.Cancel();
            Assert.AreEqual(cts.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => read)).CancellationToken);
        }
        else Assert.AreSame(error, await Assert.ThrowsAsync<InvalidOperationException>(() => read));
        AssertBorrowed(f, cts.Token);
    }

    /// <summary>检验一次只读命令所有权，无连接关闭、事务结束、锁、DDL或活动reader。</summary>
    private static void AssertBorrowed(ReadFixture fixture, CancellationToken token)
    {
        Assert.AreEqual(1, fixture.Connection.Commands); Assert.AreSame(fixture.Transaction, fixture.Connection.LastTransaction);
        Assert.AreSame(fixture.Transaction, fixture.Session.Transaction); Assert.AreEqual(token, fixture.Connection.Token);
        Assert.AreEqual(0, fixture.Connection.Closes); Assert.AreEqual(0, fixture.Connection.EndTransactions);
        Assert.AreEqual(fixture.Transaction is null ? 0 : 1, fixture.Connection.Begins);
        Assert.IsTrue(fixture.Connection.Reader is null || fixture.Connection.Reader.IsClosed);
        foreach (var forbidden in new[] { "SELECT *", "CREATE ", "DROP ", "FOR UPDATE", "HOLDLOCK", "LIKE" })
            Assert.IsFalse(fixture.Connection.Sql.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
    }

    /// <summary>输入DTO全字段用于离线投影，不读取生产private实现。</summary>
    private static GrossDemandDetailDto DetailSample() => new()
    {
        Uid = Guid.NewGuid(), Version = 7, LifecycleStatus = (GrossDemandLifecycleStatusEnum)8, SourceType = "SO",
        SourceUid = Guid.NewGuid(), SourceNo = " RAW ", SourceLineId = Guid.NewGuid(), SourceLineNumber = 4, SourceVersion = 5,
        CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), IsNonStandard = false, Sku = "SKU", LinkSku = " LINK ",
        CustomerSku = "C-SKU", Description = "说明", Color = "颜色", Specification = "规格", ImagePath = "图片",
        Unit = " PCS ", DemandQty = 12.3456m, RequiredDate = new(2026, 9, 3, 10, 11, 12), Remark = "备注",
        LifecycleChangedAt = new(2026, 9, 4, 13, 14, 15), LifecycleChangedBy = "状态人", LifecycleChangedReason = "原因",
        CreatedDate = new(2026, 9, 1, 8, 9, 10), CreatedBy = "建立人", UpdatedDate = new(2026, 9, 2, 11, 12, 13), UpdatedBy = "修改人"
    };

    /// <summary>只反射公开DTO列，日期/枚举保持真实Dapper输入类型，不反射仓储方法。</summary>
    private static DataTable DetailRows(GrossDemandDetailDto dto)
    {
        var table = new DataTable(); var values = new List<object>();
        foreach (var property in typeof(GrossDemandDetailDto).GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var value = property.GetValue(dto); if (type.IsEnum) { type = typeof(int); value = Convert.ToInt32(value); }
            table.Columns.Add(property.Name, type); values.Add(value ?? DBNull.Value);
        }
        table.Rows.Add(values.ToArray()); return table;
    }

    /// <summary>提供明确分组四列，聚合真实语义留给实库验收。</summary>
    private static DataTable GroupRows(Guid uid, string type, string no, int count)
    {
        var table = new DataTable(); table.Columns.Add("SourceUid", typeof(Guid)); table.Columns.Add("SourceType", typeof(string));
        table.Columns.Add("SourceNo", typeof(string)); table.Columns.Add("DemandCount", typeof(int)); table.Rows.Add(uid, type, no, count); return table;
    }

    /// <summary>独占无网络夹具，不修改或共享旧测试的可变结果集。</summary>
    private sealed class ReadFixture : IDisposable
    {
        public ReadConnection Connection { get; } = new();
        public DbSession Session { get; }
        public DbTransaction? Transaction { get; }
        public PostgreSqlGrossDemandQueryRepository Repository { get; }
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
        public string Sql = ""; public DataTable Rows = new(); public DbDataReader? Reader;
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
        /// <summary>禁止丢失令牌的同步reader路径。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        /// <summary>捕获参数和令牌，等待/异常仅注入测试命令边界。</summary>
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); connection.Commands++; connection.Sql = CommandText; connection.Token = token; connection.LastTransaction = DbTransaction;
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
