using System.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Mapster;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products;
using SwiftExport.Application.Services.Products.Catalog.Maintenance;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Models.Products;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Products;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Products.Colors;
using SwiftExport.Infrastructure.Repositories.Suppliers.Management;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Data;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>只在保留专用库验证真实共享保存与快照读取；提交随机产品/组成后按本例身份清理，不写回执或既有资料。</summary>
[TestClass]
[TestCategory("PostgreSqlBomConcurrencyIntegration")]
public sealed class PostgreSqlBomConcurrencyIntegrationTests
{
    /// <summary>两个无 BOM 产品分别绑定共享组后会成环，真实产品保存服务不能双提交。</summary>
    [TestMethod]
    [Timeout(90000)]
    public async Task ConcurrentProductBindings_CannotCommitCombinedCycle()
    {
        await using var fixture = new Fixture();
        fixture.A.BomNo = null; fixture.A.BomVersion = 0;
        fixture.C.BomNo = null; fixture.C.BomVersion = 0;
        await fixture.SeedAsync();
        using var first = fixture.Open();
        using var second = fixture.Open();
        var gate = new ReadGate();
        var a = fixture.A.Adapt<ProductDto>(); a.BomNo = fixture.B.BomNo; a.BomVersion = 1;
        var c = fixture.C.Adapt<ProductDto>(); c.BomNo = fixture.D.BomNo; c.BomVersion = 3;
        var one = fixture.ProductService(first, gate).UpdateAsync(new() { Uid = fixture.A.Uid, Version = fixture.A.Version, Data = a });
        var two = fixture.ProductService(second, gate).UpdateAsync(new() { Uid = fixture.C.Uid, Version = fixture.C.Version, Data = c });
        var results = await Task.WhenAll(one, two);
        Assert.AreEqual(1, results.Count(result => result.Success));
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, results.Single(result => !result.Success).Error?.Code);
        Assert.AreEqual(2, gate.Arrivals);
        Assert.IsNull(first.Transaction); Assert.IsNull(second.Transaction);
    }

    /// <summary>产品删除与新增组件引用竞争，不能同时成功留下悬空关系。</summary>
    [TestMethod]
    [Timeout(90000)]
    public async Task ProductDeletionAgainstNewReference_CannotBothCommit()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync(withLines: false);
        using var first = fixture.Open();
        using var second = fixture.Open();
        var gate = new ReadGate();
        var one = fixture.ProductService(first, gate).DeleteAsync(new() { Uid = fixture.C.Uid, Version = fixture.C.Version, IsSoftDelete = false });
        var two = fixture.Service(second, gate).AddAsync(Request(fixture.A, fixture.C));
        var results = await Task.WhenAll(one, two);
        Assert.IsFalse(results.All(result => result.Success));
        Assert.IsTrue(results.Any(result => result.Success));
        Assert.AreEqual(2, gate.Arrivals);
        Assert.IsNull(first.Transaction); Assert.IsNull(second.Transaction);
        Assert.AreEqual(0, await fixture.Observer.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.bom b LEFT JOIN public.product p ON p.uid=b.component_product_id AND p.is_deleted=FALSE WHERE b.created_by=@Audit AND p.uid IS NULL", new { fixture.Audit }));
    }

    /// <summary>各自合法的 A→B 与 C→D 合并会形成环；两套真实服务及独立事务不能同时提交。</summary>
    [TestMethod]
    [Timeout(90000)]
    public async Task ConcurrentStructureSaves_CannotCommitCombinedCycle()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        using var first = fixture.Open();
        using var second = fixture.Open();
        var gate = new ReadGate();
        var one = fixture.Service(first, gate).AddAsync(Request(fixture.A, fixture.B));
        var two = fixture.Service(second, gate).AddAsync(Request(fixture.C, fixture.D));
        var results = await Task.WhenAll(one, two);
        Assert.AreEqual(1, results.Count(result => result.Success), "两项分别合法的保存不能合并成环。");
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, results.Single(result => !result.Success).Error?.Code);
        Assert.AreEqual(2, gate.Arrivals);
        Assert.IsNull(first.Transaction);
        Assert.IsNull(second.Transaction);
        var total = await fixture.Observer.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.bom WHERE created_by=@Audit", new { fixture.Audit });
        Assert.AreEqual(3, total, "两个初始组成之外只能提交一条新组成。");
    }

    /// <summary>读事务先取产品后遇另一连接保存组成，重复取组成仍为同一旧快照；新事务才看到更新。</summary>
    [TestMethod]
    [Timeout(90000)]
    public async Task SerializableReader_DoesNotMixCompositionSnapshots()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        using var reader = fixture.Open();
        using var writer = fixture.Open();
        using var transaction = reader.Connection.BeginTransaction(IsolationLevel.Serializable);
        reader.Transaction = transaction;
        try
        {
            var context = fixture.Context(reader);
            var product = await new PostgreSqlProductRepository(context).GetByIdAsync(fixture.B.Uid);
            Assert.IsNotNull(product);
            var repository = new PostgreSqlBomRepository(context);
            var before = (await repository.GetBomByBomNoAsync(product.BomNo!)).Single();
            var result = await fixture.Service(writer).UpdateAsync(new UpdateItemRequest<BomDto>
            {
                Uid = before.Uid, Version = before.Version,
                Data = new BomDto { BomNo = product.BomNo!, SubSkuId = fixture.C.Uid, Numerator = 7, Denominator = 1 }
            });
            Assert.IsTrue(result.Success, result.Message);
            var again = (await repository.GetBomByBomNoAsync(product.BomNo!)).Single();
            Assert.AreEqual(before.Numerator, again.Numerator);
            Assert.AreEqual(before.Version, again.Version);
            var current = (await new PostgreSqlBomRepository(fixture.Context(writer)).GetBomByBomNoAsync(product.BomNo!)).Single();
            Assert.AreEqual(7, current.Numerator);
            Assert.AreEqual(before.Version + 1, current.Version);
        }
        finally { transaction.Rollback(); reader.Transaction = null; }
    }

    /// <summary>构造真实保存请求，不直接调用泛型组成写入绕过业务校验。</summary>
    private static CreateItemRequest<BomDto> Request(Product parent, Product child) => new()
    { Data = new BomDto { BomNo = parent.BomNo!, SubSkuId = child.Uid, Numerator = 1, Denominator = 1 } };

    /// <summary>屏障只暂停读取结束点，不模拟 SQL 或事务；受影响产品锁集合互不交叉，避免屏障人为死锁。</summary>
    private sealed class ReadGate
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public int Arrivals => _arrivals;
        /// <summary>等待两个真实读取完成后同时继续，不替代数据库隔离。</summary>
        public async Task<IReadOnlyList<SharedBomGroup>> WaitAsync(Task<IReadOnlyList<SharedBomGroup>> read)
        {
            var rows = await read;
            if (Interlocked.Increment(ref _arrivals) == 2) _both.TrySetResult();
            await _both.Task.WaitAsync(TimeSpan.FromSeconds(20));
            return rows;
        }
    }

    /// <summary>透明转发所有真实仓储调用，仅提供确定性的事实读取屏障。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        internal IBomRepository Target = null!;
        internal Func<Task<IReadOnlyList<SharedBomGroup>>, Task<IReadOnlyList<SharedBomGroup>>> Pause = null!;
        /// <summary>保留被调用异常原类型，避免反射包装影响服务的数据库异常分类。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                var value = targetMethod!.Invoke(Target, args);
                return targetMethod.Name == nameof(IBomRepository.GetSharedSaveFactsAsync)
                    ? Pause((Task<IReadOnlyList<SharedBomGroup>>)value!) : value;
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }

    /// <summary>随机且独占的夹具身份限制清理范围；保留库连接校验复用已有测试入口。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        public string Audit { get; } = "__pgbom_" + Guid.NewGuid().ToString("N")[..12];
        public Product A { get; }
        public Product B { get; }
        public Product C { get; }
        public Product D { get; }
        public DbSession Observer { get; }
        /// <summary>生成本例独占身份并建立目标库观察连接。</summary>
        public Fixture()
        {
            A = Product("A"); B = Product("B"); C = Product("C"); D = Product("D");
            Observer = Open();
        }
        /// <summary>建立独立事务会话，不共享测试连接。</summary>
        public DbSession Open() => new(new PostgreSqlConnectionFactory(_connection));
        /// <summary>组装给定会话的仓储上下文及本例审计身份。</summary>
        public RepositoryBaseContext Context(DbSession session)
        {
            var user = new User(Audit);
            return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        }
        /// <summary>以同一 PG 会话组装真实 BOM 保存服务。</summary>
        public BomService Service(DbSession session, ReadGate? gate = null)
        {
            var context = Context(session);
            return new(Repository(context, gate), new PostgreSqlProductRepository(context), new ServiceBaseContext(session, NullLoggerFactory.Instance));
        }
        /// <summary>所有资料校验仓储使用真实 PG 实现，不用替身跳过产品业务规则。</summary>
        public ProductMaintenanceService ProductService(DbSession session, ReadGate gate)
        {
            var context = Context(session);
            return new(new PostgreSqlProductRepository(context), new ServiceBaseContext(session, NullLoggerFactory.Instance),
                new PostgreSqlProductColorRepository(context), new PostgreSqlSupplierRepository(context),
                new PostgreSqlWarehouseQueryRepository(context), Repository(context, gate), new PostgreSqlSysDictDataRepository(context));
        }
        /// <summary>生产查询与写入保持原样，仅可选包裹一次读取屏障。</summary>
        private static IBomRepository Repository(RepositoryBaseContext context, ReadGate? gate)
        {
            IBomRepository repository = new PostgreSqlBomRepository(context);
            if (gate is not null)
            {
                var proxy = DispatchProxy.Create<IBomRepository, RepositoryProxy>();
                var dispatch = (RepositoryProxy)(object)proxy;
                dispatch.Target = repository; dispatch.Pause = gate.WaitAsync; repository = proxy;
            }
            return repository;
        }
        /// <summary>一次事务提交四个随机产品及两条基础边，使两个测试连接读取相同已提交基线。</summary>
        public async Task SeedAsync(bool withLines = true)
        {
            using var transaction = Observer.Connection.BeginTransaction();
            Observer.Transaction = transaction;
            try
            {
                foreach (var product in new[] { A, B, C, D }) await InsertAsync(product);
                if (withLines) { await InsertAsync(Line(B, C)); await InsertAsync(Line(D, A)); }
                transaction.Commit();
            }
            finally { Observer.Transaction = null; }
        }
        private Product Product(string suffix) => new()
        { Uid = Guid.NewGuid(), Sku = Audit + suffix, BomNo = Audit + suffix, BomVersion = 1, SupplyType = "采购" };
        private static Bom Line(Product parent, Product child) => new()
        {
            Uid = Guid.NewGuid(), BomNo = parent.BomNo!, BomVersion = 1, ParentProductId = parent.Uid,
            ParentSkuSnapshot = parent.Sku, ComponentProductId = child.Uid, ComponentSkuSnapshot = child.Sku,
            LineNumber = 1, Numerator = 1, Denominator = 1
        };
        /// <summary>仅通过实体元数据插入本例随机夹具，不初始化或改写现有资料。</summary>
        private async Task InsertAsync(BaseEntity entity)
        {
            Context(Observer).AuditStamp.StampForInsert(entity);
            var metadata = PostgreSqlEntityMetadata.GetMetadata(entity.GetType());
            var columns = string.Join(",", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
            var parameters = string.Join(",", metadata.Columns.Select(column => "@" + column.ParameterName));
            Assert.AreEqual(1, await Observer.Connection.ExecuteAsync(
                $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({parameters})",
                PostgreSqlParameters.Entity(entity, metadata), Observer.Transaction));
        }
        /// <summary>只清理本例审计身份与随机编号/UID同时匹配的可变测试行，随后核验零残留。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                var ids = new[] { A.Uid, B.Uid, C.Uid, D.Uid };
                var numbers = new[] { A.BomNo, B.BomNo, C.BomNo, D.BomNo };
                using var transaction = Observer.Connection.BeginTransaction();
                await Observer.Connection.ExecuteAsync("DELETE FROM public.bom WHERE created_by=@Audit AND bom_no=ANY(@Numbers)", new { Audit, Numbers = numbers }, transaction);
                await Observer.Connection.ExecuteAsync("DELETE FROM public.product WHERE created_by=@Audit AND uid=ANY(@Ids)", new { Audit, Ids = ids }, transaction);
                transaction.Commit();
                Assert.AreEqual(0, await Observer.Connection.ExecuteScalarAsync<int>(
                    "SELECT (SELECT count(*) FROM public.bom WHERE created_by=@Audit)+(SELECT count(*) FROM public.product WHERE created_by=@Audit)", new { Audit }));
            }
            finally { Observer.Dispose(); }
        }
    }

    /// <summary>提供本例唯一审计身份，便于按限定范围清理。</summary>
    private sealed class User(string audit) : ICurrentUser
    { public Guid? UserId => null; public string UserName => audit; public bool IsAuthenticated => false; }
}
