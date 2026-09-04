using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>通过内存仓储与记录型事务验证独立明细保存的编排，不连接真实数据库。</summary>
[TestClass]
public sealed class ProductNeutralBatchSaveTests
{
    /// <summary>新增、修改、删除一次提交，保持父字段版本不变，新增行父标识由服务端确定。</summary>
    [TestMethod]
    public async Task Save_ShouldCommitOnlyNeutralsAndKeepMasterUntouched()
    {
        using var fixture = new Fixture();
        var request = fixture.MixedRequest();
        var result = await fixture.Service.SaveNeutralsAsync(fixture.MasterId, request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, fixture.Connection.Transaction.Commits);
        Assert.AreEqual(0, fixture.Connection.Transaction.Rollbacks);
        Assert.IsTrue(fixture.ParentLocked);
        Assert.AreEqual(3, result.Data.Version);
        Assert.AreEqual("MASTER", result.Data.Master.MasterSku);
        Assert.AreEqual(2, result.Data.Neutrals.Count);
        Assert.IsTrue(result.Data.Neutrals.All(row => row.ProductMasterId == fixture.MasterId));
        Assert.AreEqual(1, result.Data.Neutrals.Count(row => row.Data.IsMainImage));
        Assert.IsTrue(result.Data.Neutrals.Any(row => row.Data.ProductCode == "NEW"));
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>任何中途写入失败均回滚，先前成功的新增不会部分提交。</summary>
    [TestMethod]
    public async Task WriteFailure_ShouldRollbackWholeBatch()
    {
        using var fixture = new Fixture { FailWriteNumber = 2 };
        var result = await fixture.Service.SaveNeutralsAsync(fixture.MasterId, fixture.MixedRequest());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual(0, fixture.Connection.Transaction.Commits);
        Assert.AreEqual(1, fixture.Connection.Transaction.Rollbacks);
        CollectionAssert.AreEqual(new[] { "OLD", "DELETE" }, fixture.Rows.Select(row => row.Data.ProductCode).ToArray());
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>非法归属、版本、重复编码、引用、主图和空项全部在写入前拒绝。</summary>
    [TestMethod]
    [DataRow("foreign-update", ServiceErrorCodes.BusinessBlocked)]
    [DataRow("foreign-delete", ServiceErrorCodes.BusinessBlocked)]
    [DataRow("version", ServiceErrorCodes.VersionConflict)]
    [DataRow("duplicate", ServiceErrorCodes.Validation)]
    [DataRow("occupied", ServiceErrorCodes.Validation)]
    [DataRow("color", ServiceErrorCodes.Validation)]
    [DataRow("main-image", ServiceErrorCodes.Validation)]
    [DataRow("missing-parent", ServiceErrorCodes.BusinessBlocked)]
    [DataRow("null-item", ServiceErrorCodes.Validation)]
    public async Task InvalidBatch_ShouldNotWrite(string scenario, string code)
    {
        using var fixture = new Fixture();
        var request = fixture.MixedRequest();
        switch (scenario)
        {
            case "foreign-update": request.UpdateItems[0].Uid = Guid.NewGuid(); break;
            case "foreign-delete": request.DeleteItems[0].Uid = Guid.NewGuid(); break;
            case "version": request.UpdateItems[0].Version = 99; break;
            case "duplicate": request.CreateItems.Add(new() { Data = new() { ProductCode = " new ", ColorId = fixture.ColorId } }); break;
            case "occupied": fixture.CodeOccupied = true; break;
            case "color": fixture.ColorExists = false; break;
            case "main-image": request.UpdateItems[0].Data.IsMainImage = true; break;
            case "missing-parent": fixture.ParentExists = false; break;
            case "null-item": request.CreateItems.Add(null!); break;
        }
        var result = await fixture.Service.SaveNeutralsAsync(fixture.MasterId, request);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(code, result.Error.Code);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(1, fixture.Connection.Transaction.Rollbacks);
        Assert.AreEqual(0, fixture.Connection.Transaction.Commits);
    }

    /// <summary>测试聚合，父仓储仅允许加锁读取，任何父记录写入都使测试失败。</summary>
    private sealed class Fixture : IDisposable
    {
        public Guid MasterId { get; } = Guid.NewGuid();
        public Guid ColorId { get; } = Guid.NewGuid();
        public bool ParentExists { get; set; } = true;
        public bool ColorExists { get; set; } = true;
        public bool CodeOccupied { get; set; }
        public bool ParentLocked { get; private set; }
        public int FailWriteNumber { get; set; }
        public int Writes { get; private set; }
        public List<ProductNeutralDto> Rows { get; private set; }
        public ConnectionStub Connection { get; }
        public DbSession Session { get; }
        public ProductMasterService Service { get; }

        public Fixture()
        {
            Rows = [Row("OLD", true), Row("DELETE", false)];
            Connection = new ConnectionStub();
            Connection.Transaction.OnBegin = () => _original = Rows.ToList();
            Connection.Transaction.OnRollback = () => Rows = _original!;
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            Service = new ProductMasterService(
                Proxy<IProductMasterRepository>(MasterCall), Proxy<IProductNeutralRepository>(NeutralCall),
                Proxy<IProductCategoryRepository>((_, _) => throw new AssertFailedException("独立明细不能校验头部分类。")),
                Proxy<ISupplierRepository>((_, _) => throw new AssertFailedException("独立明细不能校验头部供应商。")),
                Proxy<IProductColorRepository>((method, _) => method == "GetByIdListAsync"
                    ? Task.FromResult<IReadOnlyList<ProductColor>>(ColorExists ? [new ProductColor { Uid = ColorId }] : [])
                    : throw new AssertFailedException(method)),
                new ServiceBaseContext(Session, NullLoggerFactory.Instance));
        }

        private List<ProductNeutralDto>? _original;

        /// <summary>以路由主型号为父，旧主图取消，新建行成为默认。</summary>
        public BatchSaveRequestBase<ProductNeutralWriteDto> MixedRequest() => new()
        {
            CreateItems = [new() { Data = new() { ProductCode = " NEW ", ColorId = ColorId, IsMainImage = true } }],
            UpdateItems = [new() { Uid = Rows[0].Uid, Version = 1, Data = new() { ProductCode = "OLD", ColorId = ColorId } }],
            DeleteItems = [new() { Uid = Rows[1].Uid, Version = 1 }]
        };

        private ProductNeutralDto Row(string code, bool main) => new()
        {
            Uid = Guid.NewGuid(), Version = 1, ProductMasterId = MasterId,
            Data = new() { ProductCode = code, ColorId = ColorId, IsMainImage = main }
        };

        private object MasterCall(string method, object?[] args)
        {
            Assert.AreEqual("GetDetailHeaderAsync", method, "独立明细接口不允许写父记录。");
            Assert.IsNotNull(Session.Transaction);
            ParentLocked |= (bool)args[1]!;
            return Task.FromResult<ProductMasterDetailDto?>(ParentExists
                ? new ProductMasterDetailDto { Uid = MasterId, Version = 3, Master = new() { MasterSku = "MASTER" } }
                : null);
        }

        private object NeutralCall(string method, object?[] args)
        {
            Assert.IsNotNull(Session.Transaction);
            if (method == "GetByProductMasterIdAsync") return Task.FromResult<IReadOnlyList<ProductNeutralDto>>(Rows.ToArray());
            if (method == "GetByProductCodesAsync")
                return Task.FromResult<IReadOnlyList<ProductNeutral>>(CodeOccupied
                    ? [new ProductNeutral { Uid = Guid.NewGuid(), ProductCode = "NEW" }] : []);
            Assert.IsTrue(ParentLocked, "写入前必须锁定父记录。");
            Writes++;
            if (Writes == FailWriteNumber)
                return Task.FromResult(RepositoryWriteResult.Fail(Guid.Empty, 1, "CONCURRENCY_CONFLICT", "版本冲突"));
            if (method == "DeleteAsync")
            {
                Assert.IsTrue((bool)args[2]!, "明细删除必须是软删除。");
                Rows.RemoveAll(row => row.Uid == (Guid)args[0]!);
                return Task.FromResult(RepositoryWriteResult.Ok((Guid)args[0]!, 1, 2));
            }
            Assert.IsTrue(method is "AddAsync" or "UpdateAsync");
            var entity = (ProductNeutral)args[0]!;
            Assert.AreEqual(MasterId, entity.ProductMasterId);
            var saved = Row(entity.ProductCode, entity.IsMainImage);
            if (method == "UpdateAsync")
            {
                saved.Uid = entity.Uid;
                saved.Version = entity.Version + 1;
                Rows.RemoveAll(row => row.Uid == entity.Uid);
            }
            Rows.Add(saved);
            return Task.FromResult(RepositoryWriteResult.Ok(saved.Uid, entity.Version, saved.Version));
        }

        public void Dispose() => Session.Dispose();
    }

    private static T Proxy<T>(Func<string, object?[], object> call) where T : class
    {
        var value = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)value).Call = call;
        return value;
    }

    /// <summary>按接口方法代理仓储，未配置操作即失败，避免测试隐藏额外写入。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;
        /// <summary>把仓储调用交给当前测试的受控实现。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args ?? []);
    }

    /// <summary>仅提供事务生命周期，禁止创建数据库命令。</summary>
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

    /// <summary>记录提交或回滚并恢复内存快照；不声称验证 SQL Server 锁行为。</summary>
    private sealed class TransactionStub : IDbTransaction
    {
        public Action OnBegin { get; set; } = () => { };
        public Action OnRollback { get; set; } = () => { };
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() => Commits++;
        public void Rollback() { Rollbacks++; OnRollback(); }
        public void Dispose() { }
    }
}
