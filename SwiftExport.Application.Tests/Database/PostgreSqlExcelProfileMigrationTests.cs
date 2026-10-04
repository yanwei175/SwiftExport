using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Core.Entities.Systems.ExcelReadProfiles;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用无网络夹具验证 Excel 配置聚合的 PostgreSQL 命令及映射，不替代实库约束和并发验收。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>分组值作为参数传递，完整属性投影还原列表及锁定主配置，空结果保持空集合或 null。</summary>
    [TestMethod]
    public async Task ExcelProfile_ReadsMapHeadAndPreserveQueryBoundaries()
    {
        using var fixture = new Fixture();
        var repository = ExcelProfileRepository(fixture);
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Columns.Add("GroupCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("ConfigName", typeof(string));
        fixture.Connection.Rows.Columns.Add("EndMode", typeof(int));
        fixture.Connection.Rows.Columns.Add("SummaryCellAddress", typeof(string));
        fixture.Connection.Rows.Rows.Add(uid, 3, "SalesOrder", "客户配置", 2, DBNull.Value);
        var rows = await repository.ListAsync("SalesOrder'", default);
        Assert.AreEqual(uid, rows.Single().Uid);
        Assert.AreEqual("客户配置", rows.Single().ConfigName);
        Assert.AreEqual(2, rows.Single().EndMode);
        Assert.IsNull(rows.Single().SummaryCellAddress);
        Assert.AreEqual("SalesOrder'", fixture.Connection.Values["GroupCode"]);
        Assert.IsFalse(fixture.Connection.Text.Contains("SalesOrder'", StringComparison.Ordinal));
        StringAssert.Contains(fixture.Connection.Text, "group_code=@GroupCode AND is_deleted=FALSE ORDER BY config_name, uid");
        StringAssert.Contains(fixture.Connection.Text, "\"end_mode\" AS \"EndMode\"");
        var head = await repository.FindAsync(uid, true, default);
        Assert.AreEqual(3, head!.Version);
        Assert.AreEqual(uid, fixture.Connection.Values["Uid"]);
        StringAssert.Contains(fixture.Connection.Text, "uid=@Uid AND is_deleted=FALSE FOR UPDATE");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        await repository.FindAsync(uid, false, default);
        Assert.IsFalse(fixture.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        fixture.Connection.Rows.Rows.Clear();
        Assert.IsNull(await repository.FindAsync(uid, false, default));
        Assert.HasCount(0, await repository.ListAsync("SalesOrder", default));
        Assert.IsTrue(fixture.Connection.Commands.All(command => ReferenceEquals(fixture.Session.Transaction, command.Transaction)));
    }

    /// <summary>字段按主配置过滤，显式别名还原关系、布尔及可空工作表，不拼接主键值。</summary>
    [TestMethod]
    public async Task ExcelProfile_FieldsMapRelationAndNullableWorksheet()
    {
        using var fixture = new Fixture();
        var repository = ExcelProfileRepository(fixture);
        var uid = Guid.NewGuid();
        var fieldUid = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("ProfileUid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("TargetFieldKey", typeof(string));
        fixture.Connection.Rows.Columns.Add("WorksheetName", typeof(string));
        fixture.Connection.Rows.Columns.Add("CellAddress", typeof(string));
        fixture.Connection.Rows.Columns.Add("IsDownward", typeof(bool));
        fixture.Connection.Rows.Rows.Add(fieldUid, uid, "Line.Sku", DBNull.Value, "A8", true);
        var field = (await repository.GetFieldsAsync(uid, default)).Single();
        Assert.AreEqual(fieldUid, field.Uid);
        Assert.AreEqual(uid, field.ProfileUid);
        Assert.AreEqual("Line.Sku", field.TargetFieldKey);
        Assert.AreEqual("A8", field.CellAddress);
        Assert.IsTrue(field.IsDownward);
        Assert.IsNull(field.WorksheetName);
        Assert.AreEqual(uid, fixture.Connection.Values["Uid"]);
        StringAssert.Contains(fixture.Connection.Text, "\"profile_uid\" AS \"ProfileUid\"");
        StringAssert.Contains(fixture.Connection.Text, "profile_uid=@Uid AND is_deleted=FALSE ORDER BY target_field_key, uid");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>主配置插入保留输入主键、版本及整数结束方式，审计时间和空值经实体参数绑定。</summary>
    [TestMethod]
    public async Task ExcelProfile_InsertPreservesIdentityAndTypedParameters()
    {
        using var fixture = new Fixture();
        var repository = ExcelProfileRepository(fixture);
        var head = ExcelProfileHead();
        var uid = head.Uid;
        await repository.InsertAsync(head, default);
        Assert.AreEqual(uid, head.Uid);
        Assert.AreEqual(uid, fixture.Connection.Values["Uid"]);
        Assert.AreEqual(3, fixture.Connection.Values["Version"]);
        Assert.AreEqual(2, fixture.Connection.Values["EndMode"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["LastRowColumn"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["Remark"]);
        Assert.AreEqual(false, fixture.Connection.Values["IsDeleted"]);
        Assert.AreEqual("映射测试", fixture.Connection.Values["CreatedBy"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types["CreatedDate"]);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)fixture.Connection.Values["UpdatedDate"]!).Kind);
        StringAssert.Contains(fixture.Connection.Text, "INSERT INTO public.excel_read_profile");
        StringAssert.Contains(fixture.Connection.Text, "\"summary_cell_address\"");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>主更新按预期版本返回数据库版本，未命中不回填；软删除参数不改变分组或创建审计列。</summary>
    [TestMethod]
    public async Task ExcelProfile_UpdateReturnsVersionOnlyOnSuccess()
    {
        using var fixture = new Fixture();
        var repository = ExcelProfileRepository(fixture);
        var head = ExcelProfileHead();
        head.IsDeleted = true;
        fixture.Connection.Rows.Columns.Add("version", typeof(int));
        fixture.Connection.Rows.Rows.Add(4);
        Assert.IsTrue(await repository.UpdateAsync(head, 3, default));
        Assert.AreEqual(4, head.Version);
        Assert.AreEqual(3, fixture.Connection.Values["ExpectedVersion"]);
        Assert.AreEqual(true, fixture.Connection.Values["IsDeleted"]);
        Assert.AreEqual("映射测试", fixture.Connection.Values["UpdatedBy"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types["UpdatedDate"]);
        StringAssert.Contains(fixture.Connection.Text, "version=version+1");
        StringAssert.Contains(fixture.Connection.Text, "uid=@Uid AND version=@ExpectedVersion AND is_deleted=FALSE RETURNING version");
        Assert.IsFalse(fixture.Connection.Text.Contains("group_code=", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("created_date=", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Connection.Rows.Rows.Clear();
        head.Version = 8;
        Assert.IsFalse(await repository.UpdateAsync(head, 3, default));
        Assert.AreEqual(8, head.Version);
        // 返回多行属于持久化结果异常，不能把其中一个版本误当成功回填。
        fixture.Connection.Rows.Rows.Add(9);
        fixture.Connection.Rows.Rows.Add(10);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.UpdateAsync(head, 8, default));
        Assert.AreEqual(8, head.Version);
    }

    /// <summary>字段新增保留身份，更新检查所属配置且只递增数据库版本，软删除沿用相同聚合边界。</summary>
    [TestMethod]
    public async Task ExcelProfile_SaveFieldPreservesAggregateIdentity()
    {
        using var fixture = new Fixture();
        var repository = ExcelProfileRepository(fixture);
        var field = ExcelProfileField();
        await repository.SaveFieldAsync(field, true, default);
        Assert.AreEqual(field.Uid, fixture.Connection.Values["Uid"]);
        Assert.AreEqual(field.ProfileUid, fixture.Connection.Values["ProfileUid"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["WorksheetName"]);
        Assert.AreEqual(true, fixture.Connection.Values["IsDownward"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types["CreatedDate"]);
        StringAssert.Contains(fixture.Connection.Text, "INSERT INTO public.excel_read_field");
        StringAssert.Contains(fixture.Connection.Text, "\"target_field_key\"");
        field.IsDeleted = true;
        await repository.SaveFieldAsync(field, false, default);
        Assert.AreEqual(true, fixture.Connection.Values["IsDeleted"]);
        Assert.AreEqual(1, field.Version);
        StringAssert.Contains(fixture.Connection.Text, "version=version+1");
        StringAssert.Contains(fixture.Connection.Text, "uid=@Uid AND profile_uid=@ProfileUid AND is_deleted=FALSE");
        Assert.IsFalse(fixture.Connection.Text.Contains("target_field_key=", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("version=@", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Connection.Commands.All(command => ReferenceEquals(fixture.Session.Transaction, command.Transaction)));
    }

    /// <summary>异常字段行数抛错交还外层回滚；提供者写入异常原样透传，仓储不提交或清理调用方事务。</summary>
    [TestMethod]
    public async Task ExcelProfile_WriteFailuresDoNotOwnTransaction()
    {
        using var fixture = new Fixture();
        var repository = ExcelProfileRepository(fixture);
        var transaction = fixture.Session.Transaction;
        foreach (var rows in new[] { 0, 2 })
        {
            fixture.Connection.ExecuteWrite = () => rows;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SaveFieldAsync(ExcelProfileField(), true, default));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SaveFieldAsync(ExcelProfileField(), false, default));
        }
        var failure = new InvalidOperationException("模拟提供者失败");
        fixture.Connection.ExecuteWrite = () => throw failure;
        var observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.InsertAsync(ExcelProfileHead(), default));
        Assert.AreSame(failure, observed);
        observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SaveFieldAsync(ExcelProfileField(), false, default));
        Assert.AreSame(failure, observed);
        Assert.AreSame(transaction, fixture.Session.Transaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>所有聚合写入与更新锁在无事务时发命令前拒绝，普通读取仍可调用。</summary>
    [TestMethod]
    public async Task ExcelProfile_RequiresTransactionForWritesAndLock()
    {
        using var fixture = new Fixture();
        var repository = ExcelProfileRepository(fixture);
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.FindAsync(Guid.NewGuid(), true, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.InsertAsync(ExcelProfileHead(), default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.UpdateAsync(ExcelProfileHead(), 3, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SaveFieldAsync(ExcelProfileField(), true, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SaveFieldAsync(ExcelProfileField(), false, default));
        Assert.HasCount(0, fixture.Connection.Commands);
        Assert.IsNull(await repository.FindAsync(Guid.NewGuid(), false, default));
        await repository.ListAsync("SalesOrder", default);
        await repository.GetFieldsAsync(Guid.NewGuid(), default);
        Assert.IsTrue(fixture.Connection.Commands.All(command => command.Transaction is null));
    }

    /// <summary>六方法向 Dapper 传递已取消令牌，避免执行命令；不把此替身测试当作运行中取消或实库回滚证据。</summary>
    [TestMethod]
    public async Task ExcelProfile_CanceledTokensDoNotExecuteCommands()
    {
        using var fixture = new Fixture();
        var repository = ExcelProfileRepository(fixture);
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.ListAsync("SalesOrder", token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.FindAsync(Guid.NewGuid(), true, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetFieldsAsync(Guid.NewGuid(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.InsertAsync(ExcelProfileHead(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.UpdateAsync(ExcelProfileHead(), 3, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SaveFieldAsync(ExcelProfileField(), true, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SaveFieldAsync(ExcelProfileField(), false, token));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>为 Excel 仓储提供既有夹具的同一会话、事务和审计服务。</summary>
    private static PostgreSqlExcelReadProfileRepository ExcelProfileRepository(Fixture fixture)
    {
        var user = new User();
        return new(new RepositoryBaseContext(fixture.Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
    }

    /// <summary>提供带显式版本的主配置，空列用于验证实体参数绑定。</summary>
    private static ExcelReadProfile ExcelProfileHead() => new()
        { GroupCode = "SalesOrder", ConfigName = "客户配置", Version = 3, EndMode = 2, SummaryCellAddress = "D30" };

    /// <summary>提供归属明确的字段，不依赖数据库自动生成身份。</summary>
    private static ExcelReadField ExcelProfileField() => new()
        { ProfileUid = Guid.NewGuid(), TargetFieldKey = "Line.Sku", CellAddress = "A8", IsDownward = true };
}
