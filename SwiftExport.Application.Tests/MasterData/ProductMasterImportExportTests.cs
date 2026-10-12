using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Products.ProductMasters.Import;
using SwiftExport.Application.Services.Products.ProductMasters.Query;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证父级导入的稀疏合并、引用重验、管理员准入及原子事务，不连接业务数据库。</summary>
[TestClass]
public sealed class ProductMasterImportExportTests
{
    /// <summary>预览保持只读；更新空白保留原值，旧类型标签不会出现在传输资料中。</summary>
    [TestMethod]
    public async Task PreviewAndCommit_PreserveBlankAndRetiredFields()
    {
        var fixture = new Fixture();
        var original = fixture.Masters[0].Adapt<ProductMaster>();
        var input = new ProductMasterImportRowInput { SourceRowNumber = 2, MasterSku = " m-1 ", DefaultDescriptionZh = "新描述", DefaultNetWeight = "0" };
        var preview = await fixture.Service.PreviewAsync(new() { Rows = [input] });
        Assert.IsTrue(preview.Success);
        var row = preview.Data.Rows.Single();
        Assert.AreEqual("Update", row.Operation);
        Assert.AreEqual("旧描述", row.CurrentData.DefaultDescriptionZh);
        Assert.AreEqual("新描述", row.ProposedData.DefaultDescriptionZh);
        Assert.AreEqual(original.DefaultPcsPerPackage, row.ProposedData.DefaultPcsPerPackage);
        Assert.AreEqual(0m, row.ProposedData.DefaultNetWeight);
        Assert.AreEqual(original.DefaultDescriptionZh, fixture.Masters[0].DefaultDescriptionZh);
        Assert.IsNull(fixture.Session.Transaction);
        Assert.IsFalse(typeof(ProductMasterTransferData).GetProperties().Any(p => p.Name is "ProductType" or "ProductTag" or "ProductSeries"));

        var saved = await fixture.Service.ImportAsync(Commit([input], preview.Data));
        Assert.IsTrue(saved.Success, saved.Message);
        var master = fixture.Masters.Single();
        Assert.AreEqual("M-1", master.MasterSku);
        Assert.AreEqual("新描述", master.DefaultDescriptionZh);
        Assert.AreEqual(original.ProductType, master.ProductType);
        Assert.AreEqual(original.ProductTag, master.ProductTag);
        Assert.AreEqual(original.CreatedBy, master.CreatedBy);
        Assert.AreEqual(original.Version + 1, master.Version);
        Assert.IsTrue(fixture.Connection.Committed);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>新记录默认装率为一；无变化行不写入也不递增版本。</summary>
    [TestMethod]
    public async Task CreateAndUnchanged_SkipUnchangedWrite()
    {
        var fixture = new Fixture();
        var inputs = new List<ProductMasterImportRowInput>
        {
            new() { SourceRowNumber = 2, MasterSku = "M-1" },
            new() { SourceRowNumber = 4, MasterSku = "0002", CategoryPath = "工具/夹具", DefaultSupplierCode = "sup-1" }
        };
        var preview = await fixture.Service.PreviewAsync(new() { Rows = inputs });
        CollectionAssert.AreEqual(new[] { "Unchanged", "Create" }, preview.Data.Rows.Select(r => r.Operation).ToArray());
        Assert.AreEqual(1, preview.Data.Rows[1].ProposedData.DefaultPcsPerPackage);
        var result = await fixture.Service.ImportAsync(Commit(inputs, preview.Data));
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, fixture.Writes);
        Assert.AreEqual(4, fixture.Masters.Single(m => m.MasterSku == "M-1").Version);
        Assert.AreEqual(fixture.ChildCategory.Uid, fixture.Masters.Single(m => m.MasterSku == "0002").CategoryId);
    }

    /// <summary>持有全部父级 CRUD 权限的普通用户仍不能提交；停用账号也不能读取业务仓储。</summary>
    [TestMethod]
    public async Task NonAdminAndInactive_AreRejectedBeforeRepositories()
    {
        var fixture = new Fixture { Admin = false };
        var result = await fixture.Service.ImportAsync(new() { Rows = [new() { Input = new() { SourceRowNumber = 2, MasterSku = "M-1" } }] });
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.AreEqual(0, fixture.Reads);
        Assert.AreEqual(0, fixture.Writes);
        Assert.IsTrue((await fixture.Service.PreviewAsync(new() { Rows = [new() { SourceRowNumber = 2, MasterSku = "M-1" }] })).Success);
        fixture.Active = false;
        fixture.Reads = 0;
        result = await fixture.Service.ImportAsync(new() { Rows = [] });
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated, result.Error.Code);
        Assert.AreEqual(0, fixture.Reads);
    }

    /// <summary>重复编码、非法精度、未知引用均返回原行号错误，整个批次不可提交。</summary>
    [TestMethod]
    public async Task InvalidRows_ReturnIssuesAndRollbackWithoutWrites()
    {
        var fixture = new Fixture();
        var inputs = new List<ProductMasterImportRowInput>
        {
            new() { SourceRowNumber = 3, MasterSku = "m-1", DefaultPcsPerPackage = "1.5", DefaultNetWeight = "-1", DefaultCbm = "0.0000001" },
            new() { SourceRowNumber = 5, MasterSku = "M-1", CategoryPath = "不存在", DefaultSupplierCode = "UNKNOWN" }
        };
        var preview = await fixture.Service.PreviewAsync(new() { Rows = inputs });
        Assert.IsTrue(preview.Success);
        Assert.IsTrue(preview.Data.Rows.All(row => row.Operation == "Invalid"));
        Assert.IsTrue(preview.Data.Rows[0].Issues.Any(i => i.Code == "DuplicateCode"));
        Assert.IsTrue(preview.Data.Rows[0].Issues.Count >= 4);
        Assert.IsTrue(preview.Data.Rows[1].Issues.All(i => i.SourceRowNumber == 5));
        var result = await fixture.Service.ImportAsync(Commit(inputs, preview.Data));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Writes);
        Assert.IsTrue(fixture.Connection.RolledBack);
    }

    /// <summary>名称路径歧义、断链、环和供应商编码歧义不能自动选取一个引用。</summary>
    [TestMethod]
    [DataRow("ambiguous")]
    [DataRow("missing-parent")]
    [DataRow("cycle")]
    [DataRow("supplier")]
    public async Task InvalidReferenceGraphOrAmbiguity_IsRejected(string kind)
    {
        var fixture = new Fixture();
        if (kind == "ambiguous") { fixture.ChildCategory.ParentId = null; fixture.ChildCategory.CategoryName = "工具"; }
        if (kind == "missing-parent") fixture.RootCategory.ParentId = Guid.NewGuid();
        if (kind == "cycle") fixture.RootCategory.ParentId = fixture.ChildCategory.Uid;
        if (kind == "supplier") { var duplicate = fixture.Suppliers[0].Adapt<Supplier>(); duplicate.Uid = Guid.NewGuid(); fixture.Suppliers.Add(duplicate); }
        var preview = await fixture.Service.PreviewAsync(new() { Rows = [new() { SourceRowNumber = 2, MasterSku = "NEW",
            CategoryPath = kind == "ambiguous" ? "工具" : "工具 / 夹具", DefaultSupplierCode = "SUP-1" }] });
        Assert.AreEqual("Invalid", preview.Data.Rows.Single().Operation);
        Assert.IsTrue(preview.Data.Rows[0].Issues.Any(issue => issue.Code == "InvalidReference"));
        Assert.AreEqual(0, fixture.Writes);
    }

    /// <summary>预览后版本或引用编码指向发生变化时，提交须重验而不能盲写旧匹配。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedVersionOrReference_RejectsStaleBasis(bool reference)
    {
        var fixture = new Fixture();
        var inputs = new List<ProductMasterImportRowInput> { new() { SourceRowNumber = 2, MasterSku = "M-1", DefaultDescriptionZh = "新值", DefaultSupplierCode = "SUP-1" } };
        var preview = await fixture.Service.PreviewAsync(new() { Rows = inputs });
        if (reference) fixture.Suppliers[0].Uid = Guid.NewGuid();
        else fixture.Masters[0].Version++;
        var result = await fixture.Service.ImportAsync(Commit(inputs, preview.Data));
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual(0, fixture.Writes);
        Assert.IsTrue(fixture.Connection.RolledBack);
    }

    /// <summary>第二条保存失败或取消必须回滚已经保存的第一条，并清理会话事务。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LaterFailureOrCancellation_RollsBackEarlierWrites(bool cancel)
    {
        var fixture = new Fixture();
        var inputs = new List<ProductMasterImportRowInput>
        {
            new() { SourceRowNumber = 2, MasterSku = "NEW-1" },
            new() { SourceRowNumber = 3, MasterSku = "NEW-2" }
        };
        var preview = await fixture.Service.PreviewAsync(new() { Rows = inputs });
        using var cancellation = new CancellationTokenSource();
        if (cancel) fixture.AfterWrite = () => cancellation.Cancel();
        else fixture.FailWrite = 2;
        if (cancel)
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.ImportAsync(Commit(inputs, preview.Data), cancellation.Token));
        else
            Assert.IsFalse((await fixture.Service.ImportAsync(Commit(inputs, preview.Data))).Success);
        Assert.AreEqual(1, fixture.Masters.Count);
        Assert.AreEqual("M-1", fixture.Masters[0].MasterSku);
        Assert.IsTrue(fixture.Connection.RolledBack);
        Assert.IsFalse(fixture.Connection.Committed);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>业务上限精确接受一千行，超过上限在仓储读取前拒绝。</summary>
    [TestMethod]
    public async Task ImportLimits_AreCheckedBeforeDatabase()
    {
        var fixture = new Fixture();
        var inputs = Enumerable.Range(2, 1000).Select(n => new ProductMasterImportRowInput { SourceRowNumber = n, MasterSku = "N-" + n }).ToList();
        Assert.AreEqual(1000, (await fixture.Service.PreviewAsync(new() { Rows = inputs })).Data.Rows.Count);
        fixture.Reads = 0;
        inputs.Add(new() { SourceRowNumber = 1002, MasterSku = "OVER" });
        Assert.IsFalse((await fixture.Service.PreviewAsync(new() { Rows = inputs })).Success);
        Assert.AreEqual(0, fixture.Reads);
    }

    /// <summary>导出传递完整筛选并解析引用，超过一万行不能截断返回。</summary>
    [TestMethod]
    public async Task Export_FiltersSavedDataAndRejectsOverflow()
    {
        var fixture = new Fixture { Admin = false };
        fixture.Masters[0].CategoryId = fixture.ChildCategory.Uid;
        fixture.Masters[0].DefaultSupplierId = fixture.Suppliers[0].Uid;
        var request = new ProductMasterExportRequest { Keyword = "needle", CategoryIds = [fixture.ChildCategory.Uid], IncludeUncategorized = true };
        var result = await fixture.Query.ExportAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("工具 / 夹具", result.Data[0].CategoryPath);
        Assert.AreEqual("SUP-1", result.Data[0].DefaultSupplierCode);
        Assert.AreEqual("needle", fixture.ExportFilter!.Keyword);
        Assert.AreEqual(10000, fixture.ExportLimit);
        fixture.OverflowExport = true;
        Assert.IsFalse((await fixture.Query.ExportAsync(request)).Success);
    }

    /// <summary>将服务端预览的身份、版本和引用依据原样冻结为一次提交。</summary>
    internal static ProductMasterImportRequest Commit(List<ProductMasterImportRowInput> inputs, ProductMasterImportPreviewResult preview) => new()
    {
        Rows = preview.Rows.Select(row => new ProductMasterImportCommitRow
        {
            Input = inputs.Single(i => i.SourceRowNumber == row.SourceRowNumber), ProductMasterId = row.ProductMasterId, ExpectedVersion = row.ExpectedVersion,
            ExpectedCategoryId = row.ProposedData.CategoryId, ExpectedSupplierId = row.ProposedData.DefaultSupplierId
        }).ToList()
    };

    /// <summary>模拟有事务快照的父级仓储；任何未明确允许的调用都会使测试失败。</summary>
    internal sealed class Fixture : ICurrentUser, IPermissionEvaluationService
    {
        internal List<ProductMaster> Masters = [new() { MasterSku = "M-1", Version = 4, DefaultDescriptionZh = "旧描述", DefaultPcsPerPackage = 12, ProductType = "旧类型", ProductTag = "旧标签", CreatedBy = "original" }];
        internal List<Supplier> Suppliers = [new() { SupplierCode = "SUP-1", SupplierName = "测试供应商", DocRelateNo = "S" }];
        internal ProductCategory RootCategory = new() { CategoryName = "工具" };
        internal ProductCategory ChildCategory = new() { CategoryName = "夹具" };
        internal readonly SessionStub Session;
        internal readonly ConnectionStub Connection;
        internal readonly ProductMasterImportService Service;
        internal readonly ProductMasterQueryService Query;
        internal bool Admin = true, Active = true, OverflowExport;
        internal int Writes, Reads, FailWrite, ExportLimit;
        internal Action? AfterWrite;
        internal ProductMasterSearchRequest? ExportFilter;
        internal Fixture()
        {
            ChildCategory.ParentId = RootCategory.Uid;
            List<ProductMaster> snapshot = [];
            Connection = new ConnectionStub(() => snapshot = Masters.Select(m => m.Adapt<ProductMaster>()).ToList(),
                () => Masters = snapshot.Select(m => m.Adapt<ProductMaster>()).ToList());
            Session = new(Connection);
            var masters = Proxy<IProductMasterRepository>(MasterCall);
            var categories = Proxy<IProductCategoryRepository>((name, _) =>
            {
                Reads++;
                if (name == "GetAllForUpdateAsync") Assert.IsNotNull(Session.Transaction);
                Assert.IsTrue(name is "GetAllAsync" or "GetAllForUpdateAsync");
                return Task.FromResult<IReadOnlyList<ProductCategory>>([RootCategory, ChildCategory]);
            });
            var suppliers = Proxy<ISupplierRepository>((name, args) =>
            {
                Reads++;
                return name switch
                {
                    "GetBySupplierCodeListAsync" => Task.FromResult<IReadOnlyList<Supplier>>(Suppliers.Where(s => ((IEnumerable<string>)args[0]!).Contains(s.SupplierCode, StringComparer.OrdinalIgnoreCase)).ToArray()),
                    "GetByIdListAsync" => Task.FromResult<IReadOnlyList<Supplier>>(Suppliers.Where(s => ((IEnumerable<Guid>)args[0]!).Contains(s.Uid)).ToArray()),
                    _ => throw new AssertFailedException(name)
                };
            });
            Service = new(masters, categories, suppliers, this, this, new EmptyProductAttributeRepository(), new ServiceBaseContext(Session, NullLoggerFactory.Instance));
            Query = new(masters, Proxy<IProductNeutralRepository>((name, _) => throw new AssertFailedException("不应访问子级：" + name)), categories, suppliers, this, this, new EmptyProductAttributeRepository());
        }

        /// <summary>处理新导入路径允许的根仓储操作，并模拟数据库编码大小写规则。</summary>
        private object MasterCall(string name, object?[] args)
        {
            Reads++;
            switch (name)
            {
                case "MatchForImportAsync":
                    var keys = (IReadOnlyList<(int SourceRowNumber, string MasterSku)>)args[0]!;
                    return Task.FromResult<IReadOnlyList<ProductMasterImportMatch>>(keys.Select(k => new ProductMasterImportMatch(k.SourceRowNumber,
                        keys.Count(other => string.Equals(other.MasterSku, k.MasterSku, StringComparison.OrdinalIgnoreCase)) > 1,
                        Masters.SingleOrDefault(m => string.Equals(m.MasterSku, k.MasterSku, StringComparison.OrdinalIgnoreCase))?.Adapt<ProductMaster>())).ToArray());
                case "GetByIdListAsync":
                    return Task.FromResult<IReadOnlyList<ProductMaster>>(Masters.Where(m => ((IEnumerable<Guid>)args[0]!).Contains(m.Uid)).Select(m => m.Adapt<ProductMaster>()).ToArray());
                case "GetByIdAsync":
                    return Task.FromResult(Masters.Single(m => m.Uid == (Guid)args[0]!).Adapt<ProductMaster>());
                case "AddAsync":
                case "UpdateAsync":
                    Assert.IsNotNull(Session.Transaction);
                    Writes++;
                    var entity = ((ProductMaster)args[0]!).Adapt<ProductMaster>();
                    if (Writes == FailWrite) return Task.FromResult(RepositoryWriteResult.Fail(entity.Uid, entity.Version, "CONCURRENCY_CONFLICT", "冲突"));
                    if (name == "UpdateAsync") { Masters.RemoveAll(m => m.Uid == entity.Uid); entity.Version++; }
                    Masters.Add(entity);
                    AfterWrite?.Invoke();
                    return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, entity.Version - 1, entity.Version));
                case "GetForExportAsync":
                    ExportFilter = (ProductMasterSearchRequest)args[0]!; ExportLimit = (int)args[1]!;
                    return Task.FromResult<IReadOnlyList<ProductMaster>>(OverflowExport ? Enumerable.Range(0, 10001).Select(_ => new ProductMaster()).ToArray() : Masters.ToArray());
                default: throw new AssertFailedException("不应调用：" + name);
            }
        }

        /// <inheritdoc />
        public Guid? UserId { get; } = Guid.NewGuid();
        /// <inheritdoc />
        public string UserName => "isolated-import-test";
        /// <inheritdoc />
        public bool IsAuthenticated => true;
        /// <inheritdoc />
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active ? new UserPermissionProjection { IsAdmin = Admin, Permissions = [PermissionKeys.ProductsMasterView, PermissionKeys.ProductsMasterCreate, PermissionKeys.ProductsMasterEdit, PermissionKeys.ProductsMasterDelete] } : null);
        /// <inheritdoc />
        public async Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default) =>
            (await GetProjectionAsync(userId, cancellationToken)) is { } p && (p.IsAdmin || p.Permissions.Contains(permissionKey));
    }

    /// <summary>共享的接口代理只转发显式测试行为。</summary>
    internal static T Proxy<T>(Func<string, object?[], object> call) where T : class
    { var result = DispatchProxy.Create<T, RepositoryProxy>(); ((RepositoryProxy)(object)result).Call = call; return result; }

    /// <summary>为仓储接口提供受控转发。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        internal Func<string, object?[], object> Call = null!;
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args ?? []);
    }

    /// <summary>提供共享事务槽位，不产生数据库连接。</summary>
    internal sealed class SessionStub(IDbConnection connection) : IDbSession
    {
        /// <inheritdoc />
        public IDbConnection Connection => connection;
        /// <inheritdoc />
        public IDbTransaction? Transaction { get; set; }
        /// <inheritdoc />
        public IDatabaseExceptionClassifier Errors => throw new NotSupportedException();
    }

    /// <summary>事务开始记录快照，回滚恢复父级列表。</summary>
    internal sealed class ConnectionStub(Action begin, Action rollback) : IDbConnection, IDbTransaction
    {
        internal bool Committed, RolledBack;
        /// <inheritdoc />
        public IDbTransaction BeginTransaction() { begin(); return this; }
        /// <inheritdoc />
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        /// <inheritdoc />
        public void Commit() => Committed = true;
        /// <inheritdoc />
        public void Rollback() { RolledBack = true; rollback(); }
        /// <inheritdoc />
        public void Close() { }
        /// <inheritdoc />
        public void Open() { }
        /// <inheritdoc />
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <inheritdoc />
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        /// <inheritdoc />
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        /// <inheritdoc />
        public int ConnectionTimeout => 0;
        /// <inheritdoc />
        public string Database => "memory";
        /// <inheritdoc />
        public ConnectionState State => ConnectionState.Open;
        /// <inheritdoc />
        public IDbConnection Connection => this;
        /// <inheritdoc />
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        /// <inheritdoc />
        public void Dispose() { }
    }
}

