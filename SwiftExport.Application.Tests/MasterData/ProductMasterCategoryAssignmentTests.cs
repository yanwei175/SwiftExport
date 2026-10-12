using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证主型号批量归类的校验、乐观并发和整批事务回滚。</summary>
[TestClass]
public sealed class ProductMasterCategoryAssignmentTests
{
    /// <summary>目标分类不存在时不得写入任何主型号，并回滚当前事务。</summary>
    [TestMethod]
    public async Task MissingCategory_ShouldRejectAndRollbackWithoutWrites()
    {
        using var fixture = new Fixture { CategoryExists = false };

        var result = await fixture.Service.AssignCategoryAsync(fixture.CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.WriteCount);
        Assert.IsTrue(fixture.Connection.Transaction.WasRolledBack);
        Assert.IsFalse(fixture.Connection.Transaction.WasCommitted);
        fixture.AssertOriginalValues();
    }

    /// <summary>后续主型号发生并发冲突时，必须撤销本批次已经成功的前序更新。</summary>
    [TestMethod]
    public async Task LaterVersionConflict_ShouldRollbackEarlierUpdates()
    {
        using var fixture = new Fixture { FailWriteNumber = 2 };

        var result = await fixture.Service.AssignCategoryAsync(fixture.CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.AreEqual(2, fixture.WriteCount);
        Assert.IsTrue(fixture.Connection.Transaction.WasRolledBack);
        Assert.IsFalse(fixture.Connection.Transaction.WasCommitted);
        fixture.AssertOriginalValues();
    }

    /// <summary>全部主型号写入成功时提交事务，并逐项返回递增后的版本。</summary>
    [TestMethod]
    public async Task ValidBatch_ShouldCommitAndReturnNewVersions()
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.AssignCategoryAsync(fixture.CreateRequest());

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.Data.UpdatedCount);
        CollectionAssert.AreEqual(new[] { 4, 8 }, result.Data.Items.Select(item => item.NewVersion).ToArray());
        Assert.IsTrue(fixture.Connection.Transaction.WasCommitted);
        Assert.IsFalse(fixture.Connection.Transaction.WasRolledBack);
        Assert.IsTrue(fixture.Masters.All(master => master.CategoryId == fixture.TargetCategoryId));
    }

    /// <summary>构造受控仓储和可回滚内存事务，避免测试依赖 SQL Server。</summary>
    private sealed class Fixture : IDisposable
    {
        private List<ProductMaster>? _snapshot;

        public Fixture()
        {
            Masters =
            [
                new ProductMaster { Uid = Guid.NewGuid(), Version = 3, MasterSku = "MASTER-1", CategoryId = Guid.NewGuid() },
                new ProductMaster { Uid = Guid.NewGuid(), Version = 7, MasterSku = "MASTER-2", CategoryId = Guid.NewGuid() }
            ];
            Connection = new ConnectionStub();
            Connection.Transaction.OnBegin = () => _snapshot = Masters.Select(Clone).ToList();
            Connection.Transaction.OnRollback = () => Masters = _snapshot!.Select(Clone).ToList();
            var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(session, Connection);
            Session = session;
            Service = new ProductMasterMaintenanceService(
                Proxy<IProductMasterRepository>(MasterCall),
                Proxy<IProductNeutralRepository>(UnexpectedCall),
                Proxy<IProductCategoryRepository>(CategoryCall),
                Proxy<ISupplierRepository>(UnexpectedCall),
                Proxy<IProductColorRepository>(UnexpectedCall),
                Proxy<IProductRepository>(UnexpectedCall),
                new EmptyProductAttributeRepository(),
                new ServiceBaseContext(Session, NullLoggerFactory.Instance));
        }

        public Guid TargetCategoryId { get; } = Guid.NewGuid();
        public bool CategoryExists { get; set; } = true;
        public int FailWriteNumber { get; set; }
        public int WriteCount { get; private set; }
        public List<ProductMaster> Masters { get; private set; }
        public ConnectionStub Connection { get; }
        public DbSession Session { get; }
        public ProductMasterMaintenanceService Service { get; }

        /// <summary>创建包含全部主型号当前版本的批量归类请求。</summary>
        public AssignProductMastersToCategoryRequest CreateRequest() => new()
        {
            TargetCategoryId = TargetCategoryId,
            Items = Masters.Select(master => new AssignProductMasterToCategoryItem
            {
                ProductMasterId = master.Uid,
                ExpectedVersion = master.Version
            }).ToList()
        };

        /// <summary>确认失败事务没有遗留分类或版本变更。</summary>
        public void AssertOriginalValues()
        {
            Assert.IsNotNull(_snapshot);
            foreach (var original in _snapshot)
            {
                var current = Masters.Single(master => master.Uid == original.Uid);
                Assert.AreEqual(original.CategoryId, current.CategoryId);
                Assert.AreEqual(original.Version, current.Version);
            }
        }

        private object CategoryCall(string method, object?[] _)
        {
            Assert.AreEqual("GetByIdListAsync", method);
            Assert.IsNotNull(Session.Transaction);
            return Task.FromResult<IReadOnlyList<ProductCategory>>(
                CategoryExists ? [new ProductCategory { Uid = TargetCategoryId }] : []);
        }

        private object MasterCall(string method, object?[] args)
        {
            Assert.IsNotNull(Session.Transaction);
            if (method == "GetByIdListAsync")
                return Task.FromResult<IReadOnlyList<ProductMaster>>(Masters.Select(Clone).ToList());
            Assert.AreEqual("AssignCategoryAsync", method);
            WriteCount++;
            if (WriteCount == FailWriteNumber)
                return Task.FromResult(RepositoryWriteResult.Fail(
                    (Guid)args[0]!, (int)args[1]!, "CONCURRENCY_CONFLICT", "版本冲突"));

            var master = Masters.Single(item => item.Uid == (Guid)args[0]!);
            master.CategoryId = (Guid)args[2]!;
            master.Version++;
            return Task.FromResult(RepositoryWriteResult.Ok(master.Uid, (int)args[1]!, master.Version));
        }

        private static object UnexpectedCall(string method, object?[] _) =>
            throw new AssertFailedException($"主型号归类不应调用 {method}。");

        private static ProductMaster Clone(ProductMaster source) => new()
        {
            Uid = source.Uid,
            Version = source.Version,
            MasterSku = source.MasterSku,
            CategoryId = source.CategoryId
        };

        public void Dispose() => Session.Dispose();
    }

    private static T Proxy<T>(Func<string, object?[], object> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)proxy).Call = call;
        return proxy;
    }

    /// <summary>将接口调用路由到测试夹具中的受控实现。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;

        /// <summary>转发当前接口调用。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Call(targetMethod!.Name, args ?? []);
    }

    /// <summary>只提供事务生命周期的内存连接。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public TransactionStub Transaction { get; } = new();
        public IDbTransaction BeginTransaction() { Transaction.OnBegin(); return Transaction; }
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        public void Close() { }
        public void Open() { }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        public void Dispose() { }
    }

    /// <summary>记录提交和回滚，并在回滚时恢复内存快照。</summary>
    private sealed class TransactionStub : IDbTransaction
    {
        public Action OnBegin { get; set; } = () => { };
        public Action OnRollback { get; set; } = () => { };
        public bool WasCommitted { get; private set; }
        public bool WasRolledBack { get; private set; }
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() => WasCommitted = true;
        public void Rollback() { WasRolledBack = true; OnRollback(); }
        public void Dispose() { }
    }
}
