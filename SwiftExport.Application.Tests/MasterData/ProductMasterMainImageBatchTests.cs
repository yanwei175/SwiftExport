using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>离线验证批量补齐主图的候选、选集、锁顺序及整批回滚，不替代真实 PG 并发证据。</summary>
[TestClass]
public sealed class ProductMasterMainImageBatchTests
{
    /// <summary>颜色顺序优先于中性编码；同色沿用仓储顺序，未勾选主型号及其他字段保持不变。</summary>
    [TestMethod]
    public async Task SelectedMasters_UseColorOrderThenExistingNeutralOrder()
    {
        var f = new Fixture();
        var selected = f.AddMaster(); var outside = f.AddMaster();
        var firstColor = f.AddColor(); var laterColor = f.AddColor();
        f.AddNeutral(selected, laterColor, "A");
        var winner = f.AddNeutral(selected, firstColor, "B");
        f.AddNeutral(selected, firstColor, "Z"); f.AddNeutral(outside, firstColor, "OUTSIDE");
        var result = await f.Service.SetMainImagesAsync(f.Request(selected));
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(winner.Uid, result.Data.Items.Single().ProductNeutralId);
        Assert.AreEqual(2, result.Data.Items.Single().NewVersion);
        Assert.AreEqual(1, f.Neutrals.Count(row => row.IsMainImage));
        Assert.AreEqual("B", f.Neutrals.Single(row => row.Uid == winner.Uid).ProductCode);
        Assert.AreEqual(12m, f.Neutrals.Single(row => row.Uid == winner.Uid).DefaultSalesPriceUsd);
        Assert.AreEqual(1, f.Masters.Single(row => row.Uid == outside.Uid).Version);
        CollectionAssert.AreEqual(new[] { "color", "color", "master", "neutral" }, f.Locks.ToArray());
        Assert.IsTrue(f.Connection.Committed); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>已有默认值不重排；无有效颜色或图片的候选跳过，均不推进主型号版本。</summary>
    [TestMethod]
    public async Task ExistingDefaultAndUnavailableCandidates_AreSkipped()
    {
        var f = new Fixture(); var existing = f.AddMaster(); var empty = f.AddMaster();
        var color = f.AddColor(); var deleted = f.AddColor(); deleted.IsDeleted = true;
        var main = f.AddNeutral(existing, color, "OLD"); main.IsMainImage = true;
        f.AddNeutral(empty, deleted, "DELETED");
        f.AddNeutral(empty, new ProductColor { Uid = Guid.NewGuid() }, "MISSING");
        f.AddNeutral(empty, color, "NO-IMAGE").ImagePath = " ";
        var result = await f.Service.SetMainImagesAsync(f.Request(existing, empty));
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Data.AlreadySetCount); Assert.AreEqual(1, result.Data.NoCandidateCount);
        Assert.IsEmpty(result.Data.Items); Assert.AreEqual(0, f.Writes);
        Assert.IsTrue(f.Masters.All(row => row.Version == 1));
    }

    /// <summary>后续写入失败会撤销此前主图标记和主型号版本；不返回部分成功回执。</summary>
    [TestMethod]
    public async Task WriteFailure_RollsBackAllDefaultsAndVersions()
    {
        var f = new Fixture { FailWriteNumber = 3 }; var first = f.AddMaster(); var second = f.AddMaster();
        var color = f.AddColor(); f.AddNeutral(first, color, "A"); f.AddNeutral(second, color, "B");
        var result = await f.Service.SetMainImagesAsync(f.Request(first, second));
        Assert.IsFalse(result.Success); Assert.IsNull(result.Data);
        Assert.IsTrue(f.Connection.RolledBack); Assert.IsFalse(f.Connection.Committed);
        Assert.IsTrue(f.Neutrals.All(row => !row.IsMainImage && row.Version == 1));
        Assert.IsTrue(f.Masters.All(row => row.Version == 1)); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>过期版本拒绝整批；重复身份在开启事务前拒绝，避免重复推进版本。</summary>
    [TestMethod]
    public async Task StaleVersionAndDuplicateTargets_NeverWrite()
    {
        var f = new Fixture(); var master = f.AddMaster(); f.AddNeutral(master, f.AddColor(), "A");
        var request = f.Request(master); request.Items[0].ExpectedVersion = 2;
        var result = await f.Service.SetMainImagesAsync(request);
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Writes);
        var duplicate = f.Request(master, master);
        result = await f.Service.SetMainImagesAsync(duplicate);
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.Validation, result.Error!.Code);
        Assert.AreEqual(0, f.Writes);
    }

    /// <summary>颜色锁范围变化时不能持有主型号后逆序追加颜色锁，也不能写入候选。</summary>
    [TestMethod]
    public async Task ChangedColorLockRange_IsRejected()
    {
        var f = new Fixture { ChangeColorOnLock = true }; var master = f.AddMaster();
        f.AddNeutral(master, f.AddColor(), "A");
        var result = await f.Service.SetMainImagesAsync(f.Request(master));
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Writes); Assert.IsTrue(f.Connection.RolledBack);
    }

    /// <summary>客户端与真实 MVC Action 的批量主图路由、请求及结果类型一致。</summary>
    [TestMethod]
    public void ApiContract_UsesDedicatedPostAndSharedRequest()
    {
        var api = typeof(SwiftExport.Contracts.Apis.ProductApis.IProductMasterApi).GetMethod("SetMainImagesAsync")!;
        var controller = typeof(SwiftExport.WebApi.Controllers.Products.ProductMasters.ProductMasterController).GetMethod("SetMainImagesAsync")!;
        Assert.AreEqual("/set-main-images", api.GetCustomAttribute<Refit.PostAttribute>()!.Path);
        Assert.AreEqual("set-main-images", controller.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()!.Template);
        Assert.AreEqual(api.ReturnType, controller.ReturnType);
        Assert.AreEqual(typeof(BatchSetProductMasterMainImagesRequest), controller.GetParameters()[0].ParameterType);
        Assert.AreEqual(typeof(CancellationToken), controller.GetParameters()[1].ParameterType);
        Assert.IsNotNull(api.GetParameters()[0].GetCustomAttribute<Refit.BodyAttribute>());
    }

    /// <summary>用独立内存快照模拟共享事务，未涉及的仓储依赖保持为空，意外调用会导致测试失败。</summary>
    private sealed class Fixture
    {
        internal List<ProductMaster> Masters = []; internal List<ProductNeutral> Neutrals = [];
        internal readonly List<ProductColor> Colors = []; internal readonly List<string> Locks = [];
        internal int Writes, FailWriteNumber; internal bool ChangeColorOnLock;
        internal readonly ConnectionStub Connection = new(); internal readonly SessionStub Session;
        internal readonly ProductMasterMaintenanceService Service;
        internal Fixture()
        {
            Session = new(Connection);
            List<ProductMaster> masters = []; List<ProductNeutral> neutrals = [];
            Connection.Begin = () => { masters = Masters.Adapt<List<ProductMaster>>(); neutrals = Neutrals.Adapt<List<ProductNeutral>>(); };
            Connection.Undo = () => { Masters = masters; Neutrals = neutrals; };
            Service = new(Proxy<IProductMasterRepository>(MasterCall), Proxy<IProductNeutralRepository>(NeutralCall),
                null!, null!, Proxy<IProductColorRepository>(ColorCall), null!, null!, new(Session, NullLoggerFactory.Instance));
        }
        internal ProductMaster AddMaster() { var row = new ProductMaster { Uid = Guid.NewGuid(), Version = 1, MasterSku = "MASTER" }; Masters.Add(row); return row; }
        internal ProductColor AddColor() { var row = new ProductColor { Uid = Guid.NewGuid() }; Colors.Add(row); return row; }
        internal ProductNeutral AddNeutral(ProductMaster master, ProductColor color, string code)
        {
            var row = new ProductNeutral { Uid = Guid.NewGuid(), Version = 1, ProductMasterId = master.Uid, ColorId = color.Uid,
                ProductCode = code, ImagePath = code + ".jpg", DefaultSalesPriceUsd = 12 };
            Neutrals.Add(row); return row;
        }
        internal BatchSetProductMasterMainImagesRequest Request(params ProductMaster[] rows) => new()
        { Items = rows.Select(row => new ProductMasterMainImageTarget { ProductMasterId = row.Uid, ExpectedVersion = row.Version }).ToList() };
        /// <summary>显示顺序由仓储决定，颜色逐行锁定，不依赖 SQL 集合参数顺序。</summary>
        private object ColorCall(string method, object?[] args)
        {
            if (method == "GetAllAsync") return Task.FromResult<IReadOnlyList<ProductColor>>(Colors.ToArray());
            Assert.AreEqual("GetByIdAsync", method); Assert.IsTrue((bool)args[1]!); Locks.Add("color");
            return Task.FromResult(Colors.FirstOrDefault(row => row.Uid == (Guid)args[0]!));
        }
        /// <summary>读取独立副本并记录聚合锁，写入仅推进对应行版本。</summary>
        private object MasterCall(string method, object?[] args)
        {
            if (method == "GetByIdAsync") { Locks.Add("master"); return Task.FromResult(Masters.FirstOrDefault(row => row.Uid == (Guid)args[0]!)?.Adapt<ProductMaster>()); }
            Assert.AreEqual("UpdateAsync", method); var entity = (ProductMaster)args[0]!;
            return Write(entity.Uid, entity.Version, () => Masters.Single(row => row.Uid == entity.Uid).Version++);
        }
        /// <summary>明细按既有编码顺序返回，锁范围变化通过明确开关注入。</summary>
        private object NeutralCall(string method, object?[] args)
        {
            if (method == "GetByProductMasterIdAsync")
            {
                if ((bool)args[1]!)
                { Locks.Add("neutral"); if (ChangeColorOnLock) Neutrals[0].ColorId = Guid.NewGuid(); }
                return Task.FromResult<IReadOnlyList<ProductNeutralDto>>(Neutrals.Where(row => row.ProductMasterId == (Guid)args[0]!)
                    .OrderBy(row => row.ProductCode, StringComparer.Ordinal).ThenBy(row => row.Uid)
                    .Select(row => new ProductNeutralDto { Uid = row.Uid, Version = row.Version,
                        Data = new() { ColorId = row.ColorId, ImagePath = row.ImagePath, IsMainImage = row.IsMainImage } }).ToArray());
            }
            if (method == "GetByIdAsync") return Task.FromResult(Neutrals.Single(row => row.Uid == (Guid)args[0]!).Adapt<ProductNeutral>());
            Assert.AreEqual("UpdateAsync", method); var entity = (ProductNeutral)args[0]!;
            return Write(entity.Uid, entity.Version, () => { var row = Neutrals.Single(row => row.Uid == entity.Uid); row.IsMainImage = entity.IsMainImage; row.Version++; });
        }
        /// <summary>在指定写入点返回冲突，供外层事务验证此前写入全部撤销。</summary>
        private Task<RepositoryWriteResult> Write(Guid id, int version, Action update)
        {
            if (++Writes == FailWriteNumber) return Task.FromResult(RepositoryWriteResult.Fail(id, version, ServiceErrorCodes.ConcurrencyConflict, "测试冲突"));
            update(); return Task.FromResult(RepositoryWriteResult.Ok(id, version, version + 1));
        }
    }
    /// <summary>复用仓储接口代理，不执行 SQL。</summary>
    private static T Proxy<T>(Func<string, object?[], object> call) where T : class
    { var value = DispatchProxy.Create<T, ProductMasterCategoryAssignmentTests.RepositoryProxy>(); ((ProductMasterCategoryAssignmentTests.RepositoryProxy)(object)value).Call = call; return value; }
    /// <summary>为应用服务提供当前测试独占的事务引用。</summary>
    private sealed class SessionStub(ConnectionStub connection) : IDbSession
    {
        public IDbConnection Connection => connection;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => throw new NotSupportedException();
    }
    /// <summary>记录单一事务并在回滚时恢复夹具快照。</summary>
    private sealed class ConnectionStub : IDbConnection, IDbTransaction
    {
        internal Action Begin = () => { }, Undo = () => { }; internal bool Committed, RolledBack;
        public IDbTransaction BeginTransaction() { Begin(); return this; }
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        public void Commit() => Committed = true;
        public void Rollback() { RolledBack = true; Undo(); }
        public IDbConnection Connection => this;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0; public string Database => "memory"; public ConnectionState State => ConnectionState.Open;
        public void Open() { } public void Close() { } public void Dispose() { }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
    }
}
