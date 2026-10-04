using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Catalog;

namespace SwiftExport.Application.Tests.Products;

/// <summary>以真实 BOM 服务和可恢复的内存持久集合验证保存事务编排，不连接数据库。</summary>
[TestClass]
public sealed class BomSaveRollbackTests
{
    /// <summary>后续乐观并发失败或异常必须撤销已成功的新增、修改，并停止剩余删除。</summary>
    [TestMethod]
    [DataRow(2, false)]
    [DataRow(2, true)]
    [DataRow(3, false)]
    [DataRow(3, true)]
    public async Task BatchSave_LaterWriteFailure_RestoresWholeCollection(int failureNumber, bool throwException)
    {
        var fixture = new Fixture { FailureNumber = failureNumber, ThrowException = throwException };
        var before = fixture.Snapshot();

        var result = await fixture.Service.BatchSaveAsync(fixture.MixedRequest());

        Assert.IsFalse(result.Success, "已回滚的批次不能返回成功回执。");
        Assert.AreEqual(throwException ? ServiceErrorCodes.Unexpected : ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.IsNull(result.Data, "失败不能携带已撤销写入的成功回执。");
        Assert.AreEqual(failureNumber - 1, fixture.SuccessfulWrites);
        CollectionAssert.AreEqual(
            new[] { "AddAsync", "UpdateAsync", "DeleteAsync" }.Take(failureNumber).ToArray(),
            fixture.Writes.ToArray(), "首次失败后不得继续写入。");
        Assert.AreEqual(before, fixture.Snapshot(), "回滚必须恢复实体内容、版本和软删除状态。");
        fixture.AssertTransaction(commits: 0, rollbacks: 1);
    }

    /// <summary>整个混合批次成功时只提交一次，并保留新增、修改及两次软删除的结果。</summary>
    [TestMethod]
    public async Task BatchSave_Success_CommitsOnceAndKeepsAllWrites()
    {
        var fixture = new Fixture();
        var updatedId = fixture.Rows[0].Uid;
        var deletedIds = fixture.Rows.Skip(1).Select(x => x.Uid).ToArray();

        var result = await fixture.Service.BatchSaveAsync(fixture.MixedRequest());

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(4, result.Data.Items.Count);
        Assert.IsTrue(result.Data.AllSucceeded);
        CollectionAssert.AreEqual(new[] { "AddAsync", "UpdateAsync", "DeleteAsync", "DeleteAsync" }, fixture.Writes.ToArray());
        Assert.AreEqual(4, fixture.SuccessfulWrites);
        var updated = fixture.Rows.Single(x => x.Uid == updatedId);
        Assert.AreEqual(2, updated.Version);
        Assert.AreEqual(7, updated.Numerator);
        Assert.IsTrue(deletedIds.All(id => fixture.Rows.Single(x => x.Uid == id).IsDeleted));
        Assert.IsTrue(deletedIds.All(id => fixture.Rows.Single(x => x.Uid == id).Version == 1),
            "通用仓储软删除保留原版本，夹具应匹配该语义。");
        CollectionAssert.AreEquivalent(new[] { "B", "E" }, fixture.Rows.Where(x => !x.IsDeleted).Select(x => x.ComponentSkuSnapshot).ToArray());
        fixture.AssertTransaction(commits: 1, rollbacks: 0);
    }

    /// <summary>最终图出现父产品自引用时，预校验必须在第一次持久写入之前拒绝整个批次。</summary>
    [TestMethod]
    public async Task BatchSave_InvalidFinalGraph_DoesNotWrite()
    {
        var fixture = new Fixture();
        var request = fixture.MixedRequest();
        request.CreateItems[0].Data.SubSkuId = fixture.Parent.Uid;
        var before = fixture.Snapshot();

        var result = await fixture.Service.BatchSaveAsync(request);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "不能相同");
        Assert.AreEqual(1, fixture.GraphReads);
        Assert.AreEqual(0, fixture.Writes.Count);
        Assert.AreEqual(before, fixture.Snapshot(), "准备阶段修改的读取对象不得污染持久集合。");
        fixture.AssertTransaction(commits: 0, rollbacks: 1);
    }

    /// <summary>按 BOM 号删除不能绕过最终图校验，配置中的当前版本必须保留组成行。</summary>
    [TestMethod]
    public async Task DeleteByBomNo_RemovingCurrentGraph_IsRejectedWithoutDirectDelete()
    {
        var fixture = new Fixture();
        var before = fixture.Snapshot();

        var result = await fixture.Service.DeleteBomByBomNoAsync(fixture.Parent.BomNo!);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "至少需要保留一条");
        Assert.AreEqual(0, fixture.DirectDeletes, "服务不得调用绕过图校验的按号仓储删除。");
        Assert.AreEqual(1, fixture.GraphReads);
        Assert.AreEqual(0, fixture.Writes.Count);
        Assert.AreEqual(before, fixture.Snapshot());
        fixture.AssertTransaction(commits: 0, rollbacks: 1);
    }

    /// <summary>已确认提交后释放事务、连接或诊断日志失败，仍保留成功结果并清空会话事务。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task BatchSave_ConfirmedCommitCleanupFailure_KeepsSuccess(bool failConnectionDispose, bool failLogging)
    {
        var fixture = new Fixture
        {
            FailTransactionDispose = true, FailConnectionDispose = failConnectionDispose, FailLogging = failLogging
        };

        var result = await fixture.Service.BatchSaveAsync(fixture.MixedRequest());

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(4, result.Data.SuccessCount);
        Assert.AreEqual(4, fixture.SuccessfulWrites);
        Assert.AreEqual(2, fixture.Rows.Count(x => !x.IsDeleted));
        Assert.IsTrue(fixture.LogCalls > 0, "必须到达故障诊断入口，不能因未触发日志而误报通过。");
        fixture.AssertTransaction(commits: 1, rollbacks: 0, connectionDisposals: 1);
    }

    /// <summary>清理及日志故障不覆盖原写入失败分类，不执行后续写入；成功回滚时恢复持久集合。</summary>
    [TestMethod]
    [DataRow(false, true, false, false, false)]
    [DataRow(false, false, true, false, false)]
    [DataRow(false, false, false, true, false)]
    [DataRow(false, true, true, true, false)]
    [DataRow(false, true, true, true, true)]
    [DataRow(true, true, false, true, false)]
    [DataRow(true, false, true, true, true)]
    public async Task BatchSave_WriteFailureCleanupFaults_KeepOriginalFailure(
        bool throwException, bool failRollback, bool failDispose, bool failLogging, bool failConnectionDispose)
    {
        var fixture = new Fixture
        {
            FailureNumber = 2, ThrowException = throwException, FailRollback = failRollback,
            FailTransactionDispose = failDispose, FailLogging = failLogging, FailConnectionDispose = failConnectionDispose
        };
        var before = fixture.Snapshot();

        var result = await fixture.Service.BatchSaveAsync(fixture.MixedRequest());

        Assert.IsFalse(result.Success);
        Assert.AreEqual(throwException ? ServiceErrorCodes.Unexpected : ServiceErrorCodes.VersionConflict, result.Error.Code);
        if (!throwException) StringAssert.Contains(result.Message, "受控乐观并发失败");
        Assert.IsNull(result.Data);
        CollectionAssert.AreEqual(new[] { "AddAsync", "UpdateAsync" }, fixture.Writes.ToArray());
        Assert.AreEqual(1, fixture.SuccessfulWrites);
        if (!failRollback) Assert.AreEqual(before, fixture.Snapshot());
        else Assert.IsFalse(result.Message.Contains("已回滚", StringComparison.Ordinal), "回滚失败不能承诺操作已撤销。");
        Assert.IsTrue(fixture.LogCalls > 0);
        fixture.AssertTransaction(commits: 0, rollbacks: 1, connectionDisposals: failDispose ? 1 : 0);
    }

    /// <summary>提交前异常和提交后确认丢失都返回结果未知；清理多故障不能覆盖结果或再次写入，回滚不能撤销已提交事实。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task BatchSave_CommitFailure_PreservesUnknownOutcomeAndActualFacts(bool committed, bool cleanupFaults)
    {
        var fixture = new Fixture
        {
            FailCommitBefore = !committed, FailCommitAfter = committed,
            FailRollback = cleanupFaults, FailTransactionDispose = cleanupFaults,
            FailConnectionDispose = cleanupFaults, FailLogging = cleanupFaults
        };
        var before = fixture.Snapshot();
        var result = await fixture.Service.BatchSaveAsync(fixture.MixedRequest());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.IsNull(result.Data);
        Assert.IsFalse(result.Message.Contains("已回滚", StringComparison.Ordinal));
        Assert.IsFalse(result.Message.Contains("CommandId", StringComparison.OrdinalIgnoreCase));
        CollectionAssert.AreEqual(new[] { "AddAsync", "UpdateAsync", "DeleteAsync", "DeleteAsync" }, fixture.Writes.ToArray());
        Assert.AreEqual(4, fixture.SuccessfulWrites, "提交确认失败不能触发自动再次写入。");
        Assert.AreEqual(committed, fixture.IsCommitted);
        if (committed)
        {
            Assert.AreNotEqual(before, fixture.CommittedSnapshot);
            Assert.AreEqual(fixture.CommittedSnapshot, fixture.Snapshot(), "回滚尝试不能伪造撤销已提交事实。");
            Assert.AreEqual(2, fixture.Rows.Count(x => !x.IsDeleted));
        }
        else
        {
            Assert.AreEqual(before, fixture.CommittedSnapshot, "Commit 抛错前没有发生提交。");
            if (!cleanupFaults) Assert.AreEqual(before, fixture.Snapshot());
        }
        Assert.IsTrue(fixture.LogCalls > 0);
        fixture.AssertTransaction(commits: 1, rollbacks: 1, connectionDisposals: cleanupFaults ? 1 : 0);
    }

    /// <summary>非并发仓储业务失败仍保留业务阻断，不能全部提升为版本冲突。</summary>
    [TestMethod]
    public async Task BatchSave_OtherRepositoryFailure_RemainsBusinessBlocked()
    {
        var fixture = new Fixture { FailureNumber = 2, FailureCode = "WRITE_FAILED" };
        var before = fixture.Snapshot();
        var result = await fixture.Service.BatchSaveAsync(fixture.MixedRequest());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        Assert.IsNull(result.Data);
        Assert.AreEqual(before, fixture.Snapshot());
        CollectionAssert.AreEqual(new[] { "AddAsync", "UpdateAsync" }, fixture.Writes.ToArray());
        fixture.AssertTransaction(0, 1);
    }

    /// <summary>预校验拒绝后回滚或日志故障仍保留业务拒绝，不误报成技术错误，也不写入。</summary>
    [TestMethod]
    public async Task BatchSave_ValidationFailureCleanupFaults_KeepsBusinessRejection()
    {
        var fixture = new Fixture { FailRollback = true, FailTransactionDispose = true, FailLogging = true };
        var request = fixture.MixedRequest();
        request.CreateItems[0].Data.SubSkuId = fixture.Parent.Uid;
        var before = fixture.Snapshot();
        var result = await fixture.Service.BatchSaveAsync(request);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        StringAssert.Contains(result.Message, "不能相同");
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.Snapshot());
        fixture.AssertTransaction(commits: 0, rollbacks: 1, connectionDisposals: 1);
    }

    /// <summary>事务创建失败时没有本层拥有的事务，不得释放事务或连接，也不能写入。</summary>
    [TestMethod]
    public async Task BatchSave_BeginFailure_DoesNotDisposeUnacquiredTransaction()
    {
        var fixture = new Fixture { FailBegin = true, FailLogging = true };

        var result = await fixture.Service.BatchSaveAsync(fixture.MixedRequest());

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.AreEqual(0, fixture.Writes.Count);
        fixture.AssertNoOwnedTransaction();
    }

    /// <summary>存在外层事务时直接拒绝保存，不开始、回滚、释放或清空外层事务。</summary>
    [TestMethod]
    public async Task BatchSave_ExistingTransaction_IsLeftUntouched()
    {
        var fixture = new Fixture();
        fixture.AttachExternalTransaction();

        var result = await fixture.Service.BatchSaveAsync(fixture.MixedRequest());

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        Assert.AreEqual(0, fixture.Writes.Count);
        fixture.AssertNoOwnedTransaction(hasExternalTransaction: true);
    }

    /// <summary>仓储读取返回独立副本，事务快照只覆盖真正持久化的集合，模拟 Dapper 的对象边界。</summary>
    private sealed class Fixture
    {
        public Product Parent { get; } = new() { Uid = Guid.NewGuid(), Sku = "A", BomNo = "BOM-A", BomVersion = 1, SupplyType = "采购" };
        public List<Bom> Rows { get; private set; } = [];
        public List<string> Writes { get; } = [];
        public int FailureNumber { get; init; }
        public bool ThrowException { get; init; }
        public string FailureCode { get; init; } = "CONCURRENCY_CONFLICT";
        public int SuccessfulWrites { get; private set; }
        public int DirectDeletes { get; private set; }
        public int GraphReads { get; private set; }
        public BomService Service { get; }
        public bool FailBegin { init => _session.MemoryConnection.FailBegin = value; }
        public bool FailRollback { init => _session.MemoryConnection.Transaction.FailRollback = value; }
        public bool FailCommitBefore { init => _session.MemoryConnection.Transaction.FailCommitBefore = value; }
        public bool FailCommitAfter { init => _session.MemoryConnection.Transaction.FailCommitAfter = value; }
        public bool IsCommitted => _session.MemoryConnection.Transaction.IsCommitted;
        public string CommittedSnapshot { get; private set; } = "";
        public bool FailTransactionDispose { init => _session.MemoryConnection.Transaction.FailDispose = value; }
        public bool FailConnectionDispose { init => _session.MemoryConnection.FailDispose = value; }
        public bool FailLogging { init => _logger.Fail = value; }
        public int LogCalls => _logger.Calls;
        private readonly Product[] _products;
        private readonly SessionStub _session;
        private readonly FaultLogger _logger = new();
        private List<Bom> _original = [];

        /// <summary>准备一个父产品和四个不同组件，把事务回滚连接到持久集合恢复。</summary>
        public Fixture()
        {
            _products = [Parent, .. new[] { "B", "C", "D", "E" }.Select(sku => new Product { Uid = Guid.NewGuid(), Sku = sku, SupplyType = "采购" })];
            Rows = _products.Skip(1).Take(3).Select((child, index) => new Bom
            {
                BomNo = Parent.BomNo!, BomVersion = 1, ParentProductId = Parent.Uid,
                ParentSkuSnapshot = Parent.Sku, ComponentProductId = child.Uid,
                ComponentSkuSnapshot = child.Sku, LineNumber = index + 1, Numerator = 1, Denominator = 1
            }).ToList();
            _session = new SessionStub();
            _session.MemoryConnection.Transaction.OnBegin = () =>
            {
                _original = Rows.Select(Clone).ToList();
                CommittedSnapshot = Snapshot();
            };
            _session.MemoryConnection.Transaction.OnCommit = () => CommittedSnapshot = Snapshot();
            _session.MemoryConnection.Transaction.OnRollback = () => Rows = _original.Select(Clone).ToList();
            Service = new BomService(Proxy<IBomRepository>(BomCall), Proxy<IProductRepository>(ProductCall),
                new ServiceBaseContext(_session, new FaultLoggerFactory(_logger)));
        }

        /// <summary>新增 E、修改 B、删除 C 和 D，失败后仍有后续操作可用于检测停止写入。</summary>
        public BomBatchSaveRequest MixedRequest() => new()
        {
            CreateItems = [new() { Data = Dto(_products[4].Uid, 2) }],
            UpdateItems = [new() { Uid = Rows[0].Uid, Version = 1, Data = Dto(_products[1].Uid, 7) }],
            DeleteItems = Rows.Skip(1).Select(x => new DeleteItemRequest { Uid = x.Uid, Version = 1 }).ToList()
        };

        private BomDto Dto(Guid componentId, int numerator) => new()
        {
            BomNo = Parent.BomNo!, SubSkuId = componentId, Numerator = numerator, Denominator = 1
        };

        /// <summary>序列化全部持久字段，用于比较回滚前后内容和版本是否完全恢复。</summary>
        public string Snapshot() => JsonSerializer.Serialize(Rows);

        /// <summary>核对事务只开始一次、按预期结束、释放后从请求会话中清除。</summary>
        public void AssertTransaction(int commits, int rollbacks, int connectionDisposals = 0)
        {
            var connection = _session.MemoryConnection;
            Assert.AreEqual(1, connection.Begins);
            Assert.AreEqual(commits, connection.Transaction.Commits);
            Assert.AreEqual(rollbacks, connection.Transaction.Rollbacks);
            Assert.AreEqual(1, connection.Transaction.Disposals);
            Assert.AreEqual(connectionDisposals, connection.Disposals, "正常事务结束不得额外关闭共享连接。");
            Assert.IsNull(_session.Transaction);
        }

        /// <summary>建立调用方外层事务，仅用于核对服务不会接管其生命周期。</summary>
        public void AttachExternalTransaction() => _session.Transaction = _session.Connection.BeginTransaction();

        /// <summary>检查创建失败或外层事务拒绝路径未清理任何未拥有资源。</summary>
        public void AssertNoOwnedTransaction(bool hasExternalTransaction = false)
        {
            var connection = _session.MemoryConnection;
            Assert.AreEqual(1, connection.Begins);
            Assert.AreEqual(0, connection.Transaction.Commits);
            Assert.AreEqual(0, connection.Transaction.Rollbacks);
            Assert.AreEqual(0, connection.Transaction.Disposals);
            Assert.AreEqual(0, connection.Disposals);
            if (hasExternalTransaction) Assert.AreSame(connection.Transaction, _session.Transaction);
            else Assert.IsNull(_session.Transaction);
        }

        /// <summary>只实现保存实际使用的产品读取，返回独立副本，拒绝任何意外操作。</summary>
        private object ProductCall(string method, object?[] args) => method switch
        {
            "GetByBomNoAsync" => Task.FromResult<Product?>(Clone(_products.Single(x => x.BomNo == (string)args[0]!))),
            "GetByIdAsync" => Task.FromResult<Product?>(_products.Where(x => x.Uid == (Guid)args[0]!).Select(Clone).SingleOrDefault()),
            "GetByIdListAsync" => Task.FromResult<IReadOnlyList<Product>>(_products.Where(x => ((IEnumerable<Guid>)args[0]!).Contains(x.Uid)).Select(Clone).ToArray()),
            _ => throw new AssertFailedException($"未授权产品调用：{method}")
        };

        /// <summary>持久写入必须共享服务事务；受控失败发生前的变更真实落入集合，随后由回滚恢复。</summary>
        private object BomCall(string method, object?[] args)
        {
            if (method == "DeleteBomByBomNoAsync")
            {
                DirectDeletes++;
                return Task.FromResult(0);
            }
            if (method == "GetBomByBomNoAsync")
                return Task.FromResult<IEnumerable<Bom>>(Rows.Where(x => !x.IsDeleted && x.BomNo == (string)args[0]!).Select(Clone).ToArray());
            if (method == "GetByBomNoAndVersionAsync")
                return Task.FromResult<IReadOnlyList<Bom>>(Rows.Where(x => !x.IsDeleted && x.BomNo == (string)args[0]! && x.BomVersion == (int)args[1]!).Select(Clone).ToArray());
            Assert.IsNotNull(_session.Transaction, "保存读取和写入必须共享已开启的事务。");
            if (method == "GetByIdListAsync")
                return Task.FromResult<IReadOnlyList<Bom>>(Rows.Where(x => !x.IsDeleted && ((IEnumerable<Guid>)args[0]!).Contains(x.Uid)).Select(Clone).ToArray());
            if (method == "GetCurrentVersionBomGraphAsync")
            {
                GraphReads++;
                return Task.FromResult<IReadOnlyList<Bom>>(Rows.Where(x => !x.IsDeleted).Select(Clone).ToArray());
            }
            if (method == nameof(IBomRepository.GetSharedSaveFactsAsync))
            {
                GraphReads++;
                Assert.IsTrue((bool)args[1]!, "组成保存必须在事务中以更新锁读取共享事实。");
                return Task.FromResult(SharedBomTestFacts.Build(_products, Rows, (IReadOnlyList<string>)args[0]!));
            }
            Assert.IsTrue(method is "AddAsync" or "UpdateAsync" or "DeleteAsync", $"未授权 BOM 调用：{method}");
            Writes.Add(method);
            if (Writes.Count == FailureNumber)
            {
                if (ThrowException) throw new InvalidOperationException("受控仓储异常");
                return Task.FromResult(RepositoryWriteResult.Fail(Guid.Empty, 1, FailureCode, "受控乐观并发失败"));
            }
            SuccessfulWrites++;
            if (method == "DeleteAsync")
            {
                Assert.IsTrue((bool)args[2]!);
                var row = Rows.Single(x => x.Uid == (Guid)args[0]!);
                Assert.AreEqual(row.Version, (int)args[1]!);
                // 通用仓储按 Uid + Version 条件软删，返回原版本而不递增。
                row.IsDeleted = true;
                return Task.FromResult(RepositoryWriteResult.Ok(row.Uid, row.Version, row.Version));
            }
            var saved = Clone((Bom)args[0]!);
            if (method == "UpdateAsync")
            {
                var index = Rows.FindIndex(x => x.Uid == saved.Uid);
                Assert.AreEqual(Rows[index].Version, saved.Version);
                saved.Version++;
                Rows[index] = saved;
            }
            else Rows.Add(saved);
            return Task.FromResult(RepositoryWriteResult.Ok(saved.Uid, method == "AddAsync" ? null : saved.Version - 1, saved.Version));
        }
    }

    /// <summary>完整克隆实体字段，避免准备阶段修改读取对象造成隐式持久写入。</summary>
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    /// <summary>为当前接口安装局部受控仓储，不创建生产代码抽象。</summary>
    private static T Proxy<T>(Func<string, object?[], object> call) where T : class
    {
        var value = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)value).Call = call;
        return value;
    }

    /// <summary>转发仓储接口调用，由夹具显式决定允许的读写行为。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;
        /// <summary>向夹具传入方法名和实参，未声明的行为立即失败。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args ?? []);
    }

    /// <summary>仅提供共享事务上下文与未知异常分类，不引用真实数据库会话实现。</summary>
    private sealed class SessionStub : IDbSession, IDatabaseExceptionClassifier
    {
        public ConnectionStub MemoryConnection { get; } = new();
        public IDbConnection Connection => MemoryConnection;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => this;
        /// <summary>测试中提供者错误统一视为未知，不模拟数据库错误分类。</summary>
        public DatabaseFailureKind Classify(Exception exception) => DatabaseFailureKind.Unknown;
    }

    /// <summary>提供记录型内存事务，禁止执行数据库命令。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public TransactionStub Transaction { get; } = new();
        public int Begins { get; private set; }
        public bool FailBegin { get; set; }
        public bool FailDispose { get; set; }
        public int Disposals { get; private set; }
        /// <summary>开始事务时保存持久集合快照。</summary>
        public IDbTransaction BeginTransaction()
        {
            Begins++;
            if (FailBegin) throw new InvalidOperationException("受控事务开始失败");
            Transaction.OnBegin();
            return Transaction;
        }
        /// <summary>沿用相同内存快照行为，不模拟隔离级别。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        /// <summary>内存连接始终可用，关闭不产生数据库副作用。</summary>
        public void Close() { }
        /// <summary>内存连接无需建立网络连接。</summary>
        public void Open() { }
        /// <summary>禁止切换数据库，避免夹具意外使用真实提供者。</summary>
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>禁止执行 SQL，测试仅观察服务事务编排。</summary>
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        /// <summary>记录释放尝试，并允许注入连接清理异常，不访问外部资源。</summary>
        public void Dispose()
        {
            Disposals++;
            if (FailDispose) throw new InvalidOperationException("受控连接释放失败");
        }
    }

    /// <summary>记录事务生命周期，并在回滚时恢复真实内存持久集合；不验证数据库锁或并发。</summary>
    private sealed class TransactionStub : IDbTransaction
    {
        public Action OnBegin { get; set; } = () => { };
        public Action OnRollback { get; set; } = () => { };
        public Action OnCommit { get; set; } = () => { };
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public int Disposals { get; private set; }
        public bool FailRollback { get; set; }
        public bool FailCommitBefore { get; set; }
        public bool FailCommitAfter { get; set; }
        public bool IsCommitted { get; private set; }
        public bool FailDispose { get; set; }
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        /// <summary>区分尚未提交就失败与提交事实成立后确认丢失，记录尝试次数及真实提交快照。</summary>
        public void Commit()
        {
            Commits++;
            if (FailCommitBefore) throw new InvalidOperationException("受控提交前失败");
            IsCommitted = true;
            OnCommit();
            if (FailCommitAfter) throw new InvalidOperationException("受控提交后确认丢失");
        }
        /// <summary>记录回滚尝试；故障时不伪造已恢复，成功时恢复持久字段快照。</summary>
        public void Rollback()
        {
            Rollbacks++;
            if (FailRollback) throw new InvalidOperationException("受控事务回滚失败");
            if (!IsCommitted) OnRollback();
        }
        /// <summary>记录事务资源释放尝试，并允许注入 finally 清理故障。</summary>
        public void Dispose()
        {
            Disposals++;
            if (FailDispose) throw new InvalidOperationException("受控事务释放失败");
        }
    }

    /// <summary>只提供当前夹具的故障日志对象，不修改公共日志或其他测试夹具。</summary>
    private sealed class FaultLoggerFactory(FaultLogger logger) : ILoggerFactory
    {
        /// <summary>创建服务所需日志对象，故障仅发生在实际诊断写入时。</summary>
        public ILogger CreateLogger(string categoryName) => logger;
        /// <summary>局部夹具无需安装日志提供者。</summary>
        public void AddProvider(ILoggerProvider provider) { }
        /// <summary>局部夹具没有外部日志资源。</summary>
        public void Dispose() { }
    }

    /// <summary>记录日志入口是否被触发，允许诊断写入失败以验证原结果不会被覆盖。</summary>
    private sealed class FaultLogger : ILogger
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        /// <summary>允许所有级别，确保故障注入实际到达日志写入。</summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>夹具不创建外部日志范围。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>记录诊断调用，再按当前场景抛出受控日志异常。</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("受控日志写入失败");
        }
    }
}
