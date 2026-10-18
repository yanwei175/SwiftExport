using System.Data;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Infrastructure.Repositories.Customers.PoConfigs;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用冻结的真实 Dapper 无网络夹具验证 PO 专用查询及继承 CRUD；本文件只增加自己的用例。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>完整 DTO 保留身份及 nullable 商务字段，普通预览仅查询且传递外层事务。</summary>
    [TestMethod]
    public async Task PoConfig_PreviewMapsCompleteDtoWithoutLock()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        f.Connection.Rows.Columns.Add("CustomerId", typeof(Guid));
        f.Connection.Rows.Columns.Add("SupplierId", typeof(Guid));
        f.Connection.Rows.Columns.Add("Currency", typeof(string));
        f.Connection.Rows.Columns.Add("DefaultDiscountRate", typeof(decimal));
        f.Connection.Rows.Columns.Add("DiscountRemark", typeof(string));
        var customer = Guid.NewGuid(); var supplier = Guid.NewGuid(); var uid = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(uid, customer, supplier, DBNull.Value, 1.05m, "折扣说明");
        var dto = await new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f)).GetByPartiesAsync(customer, supplier);
        Assert.IsNotNull(dto); Assert.AreEqual(uid, dto.Uid); Assert.AreEqual(customer, dto.CustomerId);
        Assert.IsNull(dto.Currency); Assert.AreEqual(1.05m, dto.DefaultDiscountRate); Assert.AreEqual("折扣说明", dto.DiscountRemark);
        Assert.HasCount(1, f.Connection.Commands);
        StringAssert.Contains(f.Connection.Text, "cpc.\"im_type\" AS \"IMType\"");
        StringAssert.Contains(f.Connection.Text, "cpc.is_deleted=FALSE");
        StringAssert.Contains(f.Connection.Text, "LEFT JOIN public.customer customer ON customer.uid=cpc.customer_id");
        StringAssert.Contains(f.Connection.Text, "COALESCE(customer.customer_code,cpc.\"customer_code\") AS \"CustomerCode\"");
        StringAssert.Contains(f.Connection.Text, "COALESCE(supplier.supplier_code,cpc.\"supplier_code\") AS \"SupplierCode\"");
        Assert.IsFalse(f.Connection.Text.Contains("LOCK", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>空结果也先锁单表再查询，两个命令共用外层事务而仓储不提交或释放。</summary>
    [TestMethod]
    public async Task PoConfig_LockedMissingReadAcquiresTableLockBeforeQuery()
    {
        using var f = new Fixture();
        Assert.IsNull(await new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f)).GetByPartiesAsync(Guid.NewGuid(), Guid.NewGuid(), withUpdLock: true));
        Assert.HasCount(2, f.Connection.Commands);
        Assert.AreEqual("LOCK TABLE public.customer_po_config IN SHARE ROW EXCLUSIVE MODE", f.Connection.Commands[0].Sql);
        Assert.IsTrue(f.Connection.Commands.All(c => ReferenceEquals(c.Transaction, f.Session.Transaction)));
        Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks); Assert.AreEqual(0, f.Connection.TransactionDisposals);
    }

    /// <summary>无事务、高隔离及预取消读取拒绝且零命令，不隐式降隔离或创建事务。</summary>
    [TestMethod]
    public async Task PoConfig_LockedReadRejectsInvalidTransactionAndCancellation()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f));
        var original = f.Session.Transaction;
        f.Session.Transaction = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetByPartiesAsync(Guid.Empty, Guid.Empty, withUpdLock: true));
        foreach (var isolation in new[] { IsolationLevel.Serializable, IsolationLevel.RepeatableRead, IsolationLevel.Snapshot })
        {
            f.Session.Transaction = new PoConfigTransaction(f.Connection, isolation);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetByPartiesAsync(Guid.Empty, Guid.Empty, withUpdLock: true));
        }
        f.Session.Transaction = original;
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByPartiesAsync(Guid.Empty, Guid.Empty, new CancellationToken(true), true));
        Assert.HasCount(0, f.Connection.Commands);
        f.Session.Transaction = new PoConfigTransaction(f.Connection, IsolationLevel.ReadUncommitted);
        await repository.GetByPartiesAsync(Guid.Empty, Guid.Empty, withUpdLock: true);
        Assert.HasCount(2, f.Connection.Commands);
        f.Session.Transaction = original;
    }

    /// <summary>身份解析一次查询并整体回填当前编码，显式空值类型和身份优先条件不受旧编码影响。</summary>
    [TestMethod]
    public async Task PoConfig_RelationResolutionUsesUidAndBackfillsCodes()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("CustomerId", typeof(Guid)); f.Connection.Rows.Columns.Add("CustomerCode", typeof(string));
        f.Connection.Rows.Columns.Add("SupplierId", typeof(Guid)); f.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        var customer = Guid.NewGuid(); var supplier = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(customer, "当前客户", supplier, "当前供应商");
        var entity = new CustomerPoConfig { CustomerId = customer, CustomerCode = "旧客户", SupplierCode = "旧供应商" };
        Assert.IsTrue(await new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f)).PopulateRelationIdsAsync(entity));
        Assert.AreEqual(customer, entity.CustomerId); Assert.AreEqual(supplier, entity.SupplierId);
        Assert.AreEqual("当前客户", entity.CustomerCode); Assert.AreEqual("当前供应商", entity.SupplierCode);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["SupplierId"]);
        StringAssert.Contains(f.Connection.Text, "CAST(@CustomerId AS uuid) IS NOT NULL AND uid=CAST(@CustomerId AS uuid)");
        StringAssert.Contains(f.Connection.Text, "CAST(@SupplierId AS uuid) IS NULL");
        StringAssert.Contains(f.Connection.Text, "WHERE matches=1");
        Assert.IsFalse(f.Connection.Text.Contains("MAX(", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>一方解析失败时不回填另一方，Guid.Empty 仍绑定为身份而非缺失编码模式。</summary>
    [TestMethod]
    public async Task PoConfig_RelationFailureDoesNotPartiallyMutateEntity()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("CustomerId", typeof(Guid)); f.Connection.Rows.Columns.Add("CustomerCode", typeof(string));
        f.Connection.Rows.Columns.Add("SupplierId", typeof(Guid)); f.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        f.Connection.Rows.Rows.Add(Guid.NewGuid(), "当前客户", DBNull.Value, DBNull.Value);
        var entity = new CustomerPoConfig { CustomerId = Guid.Empty, CustomerCode = "旧客户", SupplierCode = "缺失" };
        Assert.IsFalse(await new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f)).PopulateRelationIdsAsync(entity));
        Assert.AreEqual(Guid.Empty, entity.CustomerId); Assert.IsNull(entity.SupplierId);
        Assert.AreEqual("旧客户", entity.CustomerCode); Assert.AreEqual("缺失", entity.SupplierCode);
        Assert.AreEqual(Guid.Empty, f.Connection.Values["CustomerId"]);
    }

    /// <summary>数据库候选交叉组合不会误报；存在性 Trim/忽略大小写但返回原编码，旧非唯一检查大小写敏感且包含软删。</summary>
    [TestMethod]
    public async Task PoConfig_PairChecksKeepDifferentTrimCaseAndDeletedSemantics()
    {
        using var f = new Fixture(); PairRows(f);
        f.Connection.Rows.Rows.Add(Guid.NewGuid(), " Ab ", "S1 ");
        f.Connection.Rows.Rows.Add(Guid.NewGuid(), "CD", "S2");
        f.Connection.Rows.Rows.Add(Guid.NewGuid(), "Ab", "S2");
        var repository = new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f));
        var requested = new[] { Check("ab", "s1"), Check("CD", "S2") };
        CollectionAssert.AreEquivalent(new[] { " Ab  + S1 ", "CD + S2" }, (await repository.GetExistingCustomerCodesAsync(requested)).ToArray());
        StringAssert.Contains(f.Connection.Text, "cpc.is_deleted=FALSE");
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, f.Connection.Types["CustomerCodes"]);
        Assert.HasCount(1, await repository.GetNonUniqueByPoConfigCheckList(requested));
        Assert.IsFalse(f.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Text, "rtrim(cpc.customer_code,' ') COLLATE public.erp_dictionary_ci");
        Assert.HasCount(1, await repository.GetNonUniqueByPoConfigCheckList([Check(" Ab ", "S1 ")]));
        CollectionAssert.AreEqual(new[] { " Ab " }, (string[])f.Connection.Values["CustomerCodes"]!);
    }

    /// <summary>占用逐项排自身，数据库尾空格保留不 Trim；同批另一身份仍会占用该组合。</summary>
    [TestMethod]
    public async Task PoConfig_UsedPairsExcludeOnlySelfAndKeepDatabaseWhitespace()
    {
        using var f = new Fixture(); PairRows(f);
        var self = Guid.NewGuid(); var other = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(self, "Ab", "S");
        var repository = new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f));
        Assert.HasCount(0, await repository.GetUsedByCustomerCodeAndSupplierCodeAsync([Check(" ab ", " s ", self)]));
        f.Connection.Rows.Rows.Add(other, "AB", "s");
        CollectionAssert.AreEqual(new[] { "ab + s" }, (await repository.GetUsedByCustomerCodeAndSupplierCodeAsync([Check(" ab ", " s ", self), Check("AB", "S", other)])).ToArray());
        f.Connection.Rows.Rows.Clear(); f.Connection.Rows.Rows.Add(other, "Ab ", "S");
        Assert.HasCount(0, await repository.GetUsedByCustomerCodeAndSupplierCodeAsync([Check("Ab", "S")]));
        Assert.IsFalse(f.Connection.Text.Contains("NOT (", StringComparison.Ordinal));
    }

    /// <summary>空输入不查询，保留旧检查 null 防御与其余 LINQ null 异常边界。</summary>
    [TestMethod]
    public async Task PoConfig_EmptyAndNullInputsKeepBoundaries()
    {
        using var f = new Fixture(); var repository = new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f));
        Assert.HasCount(0, await repository.GetExistingCustomerCodesAsync([]));
        Assert.HasCount(0, await repository.GetNonUniqueByPoConfigCheckList(null!));
        Assert.HasCount(0, await repository.GetNonUniqueByPoConfigCheckList([]));
        Assert.HasCount(0, await repository.GetUsedByCustomerCodeAndSupplierCodeAsync([Check(" ", "S")]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.GetExistingCustomerCodesAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.GetUsedByCustomerCodeAndSupplierCodeAsync(null!));
        Assert.HasCount(0, f.Connection.Commands);
    }

    /// <summary>分页默认、上限和关键词使用原规则，日期 NULL 排后且所有查询传外层事务。</summary>
    [TestMethod]
    [DataRow(0, 0, 1, 50)]
    [DataRow(2, 999, 2, 500)]
    public async Task PoConfig_PageKeepsDefaultsPatternAndStableOrder(int index, int size, int expectedIndex, int expectedSize)
    {
        using var f = new Fixture(); f.Connection.Rows.Columns.Add("Note", typeof(string)); f.Connection.Rows.Rows.Add("备注");
        var keyword = " A?%_[~ ";
        var page = await new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f)).GetPagedAsync(new CustomerPoConfigSearchRequest { PageIndex = index, PageSize = size, Keyword = keyword });
        Assert.AreEqual(expectedIndex, page.PageIndex); Assert.AreEqual(expectedSize, page.PageSize); Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual("备注", page.Items.Single().Note);
        Assert.AreEqual(SqlLikePattern.Build(keyword), f.Connection.Values["Keyword"]);
        Assert.AreEqual((expectedIndex - 1) * expectedSize, f.Connection.Values["Offset"]);
        StringAssert.Contains(f.Connection.Text, "COALESCE(customer.customer_code,cpc.customer_code) DESC NULLS LAST,cpc.uid DESC");
        Assert.IsTrue(f.Connection.Commands.All(c => ReferenceEquals(c.Transaction, f.Session.Transaction)));
        Assert.HasCount(2, f.Connection.Commands);
    }

    /// <summary>双方 ID 精确条件与当前编码关键词为 AND，总数和分页联接及筛选相同，空值不筛选。</summary>
    [TestMethod]
    [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task PoConfig_PageFiltersExactIdentitiesAndCurrentCodes(bool customerFilter, bool supplierFilter)
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("CustomerCode", typeof(string)); f.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        f.Connection.Rows.Rows.Add("CURRENT-C", "CURRENT-S");
        var customer = Guid.NewGuid(); var supplier = Guid.NewGuid();
        var page = await new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f)).GetPagedAsync(new()
        { CustomerId = customerFilter ? customer : null, SupplierId = supplierFilter ? supplier : null, Keyword = "C%", PageIndex = 2, PageSize = 10 });
        Assert.AreEqual("CURRENT-C", page.Items.Single().CustomerCode); Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual(customerFilter, f.Connection.Values.ContainsKey("CustomerId"));
        Assert.AreEqual(supplierFilter, f.Connection.Values.ContainsKey("SupplierId"));
        if (customerFilter) Assert.AreEqual(customer, f.Connection.Values["CustomerId"]);
        if (supplierFilter) Assert.AreEqual(supplier, f.Connection.Values["SupplierId"]);
        var countWhere = f.Connection.Commands[0].Sql.Split("WHERE COALESCE(cpc.is_deleted,FALSE)=FALSE", StringSplitOptions.None)[1].Trim();
        var pageWhere = f.Connection.Commands[1].Sql.Split("WHERE COALESCE(cpc.is_deleted,FALSE)=FALSE", StringSplitOptions.None)[1].Split("ORDER BY", StringSplitOptions.None)[0].Trim();
        Assert.AreEqual(countWhere, pageWhere);
        Assert.AreEqual(customerFilter, countWhere.Contains("AND cpc.customer_id=@CustomerId", StringComparison.Ordinal));
        Assert.AreEqual(supplierFilter, countWhere.Contains("AND cpc.supplier_id=@SupplierId", StringComparison.Ordinal));
        StringAssert.Contains(countWhere, "AND (COALESCE(customer.customer_code,cpc.customer_code)");
        Assert.IsTrue(f.Connection.Commands.All(command => command.Sql.Contains("LEFT JOIN public.supplier", StringComparison.Ordinal)));
    }

    /// <summary>继承新增/更新审计及 nullable 关系映射保持原版本契约，不接管事务。</summary>
    [TestMethod]
    public async Task PoConfig_InheritedCrudMapsNullableFieldsAndVersion()
    {
        using var f = new Fixture(); var repository = new PostgreSqlCustomerPoConfigRepository(DictionaryContext(f));
        var entity = new CustomerPoConfig { CustomerCode = "C", SupplierCode = "S", DefaultDiscountRate = 1.05m };
        Assert.IsTrue((await repository.AddAsync(entity)).Success); Assert.AreNotEqual(Guid.Empty, entity.Uid);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["CustomerId"]); Assert.AreEqual(DBNull.Value, f.Connection.Values["Currency"]);
        Assert.AreEqual(1.05m, f.Connection.Values["DefaultDiscountRate"]); Assert.IsNotNull(entity.CreatedDate);
        f.Connection.Rows.Columns.Add("Version", typeof(int)); f.Connection.Rows.Rows.Add(2);
        Assert.AreEqual(2, (await repository.UpdateAsync(entity)).NewVersion);
        Assert.AreEqual(1, f.Connection.Values["OriginalVersion"]); Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        f.Connection.Rows.Rows.Clear();
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(entity)).ErrorCode);
        Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>建立三字段候选表，真实 Dapper 按每个查询投影映射相同受控候选。</summary>
    private static void PairRows(Fixture f)
    { f.Connection.Rows.Columns.Add("Uid", typeof(Guid)); f.Connection.Rows.Columns.Add("CustomerCode", typeof(string)); f.Connection.Rows.Columns.Add("SupplierCode", typeof(string)); }

    /// <summary>构造独立组合请求，不规范化输入。</summary>
    private static PoConfigCheck Check(string? customer, string? supplier, Guid uid = default) => new() { Uid = uid, CustomerCode = customer, SupplierCode = supplier };

    /// <summary>仅替换隔离级别以验证拒绝路径，不执行任何提交或网络操作。</summary>
    private sealed class PoConfigTransaction(DbConnection connection, IsolationLevel isolation) : DbTransaction
    {
        /// <summary>返回用例指定隔离级别，验证仓储读取准入。</summary>
        public override IsolationLevel IsolationLevel => isolation;
        /// <summary>关联受控无网络连接，供 Dapper 绑定事务。</summary>
        protected override DbConnection DbConnection => connection;
        /// <summary>禁止被测仓储提交调用方事务。</summary>
        public override void Commit() => throw new NotSupportedException();
        /// <summary>禁止被测仓储回滚调用方事务。</summary>
        public override void Rollback() => throw new NotSupportedException();
    }
}
