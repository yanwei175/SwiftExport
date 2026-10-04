using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用记录连接验证仓库管理真实 Dapper 映射、参数、事务和错误边界；数据库约束等价另行实库验收。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>各仓库锁读取完整还原版本、停用状态、可空供应商和历史快照，不依赖全局下划线映射。</summary>
    [TestMethod]
    [DataRow("id")]
    [DataRow("code")]
    [DataRow("supplier")]
    public async Task WarehouseManagement_LockReadsMapFullEntity(string operation)
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var created = new DateTime(2026, 1, 2, 3, 4, 5);
        var rows = fixture.Connection.Rows;
        rows.Columns.Add("Uid", typeof(Guid));
        rows.Columns.Add("Version", typeof(int));
        rows.Columns.Add("IsDeleted", typeof(bool));
        rows.Columns.Add("CreatedDate", typeof(DateTime));
        rows.Columns.Add("CreatedBy", typeof(string));
        rows.Columns.Add("UpdatedDate", typeof(DateTime));
        rows.Columns.Add("UpdatedBy", typeof(string));
        rows.Columns.Add("Code", typeof(string));
        rows.Columns.Add("Name", typeof(string));
        rows.Columns.Add("SiteType", typeof(int));
        rows.Columns.Add("SupplierId", typeof(Guid));
        rows.Columns.Add("SupplierCodeSnapshot", typeof(string));
        rows.Columns.Add("SupplierNameSnapshot", typeof(string));
        rows.Columns.Add("IsActive", typeof(bool));
        rows.Rows.Add(uid, 8, false, created, "原创建人", DBNull.Value, "原更新人", " W-A ", "旧仓名", 0,
            operation == "supplier" ? (object)supplierId : DBNull.Value, "旧供应商编码", DBNull.Value, operation == "supplier");
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        var site = operation switch
        {
            "id" => await repository.GetForUpdateAsync(uid),
            "code" => await repository.FindByCodeForUpdateAsync(" W-A "),
            _ => await repository.FindActiveSupplierWarehouseForUpdateAsync(supplierId)
        };
        Assert.IsNotNull(site);
        Assert.AreEqual(uid, site.Uid);
        Assert.AreEqual(8, site.Version);
        Assert.IsFalse(site.IsDeleted);
        Assert.AreEqual(created, site.CreatedDate);
        Assert.AreEqual("原创建人", site.CreatedBy);
        Assert.IsNull(site.UpdatedDate);
        Assert.AreEqual("原更新人", site.UpdatedBy);
        Assert.AreEqual(" W-A ", site.Code);
        Assert.AreEqual("旧仓名", site.Name);
        Assert.AreEqual(WarehouseSiteTypeEnum.SupplierVirtual, site.SiteType);
        Assert.AreEqual(operation == "supplier" ? supplierId : (Guid?)null, site.SupplierId);
        Assert.AreEqual("旧供应商编码", site.SupplierCodeSnapshot);
        Assert.IsNull(site.SupplierNameSnapshot);
        Assert.AreEqual(operation == "supplier", site.IsActive);
        var sql = WarehouseManagementSql(fixture.Connection.Text);
        StringAssert.Contains(sql, "coalesce(is_deleted,false)=false");
        foreach (var alias in new[] { "created_dateascreateddate", "updated_dateasupdateddate", "site_typeassitetype",
            "supplier_idassupplierid", "supplier_code_snapshotassuppliercodesnapshot", "supplier_name_snapshotassuppliernamesnapshot" })
            StringAssert.Contains(sql, alias);
        StringAssert.Contains(sql, "forupdate");
        Assert.IsFalse(sql.Contains("select*", StringComparison.Ordinal));
        if (operation == "supplier")
        {
            Assert.AreEqual(supplierId, fixture.Connection.Values["SupplierId"]);
            Assert.AreEqual(NpgsqlDbType.Uuid, fixture.Connection.Types["SupplierId"]);
            StringAssert.Contains(sql, "site_type=0");
            StringAssert.Contains(sql, "is_active=true");
        }
        else
        {
            Assert.IsFalse(sql.Contains("is_active=true", StringComparison.Ordinal));
            if (operation == "id")
            {
                Assert.AreEqual(uid, fixture.Connection.Values["Uid"]);
                Assert.AreEqual(NpgsqlDbType.Uuid, fixture.Connection.Types["Uid"]);
            }
            else
            {
                Assert.AreEqual(" W-A ", fixture.Connection.Values["Code"]);
                StringAssert.Contains(sql, "rtrim(");
                StringAssert.Contains(sql, "collatepublic.erp_dictionary_ci");
            }
        }
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        rows.Rows.Clear();
        Assert.IsNull(operation switch
        {
            "id" => await repository.GetForUpdateAsync(uid),
            "code" => await repository.FindByCodeForUpdateAsync(" W-A "),
            _ => await repository.FindActiveSupplierWarehouseForUpdateAsync(supplierId)
        });
    }

    /// <summary>建仓供应商读取还原权威编码名称，并保留活动、软删及调用方事务边界。</summary>
    [TestMethod]
    public async Task WarehouseManagement_SupplierMapsAuthorityAndValidity()
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("SupplierName", typeof(string));
        fixture.Connection.Rows.Rows.Add(uid, "S01", "权威供应商");
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        var supplier = await repository.GetActiveSupplierAsync(uid);
        Assert.IsNotNull(supplier);
        Assert.AreEqual(uid, supplier.Uid);
        Assert.AreEqual("S01", supplier.SupplierCode);
        Assert.AreEqual("权威供应商", supplier.SupplierName);
        Assert.AreEqual(uid, fixture.Connection.Values["Uid"]);
        StringAssert.Contains(WarehouseManagementSql(fixture.Connection.Text), "is_active=true");
        StringAssert.Contains(WarehouseManagementSql(fixture.Connection.Text), "coalesce(is_deleted,false)=false");
        StringAssert.Contains(WarehouseManagementSql(fixture.Connection.Text), "forupdate");
        Assert.AreEqual(NpgsqlDbType.Uuid, fixture.Connection.Types["Uid"]);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Connection.Rows.Rows.Clear();
        Assert.IsNull(await repository.GetActiveSupplierAsync(uid));
    }

    /// <summary>锁读取没有调用方事务时全部拒绝，不能悄悄开启事务或发送查询。</summary>
    [TestMethod]
    public async Task WarehouseManagement_LockReadsRejectMissingTransaction()
    {
        using var fixture = new Fixture();
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        var uid = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetForUpdateAsync(uid));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetActiveSupplierAsync(uid));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.FindByCodeForUpdateAsync("W01"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.FindActiveSupplierWarehouseForUpdateAsync(uid));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetDeleteBlockersAsync(uid));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>新增使用实体时间映射和整数枚举，保留身份、版本、可空关联及显式创建审计，并补齐缺失审计。</summary>
    [TestMethod]
    public async Task WarehouseManagement_InsertBindsEntityAndAudit()
    {
        using var fixture = new Fixture();
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var site = new WarehouseSite
        {
            Uid = Guid.NewGuid(), Version = 6, Code = "W01", Name = "仓库",
            SiteType = WarehouseSiteTypeEnum.SupplierVirtual, SupplierId = null,
            SupplierCodeSnapshot = null, SupplierNameSnapshot = "旧名", IsActive = false,
            CreatedDate = created, CreatedBy = "迁入人"
        };
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        Assert.AreEqual(1, await repository.InsertAsync(site));
        Assert.AreEqual(site.Uid, fixture.Connection.Values["Uid"]);
        // 实体参数包由驱动按 CLR 值推断标量类型；记录连接未执行 Bind，不能要求此时已解析 NpgsqlDbType。
        Assert.IsInstanceOfType<Guid>(fixture.Connection.Values["Uid"]);
        Assert.AreEqual(6, fixture.Connection.Values["Version"]);
        Assert.AreEqual(0, fixture.Connection.Values["SiteType"]);
        Assert.IsInstanceOfType<int>(fixture.Connection.Values["SiteType"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["SupplierId"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["SupplierCodeSnapshot"]);
        Assert.AreEqual("旧名", fixture.Connection.Values["SupplierNameSnapshot"]);
        Assert.AreEqual(false, fixture.Connection.Values["IsActive"]);
        Assert.AreEqual(created.Ticks, ((DateTime)fixture.Connection.Values["CreatedDate"]!).Ticks);
        Assert.AreEqual("迁入人", fixture.Connection.Values["CreatedBy"]);
        Assert.AreEqual("映射测试", fixture.Connection.Values["UpdatedBy"]);
        foreach (var parameter in new[] { "CreatedDate", "UpdatedDate" })
        {
            Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types[parameter]);
            Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)fixture.Connection.Values[parameter]!).Kind);
        }
        StringAssert.Contains(WarehouseManagementSql(fixture.Connection.Text), "insertinto");
        StringAssert.Contains(WarehouseManagementSql(fixture.Connection.Text), "warehouse_site");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
    }

    /// <summary>各变更传递调用方预期版本和审计时间；命中与零行原样返回，不覆盖身份或创建资料。</summary>
    [TestMethod]
    [DataRow("name", 1)]
    [DataRow("name", 0)]
    [DataRow("deactivate", 1)]
    [DataRow("deactivate", 0)]
    [DataRow("activate", 1)]
    [DataRow("activate", 0)]
    [DataRow("delete", 1)]
    [DataRow("delete", 0)]
    public async Task WarehouseManagement_UpdatesPreserveVersionAuditAndRowCount(string operation, int affected)
    {
        using var fixture = new Fixture();
        fixture.Connection.ExecuteWrite = () => affected;
        var site = new WarehouseSite { Uid = Guid.NewGuid(), Version = 99, Name = "新名称" };
        var before = DateTime.Now;
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        var result = operation switch
        {
            "name" => await repository.UpdateNameAsync(site, 7),
            "deactivate" => await repository.DeactivateAsync(site, 7),
            "activate" => await repository.ActivateAsync(site, 7),
            _ => await repository.DeleteAsync(site.Uid, 7)
        };
        Assert.AreEqual(affected, result);
        Assert.AreEqual(site.Uid, fixture.Connection.Values["Uid"]);
        Assert.AreEqual(7, fixture.Connection.Values["ExpectedVersion"]);
        var sql = WarehouseManagementSql(fixture.Connection.Text);
        StringAssert.Contains(sql, "version=version+1");
        StringAssert.Contains(sql, "version=@expectedversion");
        StringAssert.Contains(sql, "coalesce(is_deleted,false)=false");
        var timestamp = fixture.Connection.Types.Single(item => item.Value == NpgsqlDbType.Timestamp).Key;
        var date = (DateTime)fixture.Connection.Values[timestamp]!;
        Assert.AreEqual(DateTimeKind.Unspecified, date.Kind);
        Assert.IsTrue(date >= before && date <= DateTime.Now);
        Assert.IsTrue(fixture.Connection.Values.Values.Contains("映射测试"));
        Assert.IsFalse(sql.Contains("created_date=", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("created_by=", StringComparison.Ordinal));
        if (operation == "name") Assert.AreEqual("新名称", fixture.Connection.Values["Name"]);
        if (operation == "activate") StringAssert.Contains(sql, "is_active=true");
        if (operation is "deactivate" or "delete") StringAssert.Contains(sql, "is_active=false");
        if (operation == "delete") StringAssert.Contains(sql, "is_deleted=true");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>非 PostgreSQL 执行异常原样传播，仓储不能将超时伪装成唯一冲突或擅自接管事务。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WarehouseManagement_WriteTimeoutPropagates(bool activate)
    {
        using var fixture = new Fixture();
        var failure = new TimeoutException("测试超时");
        fixture.Connection.ExecuteWrite = () => throw failure;
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        var site = new WarehouseSite { Uid = Guid.NewGuid(), Code = "W01" };
        Func<Task<int>> write = activate ? () => repository.ActivateAsync(site, 3) : () => repository.InsertAsync(site);
        var thrown = await Assert.ThrowsExactlyAsync<TimeoutException>(write);
        Assert.AreSame(failure, thrown);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>插入和启用只把唯一冲突转换为零，其余 PostgreSQL 故障保持原异常，不假装版本失败。</summary>
    [TestMethod]
    [DataRow(false, "23505")]
    [DataRow(true, "23505")]
    [DataRow(false, "40001")]
    [DataRow(true, "40001")]
    [DataRow(false, "40P01")]
    [DataRow(true, "40P01")]
    [DataRow(false, "23503")]
    [DataRow(true, "23503")]
    public async Task WarehouseManagement_WriteErrorsKeepSqlStateBoundary(bool activate, string state)
    {
        using var fixture = new Fixture();
        var failure = new PostgresException("测试故障", "ERROR", "ERROR", state);
        fixture.Connection.ExecuteWrite = () => throw failure;
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        var site = new WarehouseSite { Uid = Guid.NewGuid(), Code = "W01" };
        Func<Task<int>> write = activate ? () => repository.ActivateAsync(site, 3) : () => repository.InsertAsync(site);
        if (state == "23505") Assert.AreEqual(0, await write());
        else
        {
            var thrown = await Assert.ThrowsExactlyAsync<PostgresException>(write);
            Assert.AreSame(failure, thrown);
        }
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>删除阻断只检查未删非零余额并映射中文原因，不把历史流水增加为删除阻断条件。</summary>
    [TestMethod]
    public async Task WarehouseManagement_DeleteBlockersMapOnlyNonzeroBalance()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Blocker", typeof(string));
        fixture.Connection.Rows.Rows.Add("库存余额非零");
        var uid = Guid.NewGuid();
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        CollectionAssert.AreEqual(new[] { "库存余额非零" }, (await repository.GetDeleteBlockersAsync(uid)).ToArray());
        Assert.AreEqual(uid, fixture.Connection.Values["WarehouseSiteId"]);
        var sql = WarehouseManagementSql(fixture.Connection.Text);
        StringAssert.Contains(sql, "inventory_balance");
        StringAssert.Contains(sql, "on_hand_qty<>0");
        StringAssert.Contains(sql, "is_deleted=false");
        StringAssert.Contains(sql, "forupdate");
        Assert.IsFalse(sql.Contains("inventory_ledger_entry", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Connection.Rows.Rows.Clear();
        Assert.HasCount(0, await repository.GetDeleteBlockersAsync(uid));
    }

    /// <summary>停用检查同时保留非零余额及任何历史流水，流水不附加软删或数量条件。</summary>
    [TestMethod]
    public async Task WarehouseManagement_HistoryCheckKeepsAnyLedger()
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        Assert.IsTrue(await new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture)).HasBalanceOrLedgerAsync(uid));
        Assert.AreEqual(uid, fixture.Connection.Values["WarehouseSiteId"]);
        var sql = WarehouseManagementSql(fixture.Connection.Text);
        StringAssert.Contains(sql, "inventory_balance");
        StringAssert.Contains(sql, "on_hand_qty<>0");
        StringAssert.Contains(sql, "orexists(");
        var ledger = sql[sql.IndexOf("inventory_ledger_entry", StringComparison.Ordinal)..];
        StringAssert.Contains(ledger, "warehouse_site_id=@warehousesiteid");
        Assert.IsFalse(ledger.Contains("is_deleted", StringComparison.Ordinal));
        Assert.IsFalse(ledger.Contains("qty", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>有取消令牌的两个查询在请求预取消时不发送命令，也不吞掉取消。</summary>
    [TestMethod]
    public async Task WarehouseManagement_QueryCancellationDoesNotExecute()
    {
        using var fixture = new Fixture();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var repository = new PostgreSqlWarehouseManagementRepository(DictionaryContext(fixture));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetActiveSupplierAsync(Guid.NewGuid(), canceled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetDeleteBlockersAsync(Guid.NewGuid(), canceled.Token));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>只消除测试断言中的空白和标识符引号，不执行 SQL 或模拟 PostgreSQL 过滤及唯一索引。</summary>
    private static string WarehouseManagementSql(string sql) => System.Text.RegularExpressions.Regex.Replace(
        sql.Replace("\"", "", StringComparison.Ordinal), "\\s+", "").ToLowerInvariant();
}
