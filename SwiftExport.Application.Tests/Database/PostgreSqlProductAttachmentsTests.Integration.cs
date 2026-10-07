using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>使用既有通用仓储验证产品文件资料与包装的 PostgreSQL 映射及事务；随机夹具只回滚，不提交或访问真实文件。</summary>
[TestClass]
[TestCategory("PostgreSqlProductAttachmentsIntegration")]
public sealed class PostgreSqlProductAttachmentsIntegrationTests
{
    /// <summary>资料旧表名独立映射为 product_detail，全部路径只是文本；空关联、更新冲突及不递增版本的软删沿用现有语义。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Detail_CrudPreservesMappingAndNullableAssociation(bool withoutProduct) => InRollback(async context =>
    {
        var product = withoutProduct ? null : await AddProduct(context);
        var repository = new PostgreSqlDapperRepository<ProductDetail>(context);
        var detail = NewDetail(product?.Uid);
        var insert = await repository.AddAsync(detail);
        Assert.IsTrue(insert.Success, insert.Message);
        Assert.AreEqual(1, insert.NewVersion);
        Assert.AreEqual("product_detail", EntityMetadataCache.GetMetadata(typeof(ProductDetail)).TableName);
        Assert.AreEqual(detail.ProductSKU, await context.Session.Connection.ExecuteScalarAsync<string>(
            "SELECT product_sku FROM public.product_detail WHERE uid=@Uid", new { detail.Uid }, context.Session.Transaction));

        var read = await repository.GetByIdAsync(detail.Uid, true);
        Assert.IsNotNull(read);
        AssertDetail(detail, read);
        AssertAudit(read);
        var stale = await repository.GetByIdAsync(detail.Uid);
        Assert.IsNotNull(stale);
        read.ProductId = null;
        read.LabelPath = null;
        read.IMPath = "仅存文本：更新后的说明书";
        var update = await repository.UpdateAsync(read);
        Assert.IsTrue(update.Success, update.Message);
        Assert.AreEqual(2, update.NewVersion);
        var saved = await repository.GetByIdAsync(detail.Uid);
        Assert.IsNotNull(saved);
        AssertDetail(read, saved);
        Assert.AreEqual(2, saved.Version);
        stale.LabelPath = "不能写入的旧版本标签";
        AssertConflict(await repository.UpdateAsync(stale));
        Assert.IsNull((await repository.GetByIdAsync(detail.Uid))!.LabelPath);
        AssertConflict(await repository.DeleteAsync(detail.Uid, 1));
        Assert.IsNotNull(await repository.GetByIdAsync(detail.Uid));
        var deletion = await repository.DeleteAsync(detail.Uid, 2);
        Assert.IsTrue(deletion.Success, deletion.Message);
        Assert.AreEqual(2, deletion.NewVersion, "通用软删保持版本，不属于级联删除的递增协议。");
        Assert.IsNull(await repository.GetByIdAsync(detail.Uid));
        await AssertSoftDeleted(context, "product_detail", detail.Uid, 2);
    });

    /// <summary>包装全部业务列含 Nw_KG/Gw_KG 精确往返；可空字段可清除，陈旧写入不覆盖新值，软删保持当前版本。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Package_CrudPreservesWeightsAndNullableAssociation(bool withoutProduct) => InRollback(async context =>
    {
        var product = withoutProduct ? null : await AddProduct(context);
        var repository = new PostgreSqlDapperRepository<ProductPackage>(context);
        var package = NewPackage(product?.Uid);
        var insert = await repository.AddAsync(package);
        Assert.IsTrue(insert.Success, insert.Message);
        Assert.AreEqual(1, insert.NewVersion);
        var read = await repository.GetByIdAsync(package.Uid, true);
        Assert.IsNotNull(read);
        AssertPackage(package, read);
        AssertAudit(read);
        var physical = await context.Session.Connection.QuerySingleAsync<(decimal Net, decimal Gross)>(
            "SELECT nw_kg,gw_kg FROM public.product_package WHERE uid=@Uid", new { package.Uid }, context.Session.Transaction);
        Assert.AreEqual(package.Nw_KG, physical.Net);
        Assert.AreEqual(package.Gw_KG, physical.Gross);
        var stale = await repository.GetByIdAsync(package.Uid);
        Assert.IsNotNull(stale);
        read.ProductId = null;
        read.Nw_KG = null;
        read.Gw_KG = 7.4321m;
        read.Length_CM = null;
        read.PackagingMaterialSku = null;
        read.Note = null;
        read.IsDefault = false;
        var update = await repository.UpdateAsync(read);
        Assert.IsTrue(update.Success, update.Message);
        Assert.AreEqual(2, update.NewVersion);
        var saved = await repository.GetByIdAsync(package.Uid);
        Assert.IsNotNull(saved);
        AssertPackage(read, saved);
        Assert.AreEqual(2, saved.Version);
        stale.Gw_KG = 99m;
        AssertConflict(await repository.UpdateAsync(stale));
        Assert.AreEqual(7.4321m, (await repository.GetByIdAsync(package.Uid))!.Gw_KG);
        AssertConflict(await repository.DeleteAsync(package.Uid, 1));
        Assert.IsNotNull(await repository.GetByIdAsync(package.Uid));
        var deletion = await repository.DeleteAsync(package.Uid, 2);
        Assert.IsTrue(deletion.Success, deletion.Message);
        Assert.AreEqual(2, deletion.NewVersion);
        Assert.IsNull(await repository.GetByIdAsync(package.Uid));
        await AssertSoftDeleted(context, "product_package", package.Uid, 2);
    });

    /// <summary>两个不同泛型仓储复用同一会话事务，回滚后按随机 UID 确认两表暂存记录均不存在。</summary>
    [TestMethod]
    public async Task DetailAndPackage_ShouldShareRollbackTransaction()
    {
        using var session = NewSession();
        var context = Context(session);
        var details = new PostgreSqlDapperRepository<ProductDetail>(context);
        var packages = new PostgreSqlDapperRepository<ProductPackage>(context);
        var detail = NewDetail(null);
        var package = NewPackage(null);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            Assert.IsTrue((await details.AddAsync(detail)).Success);
            Assert.IsTrue((await packages.AddAsync(package)).Success);
            Assert.IsNotNull(await details.GetByIdAsync(detail.Uid));
            Assert.IsNotNull(await packages.GetByIdAsync(package.Uid));
            Assert.AreSame(transaction, session.Transaction);
        }
        finally
        {
            try { transaction.Rollback(); }
            finally { session.Transaction = null; }
        }
        Assert.IsNull(await details.GetByIdAsync(detail.Uid));
        Assert.IsNull(await packages.GetByIdAsync(package.Uid));
    }

    /// <summary>非空产品下相同包装类型的新增及更新均抛精确 23505，保存点恢复后原记录、版本和事务仍可读取。</summary>
    [TestMethod]
    public Task Package_CompositeCollisionShouldRemainProviderExceptionAndRecoverAtSavepoint() => InRollback(async context =>
    {
        var product = await AddProduct(context);
        var repository = new PostgreSqlDapperRepository<ProductPackage>(context);
        var carton = NewPackage(product.Uid);
        var pallet = NewPackage(product.Uid);
        pallet.PackageType = "Pallet";
        Assert.IsTrue((await repository.AddAsync(carton)).Success);
        Assert.IsTrue((await repository.AddAsync(pallet)).Success);
        await AssertPackageCollision(context, () => repository.AddAsync(NewPackage(product.Uid)));
        Assert.AreEqual(1, (await repository.GetByIdAsync(carton.Uid))!.Version);
        var changing = await repository.GetByIdAsync(pallet.Uid);
        Assert.IsNotNull(changing);
        changing.PackageType = "Carton";
        await AssertPackageCollision(context, () => repository.UpdateAsync(changing));
        var unchanged = await repository.GetByIdAsync(pallet.Uid);
        Assert.IsNotNull(unchanged);
        Assert.AreEqual("Pallet", unchanged.PackageType);
        Assert.AreEqual(1, unchanged.Version);
        Assert.AreEqual(2, await context.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.product_package WHERE product_id=@Id", new { Id = product.Uid }, context.Session.Transaction));
    });

    /// <summary>沿用现有普通 PackageType 大小写比较；空产品不参与组合唯一，软删记录也不占用原组合，不新增规则。</summary>
    [TestMethod]
    public Task Package_UniquenessShouldPreserveCaseNullAndSoftDeleteBoundaries() => InRollback(async context =>
    {
        var product = await AddProduct(context);
        var repository = new PostgreSqlDapperRepository<ProductPackage>(context);
        var upper = NewPackage(product.Uid);
        var lower = NewPackage(product.Uid);
        lower.PackageType = "carton";
        Assert.IsTrue((await repository.AddAsync(upper)).Success);
        Assert.IsTrue((await repository.AddAsync(lower)).Success, "普通类型比较尚未改为忽略大小写的编码规则。");
        Assert.IsTrue((await repository.AddAsync(NewPackage(null))).Success);
        Assert.IsTrue((await repository.AddAsync(NewPackage(null))).Success);
        Assert.IsTrue((await repository.DeleteAsync(upper.Uid, 1)).Success);
        var replacement = NewPackage(product.Uid);
        Assert.IsTrue((await repository.AddAsync(replacement)).Success);
        Assert.IsNotNull(await repository.GetByIdAsync(lower.Uid));
        Assert.IsNotNull(await repository.GetByIdAsync(replacement.Uid));
        Assert.IsNull(await repository.GetByIdAsync(upper.Uid));
    });

    /// <summary>每例仅在固定故障测试库开启事务；任何断言失败也回滚自己的随机夹具，不初始化、提交或删除旧数据。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> action)
    {
        using var session = NewSession();
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { await action(Context(session)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>唯一连接入口沿用已批准专用测试库校验，不接受其他连接或降级环境。</summary>
    private static DbSession NewSession() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>两个仓储共享同一连接、事务及审计上下文。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    {
        var user = new TestUser();
        return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }

    /// <summary>创建有合法必填 SKU 和供应方式的随机产品，关联资料均在同一事务中回滚。</summary>
    private static async Task<Product> AddProduct(RepositoryBaseContext context)
    {
        var product = new Product { Sku = Prefix(), SupplyType = "Purchase", Unit = "PCS" };
        var result = await new PostgreSqlDapperRepository<Product>(context).AddAsync(product);
        Assert.IsTrue(result.Success, result.Message);
        return product;
    }

    /// <summary>为所有资料列设置不同的纯文本，保证映射错误不会被相同字段值掩盖；不读取或生成实际文件。</summary>
    private static ProductDetail NewDetail(Guid? productId) => new()
    {
        ProductId = productId, ProductSKU = Prefix(), LabelPath = "文本标签：label",
        IMPath = "文本说明书：manual", DrawingPath = "文本图纸：drawing", LinedrawingPath = "文本线稿：line",
        SpecSheetPath = "文本规格书：spec", Barcode1Path = "文本条码一：barcode1", Barcode2Path = "文本条码二：barcode2"
    };

    /// <summary>构造满足现有数量和非负尺寸约束的包装，各数值不同以检测下划线字段串列。</summary>
    private static ProductPackage NewPackage(Guid? productId) => new()
    {
        ProductId = productId, PackageType = "Carton", PcsPerPackage = 12,
        Length_CM = 11.1234m, Width_CM = 22.2345m, Height_CM = 33.3456m,
        Nw_KG = 4.4567m, Gw_KG = 5.5678m, Cbm = 0.123456m,
        IsDefault = true, PackagingMaterialSku = Prefix(), Note = "随机包装备注"
    };

    /// <summary>逐项检查文件资料的身份、关联和全部路径映射。</summary>
    private static void AssertDetail(ProductDetail expected, ProductDetail actual)
    {
        Assert.AreEqual(expected.Uid, actual.Uid);
        Assert.AreEqual(expected.ProductId, actual.ProductId);
        Assert.AreEqual(expected.ProductSKU, actual.ProductSKU);
        Assert.AreEqual(expected.LabelPath, actual.LabelPath);
        Assert.AreEqual(expected.IMPath, actual.IMPath);
        Assert.AreEqual(expected.DrawingPath, actual.DrawingPath);
        Assert.AreEqual(expected.LinedrawingPath, actual.LinedrawingPath);
        Assert.AreEqual(expected.SpecSheetPath, actual.SpecSheetPath);
        Assert.AreEqual(expected.Barcode1Path, actual.Barcode1Path);
        Assert.AreEqual(expected.Barcode2Path, actual.Barcode2Path);
    }

    /// <summary>逐项检查包装身份、数量、尺寸、重量、体积及包材文本映射。</summary>
    private static void AssertPackage(ProductPackage expected, ProductPackage actual)
    {
        Assert.AreEqual(expected.Uid, actual.Uid);
        Assert.AreEqual(expected.ProductId, actual.ProductId);
        Assert.AreEqual(expected.PackageType, actual.PackageType);
        Assert.AreEqual(expected.PcsPerPackage, actual.PcsPerPackage);
        Assert.AreEqual(expected.Length_CM, actual.Length_CM);
        Assert.AreEqual(expected.Width_CM, actual.Width_CM);
        Assert.AreEqual(expected.Height_CM, actual.Height_CM);
        Assert.AreEqual(expected.Nw_KG, actual.Nw_KG);
        Assert.AreEqual(expected.Gw_KG, actual.Gw_KG);
        Assert.AreEqual(expected.Cbm, actual.Cbm);
        Assert.AreEqual(expected.IsDefault, actual.IsDefault);
        Assert.AreEqual(expected.PackagingMaterialSku, actual.PackagingMaterialSku);
        Assert.AreEqual(expected.Note, actual.Note);
    }

    /// <summary>确认通用仓储写入审计并维持有效记录初始版本。</summary>
    private static void AssertAudit(BaseEntity row)
    {
        Assert.AreEqual(1, row.Version);
        Assert.IsFalse(row.IsDeleted);
        Assert.AreEqual("__pgtest_attachments__", row.CreatedBy);
        Assert.AreEqual("__pgtest_attachments__", row.UpdatedBy);
        Assert.IsNotNull(row.CreatedDate);
        Assert.IsNotNull(row.UpdatedDate);
    }

    /// <summary>通用仓储的版本冲突保持既有写入回执，不能解释为唯一冲突。</summary>
    private static void AssertConflict(SwiftExport.Contracts.Wrappers.RepositoryWriteResult result)
    {
        Assert.IsFalse(result.Success);
        Assert.AreEqual("CONCURRENCY_CONFLICT", result.ErrorCode);
        Assert.IsNull(result.NewVersion);
        Assert.AreEqual(0, result.AffectedRows);
    }

    /// <summary>只查询本例随机 UID 的物理软删状态，不把不再返回记录误认为硬删除。</summary>
    private static async Task AssertSoftDeleted(RepositoryBaseContext context, string table, Guid uid, int version)
    {
        if (table is not "product_detail" and not "product_package") throw new ArgumentOutOfRangeException(nameof(table));
        var row = await context.Session.Connection.QuerySingleAsync<(bool Deleted, int Version)>(
            $"SELECT is_deleted,version FROM public.{table} WHERE uid=@Uid", new { Uid = uid }, context.Session.Transaction);
        Assert.IsTrue(row.Deleted);
        Assert.AreEqual(version, row.Version);
    }

    /// <summary>只在保存点内制造包装精确组合冲突，随后恢复事务；不把其他约束失败当成预期成功。</summary>
    private static async Task AssertPackageCollision(RepositoryBaseContext context, Func<Task<SwiftExport.Contracts.Wrappers.RepositoryWriteResult>> action)
    {
        await context.Session.Connection.ExecuteAsync("SAVEPOINT attachment_collision", transaction: context.Session.Transaction);
        try
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(action);
            Assert.AreEqual("23505", exception.SqlState);
            Assert.AreEqual("public", exception.SchemaName);
            Assert.AreEqual("product_package", exception.TableName);
            Assert.AreEqual("ux_product_package_product_type", exception.ConstraintName);
        }
        finally
        {
            await context.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT attachment_collision", transaction: context.Session.Transaction);
            await context.Session.Connection.ExecuteAsync("RELEASE SAVEPOINT attachment_collision", transaction: context.Session.Transaction);
        }
    }

    /// <summary>短随机文本只用于本事务的夹具身份。</summary>
    private static string Prefix() => "PGA" + Guid.NewGuid().ToString("N");

    /// <summary>为暂存随机夹具提供固定审计标记，不使用真实登录身份。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_attachments__";
        public bool IsAuthenticated => false;
    }
}
