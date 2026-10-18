using System.Data;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Suppliers.Management;
using SwiftExport.Application.Services.Suppliers.SupplierProducts.Maintenance;
using SwiftExport.Application.Services.Suppliers.SupplierProducts.Query;
using SwiftExport.Application.Interfaces.Suppliers.Management;
using SwiftExport.Application.Interfaces.Suppliers.SupplierProducts.Query;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;
using SwiftExport.WebApi.Controllers.Suppliers.Management;
using SwiftExport.WebApi.Controllers.Suppliers.SupplierProducts;

namespace SwiftExport.Application.Tests.Suppliers;

/// <summary>验证供应商快照和产品维护边界；全部使用可观察仓储/事务替身，不访问数据库。</summary>
[TestClass]
public sealed class SupplierManagementServiceTests
{
    /// <summary>全量读取使用管理投影并贯通取消，技术错误只返回统一消息。</summary>
    [TestMethod]
    public async Task ManagementSnapshot_ForwardsCancellationAndSanitizesFailure()
    {
        using var cancel = new CancellationTokenSource();
        IReadOnlyList<SupplierDto> rows = [new() { Uid = Guid.NewGuid(), Version = 4, SupplierCode = "S", TaxRate = null }];
        var repository = Proxy<ISupplierRepository>((method, args) =>
        {
            Assert.AreEqual("GetAllForManagementAsync", method.Name); Assert.AreEqual(cancel.Token, args![0]);
            return Task.FromResult(rows);
        });
        var session = Proxy<IDbSession>((_, _) => throw new NotSupportedException());
        var service = new SupplierService(repository, null!, new(session, NullLoggerFactory.Instance));
        var result = await service.GetAllAsync(cancel.Token);
        Assert.IsTrue(result.Success); Assert.AreSame(rows, result.Data);
        ((Probe)(object)repository).Handler = (_, _) => throw new InvalidOperationException("SQL_SECRET");
        var failure = await service.GetAllAsync(cancel.Token);
        Assert.IsFalse(failure.Success); Assert.AreEqual(ServiceErrorCodes.Unexpected, failure.Error.Code);
        Assert.IsFalse(failure.Message.Contains("SQL_SECRET", StringComparison.Ordinal));
        cancel.Cancel();
        ((Probe)(object)repository).Handler = (_, _) => Task.FromCanceled<IReadOnlyList<SupplierDto>>(cancel.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetAllAsync(cancel.Token));
    }

    /// <summary>HTTP 管理快照与产品分页保留原路由和取消令牌，不在 Controller 处理业务。</summary>
    [TestMethod]
    public async Task Controllers_ForwardReadCancellation()
    {
        using var cancel = new CancellationTokenSource();
        var service = Proxy<ISupplierService>((method, args) =>
        {
            Assert.AreEqual("GetAllAsync", method.Name); Assert.AreEqual(cancel.Token, args![0]);
            return Task.FromResult(ServiceResult<IReadOnlyList<SupplierDto>>.Ok([]));
        });
        Assert.IsTrue((await new SupplierController(service).GetAll(cancel.Token)).Success);
        var request = new SupplierProductSearchRequest { SupplierId = Guid.NewGuid() };
        var query = Proxy<ISupplierProductQueryService>((method, args) =>
        {
            Assert.AreEqual("GetPagedAsync", method.Name); Assert.AreSame(request, args![0]); Assert.AreEqual(cancel.Token, args[1]);
            return Task.FromResult(ServiceResult<PagedResponseBase<SupplierProductDto>>.Ok(new()));
        });
        Assert.IsTrue((await new SupplierProductController(query, null!, null!).GetPaged(request, cancel.Token)).Success);
    }

    /// <summary>产品分页服务保持供应商必填及技术异常分类，请求取消原样抛出。</summary>
    [TestMethod]
    public async Task ProductQuery_ForwardsCancellationAndKeepsSupplierScope()
    {
        using var cancel = new CancellationTokenSource(); var calls = 0;
        var request = new SupplierProductSearchRequest { SupplierId = Guid.NewGuid() };
        var repository = Proxy<ISupplierProductRepository>((method, args) =>
        {
            calls++; Assert.AreEqual("GetPagedAsync", method.Name); Assert.AreSame(request, args![0]); Assert.AreEqual(cancel.Token, args[1]);
            return Task.FromResult(new PagedResponseBase<SupplierProductDto>());
        });
        var session = Proxy<IDbSession>((_, _) => throw new NotSupportedException());
        var service = new SupplierProductQueryService(repository, new(session, NullLoggerFactory.Instance));
        Assert.IsFalse((await service.GetPagedAsync(new(), cancel.Token)).Success); Assert.AreEqual(0, calls);
        Assert.IsTrue((await service.GetPagedAsync(request, cancel.Token)).Success); Assert.AreEqual(1, calls);
        ((Probe)(object)repository).Handler = (_, _) => throw new InvalidOperationException("SQL_SECRET");
        var failure = await service.GetPagedAsync(request, cancel.Token);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, failure.Error.Code); Assert.IsFalse(failure.Message.Contains("SQL_SECRET", StringComparison.Ordinal));
        cancel.Cancel();
        ((Probe)(object)repository).Handler = (_, _) => Task.FromCanceled<PagedResponseBase<SupplierProductDto>>(cancel.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetPagedAsync(request, cancel.Token));
    }

    /// <summary>关系读取失败不从单条或批量入口泄露异常；预检失败没有业务写入。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RelationFailure_IsContainedBeforeAnyWrite(bool batch)
    {
        var fixture = new ProductFixture();
        fixture.SupplierRead = () => throw new InvalidOperationException("SQL_SECRET");
        IServiceResult result = batch
            ? await fixture.Service.BatchSaveAsync(new() { CreateItems = [new() { Data = fixture.Dto() }] })
            : await fixture.Service.AddAsync(new() { Data = fixture.Dto() });
        Assert.IsFalse(result.Success); Assert.IsFalse(result.Message.Contains("SQL_SECRET", StringComparison.Ordinal));
        Assert.AreEqual(0, fixture.Writes); Assert.AreEqual(0, fixture.Begins);
    }

    /// <summary>原有事务不得被批量入口覆盖、提交、回滚或释放。</summary>
    [TestMethod]
    public async Task Batch_DoesNotTakeOverCallerTransaction()
    {
        var fixture = new ProductFixture(); fixture.Current = fixture.Transaction;
        var result = await fixture.Service.BatchSaveAsync(new() { CreateItems = [new() { Data = fixture.Dto() }] });
        Assert.IsFalse(result.Success); Assert.AreSame(fixture.Transaction, fixture.Current);
        Assert.AreEqual(0, fixture.Begins); Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Commits + fixture.Rollbacks + fixture.Disposals);
    }

    /// <summary>单条和批量均根据身份回填真实供应商/物料编码，空关联不造占位产品。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Save_NormalizesRelationsAndAllowsNullProduct(bool batch, bool linked)
    {
        var fixture = new ProductFixture(); var dto = fixture.Dto();
        dto.SupplierCode = "FORGED"; dto.ProductSku = "STALE"; dto.SkuId = linked ? fixture.ProductId : null;
        var success = batch
            ? (await fixture.Service.BatchSaveAsync(new() { CreateItems = [new() { Data = dto }] })).Success
            : (await fixture.Service.AddAsync(new() { Data = dto })).Success;
        Assert.IsTrue(success); Assert.AreEqual(1, fixture.Writes);
        Assert.AreEqual("CURRENT-S", fixture.LastWrite!.SupplierCode);
        Assert.AreEqual(linked ? "CURRENT-P" : "", fixture.LastWrite.ProductSku);
        Assert.AreEqual(linked ? fixture.ProductId : (Guid?)null, fixture.LastWrite.SkuId);
        Assert.AreEqual("OWN", fixture.LastWrite.SupplierSku);
        Assert.AreEqual(linked ? 1 : 0, fixture.ProductReads);
    }

    /// <summary>批内同供应商重复料号及负价在开启事务前阻断，不影响合法空价格。</summary>
    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("price")]
    [DataRow("empty-product")]
    [DataRow("missing-supplier")]
    [DataRow("missing-product")]
    [DataRow("mixed-duplicate")]
    [DataRow("invalid-version")]
    public async Task Batch_InvalidRowsDoNotWrite(string scenario)
    {
        var fixture = new ProductFixture(); var dto = fixture.Dto();
        var request = new SupplierProductBatchSaveRequest { CreateItems = [new() { Data = dto }] };
        if (scenario == "duplicate") { var other = fixture.Dto(); other.SupplierSku = " own "; request.CreateItems.Add(new() { Data = other }); }
        if (scenario == "price") dto.UnitPrice = -1;
        if (scenario == "empty-product") dto.SkuId = Guid.Empty;
        if (scenario == "missing-supplier") fixture.SupplierRead = () => Task.FromResult<IReadOnlyList<Supplier>>([]);
        if (scenario == "missing-product") dto.SkuId = Guid.NewGuid();
        if (scenario is "mixed-duplicate" or "invalid-version")
        {
            var other = fixture.Dto(); if (scenario == "invalid-version") other.SupplierSku = "OTHER";
            request.UpdateItems.Add(new() { Uid = Guid.NewGuid(), Version = scenario == "invalid-version" ? 0 : 2, Data = other });
        }
        Assert.IsFalse((await fixture.Service.BatchSaveAsync(request)).Success);
        Assert.AreEqual(0, fixture.Writes); Assert.AreEqual(0, fixture.Begins);
    }

    /// <summary>批次中途写入失败必须整批回滚，并释放本层创建的事务。</summary>
    [TestMethod]
    public async Task Batch_MiddleFailureRollsBackAndClearsOwnedTransaction()
    {
        var fixture = new ProductFixture { FailWriteNumber = 2 };
        var second = fixture.Dto(); second.SupplierSku = "OTHER";
        var result = await fixture.Service.BatchSaveAsync(new() { CreateItems = [new() { Data = fixture.Dto() }, new() { Data = second }] });
        Assert.IsFalse(result.Success); Assert.AreEqual(2, fixture.Writes);
        Assert.AreEqual(1, fixture.Rollbacks); Assert.AreEqual(0, fixture.Commits);
        Assert.AreEqual(1, fixture.Disposals); Assert.IsNull(fixture.Current);
    }

    /// <summary>事务创建失败和清理异常均保持统一错误，且不残留共享事务。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Batch_TransactionFailureDoesNotEscape(bool beginFailure)
    {
        var fixture = new ProductFixture { BeginThrows = beginFailure, CleanupThrows = !beginFailure, FailWriteNumber = 1 };
        var result = await fixture.Service.BatchSaveAsync(new() { CreateItems = [new() { Data = fixture.Dto() }] });
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.IsNull(fixture.Current); Assert.AreEqual(beginFailure ? 0 : 1, fixture.Writes);
        Assert.AreEqual(beginFailure ? 0 : 1, fixture.Disposals);
    }

    /// <summary>更新以请求外层身份/版本写入，过期版本保持仓储逐行冲突回执。</summary>
    [TestMethod]
    public async Task Update_KeepsOuterIdentityAndConflictReceipt()
    {
        var fixture = new ProductFixture { FailWriteNumber = 1 }; var dto = fixture.Dto();
        dto.Uid = Guid.NewGuid(); dto.Version = 999;
        var id = Guid.NewGuid();
        var result = await fixture.Service.UpdateAsync(new() { Uid = id, Version = 2, ClientKey = "row", Data = dto });
        Assert.IsTrue(result.Success); Assert.IsFalse(result.Data.Success);
        Assert.AreEqual("CONCURRENCY_CONFLICT", result.Data.ErrorCode); Assert.AreEqual("row", result.Data.ClientKey);
        Assert.AreEqual(id, fixture.LastWrite!.Uid); Assert.AreEqual(2, fixture.LastWrite.Version);
    }

    /// <summary>创建仅允许测试显式声明的仓储和事务操作。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, Probe>(); ((Probe)(object)proxy).Handler = handler; return proxy;
    }

    /// <summary>记录服务对仓储和事务的调用，不提供网络或 SQL 能力。</summary>
    public class Probe : DispatchProxy
    {
        /// <summary>由具体场景提供允许执行的行为。</summary>
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        /// <summary>执行测试声明的行为。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    /// <summary>供应商产品维护的事务/仓储观察夹具；只模拟写入结果，不声称真实 PG 并发验收。</summary>
    private sealed class ProductFixture
    {
        /// <summary>本场景允许引用的供应商和物料身份。</summary>
        public readonly Guid SupplierId = Guid.NewGuid();
        /// <summary>本场景的有效公司物料身份。</summary>
        public readonly Guid ProductId = Guid.NewGuid();
        /// <summary>观察写入、事务与物料读取次数。</summary>
        public int Writes, Begins, Commits, Rollbacks, Disposals, ProductReads;
        /// <summary>指定哪次写入返回版本冲突，零表示全部成功。</summary>
        public int FailWriteNumber;
        /// <summary>模拟事务开始或清理的基础设施失败。</summary>
        public bool BeginThrows, CleanupThrows;
        /// <summary>模拟当前请求共享事务。</summary>
        public IDbTransaction? Current;
        /// <summary>本层可以创建的事务替身。</summary>
        public readonly IDbTransaction Transaction;
        /// <summary>保留实际交给仓储的最后一条实体。</summary>
        public SupplierProduct? LastWrite;
        /// <summary>供应商关联读取行为，异常场景可替换。</summary>
        public Func<Task<IReadOnlyList<Supplier>>> SupplierRead;
        public SupplierProductMaintenanceService Service { get; }

        /// <summary>建立同一会话中的事务与仓储替身。</summary>
        public ProductFixture()
        {
            SupplierRead = () => Task.FromResult<IReadOnlyList<Supplier>>([new() { Uid = SupplierId, SupplierCode = "CURRENT-S", SupplierName = "供应商", DocRelateNo = "S" }]);
            Transaction = Proxy<IDbTransaction>((method, _) =>
            {
                switch (method.Name) { case "Commit": Commits++; break; case "Rollback": Rollbacks++; if (CleanupThrows) throw new InvalidOperationException("rollback"); break; case "Dispose": Disposals++; if (CleanupThrows) throw new InvalidOperationException("dispose"); break; default: throw new NotSupportedException(method.Name); }
                return null;
            });
            var connection = Proxy<IDbConnection>((method, _) =>
            { Assert.AreEqual("BeginTransaction", method.Name); Begins++; if (BeginThrows) throw new InvalidOperationException("begin"); return Transaction; });
            var session = Proxy<IDbSession>((method, args) => method.Name switch
            {
                "get_Connection" => connection, "get_Transaction" => Current,
                "set_Transaction" => SetTransaction((IDbTransaction?)args![0]), _ => throw new NotSupportedException(method.Name)
            });
            var suppliers = Proxy<ISupplierRepository>((method, _) =>
            { Assert.AreEqual("GetByIdListAsync", method.Name); return SupplierRead(); });
            var products = Proxy<IProductRepository>((method, _) =>
            {
                Assert.AreEqual("GetByIdListAsync", method.Name); ProductReads++;
                return Task.FromResult<IReadOnlyList<Product>>([new() { Uid = ProductId, Sku = "CURRENT-P" }]);
            });
            var repository = Proxy<ISupplierProductRepository>((method, args) => method.Name switch
            {
                "GetExistingSupplierSkuListAsync" or "GetSupplierSkusUsedByOtherRecordsAsync" => Task.FromResult<IReadOnlyList<string>>([]),
                "GetExistingIdListAsync" => Task.FromResult<IReadOnlyList<Guid>>(((IEnumerable<Guid>)args![0]!).ToArray()),
                "GetByIdAsync" => Task.FromResult<SupplierProduct?>(new() { Uid = (Guid)args![0]!, Version = 3, SupplierSku = "OWN", SupplierCode = "CURRENT-S", ProductSku = "" }),
                "AddAsync" or "UpdateAsync" => Write((SupplierProduct)args![0]!),
                _ => throw new NotSupportedException(method.Name)
            });
            Service = new(repository, suppliers, products, new ServiceBaseContext(session, NullLoggerFactory.Instance));
        }

        /// <summary>设置共享事务引用，仅由服务控制。</summary>
        private object? SetTransaction(IDbTransaction? transaction) { Current = transaction; return null; }
        /// <summary>提供合法独立供应商资料，可由场景追加真实物料关联。</summary>
        public SupplierProductDto Dto() => new() { SupplierId = SupplierId, SupplierCode = "CURRENT-S", SupplierSku = " OWN " };
        /// <summary>模拟条件写入成功或版本冲突，保留服务传入的实际实体。</summary>
        private Task<RepositoryWriteResult> Write(SupplierProduct entity)
        {
            Writes++; LastWrite = entity;
            return Task.FromResult(Writes == FailWriteNumber
                ? RepositoryWriteResult.Fail(entity.Uid, entity.Version, "CONCURRENCY_CONFLICT", "版本已变化")
                : RepositoryWriteResult.Ok(entity.Uid, entity.Version, entity.Version + 1));
        }
    }
}
