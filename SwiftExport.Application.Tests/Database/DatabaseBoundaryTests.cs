using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.WebApi.ErrorHandling;
using SwiftExport.Core.Interfaces.Data;
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
    [DataRow(2601, DatabaseFailureKind.UniqueConflict)]
    [DataRow(2627, DatabaseFailureKind.UniqueConflict)]
    [DataRow(-2, DatabaseFailureKind.Timeout)]
    [DataRow(1222, DatabaseFailureKind.Timeout)]
    [DataRow(1205, DatabaseFailureKind.Deadlock)]
    [DataRow(53, DatabaseFailureKind.ConnectionFailure)]
    [DataRow(10054, DatabaseFailureKind.ConnectionFailure)]
    [DataRow(207, DatabaseFailureKind.SchemaMismatch)]
    [DataRow(208, DatabaseFailureKind.SchemaMismatch)]
    [DataRow(51001, DatabaseFailureKind.UnverifiedReceiptHistory)]
    [DataRow(53102, DatabaseFailureKind.Unknown)]
    public void ProviderErrors_RemainDistinct(int number, DatabaseFailureKind expected)
        => Assert.AreEqual(expected, SqlServerExceptionClassifier.Instance.Classify(CreateException(number)));

    /// <summary>取消与未知异常不通过消息或包装异常猜测。</summary>
    [TestMethod]
    public void CancellationAndUnknown_AreNotRetriedOrReinterpreted()
    {
        Assert.AreEqual(DatabaseFailureKind.Canceled, SqlServerExceptionClassifier.Instance.Classify(new OperationCanceledException()));
        Assert.AreEqual(DatabaseFailureKind.Unknown, SqlServerExceptionClassifier.Instance.Classify(new Exception("duplicate 2627")));
        Assert.AreEqual(DatabaseFailureKind.Unknown, SqlServerExceptionClassifier.Instance.Classify(new DBConcurrencyException()));
    }

    /// <summary>提供者分类替换后，入库仍区分版本冲突、结构/历史门禁和结果未知，不改变原 HTTP 协议。</summary>
    [TestMethod]
    [DataRow(207, ServiceErrorCodes.BusinessBlocked, 422)]
    [DataRow(51001, ServiceErrorCodes.BusinessBlocked, 422)]
    [DataRow(2601, ServiceErrorCodes.OutcomeUnknown, 503)]
    [DataRow(-2, ServiceErrorCodes.OutcomeUnknown, 503)]
    [DataRow(1205, ServiceErrorCodes.OutcomeUnknown, 503)]
    public async Task InboundErrorBoundary_PreservesBusinessAndHttpMeaning(int number, string code, int status)
    {
        // 本测试只验证错误转换，授权行为由已有用例测试覆盖。
        var support = new InboundServiceSupport(null!, null!, NullLogger<InboundServiceSupport>.Instance,
            SqlServerExceptionClassifier.Instance);
        var result = await support.RunAsync<int>(() => Task.FromException<int>(CreateException(number)),
            CancellationToken.None, checkPermission: false);
        Assert.AreEqual(code, result.Error.Code);
        Assert.AreEqual(status, ServiceErrorHttpMapper.GetStatusCode(result.Error.Code));
        if (number == 51001) StringAssert.Contains(result.Error.Message, "历史入库");
        if (number == 207) StringAssert.Contains(result.Error.Message, "数据库结构");
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

    /// <summary>仅在测试内构造 SqlClient 错误集合，验证真实 Number 读取而无需制造远程故障。</summary>
    private static SqlException CreateException(int number)
    {
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), true)!;
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderBy(value => value.GetParameters().Length).First();
        var values = constructor.GetParameters().Select((parameter, index) => index == 0 ? (object)number
            : parameter.ParameterType == typeof(string) ? "test"
            : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null).ToArray();
        var error = (SqlError)constructor.Invoke(values);
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(errors, [error]);
        return (SqlException)typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
            null, [typeof(SqlErrorCollection), typeof(string)], null)!.Invoke(null, [errors, "test"])!;
    }

    /// <summary>提供者分类器随工厂进入原会话，不引入第二条连接或事务。</summary>
    [TestMethod]
    public void PostgreSqlClassifier_FollowsFactoryIntoSharedSession()
    {
        var connection = new ConnectionStub();
        using var session = new DbSession(new Factory(connection, PostgreSqlExceptionClassifier.Instance));
        Assert.AreSame(PostgreSqlExceptionClassifier.Instance, session.Errors);
        session.Transaction = connection.BeginTransaction();
        Assert.AreSame(connection, session.Transaction.Connection);
        Assert.AreEqual(1, connection.Opens);
    }

    /// <summary>替身连接工厂，不读取环境配置。</summary>
    private sealed class Factory(ConnectionStub connection, IDatabaseExceptionClassifier? errors = null) : IDbConnectionFactory
    {
        public IDatabaseExceptionClassifier Errors => errors ?? SqlServerExceptionClassifier.Instance;
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
