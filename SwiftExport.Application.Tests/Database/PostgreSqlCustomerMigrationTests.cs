using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Customers.Management;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用真实 Dapper 无网络夹具验证客户七个专用查询及继承 CRUD 的参数和字段边界。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>编码查询显式使用 ICU 与尾空格比较，存在性包含软删而实体读取排除软删并还原停用状态。</summary>
    [TestMethod]
    public async Task CustomerQuery_CodeComparisonAndEntityProjectionPreserveBoundaries()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCustomerRepository(DictionaryContext(fixture));
        fixture.Connection.Rows.Columns.Add("Exists", typeof(int));
        fixture.Connection.Rows.Rows.Add(1);
        Assert.IsTrue(await repository.ExistByCustomerCode(" Code "));
        StringAssert.Contains(fixture.Connection.Text, "rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(fixture.Connection.Text, "rtrim(CAST(@CustomerCode AS text),' ')");
        Assert.IsFalse(fixture.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        Assert.AreEqual(" Code ", fixture.Connection.Values["CustomerCode"]);
        fixture.Connection.Rows = new();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("CustomerCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("IsActive", typeof(bool));
        fixture.Connection.Rows.Columns.Add("CustomerName", typeof(string));
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(uid, "Code ", false, DBNull.Value);
        var customer = await repository.GetByCustomerCodeAsync("code");
        Assert.IsNotNull(customer);
        Assert.AreEqual(uid, customer.Uid);
        Assert.AreEqual("Code ", customer.CustomerCode);
        Assert.IsFalse(customer.IsActive);
        Assert.IsNull(customer.CustomerName);
        StringAssert.Contains(fixture.Connection.Text, "COALESCE(c.is_deleted,FALSE)=FALSE");
        StringAssert.Contains(fixture.Connection.Text, "c.\"is_active\" AS \"IsActive\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("SELECT *", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>编码数组不展开、不 Trim，占用检查排除整批身份，两个入口保留不同删除过滤。</summary>
    [TestMethod]
    public async Task CustomerQuery_ArraysPreserveRawCodesAndGlobalUidExclusion()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCustomerRepository(DictionaryContext(fixture));
        Assert.HasCount(0, await repository.GetExistingCustomerCodesAsync([]));
        Assert.HasCount(0, await repository.GetCustomerCodesUsedByOtherCustomersAsync([]));
        Assert.HasCount(0, fixture.Connection.Commands);
        fixture.Connection.Rows.Columns.Add("CustomerCode", typeof(string));
        fixture.Connection.Rows.Rows.Add("Code ");
        var codes = new[] { " Code", "Code " };
        Assert.AreEqual("Code ", (await repository.GetExistingCustomerCodesAsync(codes)).Single());
        CollectionAssert.AreEqual(codes, (string[])fixture.Connection.Values["Codes"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, fixture.Connection.Types["Codes"]);
        StringAssert.Contains(fixture.Connection.Text, "EXISTS (SELECT 1 FROM unnest(@Codes)");
        Assert.IsFalse(fixture.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await repository.GetCustomerCodesUsedByOtherCustomersAsync([(ids[0], codes[0]), (ids[1], codes[1])]);
        CollectionAssert.AreEqual(ids, (Guid[])fixture.Connection.Values["Uids"]!);
        StringAssert.Contains(fixture.Connection.Text, "NOT (c.uid=ANY(@Uids)) AND c.is_deleted=FALSE");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>管理和销售页保留各自默认与上限，关键词使用真实 Build 通配规则且不提前 Trim。</summary>
    [TestMethod]
    [DataRow(0, 20, 30)]
    [DataRow(999, 200, 100)]
    public async Task CustomerQuery_PagesKeepKeywordAndDifferentLimits(int size, int managementSize, int salesSize)
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCustomerRepository(DictionaryContext(fixture));
        const string keyword = " A?%_[~ B ";
        var management = await repository.GetPagedAsync(new CustomerSearchRequest { PageIndex = 0, PageSize = size, Keyword = keyword });
        Assert.AreEqual(1, management.PageIndex);
        Assert.AreEqual(managementSize, management.PageSize);
        Assert.AreEqual("%%A_~%~_~[~~%B%%", fixture.Connection.Values["Keyword"]);
        StringAssert.Contains(fixture.Connection.Text, "c.\"linedrawing_folder_path\" AS \"LinedrawingFolderPath\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("\"IsActive\"", StringComparison.Ordinal));
        var sales = await repository.SearchSalesOrderCustomersAsync(new SalesOrderCustomerSearchRequest { PageIndex = 2, PageSize = size, Keyword = keyword });
        Assert.AreEqual(salesSize, sales.PageSize);
        Assert.AreEqual(salesSize, fixture.Connection.Values["Offset"]);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreEqual("%%A_~%~_~[~~%B%%", command.Values["Keyword"]);
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
            Assert.IsFalse(command.Sql.Contains("is_active", StringComparison.Ordinal));
            StringAssert.Contains(command.Sql, "COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'");
        }
        Assert.HasCount(4, fixture.Connection.Commands);
    }

    /// <summary>管理字段保留可空投影，销售 Lookup 使用独立身份别名、地址快照和默认商业数字。</summary>
    [TestMethod]
    public async Task CustomerQuery_MapsManagementAndSalesLookup()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCustomerRepository(DictionaryContext(fixture));
        var rows = fixture.Connection.Rows;
        rows.Columns.Add("Uid", typeof(Guid));
        rows.Columns.Add("CustomerCode", typeof(string));
        rows.Columns.Add("CustomerName", typeof(string));
        rows.Columns.Add("CustomerId", typeof(Guid));
        rows.Columns.Add("Currency", typeof(string));
        rows.Columns.Add("DepositRate", typeof(decimal));
        rows.Columns.Add("LeadTimeDays", typeof(int));
        rows.Columns.Add("ShipToAddress", typeof(string));
        var uid = Guid.NewGuid();
        rows.Rows.Add(uid, "C01", DBNull.Value, uid, "", 0m, 0, "Address");
        var management = (await repository.GetPagedAsync(new())).Items.Single();
        Assert.AreEqual(uid, management.Uid);
        Assert.IsNull(management.CustomerName);
        var lookup = (await repository.SearchSalesOrderCustomersAsync(new() { Keyword = "Address" })).Items.Single();
        Assert.AreEqual(uid, lookup.CustomerId);
        Assert.AreEqual(0m, lookup.DepositRate);
        Assert.AreEqual(0, lookup.LeadTimeDays);
        Assert.AreEqual("Address", lookup.ShipToAddress);
        StringAssert.Contains(fixture.Connection.Text, "COALESCE(c.customer_name,'') AS \"ShipToName\"");
        StringAssert.Contains(fixture.Connection.Text, "COALESCE(c.currency,'') AS \"Currency\"");
        StringAssert.Contains(fixture.Connection.Text, "c.contact_person COLLATE public.erp_dictionary_ci LIKE");
    }

    /// <summary>销售编码列表仅此处双侧普通空格裁剪及去重；可取消接口传递预取消且不接管事务。</summary>
    [TestMethod]
    public async Task CustomerQuery_SalesCodesAndCancellationKeepContract()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCustomerRepository(DictionaryContext(fixture));
        fixture.Connection.Rows.Columns.Add("CustomerCode", typeof(string));
        fixture.Connection.Rows.Rows.Add("C01");
        Assert.AreEqual("C01", (await repository.GetSalesOrderCustomerCodesAsync()).Single());
        StringAssert.Contains(fixture.Connection.Text, "DISTINCT btrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(fixture.Connection.Text, "NULLIF(btrim(c.customer_code,' '),'') IS NOT NULL");
        Assert.IsFalse(fixture.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        fixture.Connection.Commands.Clear();
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetSalesOrderCustomerCodesAsync(token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SearchSalesOrderCustomersAsync(new(), token));
        Assert.HasCount(0, fixture.Connection.Commands);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>继承 PG 新增保留显式启停，普通更新只改资料并递增版本，零行仍报告乐观并发失败。</summary>
    [TestMethod]
    public async Task CustomerCrud_PreservesActivationAndVersionBoundary()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCustomerRepository(DictionaryContext(fixture));
        var customer = new Customer { CustomerCode = "C01", DocRelateNo = "test", IsActive = false };
        Assert.IsTrue((await repository.AddAsync(customer)).Success);
        Assert.AreEqual(false, fixture.Connection.Values["IsActive"]);
        StringAssert.Contains(fixture.Connection.Text, "\"is_active\"");
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Rows.Add(4);
        customer.Version = 3;
        customer.IsActive = true;
        Assert.IsTrue((await repository.UpdateAsync(customer)).Success);
        Assert.AreEqual(3, fixture.Connection.Values["OriginalVersion"]);
        Assert.IsFalse(fixture.Connection.Text.Contains("\"is_active\" =", StringComparison.Ordinal));
        StringAssert.Contains(fixture.Connection.Text, "\"version\" = \"version\" + 1");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Connection.Rows.Rows.Clear();
        var failure = await repository.UpdateAsync(customer);
        Assert.IsFalse(failure.Success);
        Assert.AreEqual("CONCURRENCY_CONFLICT", failure.ErrorCode);
    }
}
