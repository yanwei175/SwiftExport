using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.ForExcel;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅供主控串行执行的 PG 销售读取验收；随机夹具在单事务内回滚，不建表、不提交、不清理旧数据。</summary>
[TestClass]
[TestCategory("PostgreSqlForExcelSalesIntegration")]
[DoNotParallelize]
public sealed class PostgreSqlForExcelSalesMigrationIntegrationTests
{
    /// <summary>数据库仅去 ASCII 首尾空格，仍返回停用及多个规范化客户候选；软删不返回、NULL 名称原样保留。</summary>
    [TestMethod]
    public Task Customers_KeepInactiveAmbiguityAndAsciiTrimBoundary() => InRollback(async (context, repo) =>
    {
        var code = Prefix();
        var first = CustomerRow(code); first.CustomerName = null;
        var inactive = CustomerRow(" " + code.ToLowerInvariant() + " "); inactive.IsActive = false;
        var deleted = CustomerRow("  " + code);
        var unicode = CustomerRow("\t" + code + "\u3000");
        foreach (var row in new[] { first, inactive, deleted, unicode }) await Add(context, row);
        await Delete<Customer>(context, deleted.Uid);
        var rows = await repo.FindCustomersAsync("\t" + code.ToLowerInvariant() + "\u3000", default);
        Assert.HasCount(2, rows); Assert.IsNull(rows.Single(row => row.Uid == first.Uid).CustomerName);
        Assert.IsFalse(rows.Single(row => row.Uid == inactive.Uid).IsActive);
        Assert.AreEqual(inactive.CustomerCode, rows.Single(row => row.Uid == inactive.Uid).CustomerCode);
        CollectionAssert.AreEqual(new[] { first.Uid, inactive.Uid }.OrderBy(uid => uid.ToString("N"), StringComparer.Ordinal).ToArray(), rows.Select(row => row.Uid).ToArray());
        Assert.IsFalse(rows.Any(row => row.Uid == unicode.Uid || row.Uid == deleted.Uid));
    });

    /// <summary>客户型号 ASCII 修剪后的歧义不丢失，客户及软删除范围精确限制，原 SKU、单位、图片及 LinkSku 优先投影。</summary>
    [TestMethod]
    public Task ProductMatches_KeepAmbiguityScopeProjectionAndLinkFallback() => InRollback(async (context, repo) =>
    {
        var customer = CustomerRow(Prefix()); var other = CustomerRow(Prefix()); await Add(context, customer); await Add(context, other);
        var product = ProductRow(); product.SupplyType = "生产"; product.MaterialType = "未知"; product.Unit = " PCS ";
        var neutral = ProductNeutralReferenceTestData.Create(product, " " + product.Sku + " 产品原值 ");
        await Add(context, neutral); product.ProductNeutralId = neutral.Uid; product.MainImage = "图片"; await Add(context, product);
        var key = Prefix();
        var first = Relation(customer, key, product.Uid); first.LinkSku = " 客户型号 ";
        var second = Relation(customer, " " + key.ToLowerInvariant() + " ", product.Uid); second.LinkSku = "   ";
        var foreign = Relation(other, key, product.Uid);
        var deleted = Relation(customer, "  " + key, product.Uid);
        foreach (var row in new[] { first, second, foreign, deleted }) await Add(context, row);
        await Delete<CustomerProduct>(context, deleted.Uid);
        var input = new[] { key }; var rows = await repo.MatchProductsAsync(customer.Uid, input, default);
        Assert.HasCount(2, rows); CollectionAssert.AreEqual(new[] { key }, input);
        Assert.AreEqual("客户型号", rows.Single(row => row.CustomerProductId == first.Uid).LinkSku);
        Assert.AreEqual(neutral.ProductCode, rows.Single(row => row.CustomerProductId == second.Uid).LinkSku);
        Assert.IsTrue(rows.All(row => row.IsUsable && row.ProductId == product.Uid && row.Sku == product.Sku && row.Unit == " PCS " && row.MainImage == "图片"));
        Assert.HasCount(2, await repo.MatchProductsAsync(customer.Uid, [key.ToLowerInvariant() + " "], default), "源 SQL Server 等值忽略参数尾部 ASCII 空格，但数组原值不能改写。");
    });

    /// <summary>断链、缺失和软删产品不删除客户关系候选；产品/关系停用及空单位只影响 IsUsable，Unicode 单位沿用 ASCII 校验。</summary>
    [TestMethod]
    public Task ProductMatches_KeepBrokenDisabledAndUnitBoundaries() => InRollback(async (context, repo) =>
    {
        var customer = CustomerRow(Prefix()); await Add(context, customer);
        var keys = Enumerable.Range(0, 8).Select(_ => Prefix()).ToArray();
        var active = ProductRow(); active.ProductNeutralId = null; active.MainImage = null; await Add(context, active);
        var inactive = ProductRow(); inactive.IsActive = false; await Add(context, inactive);
        var deleted = ProductRow(); await Add(context, deleted); await Delete<Product>(context, deleted.Uid);
        var emptyUnit = ProductRow(); emptyUnit.Unit = "   "; await Add(context, emptyUnit);
        var nullUnit = ProductRow(); nullUnit.Unit = null; await Add(context, nullUnit);
        var unicodeUnit = ProductRow(); unicodeUnit.Unit = "\t\u3000"; await Add(context, unicodeUnit);
        var relations = new[]
        {
            Relation(customer, keys[0], null), Relation(customer, keys[1], Guid.NewGuid()),
            Relation(customer, keys[2], deleted.Uid), Relation(customer, keys[3], inactive.Uid),
            Relation(customer, keys[4], emptyUnit.Uid), Relation(customer, keys[5], nullUnit.Uid),
            Relation(customer, keys[6], unicodeUnit.Uid), Relation(customer, keys[7], active.Uid)
        };
        relations[0].LinkSku = " 断链显示 "; relations[7].IsActive = false;
        foreach (var row in relations) await Add(context, row);
        var rows = await repo.MatchProductsAsync(customer.Uid, keys, default); Assert.HasCount(8, rows);
        foreach (var relation in relations.Take(3))
        {
            var row = rows.Single(item => item.CustomerProductId == relation.Uid);
            Assert.IsNull(row.ProductId); Assert.IsNull(row.Sku); Assert.IsNull(row.Unit); Assert.IsNull(row.MainImage); Assert.IsFalse(row.IsUsable);
        }
        Assert.AreEqual("断链显示", rows.Single(row => row.CustomerProductId == relations[0].Uid).LinkSku);
        Assert.IsNull(rows.Single(row => row.CustomerProductId == relations[1].Uid).LinkSku);
        Assert.IsTrue(rows.Single(row => row.CustomerProductId == relations[6].Uid).IsUsable);
        Assert.AreEqual("\t\u3000", rows.Single(row => row.CustomerProductId == relations[6].Uid).Unit);
        Assert.IsTrue(rows.Where(row => row.CustomerProductId != relations[6].Uid).All(row => !row.IsUsable));
        Assert.IsNull(rows.Single(row => row.CustomerProductId == relations[7].Uid).LinkSku);
    });

    /// <summary>客户 LinkSku 只有 ASCII 空白时回退；NULL 产品原值保持 NULL，Unicode 空白客户原值不被全 Trim。</summary>
    [TestMethod]
    public Task ProductMatches_LinkSkuKeepsUnicodeAndNullableFallback() => InRollback(async (context, repo) =>
    {
        var customer = CustomerRow(Prefix()); await Add(context, customer);
        var product = ProductRow(); product.ProductNeutralId = null; await Add(context, product);
        var first = Relation(customer, Prefix(), product.Uid); first.LinkSku = "\t\u3000";
        var second = Relation(customer, Prefix(), product.Uid); second.LinkSku = "   ";
        await Add(context, first); await Add(context, second);
        var rows = await repo.MatchProductsAsync(customer.Uid, [first.CustomerSku, second.CustomerSku], default);
        Assert.AreEqual("\t\u3000", rows.Single(row => row.CustomerProductId == first.Uid).LinkSku);
        Assert.IsNull(rows.Single(row => row.CustomerProductId == second.Uid).LinkSku);
    });

    /// <summary>同客户任一非空编号命中均返回；相同商业编号允许多订单，状态不限制，另一客户及软删除不混入。</summary>
    [TestMethod]
    public Task ExistingOrders_KeepOrDuplicatesStatusAndNullValues() => InRollback(async (context, repo) =>
    {
        var customer = CustomerRow(Prefix()); var other = CustomerRow(Prefix()); await Add(context, customer); await Add(context, other);
        var invoice = Prefix(); var po = Prefix();
        var byInvoice = Order(customer.Uid, " " + invoice.ToLowerInvariant() + " ", null);
        byInvoice.DocumentStatus = (SalesOrderDocumentStatusEnum)8; byInvoice.ConfirmationStatus = (ConfirmationStatusEnum)1;
        var duplicate = Order(customer.Uid, invoice, null);
        var byPo = Order(customer.Uid, null, " " + po.ToLowerInvariant() + " "); byPo.DocumentStatus = (SalesOrderDocumentStatusEnum)9;
        var foreign = Order(other.Uid, invoice, po); var deleted = Order(customer.Uid, invoice, po);
        var empty = Order(customer.Uid, "", ""); var nulls = Order(customer.Uid, null, null);
        foreach (var row in new[] { byInvoice, duplicate, byPo, foreign, deleted, empty, nulls }) await Add(context, row);
        await Delete<SalesOrderHeader>(context, deleted.Uid);
        var rows = await repo.FindExistingOrdersAsync(customer.Uid, "\t" + invoice + "\u3000", " " + po + " ", default);
        Assert.HasCount(3, rows);
        Assert.AreEqual(8, rows.Single(row => row.Uid == byInvoice.Uid).DocumentStatus); Assert.AreEqual(1, rows.Single(row => row.Uid == byInvoice.Uid).ConfirmationStatus);
        Assert.IsNull(rows.Single(row => row.Uid == byInvoice.Uid).CustomerPoNo); Assert.IsNull(rows.Single(row => row.Uid == byPo.Uid).InvoiceNo);
        CollectionAssert.AreEqual(new[] { byInvoice, duplicate, byPo }.OrderBy(row => row.OrderNo, StringComparer.Ordinal).Select(row => row.Uid).ToArray(), rows.Select(row => row.Uid).ToArray());
        Assert.HasCount(2, await repo.FindExistingOrdersAsync(customer.Uid, invoice, null!, default));
        Assert.AreEqual(byPo.Uid, (await repo.FindExistingOrdersAsync(customer.Uid, null!, po, default)).Single().Uid);
        Assert.HasCount(0, await repo.FindExistingOrdersAsync(customer.Uid, "", "\t\u3000", new CancellationToken(true)));
    });

    /// <summary>三个入口对特殊符号精确等值匹配，无 LIKE 通配扩张，也不删除内部空格或前导零。</summary>
    [TestMethod]
    [DataRow("%")] [DataRow("_")] [DataRow("[")] [DataRow("~")] [DataRow("00 A")]
    public Task ExactCodes_KeepSymbolsAndInternalSpaces(string suffix) => InRollback(async (context, repo) =>
    {
        var code = Prefix() + suffix; var customer = CustomerRow(code); var ordinary = CustomerRow(code + "X");
        await Add(context, customer); await Add(context, ordinary);
        var product = ProductRow(); await Add(context, product);
        var relation = Relation(customer, code, product.Uid); var otherRelation = Relation(customer, code + "X", product.Uid);
        await Add(context, relation); await Add(context, otherRelation);
        var order = Order(customer.Uid, code, null); var otherOrder = Order(customer.Uid, code + "X", null);
        await Add(context, order); await Add(context, otherOrder);
        Assert.AreEqual(customer.Uid, (await repo.FindCustomersAsync(code.ToLowerInvariant(), default)).Single().Uid);
        Assert.AreEqual(relation.Uid, (await repo.MatchProductsAsync(customer.Uid, [code.ToUpperInvariant()], default)).Single().CustomerProductId);
        Assert.AreEqual(order.Uid, (await repo.FindExistingOrdersAsync(customer.Uid, code.ToLowerInvariant(), null!, default)).Single().Uid);
    });

    /// <summary>数据库客户型号及商业编号的 Unicode 边界空白不会被 ASCII 修剪消除；数组也不做第二次 .NET 规范化。</summary>
    [TestMethod]
    public Task DatabaseIdentifiers_KeepUnicodeBoundaryWhitespace() => InRollback(async (context, repo) =>
    {
        var customer = CustomerRow(Prefix()); await Add(context, customer);
        var product = ProductRow(); await Add(context, product);
        var key = Prefix(); var raw = "\t" + key + "\u3000";
        var relation = Relation(customer, raw, product.Uid); await Add(context, relation);
        var order = Order(customer.Uid, raw, null); await Add(context, order);
        Assert.HasCount(0, await repo.MatchProductsAsync(customer.Uid, [key], default));
        Assert.AreEqual(relation.Uid, (await repo.MatchProductsAsync(customer.Uid, [raw], default)).Single().CustomerProductId);
        Assert.HasCount(0, await repo.FindExistingOrdersAsync(customer.Uid, raw, null!, default), "输入全 Trim、存储仅 ASCII Trim，不能悄悄扩展数据库修剪。");
    });

    /// <summary>SQL 等值补尾部 ASCII 空格，不改输入、前导/内部空格、Tab 或 NBSP；全角空格源排序差异单独登记，不放宽断言。</summary>
    [TestMethod]
    public Task ProductArray_OnlyTrailingAsciiPaddingIsIgnored() => InRollback(async (context, repo) =>
    {
        var customer = CustomerRow(Prefix()); await Add(context, customer);
        var product = ProductRow(); await Add(context, product);
        var key = Prefix() + "00 A"; var relation = Relation(customer, key, product.Uid); await Add(context, relation);
        var input = new[] { null!, key.ToLowerInvariant() + "   ", key + " " }; var original = input.ToArray();
        Assert.AreEqual(relation.Uid, (await repo.MatchProductsAsync(customer.Uid, input, default)).Single().CustomerProductId);
        CollectionAssert.AreEqual(original, input);
        foreach (var unmatched in new[] { " " + key, key.Replace("00 A", "00A"), key + "\t", key + "\u00A0" })
            Assert.HasCount(0, await repo.MatchProductsAsync(customer.Uid, [unmatched], default));
        Assert.HasCount(0, await repo.MatchProductsAsync(customer.Uid, [null!], default));
    });

    /// <summary>真正没有事务的三个查询也可执行；空数组/双空编号保留预取消短路，非空预取消不返回空成功。</summary>
    [TestMethod]
    public async Task OptionalTransactionAndCancellation_AreSupported()
    {
        using var session = Session(); var context = Context(session); var repo = new PostgreSqlForExcelSalesOrderRepository(context);
        var key = Prefix(); var customer = Guid.NewGuid(); Assert.IsNull(session.Transaction);
        Assert.HasCount(0, await repo.FindCustomersAsync(key, default));
        Assert.HasCount(0, await repo.MatchProductsAsync(customer, [key], default));
        Assert.HasCount(0, await repo.FindExistingOrdersAsync(customer, key, key, default));
        var token = new CancellationToken(true);
        Assert.HasCount(0, await repo.MatchProductsAsync(customer, [], token));
        Assert.HasCount(0, await repo.FindExistingOrdersAsync(customer, null!, "\t\u3000", token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.FindCustomersAsync(key, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.MatchProductsAsync(customer, [key], token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.FindExistingOrdersAsync(customer, key, "", token));
        Assert.IsNull(session.Transaction);
    }

    /// <summary>严格使用现有固定故障测试库校验，不提供默认连接或配置环境变量。</summary>
    private static DbSession Session() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>测试审计与仓储共享同一真实会话，不注入生产用户身份。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    { var user = new SalesUser(); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }

    /// <summary>单事务只新增本例随机实体，始终回滚；读取透传其 ReadCommitted 隔离而不强制候选服务的 Serializable。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, PostgreSqlForExcelSalesOrderRepository, Task> action)
    {
        using var session = Session(); using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
        var context = Context(session);
        try { await action(context, new(context)); Assert.AreSame(transaction, session.Transaction); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>通过现有实体元数据新增随机测试行，不手动修改公共映射或索引。</summary>
    private static async Task Add<T>(RepositoryBaseContext context, T row) where T : BaseEntity
        => Assert.IsTrue((await new PostgreSqlDapperRepository<T>(context).AddAsync(row)).Success);

    /// <summary>只软删除本例已登记身份，finally 回滚恢复。</summary>
    private static async Task Delete<T>(RepositoryBaseContext context, Guid uid) where T : BaseEntity
        => Assert.IsTrue((await new PostgreSqlDapperRepository<T>(context).DeleteAsync(uid, 1)).Success);

    /// <summary>随机 ASCII 大写编码避开现有唯一业务键。</summary>
    private static string Prefix() => "PGES" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    /// <summary>仅创建本例客户，默认启用便于单独控制停用场景。</summary>
    private static Customer CustomerRow(string code) => new() { CustomerCode = code, CustomerName = "销售导入客户", DocRelateNo = Prefix() };

    /// <summary>不依赖采购资格的独立产品，相关字段可由各场景设为空或停用。</summary>
    private static Product ProductRow() => new() { Sku = Prefix(), SupplyType = "生产", MaterialType = "未知", Unit = "PCS" };

    /// <summary>关系允许缺失产品身份，保存当前客户范围及原始客户型号。</summary>
    private static CustomerProduct Relation(Customer customer, string sku, Guid? product) => new()
    { CustomerId = customer.Uid, CustomerCode = customer.CustomerCode, CustomerSku = sku, SkuId = product };

    /// <summary>商业编号不唯一，只用随机系统订单号满足现有索引。</summary>
    private static SalesOrderHeader Order(Guid customer, string? invoice, string? po) => new()
    { OrderNo = Prefix(), CustomerId = customer, InvoiceNo = invoice, CustomerPoNo = po };

    /// <summary>随机夹具的固定测试审计名，不具备业务登录身份。</summary>
    private sealed class SalesUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_excel_sales__";
        public bool IsAuthenticated => false;
    }
}
