using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.ProductNeutrals.Maintenance;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>离线验证中性产品管理保存的观察、父子锁顺序和事务回滚，不模拟数据库锁竞争。</summary>
[TestClass]
public sealed class ProductNeutralManagerLockOrderTests
{
    /// <summary>换父更新先普通观察，再依次锁新颜色、旧新主型号和重读子行，写入保留当前主图。</summary>
    [TestMethod]
    public async Task ReparentUpdate_LocksOldAndNewMastersAfterColorsBeforeNeutral()
    {
        var f = new Fixture();
        var result = await f.Service.BatchSaveAsync(f.UpdateRequest());
        Assert.IsTrue(result.Success, result.Message);
        CollectionAssert.AreEqual(new[] { "observe", "color", "master", "neutral", "codes", "update" }, f.Events.ToArray());
        CollectionAssert.AreEqual(new[] { f.NewColorId }, f.ColorRequests.Single());
        CollectionAssert.AreEqual(new[] { f.OldMasterId, f.NewMasterId }.OrderBy(x => x).ToArray(), f.MasterRequests.Single());
        CollectionAssert.AreEqual(new[] { f.NeutralId }, f.ObservedIds);
        CollectionAssert.AreEqual(f.ObservedIds, f.LockedIds);
        Assert.AreEqual(f.NewMasterId, f.Committed.Single().ProductMasterId);
        Assert.AreEqual(f.NewColorId, f.Committed.Single().ColorId);
        Assert.IsTrue(f.Committed.Single().IsMainImage);
        Assert.AreEqual(2, f.Committed.Single().Version);
        AssertTransaction(f, committed: true);
    }

    /// <summary>目标父已有同颜色不同编码时，新增及换父均成功；仍保留观察、父锁和中性行重读流程。</summary>
    [TestMethod]
    public async Task CreateAndReparentToRepeatedMasterColor_PreserveDistinctCodes()
    {
        var f = new Fixture();
        f.Committed.Add(new ProductNeutral
        { Uid = Guid.NewGuid(), ProductMasterId = f.NewMasterId, ColorId = f.NewColorId, ProductCode = "TARGET" });
        var request = f.UpdateRequest();
        request.CreateItems.Add(new() { Data = new()
        { ProductMasterId = f.NewMasterId, ColorId = f.NewColorId, ProductCode = "NEW" } });

        var result = await f.Service.BatchSaveAsync(request);

        Assert.IsTrue(result.Success, result.Message);
        CollectionAssert.AreEqual(new[] { "observe", "color", "master", "neutral", "codes", "add", "update" }, f.Events.ToArray());
        var rows = f.Committed.Where(row => row.ProductMasterId == f.NewMasterId && row.ColorId == f.NewColorId).ToArray();
        Assert.HasCount(3, rows);
        CollectionAssert.AreEquivalent(new[] { "TARGET", "NEW", "OLD" }, rows.Select(row => row.ProductCode).ToArray());
        AssertTransaction(f, committed: true);
    }

    /// <summary>跨父型号已占用的编码仍按全局唯一拒绝，不能因允许同父同色重复而扩大编码范围。</summary>
    [TestMethod]
    public async Task CodeOwnedByAnotherMaster_RejectsBeforeWrites()
    {
        var f = new Fixture();
        f.Committed.Add(new ProductNeutral
        { Uid = Guid.NewGuid(), ProductMasterId = Guid.NewGuid(), ColorId = Guid.NewGuid(), ProductCode = "TAKEN" });
        var request = f.UpdateRequest();
        request.UpdateItems[0].Data.ProductCode = " taken ";

        var result = await f.Service.BatchSaveAsync(request);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(0, f.Writes);
        Assert.AreEqual("OLD", f.Committed.Single(row => row.Uid == f.NeutralId).ProductCode);
        AssertTransaction(f, committed: false);
    }

    /// <summary>观察后旧父被其他请求改变，锁后重读必须拒绝过期归属，不查编码或写入，并回滚本次事务。</summary>
    [TestMethod]
    public async Task ReparentedAfterObservation_ReturnsBusinessBlockedWithoutWrites()
    {
        var f = new Fixture { ChangeParentAfterObservation = true };
        var result = await f.Service.BatchSaveAsync(f.UpdateRequest());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        CollectionAssert.AreEqual(new[] { "observe", "color", "master", "neutral" }, f.Events.ToArray());
        Assert.AreEqual(0, f.Writes);
        Assert.AreEqual(f.ConcurrentMasterId, f.Committed.Single().ProductMasterId,
            "本事务回滚不能抹去模拟的外部已提交归属变化。");
        CollectionAssert.AreEqual(new[] { f.OldMasterId, f.NewMasterId }.OrderBy(x => x).ToArray(), f.MasterRequests.Single());
        AssertTransaction(f, committed: false);
    }

    /// <summary>锁后发现更新或删除版本冲突，必须在任何写入和编码占用查询前返回版本冲突。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VersionChangedAfterObservation_RollsBackWithoutWrites(bool deleting)
    {
        var f = new Fixture { ChangeVersionAfterObservation = true };
        var result = await f.Service.BatchSaveAsync(deleting ? f.DeleteRequest() : f.UpdateRequest());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual(0, f.Writes);
        Assert.IsFalse(f.Events.Contains("codes"));
        Assert.AreEqual(2, f.Committed.Single().Version);
        AssertTransaction(f, committed: false);
    }

    /// <summary>仅删除也先锁观察到的旧父再锁子行，空颜色集合不产生颜色锁，最终仅软删目标行。</summary>
    [TestMethod]
    public async Task DeleteOnly_LocksObservedOldMasterWithoutInventingColorLocks()
    {
        var f = new Fixture();
        var result = await f.Service.BatchSaveAsync(f.DeleteRequest());
        Assert.IsTrue(result.Success, result.Message);
        CollectionAssert.AreEqual(new[] { "observe", "master", "neutral", "codes", "delete" }, f.Events.ToArray());
        Assert.IsTrue(f.ColorRequests.All(ids => ids.Length == 0));
        CollectionAssert.AreEqual(new[] { f.OldMasterId }, f.MasterRequests.Single());
        Assert.AreEqual(1, f.Writes);
        Assert.IsTrue(f.Committed.Single().IsDeleted);
        AssertTransaction(f, committed: true);
    }

    /// <summary>换父更新返回精确占用、并发或其他失败时撤销前序新增；技术异常仍返回 Unexpected 且不执行后续删除。</summary>
    [TestMethod]
    [DataRow(false, "WRITE_FAILED", ServiceErrorCodes.BusinessBlocked)]
    [DataRow(true, "WRITE_FAILED", ServiceErrorCodes.Unexpected)]
    [DataRow(false, "PRODUCT_NEUTRAL_CODE_OCCUPIED", ServiceErrorCodes.Validation)]
    [DataRow(false, "CONCURRENCY_CONFLICT", ServiceErrorCodes.VersionConflict)]
    public async Task SecondWriteFailure_RollsBackEarlierInsert(bool throws, string writeCode, string expectedCode)
    {
        var f = new Fixture { FailWriteNumber = 2, ThrowOnWriteFailure = throws, WriteErrorCode = writeCode };
        var request = f.UpdateRequest();
        request.CreateItems.Add(new() { Data = new()
        { ProductMasterId = f.NewMasterId, ColorId = f.NewColorId, ProductCode = "NEW" } });
        var deleting = new ProductNeutral { Uid = Guid.NewGuid(), ProductMasterId = f.OldMasterId, ProductCode = "DELETE", ColorId = f.OldColorId };
        f.Committed.Add(deleting);
        request.DeleteItems.Add(new() { Uid = deleting.Uid, Version = 1 });
        var result = await f.Service.BatchSaveAsync(request);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(expectedCode, result.Error.Code);
        Assert.AreEqual(2, f.Writes);
        Assert.AreEqual(1, f.SuccessfulWrites);
        Assert.IsFalse(f.Events.Contains("delete"));
        CollectionAssert.AreEqual(new[] { "OLD", "DELETE" }, f.Committed.Select(x => x.ProductCode).ToArray());
        Assert.AreEqual(f.OldMasterId, f.Committed[0].ProductMasterId);
        Assert.AreEqual(1, f.Committed[0].Version);
        Assert.IsTrue(f.Committed.All(x => !x.IsDeleted));
        AssertTransaction(f, committed: false);
    }

    /// <summary>每条路径结束都提交或回滚一次、释放事务一次并清理会话引用。</summary>
    private static void AssertTransaction(Fixture f, bool committed)
    {
        Assert.AreEqual(1, f.Session.ConnectionStub.Begins);
        Assert.AreEqual(committed ? 1 : 0, f.Session.ConnectionStub.Tx.Commits);
        Assert.AreEqual(committed ? 0 : 1, f.Session.ConnectionStub.Tx.Rollbacks);
        Assert.AreEqual(1, f.Session.ConnectionStub.Tx.Disposals);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>受控仓储状态区分外部已提交数据与本事务暂存写入；观察和重读返回独立快照。</summary>
    private sealed class Fixture
    {
        public Guid NeutralId { get; } = Guid.NewGuid();
        public Guid OldMasterId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public Guid NewMasterId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000002");
        public Guid ConcurrentMasterId { get; } = Guid.NewGuid();
        public Guid OldColorId { get; } = Guid.NewGuid();
        public Guid NewColorId { get; } = Guid.NewGuid();
        public bool ChangeParentAfterObservation { get; set; }
        public bool ChangeVersionAfterObservation { get; set; }
        public int FailWriteNumber { get; set; }
        public bool ThrowOnWriteFailure { get; set; }
        public string WriteErrorCode { get; set; } = "WRITE_FAILED";
        public int Writes { get; private set; }
        public int SuccessfulWrites { get; private set; }
        public List<string> Events { get; } = [];
        public List<Guid[]> ColorRequests { get; } = [];
        public List<Guid[]> MasterRequests { get; } = [];
        public Guid[] ObservedIds { get; private set; } = [];
        public Guid[] LockedIds { get; private set; } = [];
        public List<ProductNeutral> Committed { get; private set; }
        private List<ProductNeutral> _pending = [];
        public SessionStub Session { get; } = new();
        public ProductNeutralBatchMaintenanceService Service { get; }

        /// <summary>连接仅提供事务回调，所有仓储调用必须共享服务开启的同一事务。</summary>
        public Fixture()
        {
            Committed = [new ProductNeutral { Uid = NeutralId, ProductCode = "OLD", ProductMasterId = OldMasterId, ColorId = OldColorId, IsMainImage = true }];
            Session.ConnectionStub.OnBegin = () => _pending = Committed.Select(Copy).ToList();
            Session.ConnectionStub.Tx.OnCommit = () => Committed = _pending.Select(Copy).ToList();
            Session.ConnectionStub.Tx.OnRollback = () => _pending.Clear();
            Service = new ProductNeutralBatchMaintenanceService(
                Proxy<IProductNeutralManagerRepository>(NeutralCall),
                Proxy<IProductMasterRepository>(MasterCall), Proxy<IProductColorRepository>(ColorCall),
                new ServiceBaseContext(Session, NullLoggerFactory.Instance));
        }

        /// <summary>提交换父和换颜色更新；主图不在管理写入 DTO 中，必须从重读实体保留。</summary>
        public ProductNeutralManagerBatchSaveRequest UpdateRequest() => new()
        { UpdateItems = [new() { Uid = NeutralId, Version = 1, Data = new() { ProductMasterId = NewMasterId, ColorId = NewColorId, ProductCode = " OLD " } }] };

        /// <summary>只提交删除，不包含颜色或新父型号输入。</summary>
        public ProductNeutralManagerBatchSaveRequest DeleteRequest() => new()
        { DeleteItems = [new() { Uid = NeutralId, Version = 1 }] };

        /// <summary>记录颜色集合，只有非空集合构成颜色锁，禁止访问其他颜色操作。</summary>
        private object ColorCall(string method, object?[] args)
        {
            SharedTransaction();
            Assert.AreEqual("GetByIdListAsync", method);
            Assert.IsTrue((bool)args[1]!);
            var ids = ((IEnumerable<Guid>)args[0]!).ToArray();
            ColorRequests.Add(ids);
            if (ids.Length > 0) Events.Add("color");
            return Task.FromResult<IReadOnlyList<ProductColor>>(ids.Select(id => new ProductColor { Uid = id }).ToArray());
        }

        /// <summary>锁父集合时注入外部归属或版本变更，子行重读必须观察到变化；不允许写父记录。</summary>
        private object MasterCall(string method, object?[] args)
        {
            SharedTransaction();
            Assert.AreEqual("GetByIdListAsync", method);
            Assert.IsTrue((bool)args[1]!);
            var ids = ((IEnumerable<Guid>)args[0]!).ToArray();
            MasterRequests.Add(ids);
            Events.Add("master");
            foreach (var row in Committed.Concat(_pending).Where(x => x.Uid == NeutralId))
            {
                if (ChangeParentAfterObservation) row.ProductMasterId = ConcurrentMasterId;
                if (ChangeVersionAfterObservation) row.Version = 2;
            }
            return Task.FromResult<IReadOnlyList<ProductMaster>>(ids.Select(id => new ProductMaster { Uid = id }).ToArray());
        }

        /// <summary>观察及锁重读分别取快照；写入只改暂存数据，受控失败可以发生在已成功新增之后。</summary>
        private object NeutralCall(string method, object?[] args)
        {
            SharedTransaction();
            if (method == "GetByIdListAsync")
            {
                var ids = ((IEnumerable<Guid>)args[0]!).ToArray();
                var locking = (bool)args[1]!;
                if (locking) LockedIds = ids; else ObservedIds = ids;
                Events.Add(locking ? "neutral" : "observe");
                return Task.FromResult<IReadOnlyList<ProductNeutral>>(_pending.Where(x => ids.Contains(x.Uid)).Select(Copy).ToArray());
            }
            if (method == "GetByProductCodesAsync")
            {
                Assert.IsTrue((bool)args[1]!);
                Events.Add("codes");
                // 返回事务快照中的真实占用者，验证跨父编码仍被识别，自己的原编码则可继续更新。
                var codes = ((IEnumerable<string>)args[0]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return Task.FromResult<IReadOnlyList<ProductNeutral>>(_pending.Where(row => codes.Contains(row.ProductCode)).Select(Copy).ToArray());
            }
            Assert.IsTrue(method is "AddAsync" or "UpdateAsync" or "DeleteAsync", method);
            Writes++;
            Events.Add(method == "AddAsync" ? "add" : method == "UpdateAsync" ? "update" : "delete");
            if (Writes == FailWriteNumber)
            {
                if (ThrowOnWriteFailure) throw new InvalidOperationException("受控写入故障");
                return Task.FromResult(RepositoryWriteResult.Fail(NeutralId, 1, WriteErrorCode, "受控写入失败"));
            }
            Guid uid;
            if (method == "DeleteAsync")
            {
                Assert.IsTrue((bool)args[2]!);
                uid = (Guid)args[0]!;
                _pending.Single(x => x.Uid == uid).IsDeleted = true;
            }
            else
            {
                var row = Copy((ProductNeutral)args[0]!);
                uid = row.Uid;
                if (method == "UpdateAsync") { _pending.RemoveAll(x => x.Uid == uid); row.Version++; }
                _pending.Add(row);
            }
            SuccessfulWrites++;
            return Task.FromResult(RepositoryWriteResult.Ok(uid, 1, 2));
        }

        /// <summary>每次仓储调用必须处于同一外层事务，不允许另开事务或在仓储提交。</summary>
        private void SharedTransaction() => Assert.AreSame(Session.ConnectionStub.Tx, Session.Transaction);

        /// <summary>复制本场景涉及的字段，防止观察结果共享对象导致归属变化比较失效。</summary>
        private static ProductNeutral Copy(ProductNeutral row) => new()
        { Uid = row.Uid, Version = row.Version, ProductMasterId = row.ProductMasterId, ColorId = row.ColorId, ProductCode = row.ProductCode, IsMainImage = row.IsMainImage, IsDeleted = row.IsDeleted };
    }

    /// <summary>创建仅响应声明方法的接口代理，禁止未预期仓储操作。</summary>
    private static T Proxy<T>(Func<string, object?[], object> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)proxy).Call = call;
        return proxy;
    }

    /// <summary>供运行时派生的受控仓储代理，不访问任何数据库。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;
        /// <summary>将接口方法和参数交给场景回调。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args ?? []);
    }

    /// <summary>直接实现核心会话契约，仅记录当前事务引用。</summary>
    private sealed class SessionStub : IDbSession
    {
        public ConnectionStub ConnectionStub { get; } = new();
        public IDbConnection Connection => ConnectionStub;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => throw new NotSupportedException();
    }

    /// <summary>禁止创建 SQL 命令，仅提供事务开始和状态记录。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public TransactionStub Tx { get; } = new();
        public Action OnBegin { get; set; } = () => { };
        public int Begins { get; private set; }
        /// <summary>开始一次暂存事务并记录调用次数。</summary>
        public IDbTransaction BeginTransaction() { Begins++; OnBegin(); return Tx; }
        /// <summary>隔离级别重载沿用同一离线事务。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel level) => BeginTransaction();
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

    /// <summary>提交发布暂存数据，回滚丢弃本事务写入，释放仅记录次数。</summary>
    private sealed class TransactionStub : IDbTransaction
    {
        public Action OnCommit { get; set; } = () => { };
        public Action OnRollback { get; set; } = () => { };
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public int Disposals { get; private set; }
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        /// <summary>发布暂存变更并记录提交次数。</summary>
        public void Commit() { Commits++; OnCommit(); }
        /// <summary>丢弃暂存变更，不撤销外部已提交模拟状态。</summary>
        public void Rollback() { Rollbacks++; OnRollback(); }
        /// <summary>记录服务是否释放了事务。</summary>
        public void Dispose() => Disposals++;
    }
}
