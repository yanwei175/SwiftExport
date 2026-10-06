using System.Data;
using System.Diagnostics.CodeAnalysis;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.WebApi.ErrorHandling;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证会话边界、DI 同一实例、连接失败清理及提供者异常分类；不连接数据库。</summary>
[TestClass]
public sealed class DatabaseBoundaryTests
{
    /// <summary>Application 与 Infrastructure 解析到同一 scoped 实例，重复释放不会再次清理连接。</summary>
    [TestMethod]
    public void DependencyInjection_SharesSessionAndDisposesOnce()
    {
        var connection = new ConnectionStub();
        var services = new ServiceCollection();
        services.AddInfrastructureServices();
        services.AddSingleton<IDbConnectionFactory>(new Factory(connection));
        services.AddScoped<ServiceBaseContext>();
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDbSession>();
            Assert.AreSame(session, scope.ServiceProvider.GetRequiredService<DbSession>());
            Assert.AreSame(session, scope.ServiceProvider.GetRequiredService<ServiceBaseContext>().Session);
            session.Transaction = connection.BeginTransaction();
            Assert.AreSame(session.Transaction, scope.ServiceProvider.GetRequiredService<DbSession>().Transaction);
            Assert.AreEqual(1, connection.Opens);
        }
        Assert.AreEqual(1, connection.Disposals);
    }

    /// <summary>连接打开失败释放资源；事务释放失败也不泄漏连接。</summary>
    [TestMethod]
    public void Failures_ReleaseConnectionWithoutMaskingOpenError()
    {
        var opening = new ConnectionStub { FailOpen = true };
        Assert.Throws<IOException>(() => new DbSession(new Factory(opening)));
        Assert.AreEqual(1, opening.Disposals);
        var connection = new ConnectionStub();
        var session = new DbSession(new Factory(connection));
        session.Transaction = new TransactionStub(connection, true);
        Assert.Throws<InvalidOperationException>(() => session.Dispose());
        Assert.AreEqual(1, connection.Disposals);
        Assert.IsNull(session.Transaction);
        session.Dispose();
        Assert.AreEqual(1, connection.Disposals);
    }

    /// <summary>同一提供者错误仍按不同语义分类，不把唯一冲突、历史门禁和结构错误合并。</summary>
    [TestMethod]
    [DataRow("23505", DatabaseFailureKind.UniqueConflict)]
    [DataRow("CLIENT_TIMEOUT", DatabaseFailureKind.Timeout)]
    [DataRow("55P03", DatabaseFailureKind.Unknown)]
    [DataRow("40P01", DatabaseFailureKind.Deadlock)]
    [DataRow("08001", DatabaseFailureKind.ConnectionFailure)]
    [DataRow("08006", DatabaseFailureKind.ConnectionFailure)]
    [DataRow("42703", DatabaseFailureKind.SchemaMismatch)]
    [DataRow("42P01", DatabaseFailureKind.SchemaMismatch)]
    [DataRow("UNVERIFIED_HISTORY", DatabaseFailureKind.UnverifiedReceiptHistory)]
    [DataRow("P3102", DatabaseFailureKind.Unknown)]
    public void ProviderErrors_RemainDistinct(string state, DatabaseFailureKind expected)
        => Assert.AreEqual(expected, ((IDbConnectionFactory)new PostgreSqlConnectionFactory(string.Empty)).Errors.Classify(CreateException(state)));

    /// <summary>取消与未知异常不通过消息或包装异常猜测。</summary>
    [TestMethod]
    public void CancellationAndUnknown_AreNotRetriedOrReinterpreted()
    {
        Assert.AreEqual(DatabaseFailureKind.Canceled, PostgreSqlExceptionClassifier.Instance.Classify(new OperationCanceledException()));
        Assert.AreEqual(DatabaseFailureKind.Unknown, PostgreSqlExceptionClassifier.Instance.Classify(new Exception("duplicate 23505")));
        Assert.AreEqual(DatabaseFailureKind.Unknown, PostgreSqlExceptionClassifier.Instance.Classify(new DBConcurrencyException()));
    }

    /// <summary>提供者分类替换后，入库仍区分版本冲突、结构/历史门禁和结果未知，不改变原 HTTP 协议。</summary>
    [TestMethod]
    [DataRow("42703", ServiceErrorCodes.BusinessBlocked, 422)]
    [DataRow("UNVERIFIED_HISTORY", ServiceErrorCodes.BusinessBlocked, 422)]
    [DataRow("23505", ServiceErrorCodes.OutcomeUnknown, 503)]
    [DataRow("CLIENT_TIMEOUT", ServiceErrorCodes.OutcomeUnknown, 503)]
    [DataRow("40P01", ServiceErrorCodes.OutcomeUnknown, 503)]
    public async Task InboundErrorBoundary_PreservesBusinessAndHttpMeaning(string state, string code, int status)
    {
        // 本测试只验证错误转换，授权行为由已有用例测试覆盖。
        var support = new InboundServiceSupport(null!, null!, NullLogger<InboundServiceSupport>.Instance,
            PostgreSqlExceptionClassifier.Instance);
        var result = await support.RunAsync<int>(() => Task.FromException<int>(CreateException(state)),
            CancellationToken.None, checkPermission: false);
        Assert.AreEqual(code, result.Error.Code);
        Assert.AreEqual(status, ServiceErrorHttpMapper.GetStatusCode(result.Error.Code));
        if (state == "UNVERIFIED_HISTORY") StringAssert.Contains(result.Error.Message, "历史入库");
        if (state == "42703") StringAssert.Contains(result.Error.Message, "数据库结构");
    }

    /// <summary>阻断 Application 再次引入 SqlClient、Dapper 或 Infrastructure 程序集依赖。</summary>
    [TestMethod]
    public void Application_DoesNotReferenceProviderImplementation()
    {
        var references = typeof(ServiceBaseContext).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        CollectionAssert.DoesNotContain(references, "SwiftExport.Infrastructure");
        CollectionAssert.DoesNotContain(references, "Microsoft.Data.SqlClient");
        CollectionAssert.DoesNotContain(references, "Dapper");
    }

    /// <summary>构造驱动 SQLSTATE、明确超时原因或真实历史检查信号；不通过消息猜测业务含义或访问网络。</summary>
    private static Exception CreateException(string state) => state switch
    {
        "CLIENT_TIMEOUT" => new NpgsqlException("测试超时", new TimeoutException()),
        "UNVERIFIED_HISTORY" => new UnverifiedReceiptHistoryException(),
        _ => new PostgresException("测试错误", "ERROR", "ERROR", state)
    };

    /// <summary>接口默认PG分类器随工厂进入共享会话，不引入第二条连接或事务。</summary>
    [TestMethod]
    public void PostgreSqlClassifier_FollowsFactoryIntoSharedSession()
    {
        var connection = new ConnectionStub();
        using var session = new DbSession(new Factory(connection));
        Assert.AreSame(PostgreSqlExceptionClassifier.Instance, session.Errors);
        session.Transaction = connection.BeginTransaction();
        Assert.AreSame(connection, session.Transaction.Connection);
        Assert.AreEqual(1, connection.Opens);
    }

    /// <summary>只替换连接的工厂，沿用真实接口默认分类器，不读取环境配置。</summary>
    private sealed class Factory(ConnectionStub connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>记录连接生命周期并注入打开失败。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Opens, Disposals;
        public bool FailOpen;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "none";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() => new TransactionStub(this);
        public IDbTransaction BeginTransaction(IsolationLevel level) => BeginTransaction();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { Opens++; if (FailOpen) throw new IOException("连接打开失败"); }
        public void Dispose() => Disposals++;
    }

    /// <summary>模拟事务释放异常，验证连接总能释放。</summary>
    private sealed class TransactionStub(ConnectionStub connection, bool failDispose = false) : IDbTransaction
    {
        public IDbConnection Connection => connection;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { if (failDispose) throw new InvalidOperationException("事务释放失败"); }
    }
}
