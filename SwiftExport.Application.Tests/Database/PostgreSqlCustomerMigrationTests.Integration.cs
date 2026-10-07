using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Customers.Management;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅在批准专用库的回滚事务中验证客户查询及临时客户 CRUD，不修改旧记录或调整唯一索引。</summary>
[TestClass]
public sealed class PostgreSqlCustomerMigrationIntegrationTests
{
    /// <summary>新增启停由输入确定，普通更新不能启用客户，版本冲突与临时记录软硬删除保持原结果契约。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    public Task Crud_KeepActivationFrozenAndVersionConflicts() => InRollback(async context =>
    {
        var repository = new PostgreSqlCustomerRepository(context);
        var customer = NewCustomer(Prefix(), active: false);
        var added = await repository.AddAsync(customer);
        Assert.IsTrue(added.Success);
        Assert.AreEqual(1, (await repository.GetByIdAsync(customer.Uid, true))!.Version);
        customer.IsActive = true;
        customer.CustomerName = "临时修改名称";
        Assert.IsTrue((await repository.UpdateAsync(customer)).Success);
        var saved = await repository.GetByIdAsync(customer.Uid);
        Assert.IsNotNull(saved);
        Assert.IsFalse(saved.IsActive);
        Assert.AreEqual(2, saved.Version);
        Assert.AreEqual("临时修改名称", saved.CustomerName);
        var stale = NewCustomer(customer.CustomerCode);
        stale.Uid = customer.Uid;
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(stale)).ErrorCode);
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.DeleteAsync(customer.Uid, 1)).ErrorCode);
        Assert.IsTrue((await repository.DeleteAsync(customer.Uid, saved.Version)).Success);
        Assert.IsNull(await repository.GetByIdAsync(customer.Uid));
        Assert.IsTrue(await repository.ExistByCustomerCode(customer.CustomerCode));
        var second = NewCustomer(Prefix());
        Assert.IsTrue((await repository.AddAsync(second)).Success);
        // 只删除本事务刚新增且保存了返回身份的临时记录，绝不枚举或删除旧客户。
        Assert.IsTrue((await repository.DeleteAsync(second.Uid, second.Version, false)).Success);
        Assert.IsFalse(await repository.ExistsIdAsync(second.Uid));
    });

    /// <summary>存在及已有编码包含软删，实体读取只取未删；停用客户仍可 Lookup，空商业字段保留各自投影规则。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    public Task DeletedInactiveAndNullableFields_PreserveQueryDifferences() => InRollback(async context =>
    {
        var prefix = Prefix();
        var inactive = NewCustomer(prefix + "A", active: false);
        var deleted = NewCustomer(prefix + "D", deleted: true);
        await Insert(context, inactive);
        await Insert(context, deleted);
        var repository = new PostgreSqlCustomerRepository(context);
        Assert.IsTrue(await repository.ExistByCustomerCode(deleted.CustomerCode));
        Assert.IsNull(await repository.GetByCustomerCodeAsync(deleted.CustomerCode));
        CollectionAssert.AreEquivalent(new[] { inactive.CustomerCode, deleted.CustomerCode },
            (await repository.GetExistingCustomerCodesAsync([inactive.CustomerCode, deleted.CustomerCode])).ToArray());
        var entity = await repository.GetByCustomerCodeAsync(inactive.CustomerCode.ToLowerInvariant() + " ");
        Assert.IsNotNull(entity);
        Assert.IsFalse(entity.IsActive);
        Assert.IsNull(entity.CustomerName);
        var management = await repository.GetPagedAsync(new CustomerSearchRequest { Keyword = prefix });
        Assert.AreEqual(1, management.TotalCount);
        Assert.IsNull(management.Items.Single().CustomerName);
        Assert.IsNull(management.Items.Single().Currency);
        var lookup = await repository.SearchSalesOrderCustomersAsync(new SalesOrderCustomerSearchRequest { Keyword = prefix });
        Assert.AreEqual(1, lookup.TotalCount);
        var row = lookup.Items.Single();
        Assert.AreEqual(inactive.Uid, row.CustomerId);
        Assert.AreEqual("", row.CustomerName);
        Assert.AreEqual("", row.Currency);
        Assert.AreEqual("", row.ShipToAddress);
        Assert.AreEqual("", row.ShipToPhone);
        Assert.AreEqual(0m, row.DepositRate);
        Assert.AreEqual(0, row.LeadTimeDays);
        var codes = await repository.GetSalesOrderCustomerCodesAsync();
        Assert.IsTrue(codes.Contains(inactive.CustomerCode));
        Assert.IsFalse(codes.Contains(deleted.CustomerCode));
    });

    /// <summary>编码等值只忽略尾部普通空格与 ICU 等价差异，前导空格保留；占用检查排除全批身份。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    public Task CodeComparisonAndBatchExclusion_DoNotTrimLeadingSpace() => InRollback(async context =>
    {
        var prefix = Prefix();
        var first = NewCustomer(prefix + "Code ");
        var leading = NewCustomer(" " + prefix + "Lead");
        var deleted = NewCustomer(prefix + "Deleted", deleted: true);
        await Insert(context, first);
        await Insert(context, leading);
        await Insert(context, deleted);
        var repository = new PostgreSqlCustomerRepository(context);
        Assert.AreEqual(first.Uid, (await repository.GetByCustomerCodeAsync(prefix.ToLowerInvariant() + "code"))!.Uid);
        Assert.IsTrue(await repository.ExistByCustomerCode(first.CustomerCode.TrimEnd().ToLowerInvariant()));
        Assert.IsFalse(await repository.ExistByCustomerCode(leading.CustomerCode.TrimStart()));
        CollectionAssert.AreEqual(new[] { first.CustomerCode },
            (await repository.GetExistingCustomerCodesAsync([first.CustomerCode.TrimEnd().ToLowerInvariant(), prefix + "Missing"])).ToArray());
        // 第二个提交项的编码虽然与该身份不配对，该身份仍在全批排除集合内。
        Assert.HasCount(0, await repository.GetCustomerCodesUsedByOtherCustomersAsync(
            [(first.Uid, leading.CustomerCode), (leading.Uid, first.CustomerCode)]));
        CollectionAssert.AreEqual(new[] { first.CustomerCode },
            (await repository.GetCustomerCodesUsedByOtherCustomersAsync([(Guid.NewGuid(), first.CustomerCode), (Guid.NewGuid(), deleted.CustomerCode)])).ToArray());
        var codes = await repository.GetSalesOrderCustomerCodesAsync();
        Assert.IsTrue(codes.Contains(first.CustomerCode.Trim()));
        Assert.IsTrue(codes.Contains(leading.CustomerCode.Trim()));
    });

    /// <summary>真实 Build 的问号及空格可通配，百分号下划线方括号和 escape 字符仍字面，不把反斜杠当 escape。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    [DataRow("%", "%", "X")]
    [DataRow("_", "_", "X")]
    [DataRow("[", "[", "X")]
    [DataRow("~", "~", "X")]
    [DataRow("\\", "\\", "X")]
    [DataRow("?", "Z", "XX")]
    [DataRow(" ", "ManyLetters", "")]
    public Task KeywordRules_KeepLiteralAndWildcardMeaning(string input, string target, string other) => InRollback(async context =>
    {
        var prefix = Prefix();
        var first = NewCustomer(prefix + "A");
        first.CustomerName = prefix + "Start" + target + "End";
        var second = NewCustomer(prefix + "B");
        second.CustomerName = prefix + "Start" + other + "End";
        await Insert(context, first);
        await Insert(context, second);
        var repository = new PostgreSqlCustomerRepository(context);
        var keyword = prefix.ToLowerInvariant() + "start" + input + "end";
        var management = await repository.GetPagedAsync(new CustomerSearchRequest { Keyword = keyword });
        var sales = await repository.SearchSalesOrderCustomersAsync(new SalesOrderCustomerSearchRequest { Keyword = keyword });
        var expectedCount = input == " " ? 2 : 1;
        Assert.AreEqual(expectedCount, management.TotalCount);
        Assert.IsTrue(management.Items.Any(row => row.Uid == first.Uid));
        Assert.AreEqual(input == " ", management.Items.Any(row => row.Uid == second.Uid));
        Assert.AreEqual(expectedCount, sales.TotalCount);
        Assert.IsTrue(sales.Items.Any(row => row.CustomerId == first.Uid));
        Assert.AreEqual(input == " ", sales.Items.Any(row => row.CustomerId == second.Uid));
    });

    /// <summary>分页默认、上限、编码排序与越界页计数保持；Lookup 额外匹配联系人，取消不破坏外层事务。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    public Task PagingContactSearchAndCancellation_UseCallerTransaction() => InRollback(async context =>
    {
        var prefix = Prefix();
        var first = NewCustomer(prefix + "A");
        first.ContactPerson = prefix + "Contact";
        var second = NewCustomer(prefix + "B");
        await Insert(context, first);
        await Insert(context, second);
        var repository = new PostgreSqlCustomerRepository(context);
        var page = await repository.GetPagedAsync(new CustomerSearchRequest { Keyword = prefix, PageIndex = 2, PageSize = 1 });
        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(second.Uid, page.Items.Single().Uid);
        var empty = await repository.SearchSalesOrderCustomersAsync(new SalesOrderCustomerSearchRequest { Keyword = prefix, PageIndex = 3, PageSize = 1 });
        Assert.AreEqual(2, empty.TotalCount);
        Assert.HasCount(0, empty.Items);
        Assert.AreEqual(0, (await repository.GetPagedAsync(new CustomerSearchRequest { Keyword = first.ContactPerson })).TotalCount);
        Assert.AreEqual(first.Uid, (await repository.SearchSalesOrderCustomersAsync(new SalesOrderCustomerSearchRequest
            { Keyword = first.ContactPerson })).Items.Single().CustomerId);
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SearchSalesOrderCustomersAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetSalesOrderCustomerCodesAsync(token));
        Assert.IsNotNull(context.Session.Transaction);
        Assert.AreEqual(ConnectionState.Open, context.Session.Connection.State);
    });

    /// <summary>专用测试入口只开启回滚事务；不提交、不建库、不调整索引，也不触碰旧客户。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var user = new TestUser();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        try { await scenario(context); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>创建随机临时身份，空商业字段专门用于验证管理与销售投影差异。</summary>
    private static Customer NewCustomer(string code, bool active = true, bool deleted = false) => new()
    {
        Uid = Guid.NewGuid(), CustomerCode = code, DocRelateNo = Prefix(), IsActive = active, IsDeleted = deleted
    };

    /// <summary>元数据 INSERT 保留软删除夹具及指定身份，不经会重置这些字段的通用 Add。</summary>
    private static async Task Insert(RepositoryBaseContext context, Customer customer)
    {
        context.AuditStamp.StampForInsert(customer);
        var metadata = EntityMetadataCache.GetMetadata(typeof(Customer));
        var columns = string.Join(", ", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
        var values = string.Join(", ", metadata.Columns.Select(column => "@" + column.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO public.customer ({columns}) VALUES ({values});", PostgreSqlParameters.Entity(customer, metadata), context.Session.Transaction)));
    }

    /// <summary>随机短编码确保测试仅查自己的临时记录，不依赖原有基础资料。</summary>
    private static string Prefix() => "PGC" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>提供临时数据审计名称，不模拟登录权限。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_customer__";
        public bool IsAuthenticated => false;
    }
}
