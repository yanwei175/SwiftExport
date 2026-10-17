using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Products.BomManagement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.BomManagement;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Application.Tests.Products;

/// <summary>绑定服务离线验证真实资格与结构校验、窄仓储调用、事务和权限，不模拟数据库并发。</summary>
[TestClass]
public sealed class BomManagementBindingTests
{
    /// <summary>所有供货方式成品可建立或绑定 BOM，禁用状态不新增资格限制。</summary>
    [TestMethod]
    [DataRow("生产", false)] [DataRow("采购", false)] [DataRow("外协", false)]
    [DataRow("生产", true)] [DataRow("采购", true)] [DataRow("外协", true)]
    public async Task ParentEligibility_FinishedSupplyTypesAreAllowed(string supply, bool existing)
    {
        var fixture = new Fixture(existing);
        fixture.Target.SupplyType = supply;
        var result = await fixture.Save(existing);
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsNotNull(fixture.Candidate);
    }

    /// <summary>专用绑定入口拒绝全部非成品，既有共享组的实际父件也必须合格。</summary>
    [TestMethod]
    [DataRow("零件", false)] [DataRow("虚拟件", false)] [DataRow("客供件", false)] [DataRow("包材", false)]
    [DataRow("零件", true)] [DataRow("虚拟件", true)] [DataRow("客供件", true)] [DataRow("包材", true)]
    public async Task ParentEligibility_BindingRejectsNonFinishedParents(string material, bool sharedParent)
    {
        var fixture = new Fixture(sharedParent);
        if (sharedParent) fixture.Parents[0].MaterialType = material;
        else fixture.Target.MaterialType = material;
        var result = await fixture.Save(sharedParent);
        Assert.IsFalse(result.Success);
        Assert.IsNull(fixture.Candidate);
        Assert.AreEqual(0, fixture.Session.Memory.Commits);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>专用绑定不能静默覆盖既有的不成对编号或负标记，必须先核实历史事实。</summary>
    [TestMethod]
    [DataRow("OLD", 0)] [DataRow(null, 1)] [DataRow(null, -1)]
    public async Task ParentEligibility_BindingRejectsInconsistentCurrentState(string? number, int marker)
    {
        var fixture = new Fixture(false);
        fixture.Target.BomNo = number;
        fixture.Target.BomVersion = marker;
        var before = JsonSerializer.Serialize(fixture.Target);
        var result = await fixture.Save(false);
        Assert.IsFalse(result.Success);
        Assert.IsNull(fixture.Candidate);
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Target));
        Assert.AreEqual(0, fixture.Session.Memory.Commits);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>新空编号与既有组成都使用完整产品候选，其他产品资料保持逐字段相同。</summary>
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Binding_PreservesProductDataAndUsesSerializable(bool existing)
    {
        var fixture = new Fixture(existing);
        var before = JsonSerializer.Serialize(fixture.Target);
        var result = await fixture.Save(existing);
        Assert.IsTrue(result.Success, result.Message); Assert.AreEqual(8, result.Data.NewVersion);
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Target), "读取实体不应被候选映射原地修改。");
        var candidate = fixture.Candidate!;
        Assert.AreEqual("SHARED", candidate.BomNo); Assert.AreEqual(1, candidate.BomVersion);
        candidate.BomNo = fixture.Target.BomNo; candidate.BomVersion = fixture.Target.BomVersion;
        Assert.AreEqual(before, JsonSerializer.Serialize(candidate));
        Assert.AreEqual(IsolationLevel.Serializable, fixture.Session.Memory.Isolation);
        Assert.AreEqual(1, fixture.Session.Memory.Commits); Assert.IsNull(fixture.Session.Transaction);
        CollectionAssert.AreEqual(new[] { PermissionKeys.ProductsProductEdit, PermissionKeys.ProductsBomView }, fixture.PermissionKeys.ToArray());
    }

    /// <summary>不满足模式、当前产品或结构边界时不调用写仓储，不能靠客户端预览绕过提交校验。</summary>
    [TestMethod]
    [DataRow("new-exists")] [DataRow("existing-empty")] [DataRow("already-bound")]
    [DataRow("version")] [DataRow("self")] [DataRow("invalid")] [DataRow("customer")]
    public async Task Binding_RejectsUnsafeOrStaleState(string scenario)
    {
        var fixture = new Fixture(true);
        var existing = scenario != "new-exists";
        if (scenario == "existing-empty") fixture.Lines.Clear();
        if (scenario == "already-bound") { fixture.Target.BomNo = "OLD"; fixture.Target.BomVersion = 2; }
        if (scenario == "version") fixture.Target.Version++;
        if (scenario == "self") fixture.Lines[0].ComponentProductId = fixture.Target.Uid;
        if (scenario == "invalid") fixture.Lines[0].BomVersion = 0;
        if (scenario == "customer") fixture.Target.MaterialType = "客供件";
        var result = await fixture.Save(existing);
        Assert.IsFalse(result.Success); Assert.IsNull(fixture.Candidate); Assert.AreEqual(0, fixture.Session.Memory.Commits);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>两个权限均必需；版本冲突与提交确认丢失返回不同结果，提交不会自动重试。</summary>
    [TestMethod]
    [DataRow("permission-edit")] [DataRow("permission-view")] [DataRow("write-conflict")] [DataRow("unknown-commit")]
    public async Task Binding_PermissionAndWriteFailuresRemainExplicit(string scenario)
    {
        var fixture = new Fixture(false);
        if (scenario.StartsWith("permission", StringComparison.Ordinal)) fixture.DeniedPermission = scenario.EndsWith("edit", StringComparison.Ordinal) ? PermissionKeys.ProductsProductEdit : PermissionKeys.ProductsBomView;
        if (scenario == "write-conflict") fixture.FailWrite = true;
        if (scenario == "unknown-commit") fixture.Session.Memory.FailCommit = true;
        var result = await fixture.Save(false);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(scenario.StartsWith("permission", StringComparison.Ordinal) ? ServiceErrorCodes.Forbidden
            : scenario == "write-conflict" ? ServiceErrorCodes.VersionConflict : ServiceErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.AreEqual(scenario == "unknown-commit" ? 1 : 0, fixture.Session.Memory.Commits);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>可控完整产品和共享组，任何普通产品 UPDATE 或组成写入都会使测试失败。</summary>
    private sealed class Fixture
    {
        public readonly Product Target = new() { Uid = Guid.NewGuid(), Version = 7, Sku = "TARGET", MaterialType = "成品", SupplyType = "采购", ProductMasterId = Guid.NewGuid(), IsActive = false, Note = "保留", MainImage = "image.jpg", ManufacturerModel = "M", USDPrice = 12m };
        public readonly Product Child = new() { Uid = Guid.NewGuid(), Sku = "CHILD", MaterialType = "原材料", SupplyType = "采购" };
        public readonly List<Bom> Lines = [];
        public readonly List<Product> Parents = [];
        public readonly Session Session = new();
        public readonly List<string> PermissionKeys = [];
        public Product? Candidate;
        public string? DeniedPermission;
        public bool FailWrite;
        private readonly ProductBomBindingService _service;
        public Fixture(bool existing)
        {
            if (existing)
            {
                var parent = new Product { Uid = Guid.NewGuid(), Sku = "SOURCE", MaterialType = "成品", SupplyType = "采购", BomNo = "SHARED", BomVersion = 5 };
                Parents.Add(parent);
                Lines.Add(new Bom { Uid = Guid.NewGuid(), BomNo = "SHARED", BomVersion = 8, ParentProductId = parent.Uid, ParentSkuSnapshot = parent.Sku, ComponentProductId = Child.Uid, ComponentSkuSnapshot = Child.Sku, Numerator = 1, Denominator = 1 });
            }
            var products = Proxy<IProductRepository>((method, _) => method.Name switch
            {
                "GetByIdAsync" => Task.FromResult<Product?>(Target),
                "GetByIdListAsync" => Task.FromResult<IReadOnlyList<Product>>([Child]),
                _ => throw new AssertFailedException("绑定不得调用普通产品写入：" + method.Name)
            });
            var boms = Proxy<IBomRepository>((method, _) => method.Name == "GetSharedSaveFactsAsync"
                ? Task.FromResult<IReadOnlyList<SharedBomGroup>>([new() { GroupKey = 1, BomNo = "SHARED", InputIndexes = [0], Lines = Lines, Products = Parents }])
                : throw new AssertFailedException("绑定不得改写组成：" + method.Name));
            var writer = Proxy<IProductBomBindingRepository>((_, args) =>
            {
                Candidate = (Product)args![0]!;
                Assert.AreSame(Session.Transaction, Session.Memory.Transaction);
                return Task.FromResult(FailWrite ? RepositoryWriteResult.Fail(Target.Uid, 7, ServiceErrorCodes.VersionConflict, "冲突") : RepositoryWriteResult.Ok(Target.Uid, 7, 8));
            });
            var user = Proxy<ICurrentUser>((method, _) => method.Name switch { "get_IsAuthenticated" => true, "get_UserId" => (Guid?)Guid.NewGuid(), "get_UserName" => "测试", _ => null });
            var permissions = Proxy<IPermissionEvaluationService>((_, args) => { var key = (string)args![1]!; PermissionKeys.Add(key); return Task.FromResult(key != DeniedPermission); });
            _service = new(products, boms, writer, user, permissions, new ServiceBaseContext(Session, NullLoggerFactory.Instance));
        }
        public Task<ServiceResult<SaveItemResult>> Save(bool existing) => _service.BindAsync(Target.Uid, new ProductBomBindingRequest { BomNo = " SHARED ", ExpectedVersion = 7, BindExisting = existing }, default);
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> action) where T : class
    { var proxy = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)proxy).Action = action; return proxy; }
    /// <summary>为测试隔离庞大接口，仅执行用例显式允许的成员。</summary>
    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Action { get; set; } = null!;
        /// <summary>转交配置的成员行为，不提供默认成功值。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Action(targetMethod!, args);
    }
    /// <summary>记录事务共享身份，不接触真实连接。</summary>
    private sealed class Session : IDbSession, IDatabaseExceptionClassifier
    {
        public Connection Memory { get; } = new();
        public IDbConnection Connection => Memory;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => this;
        public DatabaseFailureKind Classify(Exception exception) => DatabaseFailureKind.Unknown;
    }
    /// <summary>禁止 SQL 的事务记录连接，捕获隔离级别与提交次数。</summary>
    private sealed class Connection : IDbConnection
    {
        public IsolationLevel Isolation;
        public int Commits;
        public bool FailCommit;
        public IDbTransaction? Transaction;
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() => BeginTransaction(IsolationLevel.ReadCommitted);
        public IDbTransaction BeginTransaction(IsolationLevel il) { Isolation = il; return Transaction = new TransactionRecord(this); }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
    }
    /// <summary>提交失败仅表示回执不明，模拟器不声称验证 PostgreSQL 隔离。</summary>
    private sealed class TransactionRecord(Connection connection) : IDbTransaction
    {
        public IDbConnection Connection => connection;
        public IsolationLevel IsolationLevel => connection.Isolation;
        public void Commit() { connection.Commits++; if (connection.FailCommit) throw new InvalidOperationException("提交回执丢失"); }
        public void Rollback() { }
        public void Dispose() { }
    }
}
