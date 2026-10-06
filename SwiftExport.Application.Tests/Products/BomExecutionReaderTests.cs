using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.Execution;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Data;
using System.Runtime.CompilerServices;

namespace SwiftExport.Application.Tests.Products;

/// <summary>验证读取端口的事务前置条件与完整可达读取；数据库锁协议由当前PG集成测试验收。</summary>
[TestClass]
public sealed class BomExecutionReaderTests
{
    /// <summary>调用方未开启事务时必须在访问仓储之前失败，不能降级为多次无锁读取。</summary>
    [TestMethod]
    public async Task Reader_RejectsMissingTransactionBeforeQuery()
    {
        var repository = new RepositoryStub();
        var reader = Reader(repository, withTransaction: false);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync(Guid.NewGuid()));

        StringAssert.Contains(error.Message, "事务");
        Assert.AreEqual(0, repository.ReadCount);
    }

    /// <summary>有事务时包括终端组件的空BOM范围也必须读取，根没有组成才可声明无BOM。</summary>
    [TestMethod]
    public async Task Reader_ReadsTerminalRangesAndFailsMissingChild()
    {
        var root = Product("ROOT");
        root.BomNo = "BOM-ROOT";
        root.BomVersion = 1;
        var child = Product("CHILD");
        var line = new Bom
        {
            ParentProductId = root.Uid, ComponentProductId = child.Uid, BomNo = root.BomNo,
            BomVersion = 1, ParentSkuSnapshot = root.Sku, ComponentSkuSnapshot = child.Sku
        };
        var repository = new RepositoryStub
        {
            Products = new() { [root.Uid] = root, [child.Uid] = child },
            Lines = new() { [root.Uid] = [line] }
        };
        var snapshot = await Reader(repository, true).ReadAsync(root.Uid);
        Assert.AreEqual(1, snapshot.Nodes.Count);
        CollectionAssert.AreEquivalent(new[] { root.Uid, child.Uid }, repository.RangeReads.ToArray());

        repository.Products.Remove(child.Uid);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Reader(repository, true).ReadAsync(root.Uid));
        StringAssert.Contains(error.Message, "不存在");
    }

    /// <summary>测试只模拟会话中的事务引用，不构造连接、不读取连接字符串或访问业务库。</summary>
    private static BomExecutionReader Reader(RepositoryStub repository, bool withTransaction)
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        session.Transaction = withTransaction ? new TransactionStub() : null;
        return new BomExecutionReader(new ServiceBaseContext(session, NullLoggerFactory.Instance), repository);
    }

    private static Product Product(string sku) => new()
    {
        Sku = sku, MaterialType = "零件", SupplyType = "采购", Unit = "PCS", IsActive = true
    };

    /// <summary>只提供明确测试图，并记录终端范围读取，任何数据库连接都不参与测试。</summary>
    private sealed class RepositoryStub : IBomExecutionRepository
    {
        public Dictionary<Guid, Product> Products { get; set; } = [];
        public Dictionary<Guid, IReadOnlyList<Bom>> Lines { get; set; } = [];
        public List<Guid> RangeReads { get; } = [];
        public int ReadCount { get; private set; }

        /// <summary>返回测试产品，缺失身份保持为空。</summary>
        public Task<Product?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(Products.GetValueOrDefault(productId));
        }

        /// <summary>记录每个父产品范围读取，终端节点明确返回空范围。</summary>
        public Task<IReadOnlyList<Bom>> GetLinesAsync(Guid parentProductId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            RangeReads.Add(parentProductId);
            return Task.FromResult(Lines.GetValueOrDefault(parentProductId) ?? (IReadOnlyList<Bom>)[]);
        }

        /// <summary>提供测试唯一启用单位。</summary>
        public Task<IReadOnlyList<SysDictData>> GetUnitsAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<SysDictData>>([new() { DictCode = "Unit", DataNameEn = "PCS", IsEnabled = true }]);
        }
    }

    /// <summary>事务标记替身；读取器若试图提交或回滚调用方事务，立即使测试失败。</summary>
    private sealed class TransactionStub : IDbTransaction
    {
        public IDbConnection Connection => throw new AssertFailedException("读取器不应创建或访问测试数据库连接。");
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        /// <summary>禁止读取器提交调用方事务。</summary>
        public void Commit() => throw new AssertFailedException("读取器不能提交调用方事务。");
        /// <summary>禁止读取器回滚调用方事务。</summary>
        public void Rollback() => throw new AssertFailedException("读取器不能回滚调用方事务。");
        /// <summary>替身不持有数据库资源。</summary>
        public void Dispose() { }
    }

}
