using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Overview;
using SwiftExport.Infrastructure.Repositories.OrderTracking.DemandDetail;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>驱动真实 Dapper 映射的无网络连接，记录只读命令、参数、取消及外层事务。</summary>
internal sealed class OrderTrackingReadFixture : IDisposable
{
    internal readonly RecordingConnection Connection = new();
    internal readonly DbSession Session;
    internal readonly PostgreSqlOrderTrackingOverviewRepository Overview;
    internal readonly PostgreSqlDemandTrackingHeaderRepository Detail;
    internal readonly RepositoryBaseContext Context;

    internal OrderTrackingReadFixture(bool transaction = false)
    {
        Session = new(new Factory(Connection));
        if (transaction) Session.Transaction = Connection.BeginTransaction();
        var user = new User();
        var context = new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        Context = context;
        Overview = new(context); Detail = new(context);
    }

    /// <summary>释放本例独占的会话与连接。</summary>
    public void Dispose() => Session.Dispose();

    internal void Page(int total, params object[] rows)
    {
        Connection.Results.Enqueue(Table(new { Count = total }));
        Connection.Results.Enqueue(rows.Length == 0 ? new DataTable() : Table(rows));
    }

    /// <summary>依据公开 DTO 属性生成驱动结果，NULL、日期和枚举走真实映射通道。</summary>
    internal static DataTable Table(params object[] rows)
    {
        var table = new DataTable();
        var properties = rows[0].GetType().GetProperties();
        foreach (var property in properties)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            table.Columns.Add(property.Name, type.IsEnum ? typeof(int) : type);
        }
        foreach (var row in rows)
            table.Rows.Add(properties.Select(property =>
            {
                var value = property.GetValue(row);
                return value is Enum ? Convert.ToInt32(value) : value ?? DBNull.Value;
            }).ToArray());
        return table;
    }

    /// <summary>以公开审计身份构造仓储，不访问用户数据库。</summary>
    internal sealed class User : ICurrentUser
    {
        /// <summary>查询测试身份。</summary>
        public string UserName => "order-tracking-read";
        /// <summary>不用于查询授权的空审计标识。</summary>
        public Guid? UserId => null;
        /// <summary>权限测试另走真实过滤器，此夹具只验证仓储。</summary>
        public bool IsAuthenticated => false;
    }

    /// <summary>仅交付当前内存连接，不提供外部数据库访问入口。</summary>
    private sealed class Factory(RecordingConnection connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => connection;
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
    }

    /// <summary>命令记录可供只读实库矩阵复用真实 SQL，绝不在替身中实现筛选算法。</summary>
    internal sealed class RecordingConnection : DbConnection
    {
        internal readonly Queue<DataTable> Results = new();
        internal readonly List<RecordedCommand> Commands = [];
        // 可选回调只用于显式只读 PG 夹具，让真实仓储编排执行同一 SQL 的内联数据版本。
        internal Func<RecordedCommand,CancellationToken,Task<DbDataReader>>? ReadAsync;
        internal CancellationToken Token;
        internal int Begins, Commits, Rollbacks;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new RecordedTransaction(this); }
        protected override DbCommand CreateDbCommand() => new Command(this);

        /// <summary>记录查询是否错误地提交或回滚调用方事务。</summary>
        private sealed class RecordedTransaction(RecordingConnection connection) : DbTransaction
        {
            protected override DbConnection DbConnection => connection;
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public override void Commit() => connection.Commits++;
            public override void Rollback() => connection.Rollbacks++;
        }

        /// <summary>借用 Npgsql 参数容器，所有执行结果均来自预排队数据表。</summary>
        private sealed class Command(RecordingConnection connection) : DbCommand
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
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            /// <summary>经真实标量通道消费预先指定的计数结果。</summary>
            public override object ExecuteScalar() { using var reader = ExecuteDbDataReader(CommandBehavior.Default); return reader.Read() ? reader.GetValue(0) : DBNull.Value; }
            /// <summary>执行前观察取消，记录实际传入的取消令牌。</summary>
            public override async Task<object?> ExecuteScalarAsync(CancellationToken token)
            {
                connection.Token=token;token.ThrowIfCancellationRequested();
                if(connection.ReadAsync is null) return ExecuteScalar();
                await using var reader=await ExecuteDbDataReaderAsync(CommandBehavior.Default,token);
                return await reader.ReadAsync(token)?reader.GetValue(0):DBNull.Value;
            }
            /// <summary>保存执行时 SQL、参数类型及外层事务，再交给真实 Dapper 映射。</summary>
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                connection.Commands.Add(new(CommandText, parameters.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToArray(), DbTransaction));
                return connection.Results.Dequeue().CreateDataReader();
            }
            /// <summary>异步读取保持相同命令记录，取消时不消费数据表。</summary>
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            {
                connection.Token=token;token.ThrowIfCancellationRequested();
                if(connection.ReadAsync is null) return Task.FromResult(ExecuteDbDataReader(behavior));
                var recorded=new RecordedCommand(CommandText,parameters.Parameters.Cast<NpgsqlParameter>().Select(parameter=>parameter.Clone()).ToArray(),DbTransaction);
                connection.Commands.Add(recorded);
                return connection.ReadAsync(recorded,token);
            }
            /// <summary>释放本命令参数容器，不关闭借用连接。</summary>
            protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }

    /// <summary>保存实际 SQL、显式参数类型和借用事务。</summary>
    internal sealed record RecordedCommand(string Sql, NpgsqlParameter[] Parameters, DbTransaction? Transaction)
    {
        internal object Value(string name) => Parameters.Single(parameter => parameter.ParameterName == name).Value ?? DBNull.Value;
        internal NpgsqlDbType Type(string name) => Parameters.Single(parameter => parameter.ParameterName == name).NpgsqlDbType;
    }
}
