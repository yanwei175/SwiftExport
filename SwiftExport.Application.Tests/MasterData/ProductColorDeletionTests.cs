using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.Colors;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Colors;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证单条和批量删除共享引用保护，锁先于引用查询且失败不提交。</summary>
[TestClass]
public sealed class ProductColorDeletionTests
{
    /// <summary>有效引用阻止单条和批量删除；无引用正常提交，所有仓储调用共享外层事务。</summary>
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    public async Task Delete_ProtectsReferencesAndOwnsTransaction(bool batch, bool referenced, bool conflict)
    {
        var session = new SessionStub();
        var calls = new List<string>();
        var uid = Guid.NewGuid();
        var repository = DispatchProxy.Create<IProductColorRepository, RepositoryProxy>();
        ((RepositoryProxy)(object)repository).Call = (name, args) =>
        {
            Assert.AreSame(session.ConnectionStub.Tx, session.Transaction);
            calls.Add(name);
            return name switch
            {
                "GetByIdListAsync" => Locked(args),
                "GetByColorCodesAsync" => Task.FromResult<IReadOnlyList<ProductColor>>([]),
                "GetReferencedColorIdsAsync" => Task.FromResult<IReadOnlyList<Guid>>(referenced ? [uid] : []),
                "DeleteAsync" => Task.FromResult(conflict
                    ? RepositoryWriteResult.Fail(uid, 1, "CONCURRENCY_CONFLICT", "版本冲突")
                    : RepositoryWriteResult.Ok(uid, 1, 2)),
                _ => throw new AssertFailedException(name)
            };
        };
        var service = new ProductColorService(repository, new ServiceBaseContext(session, NullLoggerFactory.Instance));
        var request = new DeleteItemRequest { Uid = uid, Version = 1 };
        bool success;
        string? error;
        if (batch)
        {
            var result = await service.BatchSaveAsync(new ProductColorBatchSaveRequest { DeleteItems = [request] });
            success = result.Success;
            error = result.Error?.Code;
        }
        else
        {
            var result = await service.DeleteAsync(request);
            success = result.Success;
            error = result.Error?.Code;
            if (!referenced && conflict)
            {
                Assert.IsFalse(result.Data.Success);
                Assert.AreEqual("CONCURRENCY_CONFLICT", result.Data.ErrorCode);
            }
        }
        Assert.AreEqual(!referenced && !(batch && conflict), success);
        if (referenced) Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, error);
        Assert.AreEqual("GetByIdListAsync", calls[0]);
        Assert.IsTrue(calls.IndexOf("GetReferencedColorIdsAsync") > 0);
        Assert.AreEqual(!referenced, calls.Contains("DeleteAsync"));
        Assert.AreEqual(referenced || conflict ? 0 : 1, session.ConnectionStub.Tx.Commits);
        Assert.AreEqual(referenced || conflict ? 1 : 0, session.ConnectionStub.Tx.Rollbacks);
        Assert.IsNull(session.Transaction);

        Task<IReadOnlyList<ProductColor>> Locked(object?[] args)
        {
            Assert.IsTrue((bool)args[1]!);
            return Task.FromResult<IReadOnlyList<ProductColor>>([new() { Uid = uid, Version = 1 }]);
        }
    }

    /// <summary>只代理当前场景需要的仓储调用，未声明的调用直接失败。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;
        /// <summary>转发到受控仓储行为。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }

    /// <summary>只记录事务归属，不连接数据库。</summary>
    private sealed class SessionStub : IDbSession
    {
        public ConnectionStub ConnectionStub { get; } = new();
        public IDbConnection Connection => ConnectionStub;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => throw new NotSupportedException();
    }

    /// <summary>禁止 SQL，只提供单次事务记录。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public TransactionStub Tx { get; } = new();
        public IDbTransaction BeginTransaction() => Tx;
        public IDbTransaction BeginTransaction(IsolationLevel level) => Tx;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        public void Open() { }
        public void Close() { }
        public void Dispose() { }
        public void ChangeDatabase(string name) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
    }

    /// <summary>记录提交与回滚次数。</summary>
    private sealed class TransactionStub : IDbTransaction
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() => Commits++;
        public void Rollback() => Rollbacks++;
        public void Dispose() { }
    }
}
