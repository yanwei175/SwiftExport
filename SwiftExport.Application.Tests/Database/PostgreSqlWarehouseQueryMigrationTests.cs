using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用真实 Dapper 无网络夹具验证仓库查询投影、参数、分页和调用方事务。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>供应商筛选只投影三个字段，保留停用/排软删口径，计数与页不依赖采购库存并安全绑定大偏移。</summary>
    [TestMethod]
    public async Task WarehouseQuery_SupplierFilterUsesNarrowFieldsAndBigintOffset()
    {
        using var fixture=new Fixture();var rows=fixture.Connection.Rows;
        rows.Columns.Add("Uid",typeof(Guid));rows.Columns.Add("SupplierCode",typeof(string));rows.Columns.Add("SupplierName",typeof(string));
        var uid=Guid.NewGuid();rows.Rows.Add(uid,"S-01","供应商");
        var repository=new PostgreSqlWarehouseQueryRepository(DictionaryContext(fixture));
        using var cancellation=new CancellationTokenSource();
        var result=await repository.SearchSupplierFilterOptionsAsync(new(){PageIndex=int.MaxValue,PageSize=200,Keyword="  ~%_[  "},cancellation.Token);
        Assert.AreEqual(uid,result.Items.Single().Uid);Assert.AreEqual("S-01",result.Items.Single().SupplierCode);
        Assert.AreEqual((long)(int.MaxValue-1)*200,fixture.Connection.Values["Offset"]);
        Assert.AreEqual(NpgsqlDbType.Bigint,fixture.Connection.Types["Offset"]);
        Assert.AreEqual("%~~~%~_~[%",fixture.Connection.Values["Keyword"]);
        Assert.AreEqual(2,fixture.Connection.Commands.Count);
        foreach(var command in fixture.Connection.Commands)
        {
            StringAssert.Contains(command.Sql,"FROM public.supplier s");
            StringAssert.Contains(command.Sql,"COALESCE(s.is_deleted,FALSE)=FALSE");
            foreach(var forbidden in new[]{"is_active","purchase_order","inventory_balance","contact_","currency"})
                Assert.IsFalse(command.Sql.Contains(forbidden,StringComparison.OrdinalIgnoreCase),forbidden);
            Assert.AreSame(fixture.Session.Transaction,command.Transaction);
        }
        StringAssert.Contains(fixture.Connection.Text,"ORDER BY s.supplier_code COLLATE public.erp_dictionary_ci,s.uid");
        Assert.AreEqual(0,fixture.Connection.Commits);Assert.AreEqual(0,fixture.Connection.Rollbacks);
    }

    /// <summary>空身份集合不查询；非空身份作为单个 uuid 数组传递，映射停用仓而不附加活动过滤。</summary>
    [TestMethod]
    public async Task WarehouseQuery_IdsPreserveInactiveAndUseArray()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlWarehouseQueryRepository(DictionaryContext(fixture));
        Assert.HasCount(0, await repository.GetByIdsAsync([]));
        Assert.HasCount(0, fixture.Connection.Commands);
        var uid = Guid.NewGuid();
        var rows = fixture.Connection.Rows;
        rows.Columns.Add("Uid", typeof(Guid));
        rows.Columns.Add("IsActive", typeof(bool));
        rows.Columns.Add("IsDeleted", typeof(bool));
        rows.Columns.Add("Code", typeof(string));
        rows.Columns.Add("Name", typeof(string));
        rows.Rows.Add(uid, false, false, "W01", "停用仓");
        var result = await repository.GetByIdsAsync([uid]);
        Assert.AreEqual(uid, result.Single().Uid);
        Assert.IsFalse(result.Single().IsActive);
        CollectionAssert.AreEqual(new[] { uid }, (Guid[])fixture.Connection.Values["Uids"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Uuid, fixture.Connection.Types["Uids"]);
        StringAssert.Contains(fixture.Connection.Text, "uid=ANY(@Uids)");
        Assert.IsFalse(fixture.Connection.Text.Contains("is_active=TRUE", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>各候选与管理页保留各自上限、默认页和稳定排序，计数及取页均使用相同事务。</summary>
    [TestMethod]
    [DataRow(0, 0, 1, 20)]
    [DataRow(2, 999, 2, 200)]
    public async Task WarehouseQuery_PagesPreserveLimitsAndNullParameters(int index, int size, int expectedIndex, int expectedSize)
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlWarehouseQueryRepository(DictionaryContext(fixture));
        var request = new PagedRequestBase { PageIndex = index, PageSize = size, Keyword = "  " };
        var products = await repository.GetProductDefaultCandidatesAsync(request);
        var picker = await repository.GetPickerPagedAsync(request);
        var suppliers = await repository.SearchSupplierLookupAsync(request);
        var management = await repository.SearchAsync(new WarehouseSiteSearchRequest { PageIndex = index, PageSize = size });
        Assert.AreEqual(expectedIndex, products.PageIndex);
        Assert.AreEqual(expectedSize, products.PageSize);
        Assert.AreEqual(expectedSize, picker.PageSize);
        Assert.AreEqual(Math.Min(expectedSize, 100), suppliers.PageSize);
        Assert.AreEqual(expectedSize, management.PageSize);
        Assert.HasCount(8, fixture.Connection.Commands);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreEqual(DBNull.Value, command.Values["Keyword"]);
            StringAssert.Contains(command.Sql, "CAST(@Keyword AS text)");
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
        }
        Assert.AreEqual((expectedIndex - 1) * expectedSize, fixture.Connection.Values["Offset"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["SupplierId"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["IsActive"]);
        StringAssert.Contains(fixture.Connection.Text, "CAST(@SupplierId AS uuid)");
        StringAssert.Contains(fixture.Connection.Text, "CAST(@IsActive AS boolean)");
        StringAssert.Contains(fixture.Connection.Text, "w.is_active DESC,w.code COLLATE public.erp_dictionary_ci,w.uid");
        Assert.AreEqual(0, fixture.Connection.Commits);
    }

    /// <summary>关键词仅按原仓库字面规则转义，空格、问号及反斜杠不变，计数与取页复用克隆参数。</summary>
    [TestMethod]
    public async Task WarehouseQuery_SearchKeepsLiteralKeywordAndSnapshotDifference()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlWarehouseQueryRepository(DictionaryContext(fixture));
        const string keyword = "  A B?~%_[\\  ";
        await repository.GetPickerPagedAsync(new PagedRequestBase { Keyword = keyword });
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreEqual("%A B?~~~%~_~[\\%", command.Values["Keyword"]);
            StringAssert.Contains(command.Sql, "COALESCE(s.supplier_code,w.supplier_code_snapshot,'') COLLATE");
            StringAssert.Contains(command.Sql, "ESCAPE '~'");
        }
        fixture.Connection.Commands.Clear();
        var supplierId = Guid.NewGuid();
        await repository.SearchAsync(new WarehouseSiteSearchRequest { Keyword = keyword, SupplierId = supplierId, IsActive = false });
        Assert.AreEqual(supplierId, fixture.Connection.Values["SupplierId"]);
        Assert.AreEqual(false, fixture.Connection.Values["IsActive"]);
        foreach (var command in fixture.Connection.Commands)
        {
            var where = command.Sql[(command.Sql.IndexOf("WHERE", StringComparison.Ordinal))..];
            Assert.IsFalse(where.Contains("supplier_code_snapshot", StringComparison.Ordinal));
            StringAssert.Contains(where, "s.supplier_code COLLATE public.erp_dictionary_ci LIKE");
        }
    }

    /// <summary>选择器别名映射版本、枚举、可空身份和显示文字；管理详情保留空值且未命中返回空。</summary>
    [TestMethod]
    public async Task WarehouseQuery_MapsPickerAndNullableDetail()
    {
        using var fixture = new Fixture();
        var rows = fixture.Connection.Rows;
        rows.Columns.Add("Uid", typeof(Guid));
        rows.Columns.Add("Version", typeof(int));
        rows.Columns.Add("Code", typeof(string));
        rows.Columns.Add("Name", typeof(string));
        rows.Columns.Add("SupplierId", typeof(Guid));
        rows.Columns.Add("SupplierCode", typeof(string));
        rows.Columns.Add("SupplierName", typeof(string));
        rows.Columns.Add("SiteType", typeof(int));
        rows.Columns.Add("IsActive", typeof(bool));
        rows.Columns.Add("UpdatedDate", typeof(DateTime));
        var uid = Guid.NewGuid();
        rows.Rows.Add(uid, 3, "W01", "仓库", DBNull.Value, "", "", 0, true, DBNull.Value);
        var repository = new PostgreSqlWarehouseQueryRepository(DictionaryContext(fixture));
        var picker = (await repository.GetPickerPagedAsync(new())).Items.Single();
        Assert.AreEqual(uid, picker.Uid);
        Assert.AreEqual(3, picker.Version);
        Assert.AreEqual(WarehouseSiteTypeEnum.SupplierVirtual, picker.SiteType);
        Assert.IsNull(picker.SupplierId);
        Assert.AreEqual("", picker.SupplierName);
        rows.Rows.Clear();
        rows.Rows.Add(uid, 3, "W01", "仓库", DBNull.Value, DBNull.Value, DBNull.Value, 0, false, DBNull.Value);
        var detail = await repository.GetDtoAsync(uid);
        Assert.IsNotNull(detail);
        Assert.IsNull(detail.SupplierCode);
        Assert.IsNull(detail.UpdatedDate);
        Assert.IsFalse(detail.IsActive);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        rows.Rows.Clear();
        Assert.IsNull(await repository.GetDtoAsync(Guid.NewGuid()));
    }

    /// <summary>产品与供应商窄候选分别还原其身份、编码和名称，不依赖全局 Dapper 下划线映射。</summary>
    [TestMethod]
    public async Task WarehouseQuery_MapsBothNarrowCandidateTypes()
    {
        using var fixture = new Fixture();
        var rows = fixture.Connection.Rows;
        rows.Columns.Add("Uid", typeof(Guid));
        rows.Columns.Add("Code", typeof(string));
        rows.Columns.Add("Name", typeof(string));
        rows.Columns.Add("SupplierCode", typeof(string));
        rows.Columns.Add("SupplierName", typeof(string));
        var uid = Guid.NewGuid();
        rows.Rows.Add(uid, "W01", "产品默认仓", "S01", "供应商候选");
        var repository = new PostgreSqlWarehouseQueryRepository(DictionaryContext(fixture));
        var product = (await repository.GetProductDefaultCandidatesAsync(new())).Items.Single();
        Assert.AreEqual(uid, product.Uid);
        Assert.AreEqual("W01", product.Code);
        Assert.AreEqual("产品默认仓", product.Name);
        var supplier = (await repository.SearchSupplierLookupAsync(new())).Items.Single();
        Assert.AreEqual(uid, supplier.Uid);
        Assert.AreEqual("S01", supplier.SupplierCode);
        Assert.AreEqual("供应商候选", supplier.SupplierName);
        foreach (var command in fixture.Connection.Commands)
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
    }

    /// <summary>六个方法均传递预取消令牌，不能写入、提交或回滚调用方事务。</summary>
    [TestMethod]
    public async Task WarehouseQuery_AllMethodsForwardCancellation()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlWarehouseQueryRepository(DictionaryContext(fixture));
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByIdsAsync([Guid.NewGuid()], token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetProductDefaultCandidatesAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetPickerPagedAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SearchSupplierLookupAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SearchAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetDtoAsync(Guid.NewGuid(), token));
        Assert.HasCount(0, fixture.Connection.Commands);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }
}
