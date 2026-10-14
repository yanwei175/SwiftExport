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
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Suppliers.Management;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证逐行事务、空白清空、服务器重验和管理员准入，不连接业务数据库。</summary>
[TestClass]
public sealed class ProductMasterBatchImportTests
{
    /// <summary>空白清空可空字段，保留模板与系列、编码和审计，只更新父级。</summary>
    [TestMethod]
    public async Task BlankFieldsClearAndUnrelatedFieldsRemain()
    {
        var fixture = new Fixture();
        var profileId = fixture.Masters.Single().ProductAttributeProfileId;
        var receipt = await fixture.Service.ImportAsync([new() { SourceRowNumber = 2, MasterSku = " m-1 " }]);
        Assert.IsTrue(receipt.Success, receipt.Message);
        Assert.IsTrue(receipt.Data.Items.Single().Success);
        var master = fixture.Masters.Single();
        Assert.AreEqual("M-1", master.MasterSku);
        Assert.IsNull(master.DefaultDescriptionZh);
        Assert.IsNull(master.DefaultPcsPerPackage);
        Assert.IsNull(master.DefaultNetWeight);
        Assert.AreEqual(Guid.Empty, master.CategoryId);
        Assert.AreEqual(Guid.Empty, master.DefaultSupplierId);
        Assert.AreEqual(profileId, master.ProductAttributeProfileId);
        Assert.AreEqual(fixture.OriginalSeriesId, master.ProductSeriesId);
        Assert.AreEqual("original", master.CreatedBy);
        Assert.AreEqual(5, master.Version);
        Assert.AreEqual(1, fixture.Connection.Commits);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>一行写入失败回滚当前行，后续行仍保存，先前行保持提交。</summary>
    [TestMethod]
    public async Task MixedRowsRollbackOnlyFailedRow()
    {
        var fixture = new Fixture { FailWrite = 2 };
        var saved = await fixture.Service.ImportAsync([
            new() { SourceRowNumber = 2, MasterSku = "M-1", DefaultDescriptionZh = "成功更新" },
            new() { SourceRowNumber = 3, MasterSku = "BAD" },
            new() { SourceRowNumber = 4, MasterSku = "GOOD", DefaultPcsPerPackage = "24", CategoryPath = "工具/夹具", DefaultSupplierCode = "sup-1" }
        ]);
        Assert.IsTrue(saved.Success);
        CollectionAssert.AreEqual(new[] { true, false, true }, saved.Data.Items.Select(item => item.Success).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, saved.Data.Items.Select(item => item.RowIndex!.Value).ToArray());
        Assert.AreEqual("成功更新", fixture.Masters.Single(m => m.MasterSku == "M-1").DefaultDescriptionZh);
        Assert.IsFalse(fixture.Masters.Any(m => m.MasterSku == "BAD"));
        var created = fixture.Masters.Single(m => m.MasterSku == "GOOD");
        Assert.AreEqual(24, created.DefaultPcsPerPackage);
        Assert.AreEqual(fixture.ChildCategory.Uid, created.CategoryId);
        Assert.AreEqual(fixture.Suppliers.Single().Uid, created.DefaultSupplierId);
        Assert.AreEqual(2, fixture.Connection.Commits);
        Assert.AreEqual(1, fixture.Connection.Rollbacks);
    }

    /// <summary>非管理员和失效账号必须在读取业务资料之前拒绝。</summary>
    [TestMethod]
    public async Task LiveAdminRequiredBeforeAnyRead()
    {
        var fixture = new Fixture { Admin = false };
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await fixture.Service.ImportAsync([new() { SourceRowNumber = 2, MasterSku = "M-1" }])).Error.Code);
        fixture.Admin = true; fixture.Active = false;
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated, (await fixture.Service.ImportAsync([new() { SourceRowNumber = 2, MasterSku = "M-1" }])).Error.Code);
        Assert.AreEqual(0, fixture.Reads);
    }

    /// <summary>一百行是传输批次上限，禁止无效和重复原行号。</summary>
    [TestMethod]
    public async Task BatchBoundaryRejectsInvalidShape()
    {
        var fixture = new Fixture();
        foreach (var input in new[] { new List<ProductMasterImportRowDto>(), Enumerable.Range(2,101).Select(i => new ProductMasterImportRowDto { SourceRowNumber=i,MasterSku="M"+i }).ToList(),
            new List<ProductMasterImportRowDto> { new() { SourceRowNumber=1 }, new() { SourceRowNumber=2 } },
            new List<ProductMasterImportRowDto> { new() { SourceRowNumber=2 }, new() { SourceRowNumber=2 } } })
            Assert.AreEqual(ServiceErrorCodes.Validation, (await fixture.Service.ImportAsync(input)).Error.Code);
        Assert.AreEqual(0, fixture.Reads);
    }

    /// <summary>逐行校验所有字段、精度及文件内重复，错误行不写入。</summary>
    [TestMethod]
    public async Task InvalidNumbersReferencesAndDuplicatesReturnRowReasons()
    {
        var fixture = new Fixture();
        var saved = await fixture.Service.ImportAsync([
            new() { SourceRowNumber=2, MasterSku="A", DefaultPcsPerPackage="0", DefaultNetWeight="-1", DefaultCbm="0.1234567" },
            new() { SourceRowNumber=3, MasterSku="B", CategoryPath="不存在" },
            new() { SourceRowNumber=4, MasterSku="C", DefaultSupplierCode="不存在" },
            new() { SourceRowNumber=5, MasterSku="D" }, new() { SourceRowNumber=6, MasterSku=" d " }
        ]);
        Assert.IsTrue(saved.Success); Assert.AreEqual(5, saved.Data.FailedCount); Assert.AreEqual(0, fixture.Writes);
        StringAssert.Contains(saved.Data.Items[0].Message, "装率"); StringAssert.Contains(saved.Data.Items[0].Message, "体积");
        Assert.IsTrue(saved.Data.Items.All(item=>!string.IsNullOrWhiteSpace(item.Message)));
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>分类清空不再影响模板；导入不能改写未包含在模板中的属性模板字段。</summary>
    [TestMethod]
    public async Task ImportCanClearCategoryWithoutChangingProfile()
    {
        var fixture = new Fixture(); fixture.Masters[0].CategoryId=fixture.ChildCategory.Uid;
        var profileId = fixture.Masters[0].ProductAttributeProfileId;
        var saved = await fixture.Service.ImportAsync([new() { SourceRowNumber=2,MasterSku="M-1" }]);
        Assert.IsTrue(saved.Data.Items.Single().Success);
        Assert.AreEqual(Guid.Empty,fixture.Masters[0].CategoryId);
        Assert.AreEqual(profileId,fixture.Masters[0].ProductAttributeProfileId);
        Assert.AreEqual(1,fixture.Writes);
    }

    /// <summary>取消发生在第二行写入后时只撤销该行，已提交的第一行保持有效。</summary>
    [TestMethod]
    public async Task CancellationAndTechnicalFailureKeepPriorCommit()
    {
        var fixture = new Fixture(); using var cancellation = new CancellationTokenSource();
        fixture.AfterWrite=()=>{if(fixture.Writes==2)cancellation.Cancel();};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>fixture.Service.ImportAsync([
            new() {SourceRowNumber=2,MasterSku="A"},new() {SourceRowNumber=3,MasterSku="B"},new() {SourceRowNumber=4,MasterSku="C"}],cancellation.Token));
        Assert.IsTrue(fixture.Masters.Any(m=>m.MasterSku=="A"));Assert.IsFalse(fixture.Masters.Any(m=>m.MasterSku=="B"));Assert.AreEqual(1,fixture.Connection.Commits);
        fixture=new Fixture {TechnicalFailWrite=2};
        var interrupted=await fixture.Service.ImportAsync([new(){SourceRowNumber=2,MasterSku="A"},new(){SourceRowNumber=3,MasterSku="B"}]);
        Assert.IsFalse(interrupted.Success);Assert.IsTrue(fixture.Masters.Any(m=>m.MasterSku=="A"));Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>模拟有事务快照的父级仓储；任何未明确允许的调用都会使测试失败。</summary>
    internal sealed class Fixture : ICurrentUser, IPermissionEvaluationService
    {
        internal readonly Guid OriginalSeriesId = Guid.NewGuid();
        internal List<ProductMaster> Masters = [new() { MasterSku = "M-1", Version = 4, DefaultDescriptionZh = "旧描述", DefaultPcsPerPackage = 12, ProductAttributeProfileId = Guid.NewGuid(), CreatedBy = "original" }];
        internal List<Supplier> Suppliers = [new() { SupplierCode = "SUP-1", SupplierName = "测试供应商", DocRelateNo = "S" }];
        internal ProductCategory RootCategory = new() { CategoryName = "工具" };
        internal ProductCategory ChildCategory = new() { CategoryName = "夹具" };
        internal readonly SessionStub Session;
        internal readonly ConnectionStub Connection;
        internal readonly ProductMasterImportService Service;
        internal bool Admin = true, Active = true;
        internal int Writes, Reads, FailWrite, TechnicalFailWrite;
        internal Action? AfterWrite;
        internal Fixture()
        {
            Masters[0].ProductSeriesId = OriginalSeriesId;
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
                    "GetByIdAsync" => Task.FromResult(Suppliers.SingleOrDefault(s => s.Uid == (Guid)args[0]!)),
                    _ => throw new AssertFailedException(name)
                };
            });
            Service = new(masters, categories, suppliers, this, this,
                new ServiceBaseContext(Session, NullLoggerFactory.Instance));
        }

        /// <summary>处理新导入路径允许的根仓储操作，并模拟数据库编码大小写规则。</summary>
        private object MasterCall(string name, object?[] args)
        {
            Reads++;
            switch (name)
            {
                case "GetByMasterSkusAsync":
                    Assert.IsNotNull(Session.Transaction);
                    // 每行先观察编码身份再取主型号锁，避免锁前后占用者变化；不再读取标签仓储。
                    Assert.IsInstanceOfType<bool>(args[1]);
                    return Task.FromResult<IReadOnlyList<ProductMaster>>(Masters.Where(m => ((IEnumerable<string>)args[0]!).Contains(m.MasterSku, StringComparer.OrdinalIgnoreCase)).Select(m => m.Adapt<ProductMaster>()).ToArray());
                case "GetByIdListAsync":
                    return Task.FromResult<IReadOnlyList<ProductMaster>>(Masters.Where(m => ((IEnumerable<Guid>)args[0]!).Contains(m.Uid)).Select(m => m.Adapt<ProductMaster>()).ToArray());
                case "GetByIdAsync":
                    return Task.FromResult(Masters.Single(m => m.Uid == (Guid)args[0]!).Adapt<ProductMaster>());
                case "AddAsync":
                case "UpdateAsync":
                    Assert.IsNotNull(Session.Transaction);
                    Writes++;
                    if (Writes == TechnicalFailWrite) throw new InvalidOperationException("存储连接失效");
                    var entity = ((ProductMaster)args[0]!).Adapt<ProductMaster>();
                    if (name == "UpdateAsync") { Masters.RemoveAll(m => m.Uid == entity.Uid); entity.Version++; }
                    Masters.Add(entity);
                    if (Writes == FailWrite) return Task.FromResult(RepositoryWriteResult.Fail(entity.Uid, entity.Version, "CONCURRENCY_CONFLICT", "冲突"));
                    AfterWrite?.Invoke();
                    return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, entity.Version - 1, entity.Version));
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
        internal int Commits, Rollbacks;
        internal bool Committed => Commits > 0;
        internal bool RolledBack => Rollbacks > 0;
        /// <inheritdoc />
        public IDbTransaction BeginTransaction() { begin(); return this; }
        /// <inheritdoc />
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        /// <inheritdoc />
        public void Commit() => Commits++;
        /// <inheritdoc />
        public void Rollback() { Rollbacks++; rollback(); }
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


