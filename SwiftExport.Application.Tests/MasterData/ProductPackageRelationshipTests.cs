using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>包装物料身份迁移的定向验收；只在获批复制库的回滚事务中调整包装表及写入随机夹具。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductPackageRelationshipTests
{
    /// <summary>生成结构保留包装约束，两个物料引用均为可空 UUID，包装关系仍不生成物理外键。</summary>
    [TestMethod]
    public void Schema_ShouldDescribeBothMaterialRoles()
    {
        var generated = new PostgreSqlSchemaGenerator().Generate(typeof(ProductPackage).Assembly);
        var start = generated.IndexOf("CREATE TABLE \"public\".\"product_package\"", StringComparison.Ordinal);
        var end = generated.IndexOf("\nCREATE TABLE", start + 1, StringComparison.Ordinal);
        var table = generated[start..end];
        StringAssert.Contains(table, "\"owner_product_id\" uuid NULL");
        StringAssert.Contains(table, "\"packaging_material_id\" uuid NULL");
        StringAssert.Contains(table, "ck_product_package_pcs");
        Assert.IsFalse(table.Contains("\"product_id\"", StringComparison.Ordinal));
        Assert.IsFalse(table.Contains("packaging_material_sku", StringComparison.Ordinal));
        Assert.IsFalse(table.Contains("FOREIGN KEY", StringComparison.Ordinal));
        StringAssert.Contains(generated, "CREATE UNIQUE INDEX \"ux_product_package_owner_type\" ON \"public\".\"product_package\" (\"owner_product_id\", \"package_type\") WHERE \"is_deleted\" = FALSE AND \"owner_product_id\" IS NOT NULL;");
    }

    /// <summary>空表迁移后真实往返两个身份并验证三个查询；包含软删记录时拒绝迁移，最后恢复全部结构和夹具。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Migration_ShouldPreserveRolesAndRejectExistingRows(bool hasDeletedRow)
    {
        using var session = NewTestSession();
        var before = await SnapshotAsync(session);
        Assert.AreEqual(0L, await session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.product_package"),
            "专用库包装表有既存数据，不能清理或尝试空表迁移。");
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            // 使用正式脚本的同一正文，仅替换目标测试库并由测试拥有外层回滚事务。
            var source = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure", "Database", "PostgreSQL产品包装物料身份迁移.sql"));
            var sql = string.Join('\n', source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Where(line => line != "BEGIN;" && line != "COMMIT;"))
                .Replace("'dv_next'", "'dv_next_product_attribute_tests_20261012'", StringComparison.Ordinal);
            if (hasDeletedRow)
            {
                await session.Connection.ExecuteAsync("""
                    INSERT INTO public.product_package(uid,package_type,pcs_per_package,is_default,is_deleted)
                    VALUES(@Id,'Carton',1,FALSE,TRUE)
                    """, new { Id = Guid.NewGuid() }, transaction);
                var blocked = await Assert.ThrowsAsync<PostgresException>(() => session.Connection.ExecuteAsync(sql, transaction: transaction));
                Assert.AreEqual("P0001", blocked.SqlState);
                StringAssert.Contains(blocked.MessageText, "包装表已有数据");
            }
            else
            {
                await session.Connection.ExecuteAsync(sql, transaction: transaction);
                var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(new TestUser()), new TestUser());
                var products = new PostgreSqlDapperRepository<Product>(context);
                var owner = new Product { Sku = "PKG_OWNER_" + Guid.NewGuid().ToString("N"), SupplyType = "采购" };
                var material = new Product { Sku = "PKG_MATERIAL_" + Guid.NewGuid().ToString("N"), SupplyType = "采购" };
                Assert.IsTrue((await products.AddAsync(owner)).Success);
                Assert.IsTrue((await products.AddAsync(material)).Success);
                var packages = new PostgreSqlDapperRepository<ProductPackage>(context);
                var package = new ProductPackage { OwnerProductId = owner.Uid, PackagingMaterialId = material.Uid,
                    PcsPerPackage = 12, IsDefault = true, Nw_KG = 20m, Gw_KG = 22m, Cbm = 0.4m };
                Assert.IsTrue((await packages.AddAsync(package)).Success);
                var read = await packages.GetByIdAsync(package.Uid);
                Assert.IsNotNull(read);
                Assert.AreEqual(owner.Uid, read.OwnerProductId);
                Assert.AreEqual(material.Uid, read.PackagingMaterialId);
                var catalog = new PostgreSqlProductRepository(context);
                Assert.AreEqual(12m, (await catalog.GetDetailAsync(owner.Uid))!.PcsOfCarton);
                Assert.AreEqual(22m, (await catalog.GetPagedAsync(new() { Keyword = owner.Sku })).Items.Single().GwOfCarton);
                Assert.HasCount(0, await new PostgreSqlPackingListRepository(context).GetSourceEnrichmentsAsync([Guid.NewGuid()]));
                // 同一所属物料及层级仍唯一；包材引用不参与这个原有业务键。
                await session.Connection.ExecuteAsync("SAVEPOINT package_collision", transaction: transaction);
                var duplicate = await Assert.ThrowsAsync<PostgresException>(() => packages.AddAsync(new ProductPackage { OwnerProductId = owner.Uid }));
                Assert.AreEqual("23505", duplicate.SqlState);
                Assert.AreEqual("ux_product_package_owner_type", duplicate.ConstraintName);
                await session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT package_collision", transaction: transaction);
                read.OwnerProductId = null;
                read.PackagingMaterialId = null;
                Assert.IsTrue((await packages.UpdateAsync(read)).Success);
                var cleared = await packages.GetByIdAsync(package.Uid);
                Assert.IsNull(cleared!.OwnerProductId);
                Assert.IsNull(cleared.PackagingMaterialId);
            }
        }
        finally
        {
            try { transaction.Rollback(); }
            finally { session.Transaction = null; }
        }
        Assert.AreEqual(before, await SnapshotAsync(session), "迁移预演不得永久改变专用库结构或数据。");
    }

    /// <summary>只接受显式启用的既有专用库，凭据只从环境读取，并核对真实服务端身份。</summary>
    private static DbSession NewTestSession()
    {
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_ATTRIBUTE_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_ATTRIBUTE_TESTS") != "1" || string.IsNullOrWhiteSpace(value))
            Assert.Inconclusive("未显式启用包装关系迁移的专用复制库测试。");
        var settings = new NpgsqlConnectionStringBuilder(value);
        Assert.AreEqual("192.168.3.12", settings.Host);
        Assert.AreEqual(5432, settings.Port);
        Assert.AreEqual("dv_next_product_attribute_tests_20261012", settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        var session = new DbSession(new PostgreSqlConnectionFactory(value!));
        try
        {
            Assert.AreEqual("dv_next_product_attribute_tests_20261012", session.Connection.ExecuteScalar<string>("SELECT current_database()"));
            Assert.AreEqual("192.168.3.12", session.Connection.ExecuteScalar<string>("SELECT host(inet_server_addr())"));
            Assert.AreEqual("5432", session.Connection.ExecuteScalar<string>("SELECT current_setting('port')"));
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>比较包装表列、索引及行数的前后快照，确认本次事务没有留下结构或夹具。</summary>
    private static async Task<string> SnapshotAsync(DbSession session) => await session.Connection.ExecuteScalarAsync<string>("""
        SELECT jsonb_build_object(
            'columns',(SELECT jsonb_agg(jsonb_build_array(attname,format_type(atttypid,atttypmod)) ORDER BY attnum)
                FROM pg_attribute WHERE attrelid='public.product_package'::regclass AND attnum>0 AND NOT attisdropped),
            'indexes',(SELECT jsonb_agg(indexdef ORDER BY indexname) FROM pg_indexes WHERE schemaname='public' AND tablename='product_package'),
            'rows',(SELECT count(*) FROM public.product_package))::text
        """) ?? throw new InvalidOperationException("包装结构快照读取失败。");

    /// <summary>定位工作区正式迁移文件，不读取运行配置。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    /// <summary>回滚夹具使用独立审计身份。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_package_roles__";
        public bool IsAuthenticated => false;
    }
}
