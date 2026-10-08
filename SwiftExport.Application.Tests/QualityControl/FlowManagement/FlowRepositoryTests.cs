using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.QualityControl.FlowManagement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.QualityControl.FlowManagement;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.QualityControl.FlowManagement;

/// <summary>记录真实 Dapper 命令及读取映射，验证事务、取消、参数和头行一致投影；不会连接 PostgreSQL。</summary>
[TestClass]
public sealed class FlowRepositoryTests
{
    /// <summary>真实仓储新增、更新与软删使用同一事务，保留预期版本、审计及取消令牌。</summary>
    [TestMethod]
    public async Task WritesCarryVersionAuditTransactionAndCancellation()
    {
        using var f = new Fixture();
        using var token = new CancellationTokenSource();
        using var transaction = f.Session.Connection.BeginTransaction();
        f.Session.Transaction = transaction;
        var header = new FlowHeader { MaterialId = Guid.NewGuid(), SkuSnapshot = "SKU-A", TestDate = new(2026, 10, 8) };
        var oldId = header.Uid;
        var inserted = await f.Repository.AddHeaderAsync(header, token.Token);
        Assert.IsTrue(inserted.Success);
        Assert.AreNotEqual(oldId, inserted.Uid);
        StringAssert.Contains(f.Connection.Text, "INSERT INTO \"public\".\"flow_header\"");
        Assert.AreEqual("flow-repository-test", f.Connection.Values["CreatedBy"]);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)f.Connection.Values["TestDate"]!).Kind);
        Assert.AreSame(transaction, f.Connection.LastTransaction);
        Assert.AreEqual(token.Token, f.Connection.LastToken);
        f.Connection.Results = [VersionTable(2)];
        var updated = await f.Repository.UpdateHeaderAsync(header, token.Token);
        Assert.IsTrue(updated.Success);
        Assert.AreEqual(1, updated.OldVersion);
        Assert.AreEqual(2, header.Version);
        Assert.AreEqual(1, f.Connection.Values["ExpectedVersion"]);
        StringAssert.Contains(f.Connection.Text, "version=@ExpectedVersion AND is_deleted=FALSE RETURNING version");
        var deleted = await f.Repository.DeleteHeaderAsync(header.Uid, 2, token.Token);
        Assert.IsTrue(deleted.Success);
        StringAssert.Contains(f.Connection.Text, "SET is_deleted=TRUE");
        Assert.AreEqual(2, f.Connection.Values["Version"]);
        Assert.AreSame(transaction, f.Connection.LastTransaction);
    }

    /// <summary>重复 Uid 别名按头行分段映射，一次查询准确返回多行、零值、空测量点和历史名称。</summary>
    [TestMethod]
    public async Task JoinedDetailMapsHeaderAndAllLinesInOneCommand()
    {
        using var f = new Fixture();
        var header = new FlowHeader { MaterialId = Guid.NewGuid(), SkuSnapshot = "OLD-SKU", Version = 7, TestDate = new(2026, 10, 8) };
        var first = new FlowLine { HeaderId = header.Uid, FunctionId = Guid.NewGuid(), FunctionNameSnapshot = "历史功能", LimiterSpecificationId = Guid.NewGuid(), LimiterSpecificationNameSnapshot = "不限流", Flow1BarLMin = 0 };
        var second = new FlowLine { HeaderId = header.Uid, FunctionId = Guid.NewGuid(), FunctionNameSnapshot = "第二功能", LimiterSpecificationId = Guid.NewGuid(), LimiterSpecificationNameSnapshot = "1.8GPM", Flow100PsiGpm = 1.5m };
        var table = EntityTable<FlowHeader>("h");
        AppendColumns<FlowLine>(table, "l");
        table.Rows.Add(Values(header).Concat(Values(first)).ToArray());
        table.Rows.Add(Values(header).Concat(Values(second)).ToArray());
        f.Connection.Results = [table];
        var detail = await f.Repository.GetDetailAsync(header.Uid, default);
        Assert.IsNotNull(detail);
        Assert.AreEqual(header.Uid, detail.Uid);
        Assert.AreEqual(7, detail.Version);
        Assert.AreEqual("OLD-SKU", detail.SkuSnapshot);
        Assert.AreEqual(2, detail.Lines.Count);
        Assert.AreEqual(0m, detail.Lines[0].Flow1BarLMin);
        Assert.IsNull(detail.Lines[0].Flow20PsiGpm);
        Assert.AreEqual(1.5m, detail.Lines[1].Flow100PsiGpm);
        StringAssert.Contains(f.Connection.Text, "LEFT JOIN public.flow_line");
        Assert.IsFalse(f.Connection.Text.Contains("sys_dict", StringComparison.Ordinal));
        Assert.AreEqual(1, f.Connection.Commands);
    }

    /// <summary>维护读取要求事务，锁行 SQL、字典参数及物料查询都绑定实际取消令牌。</summary>
    [TestMethod]
    public async Task MaintenanceReadsRequireAndBorrowTransaction()
    {
        using var f = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Repository.ReadLinesAsync(Guid.NewGuid(), default));
        Assert.AreEqual(0, f.Connection.Commands);
        using var transaction = f.Session.Connection.BeginTransaction();
        f.Session.Transaction = transaction;
        using var token = new CancellationTokenSource();
        f.Connection.Results = [EntityTable<FlowLine>("l")];
        await f.Repository.ReadLinesAsync(Guid.NewGuid(), token.Token);
        StringAssert.Contains(f.Connection.Text, "ORDER BY l.uid FOR UPDATE");
        Assert.AreSame(transaction, f.Connection.LastTransaction);
        Assert.AreEqual(token.Token, f.Connection.LastToken);
        f.Connection.Results = [EntityTable<SwiftExport.Core.Entities.Systems.Dictionaries.Data.SysDictData>("d")];
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await f.Repository.ReadDictionaryItemsAsync(ids, token.Token);
        StringAssert.Contains(f.Connection.Text, "d.uid=ANY(@Uids)");
        StringAssert.Contains(f.Connection.Text, "FOR SHARE OF d");
        CollectionAssert.AreEquivalent(ids, (Guid[])f.Connection.Values["Uids"]!);
    }

    /// <summary>真实分页执行两个结果集，稳定排序和分页参数不会因日期或 SKU 条件失效。</summary>
    [TestMethod]
    public async Task PagingReadsBothResultSetsAndStableOrder()
    {
        using var f = new Fixture();
        var count = VersionTable(3);
        var rows = EntityTable<FlowHeader>("h");
        rows.Rows.Add(Values(new FlowHeader { MaterialId = Guid.NewGuid(), SkuSnapshot = "SKU", TestDate = new(2026, 10, 8) }));
        f.Connection.Results = [count, rows];
        var result = await f.Repository.GetPagedAsync(new FlowSearchRequest { PageIndex = 4, PageSize = 2, SkuSnapshot = "%'probe" }, default);
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(6L, f.Connection.Values["Offset"]);
        Assert.AreEqual(2, f.Connection.Values["Limit"]);
        Assert.AreEqual("%'probe", f.Connection.Values["Sku"]);
        StringAssert.Contains(f.Connection.Text, "ORDER BY h.test_date DESC,h.uid DESC LIMIT @Limit OFFSET @Offset");
        Assert.IsFalse(f.Connection.Text.Contains("%'probe", StringComparison.Ordinal));
    }

    private static DataTable VersionTable(int version)
    {
        var table = new DataTable();
        table.Columns.Add("xVersion", typeof(int));
        table.Rows.Add(version);
        return table;
    }
    private static DataTable EntityTable<T>(string prefix) where T : BaseEntity
    { var table = new DataTable(); AppendColumns<T>(table, prefix); return table; }
    private static void AppendColumns<T>(DataTable table, string prefix) where T : BaseEntity
    {
        foreach (var column in EntityMetadataCache.GetMetadata<T>().Columns)
            table.Columns.Add(prefix + column.PropertyName, Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType);
    }
    private static object[] Values<T>(T entity) where T : BaseEntity => EntityMetadataCache.GetMetadata<T>().Columns
        .Select(x => x.Property.GetValue(entity) ?? DBNull.Value).ToArray();

    /// <summary>使用真实会话及审计服务，但将底层连接替换为完全离线记录连接。</summary>
    private sealed class Fixture : IDisposable
    {
        public RecordingConnection Connection { get; } = new();
        public DbSession Session { get; }
        public PostgreSqlFlowRepository Repository { get; }
        /// <summary>组装真实仓储依赖，底层执行由离线连接接管。</summary>
        public Fixture()
        {
            Session = new(new Factory(Connection));
            var user = new User();
            Repository = new(new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>提供审计身份。</summary>
    private sealed class User : ICurrentUser
    { public string UserName => "flow-repository-test"; public Guid? UserId => Guid.NewGuid(); public bool IsAuthenticated => true; }
    /// <summary>创建记录连接，不访问网络。</summary>
    private sealed class Factory(RecordingConnection connection) : IDbConnectionFactory
    {
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>记录 SQL、驱动参数、事务和取消令牌，结果集由测试提供。</summary>
    private sealed class RecordingConnection : DbConnection
    {
        public string Text = "";
        public Dictionary<string, object?> Values = [];
        public DbTransaction? LastTransaction;
        public CancellationToken LastToken;
        public DataTable[] Results = [VersionTable(1)];
        public int Commands;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "flow-offline";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string name) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level) => new Transaction(this);
        protected override DbCommand CreateDbCommand() => new Command(this);

        /// <summary>只提供事务身份，不执行数据库操作。</summary>
        private sealed class Transaction(RecordingConnection connection) : DbTransaction
        {
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            protected override DbConnection DbConnection => connection;
            public override void Commit() { }
            public override void Rollback() { }
        }

        /// <summary>保留真实 Npgsql 参数容器，执行由离线记录连接接管。</summary>
        private sealed class Command(RecordingConnection connection) : DbCommand
        {
            private readonly NpgsqlCommand _parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
            protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery() { Capture(); return 1; }
            public override object ExecuteScalar() { Capture(); return 1; }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            { Capture(); return new AliasReader(new DataTableReader(connection.Results)); }
            /// <summary>将 Dapper 的异步取消令牌记录到实际执行边界。</summary>
            public override Task<int> ExecuteNonQueryAsync(CancellationToken token)
            { connection.LastToken = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteNonQuery()); }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            { connection.LastToken = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteDbDataReader(behavior)); }
            /// <summary>保存实际命令、参数与事务，禁止调用驱动执行。</summary>
            private void Capture()
            {
                connection.Commands++;
                connection.Text = CommandText;
                connection.LastTransaction = DbTransaction;
                connection.Values = _parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(x => x.ParameterName, x => (object?)x.Value);
            }
            protected override void Dispose(bool disposing) { if (disposing) _parameters.Dispose(); base.Dispose(disposing); }
        }
    }

    /// <summary>移除测试列前缀以提供真实 SQL 的重复头行别名，其他读取委托标准 DataTableReader。</summary>
    private sealed class AliasReader(DbDataReader reader) : DbDataReader
    {
        public override string GetName(int ordinal) => reader.GetName(ordinal)[1..];
        public override int GetOrdinal(string name) => Enumerable.Range(0, FieldCount).First(x => GetName(x) == name);
        public override object this[int ordinal] => reader[ordinal];
        public override object this[string name] => reader[GetOrdinal(name)];
        public override int Depth => reader.Depth;
        public override int FieldCount => reader.FieldCount;
        public override bool HasRows => reader.HasRows;
        public override bool IsClosed => reader.IsClosed;
        public override int RecordsAffected => reader.RecordsAffected;
        public override bool Read() => reader.Read();
        public override bool NextResult() => reader.NextResult();
        public override object GetValue(int ordinal) => reader.GetValue(ordinal);
        public override int GetValues(object[] values) => reader.GetValues(values);
        public override Type GetFieldType(int ordinal) => reader.GetFieldType(ordinal);
        public override string GetDataTypeName(int ordinal) => reader.GetDataTypeName(ordinal);
        public override bool IsDBNull(int ordinal) => reader.IsDBNull(ordinal);
        public override bool GetBoolean(int ordinal) => reader.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => reader.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int index, int length) => reader.GetBytes(ordinal, offset, buffer, index, length);
        public override char GetChar(int ordinal) => reader.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int index, int length) => reader.GetChars(ordinal, offset, buffer, index, length);
        public override DateTime GetDateTime(int ordinal) => reader.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => reader.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => reader.GetDouble(ordinal);
        public override float GetFloat(int ordinal) => reader.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => reader.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => reader.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => reader.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => reader.GetInt64(ordinal);
        public override string GetString(int ordinal) => reader.GetString(ordinal);
        public override System.Collections.IEnumerator GetEnumerator() => ((System.Collections.IEnumerable)reader).GetEnumerator();
        public override void Close() => reader.Close();
        protected override void Dispose(bool disposing) { if (disposing) reader.Dispose(); base.Dispose(disposing); }
    }
}
