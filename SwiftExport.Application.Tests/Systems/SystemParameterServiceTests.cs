using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Systems.SystemParameters;
using SwiftExport.Application.Tests.Files;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>验证参数用例的活动账号、管理员、首次配置和版本竞争，不连接业务数据库。</summary>
[TestClass]
public sealed class SystemParameterServiceTests
{
    /// <summary>活动普通用户可读未配置定义，非管理员或无效活动投影不能写。</summary>
    [TestMethod]
    public async Task Authorization_ShouldReadDefinitionsAndBlockUnauthorizedWrites()
    {
        var repository = new SystemParameterRepositoryStub(null);
        var session = new Session();
        var ordinary = Create(repository, session, new UserPermissionProjection());
        var read = await ordinary.ListAsync();
        Assert.IsTrue(read.Success);
        Assert.AreEqual(0, read.Data[0].Version);
        Assert.IsFalse(read.Data[0].IsConfigured);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await ordinary.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(0))).Error.Code);
        var inactive = Create(repository, session, null);
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated, (await inactive.ListAsync()).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated, (await inactive.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(0))).Error.Code);
        var anonymous = Create(repository, session, new UserPermissionProjection { IsAdmin = true }, false);
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated, (await anonymous.ListAsync()).Error.Code);
        Assert.AreEqual(0, repository.Writes);
        Assert.AreEqual(0, session.Db.Begins);
    }

    /// <summary>首次建立版本 1，后续只接受预期版本；并发首次保存不能覆盖已存在值。</summary>
    [TestMethod]
    public async Task Save_ShouldNormalizeAndDetectBothKindsOfConflict()
    {
        var repository = new SystemParameterRepositoryStub(null);
        var session = new Session();
        var service = Create(repository, session);
        var first = await service.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(0));
        Assert.IsTrue(first.Success);
        Assert.AreEqual(1, first.Data.Version);
        Assert.AreEqual("https://example.test/images/", first.Data.Value);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await service.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(0))).Error.Code);
        var second = await service.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(1, "https://example.test/new"));
        Assert.IsTrue(second.Success);
        Assert.AreEqual(2, second.Data.Version);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await service.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(1))).Error.Code);
        Assert.AreEqual("https://example.test/new/", repository.Value);
        Assert.AreEqual(2, session.Db.Commits);
        Assert.AreEqual(2, session.Db.Rollbacks);
        Assert.IsNull(session.Transaction);
    }

    /// <summary>未知键、负版本及不安全地址在开启事务前拒绝；存储异常清理事务。</summary>
    [TestMethod]
    public async Task InvalidAndFailedSave_ShouldNotCommitOrLeakTransaction()
    {
        var repository = new SystemParameterRepositoryStub();
        var session = new Session();
        var service = Create(repository, session);
        Assert.AreEqual(ServiceErrorCodes.Validation, (await service.UpdateAsync("unknown", Request(1))).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Validation, (await service.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(-1))).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Validation, (await service.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(1, "https://u:p@example.test/"))).Error.Code);
        Assert.AreEqual(0, session.Db.Begins);
        repository.FailWrites = true;
        Assert.AreEqual(ServiceErrorCodes.Unexpected, (await service.UpdateAsync(SystemParameterKeys.ProductImagesBaseUrl, Request(1))).Error.Code);
        Assert.AreEqual(0, session.Db.Commits);
        Assert.AreEqual(1, session.Db.Rollbacks);
        Assert.IsNull(session.Transaction);
    }

    /// <summary>网络基址必须是原图目录且无额外身份或请求片段，保存后规范尾斜杠。</summary>
    [TestMethod]
    [DataRow(" https://example.test/a ", true)]
    [DataRow("http://example.test:8080/中文/", true)]
    [DataRow("https://example.test/Thumbnails/", false)]
    [DataRow("https://example.test/%54humbnails/", false)]
    [DataRow("https://example.test/a?x=1", false)]
    [DataRow("https://example.test/a#top", false)]
    [DataRow("ftp://example.test/", false)]
    [DataRow("", false)]
    public void BaseUrlValidation_ShouldEnforcePublicOriginalDirectory(string value, bool valid)
        => Assert.AreEqual(valid, ProductImageAddress.TryNormalizeBaseUrl(value, out _));

    private static UpdateSystemParameterRequest Request(int version, string value = " https://example.test/images ")
        => new() { ExpectedVersion = version, Value = value };
    private static SystemParameterService Create(SystemParameterRepositoryStub repository, Session session)
        => Create(repository, session, new UserPermissionProjection { IsAdmin = true });
    private static SystemParameterService Create(SystemParameterRepositoryStub repository, Session session, UserPermissionProjection? projection, bool authenticated = true)
        => new(repository, new User(authenticated), new Permissions(projection), new ServiceBaseContext(session, NullLoggerFactory.Instance));

    /// <summary>测试请求身份，活动资格仍由实时投影决定。</summary>
    private sealed class User(bool authenticated) : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "parameter-test";
        public bool IsAuthenticated => authenticated;
    }
    /// <summary>控制实时活动资格和管理员身份。</summary>
    private sealed class Permissions(UserPermissionProjection? projection) : IPermissionEvaluationService
    {
        /// <inheritdoc />
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(projection);
        /// <inheritdoc />
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
    /// <summary>记录事务所有权；测试从不执行 SQL。</summary>
    private sealed class Session : IDbSession
    {
        public Connection Db { get; } = new();
        public IDbConnection Connection => Db;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => throw new NotSupportedException();
    }
    /// <summary>只提供参数保存所需的短事务，未实现命令不会被误用于数据库测试。</summary>
    private sealed class Connection : IDbConnection
    {
        public int Begins { get; private set; }
        public int Commits { get; set; }
        public int Rollbacks { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "fake";
        public ConnectionState State => ConnectionState.Open;
        /// <inheritdoc />
        public IDbTransaction BeginTransaction() => BeginTransaction(IsolationLevel.ReadCommitted);
        /// <inheritdoc />
        public IDbTransaction BeginTransaction(IsolationLevel il) { Begins++; return new Transaction(this, il); }
        /// <inheritdoc />
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <inheritdoc />
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        /// <inheritdoc />
        public void Close() { }
        /// <inheritdoc />
        public void Open() { }
        /// <inheritdoc />
        public void Dispose() { }
    }
    /// <summary>记录提交与回滚计数供事务失败断言使用。</summary>
    private sealed class Transaction(Connection connection, IsolationLevel isolation) : IDbTransaction
    {
        public IDbConnection Connection => connection;
        public IsolationLevel IsolationLevel => isolation;
        /// <inheritdoc />
        public void Commit() => connection.Commits++;
        /// <inheritdoc />
        public void Rollback() => connection.Rollbacks++;
        /// <inheritdoc />
        public void Dispose() { }
    }
}
