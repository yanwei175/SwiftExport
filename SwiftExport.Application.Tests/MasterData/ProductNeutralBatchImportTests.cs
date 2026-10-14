using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Products.ProductNeutrals.Import;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using static SwiftExport.Application.Tests.MasterData.ProductMasterBatchImportTests;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证中性导入的编码匹配、引用锁、逐行回滚及权限，不连接业务数据库。</summary>
[TestClass]
public sealed class ProductNeutralBatchImportTests
{
    /// <summary>已有编码只覆盖导入字段，空白清空，颜色可更新，主图和审计保持不变。</summary>
    [TestMethod]
    public async Task UpdateClearsBlankFieldsPreservesIdentityAndMainFlag()
    {
        var f = new Fixture(); var uid = f.Neutrals[0].Uid;
        var result = await f.Service.ImportAsync([f.Row(" n-1 ")]);
        Assert.IsTrue(result.Data.Items.Single().Success);
        var saved = f.Neutrals.Single();
        Assert.AreEqual(uid, saved.Uid); Assert.AreEqual("N-1", saved.ProductCode);
        Assert.AreEqual(f.Color.Uid, saved.ColorId); Assert.IsTrue(saved.IsMainImage);
        Assert.IsNull(saved.DefaultPurchasePriceCny); Assert.IsNull(saved.DefaultSalesPriceUsd);
        Assert.IsNull(saved.DefaultExchangeRate); Assert.IsNull(saved.ProfitMargin); Assert.IsNull(saved.ImagePath);
        Assert.AreEqual("original", saved.CreatedBy); Assert.AreEqual(5, saved.Version);
        CollectionAssert.AreEqual(new[] { "color", "parent", "neutral" }, f.Locks.ToArray());
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>不同编码可使用任意有效颜色；价格直接保存，新记录不自动设主图。</summary>
    [TestMethod]
    public async Task NewCodeSavesRawPricesAndImageWithoutCalculating()
    {
        var f = new Fixture(); var row = f.Row("NEW");
        row.DefaultPurchasePriceCny = "12.3456"; row.DefaultSalesPriceUsd = "6.5";
        row.DefaultExchangeRate = "7.123456"; row.ProfitMargin = "0.2"; row.ImagePath = " https://images.example/a.png ";
        Assert.IsTrue((await f.Service.ImportAsync([row])).Data.Items.Single().Success);
        var saved = f.Neutrals.Single(item => item.ProductCode == "NEW");
        Assert.AreEqual(f.Parent.Uid, saved.ProductMasterId); Assert.IsFalse(saved.IsMainImage);
        Assert.AreEqual(12.3456m, saved.DefaultPurchasePriceCny); Assert.AreEqual(6.5m, saved.DefaultSalesPriceUsd);
        Assert.AreEqual(7.123456m, saved.DefaultExchangeRate); Assert.AreEqual(0.2m, saved.ProfitMargin);
        Assert.AreEqual("https://images.example/a.png", saved.ImagePath);
    }

    /// <summary>普通模板不调整已有编码归属，另一个父级不能覆盖该对象。</summary>
    [TestMethod]
    public async Task OtherParentCannotTakeExistingCode()
    {
        var f = new Fixture(); f.Neutrals[0].ProductMasterId = Guid.NewGuid();
        var result = await f.Service.ImportAsync([f.Row("N-1")]);
        Assert.IsFalse(result.Data.Items.Single().Success); Assert.AreEqual(ServiceErrorCodes.Validation, result.Data.Items[0].ErrorCode);
        Assert.AreEqual(0, f.Writes); Assert.AreEqual(0, f.Connection.Commits);
    }

    /// <summary>业务失败只回滚当前行，下一行仍可提交，已提交行保持有效。</summary>
    [TestMethod]
    public async Task MixedBatchCommitsOnlySuccessfulRows()
    {
        var f = new Fixture { FailWrite = 2 };
        var result = await f.Service.ImportAsync([f.Row("A", 2), f.Row("B", 3), f.Row("C", 4)]);
        CollectionAssert.AreEqual(new[] { true, false, true }, result.Data.Items.Select(item => item.Success).ToArray());
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Data.Items[1].ErrorCode);
        Assert.IsFalse(f.Neutrals.Any(item => item.ProductCode == "B"));
        Assert.AreEqual(2, f.Connection.Commits); Assert.AreEqual(1, f.Connection.Rollbacks);
    }

    /// <summary>数值范围和精度在写入前拒绝，既不四舍五入也不依赖机器文化。</summary>
    [TestMethod]
    [DataRow("price", "-1")][DataRow("price", "100000000000000")][DataRow("price", "0.12345")]
    [DataRow("rate", "0")][DataRow("rate", "1.1234567")][DataRow("rate", "1000000000000")]
    [DataRow("margin", "1")][DataRow("margin", "-0.1")][DataRow("margin", "20%")]
    public async Task InvalidNumericValuesReturnRowReasons(string field, string value)
    {
        var f = new Fixture(); var row = f.Row("NEW");
        if (field == "price") row.DefaultPurchasePriceCny = value;
        if (field == "rate") row.DefaultExchangeRate = value;
        if (field == "margin") row.ProfitMargin = value;
        var result = await f.Service.ImportAsync([row]);
        Assert.IsFalse(result.Data.Items.Single().Success); Assert.AreEqual(0, f.Reads);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Data.Items[0].Message));
    }

    /// <summary>重复编码、缺失引用和过长字段均返回原行号及原因，不创建隐式主数据。</summary>
    [TestMethod]
    public async Task DuplicatesAndMissingReferencesDoNotWrite()
    {
        var f = new Fixture(); var noParent = f.Row("A", 2); noParent.MasterSku = "missing";
        var noColor = f.Row("B", 3); noColor.ColorCodeUnique = "missing";
        var longImage = f.Row("C", 4); longImage.ImagePath = new string('x', 1001);
        var result = await f.Service.ImportAsync([noParent, noColor, longImage, f.Row("D", 5), f.Row(" d ", 6)]);
        Assert.AreEqual(5, result.Data.FailedCount); Assert.AreEqual(0, f.Writes);
        CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6 }, result.Data.Items.Select(item => item.RowIndex!.Value).ToArray());
    }

    /// <summary>锁前颜色删除、父级替换或编码搬走均需重验，不能使用过期观察继续保存。</summary>
    [TestMethod]
    [DataRow("color")][DataRow("parent")][DataRow("neutral")]
    public async Task ChangedReferencesOrIdentityRejectAfterLock(string change)
    {
        var f = new Fixture();
        f.BeforeLock = name =>
        {
            if (name != change) return;
            if (change == "color") f.Color.IsDeleted = true;
            if (change == "parent") f.Parent.Uid = Guid.NewGuid();
            if (change == "neutral") f.Neutrals[0].ProductMasterId = Guid.NewGuid();
        };
        Assert.IsFalse((await f.Service.ImportAsync([f.Row("N-1")])).Data.Items.Single().Success);
        Assert.AreEqual(0, f.Writes); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>观察后编码搬走时，在持本父锁期间不得再锁另一父级的子行。</summary>
    [TestMethod]
    public async Task MovedOwnerIsRejectedBeforeTakingForeignChildLock()
    {
        var f = new Fixture();
        f.BeforeLock = name => { if (name == "parent") f.Neutrals[0].ProductMasterId = Guid.NewGuid(); };
        var result = await f.Service.ImportAsync([f.Row("N-1")]);
        Assert.IsFalse(result.Data.Items.Single().Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Data.Items[0].ErrorCode);
        CollectionAssert.AreEqual(new[] { "color", "parent" }, f.Locks.ToArray());
        Assert.AreEqual(0, f.Writes);
    }

    /// <summary>实时活动管理员检查早于资料读取，原有事务也不会被此用例清理。</summary>
    [TestMethod]
    public async Task AuthorityAndBatchShapeAreCheckedBeforeReading()
    {
        var f = new Fixture { Admin = false };
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Service.ImportAsync([f.Row("A")])).Error.Code);
        f.Admin = true; f.Active = false;
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated, (await f.Service.ImportAsync([f.Row("A")])).Error.Code);
        f.Active = true;
        Assert.IsFalse((await f.Service.ImportAsync([])).Success);
        Assert.IsFalse((await f.Service.ImportAsync(Enumerable.Range(2, 101).Select(i => f.Row("N" + i, i)).ToList())).Success);
        Assert.IsFalse((await f.Service.ImportAsync([f.Row("A", 1)])).Success);
        Assert.IsFalse((await f.Service.ImportAsync([f.Row("A"), f.Row("B")])).Success);
        f.Session.Transaction = f.Connection;
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, (await f.Service.ImportAsync([f.Row("A")])).Error.Code);
        Assert.AreSame(f.Connection, f.Session.Transaction); Assert.AreEqual(0, f.Reads);
    }

    /// <summary>取消及技术中断仅撤销在途行，前行提交仍在，批次不能被当成完整成功。</summary>
    [TestMethod]
    public async Task InterruptionsPreservePreviousCommit()
    {
        var f = new Fixture { TechnicalFailWrite = 2 };
        Assert.IsFalse((await f.Service.ImportAsync([f.Row("A", 2), f.Row("B", 3)])).Success);
        Assert.IsTrue(f.Neutrals.Any(item => item.ProductCode == "A")); Assert.IsNull(f.Session.Transaction);
        f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.AfterWrite = () => { if (f.Writes == 2) cancellation.Cancel(); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.ImportAsync([f.Row("A", 2), f.Row("B", 3)], cancellation.Token));
        Assert.IsTrue(f.Neutrals.Any(item => item.ProductCode == "A")); Assert.IsFalse(f.Neutrals.Any(item => item.ProductCode == "B"));
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>显式模拟引用和行事务快照，可观察加锁顺序以及提交与回滚效果。</summary>
    internal sealed class Fixture : ICurrentUser, IPermissionEvaluationService
    {
        internal readonly ProductMaster Parent = new() { MasterSku = "M-1" };
        internal readonly ProductColor Color = new() { ColorCodeUnique = "C-1", ColorCode = "C" };
        internal List<ProductNeutral> Neutrals;
        internal readonly List<string> Locks = [];
        internal readonly ConnectionStub Connection;
        internal readonly SessionStub Session;
        internal readonly ProductNeutralImportService Service;
        internal bool Admin = true, Active = true;
        internal int Reads, Writes, FailWrite, TechnicalFailWrite;
        internal Action<string>? BeforeLock;
        internal Action? AfterWrite;
        internal Fixture()
        {
            Neutrals = [new() { ProductCode = "N-1", ProductMasterId = Parent.Uid, ColorId = Guid.NewGuid(), Version = 4,
                IsMainImage = true, ImagePath = "old", CreatedBy = "original", DefaultPurchasePriceCny = 10, DefaultSalesPriceUsd = 20,
                DefaultExchangeRate = 7, ProfitMargin = 0.2m }];
            List<ProductNeutral> snapshot = [];
            Connection = new(() => snapshot = Neutrals.Select(item => item.Adapt<ProductNeutral>()).ToList(),
                () => Neutrals = snapshot.Select(item => item.Adapt<ProductNeutral>()).ToList());
            Session = new(Connection);
            var master = Proxy<IProductMasterRepository>((name, args) =>
            {
                Reads++; Assert.AreEqual("GetByMasterSkusAsync", name); Lock("parent", (bool)args[1]!);
                return Task.FromResult<IReadOnlyList<ProductMaster>>(((IEnumerable<string>)args[0]!).Contains(Parent.MasterSku, StringComparer.OrdinalIgnoreCase)
                    && !Parent.IsDeleted ? [Parent.Adapt<ProductMaster>()] : []);
            });
            var color = Proxy<IProductColorRepository>((name, args) =>
            {
                Reads++;
                if (name == "GetByColorCodesAsync") return Task.FromResult<IReadOnlyList<ProductColor>>(((IEnumerable<string>)args[0]!).Contains(Color.ColorCodeUnique, StringComparer.OrdinalIgnoreCase) && !Color.IsDeleted ? [Color.Adapt<ProductColor>()] : []);
                Assert.AreEqual("GetByIdAsync", name); Lock("color", (bool)args[1]!);
                return Task.FromResult(!Color.IsDeleted && (Guid)args[0]! == Color.Uid ? Color.Adapt<ProductColor>() : null);
            });
            var neutral = Proxy<IProductNeutralRepository>((name, args) =>
            {
                Reads++;
                if (name == "GetByProductCodesAsync")
                {
                    Lock("neutral", (bool)args[1]!);
                    return Task.FromResult<IReadOnlyList<ProductNeutral>>(Neutrals.Where(item => !item.IsDeleted && ((IEnumerable<string>)args[0]!).Contains(item.ProductCode, StringComparer.OrdinalIgnoreCase)).Select(item => item.Adapt<ProductNeutral>()).ToArray());
                }
                if (name == "GetByIdAsync")
                {
                    Lock("neutral", (bool)args[1]!);
                    return Task.FromResult(Neutrals.SingleOrDefault(item => item.Uid == (Guid)args[0]! && !item.IsDeleted)?.Adapt<ProductNeutral>());
                }
                Assert.IsTrue(name is "AddAsync" or "UpdateAsync"); Assert.IsNotNull(Session.Transaction);
                Writes++; if (Writes == TechnicalFailWrite) throw new InvalidOperationException("受控技术中断");
                var entity = ((ProductNeutral)args[0]!).Adapt<ProductNeutral>(); var oldVersion = entity.Version;
                if (name == "UpdateAsync") { Neutrals.RemoveAll(item => item.Uid == entity.Uid); entity.Version++; }
                Neutrals.Add(entity); AfterWrite?.Invoke();
                return Task.FromResult(Writes == FailWrite ? RepositoryWriteResult.Fail(entity.Uid, oldVersion, "PRODUCT_NEUTRAL_CODE_OCCUPIED", "编码冲突")
                    : RepositoryWriteResult.Ok(entity.Uid, name == "UpdateAsync" ? oldVersion : null, entity.Version));
            });
            Service = new(master, color, neutral, this, this, new ServiceBaseContext(Session, NullLoggerFactory.Instance));
        }
        internal ProductNeutralImportRowDto Row(string code, int row = 2) => new() { SourceRowNumber = row, MasterSku = " m-1 ", ProductCode = code, ColorCodeUnique = " c-1 " };
        private void Lock(string name, bool locked) { if (!locked) return; Assert.IsNotNull(Session.Transaction); Locks.Add(name); BeforeLock?.Invoke(name); }
        /// <inheritdoc />
        public Guid? UserId { get; } = Guid.NewGuid();
        /// <inheritdoc />
        public string UserName => "neutral-import-test";
        /// <inheritdoc />
        public bool IsAuthenticated => true;
        /// <inheritdoc />
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active ? new UserPermissionProjection { IsAdmin = Admin, Permissions = [PermissionKeys.ProductsNeutralView, PermissionKeys.ProductsNeutralEdit] } : null);
        /// <inheritdoc />
        public async Task<bool> HasPermissionAsync(Guid userId, string key, CancellationToken cancellationToken = default) =>
            (await GetProjectionAsync(userId, cancellationToken)) is { } p && (p.IsAdmin || p.Permissions.Contains(key));
    }
}
