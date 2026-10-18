using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Infrastructure.Repositories.Suppliers.Management;
using SwiftExport.Infrastructure.Repositories.Suppliers.SupplierProducts;

namespace SwiftExport.Application.Tests.Database;

/// <summary>供应商管理快照的真实 Dapper 映射与 SQL 边界；全部使用离线连接。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>全量和分页使用完全相同 DTO 列及未软删范围，快照不截断记录或过滤停用资格。</summary>
    [TestMethod]
    public async Task SupplierManagementSnapshot_MatchesPagedProjectionAndVisibility()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("TaxRate", typeof(decimal));
        fixture.Connection.Rows.Columns.Add("DefaultLeadTime", typeof(int));
        var id = Guid.NewGuid(); fixture.Connection.Rows.Rows.Add(id, 8, "S", DBNull.Value, 0);
        var repository = new PostgreSqlSupplierRepository(DictionaryContext(fixture));
        var snapshot = await repository.GetAllForManagementAsync();
        var snapshotSql = fixture.Connection.Text;
        Assert.AreEqual(id, snapshot.Single().Uid); Assert.AreEqual(8, snapshot.Single().Version);
        Assert.IsNull(snapshot.Single().TaxRate); Assert.AreEqual(0, snapshot.Single().DefaultLeadTime);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.IsFalse(snapshotSql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(snapshotSql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(snapshotSql.Contains("is_active", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(snapshotSql, "s.is_deleted = FALSE OR s.is_deleted IS NULL");
        StringAssert.Contains(snapshotSql, "ORDER BY s.supplier_code COLLATE public.erp_dictionary_ci, s.uid");
        await repository.GetPagedAsync(new());
        var aliases = Regex.Matches(snapshotSql, "AS \"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();
        var pagedAliases = Regex.Matches(fixture.Connection.Text, "AS \"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();
        CollectionAssert.AreEqual(pagedAliases, aliases);
        CollectionAssert.AreEquivalent(typeof(SupplierDto).GetProperties().Select(property => property.Name).ToArray(), aliases);
        Assert.AreEqual(0, fixture.Connection.Commits + fixture.Connection.Rollbacks);
    }

    /// <summary>已取消的全量或分页读取不得启动数据库命令，令牌由仓储直接传给 Dapper。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SupplierQueries_PreCanceledTokenDoesNotExecute(bool products)
    {
        using var fixture = new Fixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var transaction = fixture.Session.Transaction;
        if (products)
            await Assert.ThrowsAsync<OperationCanceledException>(() => new PostgreSqlSupplierProductRepository(DictionaryContext(fixture))
                .GetPagedAsync(new() { SupplierId = Guid.NewGuid() }, cancel.Token));
        else
            await Assert.ThrowsAsync<OperationCanceledException>(() => new PostgreSqlSupplierRepository(DictionaryContext(fixture))
                .GetAllForManagementAsync(cancel.Token));
        Assert.HasCount(0, fixture.Connection.Commands);
        Assert.AreSame(transaction, fixture.Session.Transaction);
        Assert.AreEqual(0, fixture.Connection.Commits + fixture.Connection.Rollbacks);
    }
}
