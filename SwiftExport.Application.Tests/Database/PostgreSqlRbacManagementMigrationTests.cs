using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auth.Permissions;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过独立记录连接覆盖 RBAC 十九方法的映射、版本、锁顺序和事务；不以模拟替代 Serializable 并发验收。</summary>
[TestClass]
[TestCategory("PostgreSqlRbacOffline")]
public sealed class PostgreSqlRbacManagementMigrationTests
{
    /// <summary>两个列表返回 count 与 DTO，保留关键词通配、分页和 CI 顺序，不暴露凭据。</summary>
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public async Task Search_MapsPageAndPreservesWildcard(bool users, bool transaction)
    {
        using var f = new Fixture(transaction);
        var id = Guid.NewGuid();
        var rows = users ? Rows(new ManagedUserDto { Uid = id, UserName = "User", Version = 3, IsAdmin = true })
            : Rows(new ManagedRoleDto { Uid = id, RoleCode = "ROLE", Version = 4, IsSystem = false });
        f.Connection.Scalars.Enqueue(9);
        f.Connection.Readers.Enqueue([rows]);
        var request = new ManagedPageRequest { Keyword = "  %_\\x  ", PageIndex = 2, PageSize = 3 };
        if (users)
        {
            var result = await f.Repository.SearchUsersAsync(request, default);
            Assert.AreEqual(9, result.TotalCount);
            Assert.AreEqual(id, result.Items.Single().Uid);
            Assert.IsTrue(result.Items.Single().IsAdmin);
        }
        else
        {
            var result = await f.Repository.SearchRolesAsync(request, default);
            Assert.AreEqual(9, result.TotalCount);
            Assert.AreEqual(id, result.Items.Single().Uid);
            Assert.AreEqual("ROLE", result.Items.Single().RoleCode);
        }
        Assert.AreEqual(3, f.Connection.Values["Offset"]);
        Assert.AreEqual("%_\\x", f.Connection.Values["Keyword"]);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["IsActive"]);
        StringAssert.Contains(f.Connection.Text, "COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(f.Connection.Text, "ESCAPE ''");
        Assert.IsFalse(f.Connection.Text.Contains("WITH RECURSIVE", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("password_hash", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Commands[0].Sql, "SELECT COUNT(*)");
        Assert.IsFalse(f.Connection.Commands[1].Sql.Contains("SELECT COUNT(*)", StringComparison.Ordinal));
        Assert.AreEqual(f.Connection.Commands[0].Values["Keyword"], f.Connection.Commands[1].Values["Keyword"]);
        Assert.AreEqual(f.Connection.Commands[0].Values["IsActive"], f.Connection.Commands[1].Values["IsActive"]);
        AssertBoundary(f, 2);
    }

    /// <summary>字符组参数不进入 SQL；两个查询复用解析结果、原事务和有界去重状态，不使用正则或内存分页。</summary>
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task SearchBracket_UsesParameterizedBoundedStates(bool users, bool transaction)
    {
        using var f = new Fixture(transaction);
        f.Connection.Scalars.Enqueue(0);
        f.Connection.Readers.Enqueue([users ? Rows(new ManagedUserDto()) : Rows(new ManagedRoleDto())]);
        const string keyword = "x'[Aa]%[^b-d]_\\[[]];DROP TABLE sys_user;--";
        var request = new ManagedPageRequest { Keyword = keyword };
        if (users) await f.Repository.SearchUsersAsync(request, default);
        else await f.Repository.SearchRolesAsync(request, default);
        Assert.AreEqual(keyword, f.Connection.Values["Keyword"]);
        Assert.IsFalse(f.Connection.Text.Contains("DROP TABLE", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Text, "WITH RECURSIVE");
        StringAssert.Contains(f.Connection.Text, "UNION\n");
        StringAssert.Contains(f.Connection.Text, "state.group_index < jsonb_array_length");
        StringAssert.Contains(f.Connection.Text, "char_length(source.value)");
        StringAssert.Contains(f.Connection.Text, "CAST(@LikeGroups AS jsonb)");
        StringAssert.Contains(f.Connection.Text,
            "BETWEEN ((bounds.value ->> 'start') COLLATE public.erp_dictionary_ci) AND ((bounds.value ->> 'end') COLLATE public.erp_dictionary_ci)");
        Assert.IsFalse(f.Connection.Text.Contains("SIMILAR TO", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("~", StringComparison.Ordinal));
        Assert.AreEqual(f.Connection.Commands[0].Values["LikeGroups"], f.Connection.Commands[1].Values["LikeGroups"]);
        Assert.AreEqual(f.Connection.Commands[0].Values["LikeTail"], f.Connection.Commands[1].Values["LikeTail"]);
        using var json = JsonDocument.Parse((string)f.Connection.Values["LikeGroups"]!);
        Assert.AreEqual(3, json.RootElement.GetArrayLength());
        Assert.AreEqual("%x'", json.RootElement[0].GetProperty("prefix").GetString());
        Assert.IsTrue(json.RootElement[1].GetProperty("negated").GetBoolean());
        Assert.AreEqual("b", json.RootElement[1].GetProperty("ranges")[0].GetProperty("start").GetString());
        Assert.AreEqual("d", json.RootElement[1].GetProperty("ranges")[0].GetProperty("end").GetString());
        Assert.AreEqual("_\\", json.RootElement[2].GetProperty("prefix").GetString());
        Assert.AreEqual("[", json.RootElement[2].GetProperty("members")[0].GetString());
        Assert.AreEqual("];DROP TABLE sys_user;--%", f.Connection.Values["LikeTail"]);
        var firstFilter = f.Connection.Commands[0].Sql.Split(" WHERE ", 2)[1].TrimEnd(';');
        StringAssert.Contains(f.Connection.Commands[1].Sql, firstFilter);
        AssertBoundary(f, 2);
        // 输出记录连接实际捕获的完整命令，供授权的只读 PG EXPLAIN 绑定固定测试参数；本测试不连接数据库。
        foreach (var command in f.Connection.Commands)
            Console.WriteLine("M10_PG_PLAN:" + JsonSerializer.Serialize(new
            {
                sql = command.Sql,
                parameters = command.Values.ToDictionary(p => p.Key, p => p.Value is DBNull ? null : p.Value)
            }));
    }

    /// <summary>直接检查送库解析参数，保留空/未闭合、反组、连字符及多组边界；不把模拟解析声称为数据库匹配验证。</summary>
    [TestMethod]
    [DataRow("[ab]", false, "ab", 0, "%")]
    [DataRow("[^abc]", true, "abc", 0, "%")]
    [DataRow("[^]", true, "", 0, "%")]
    [DataRow("[]", false, "", 0, "%")]
    [DataRow("[]]", false, "", 0, "]%")]
    [DataRow("[", false, "", 0, "")]
    [DataRow("[^", false, "", 0, "")]
    [DataRow("[[]", false, "[", 0, "%")]
    [DataRow("[a-]", false, "a-", 0, "%")]
    [DataRow("[-a]", false, "-a", 0, "%")]
    [DataRow("[a^]", false, "a^", 0, "%")]
    [DataRow("[a-c]", false, "", 1, "%")]
    [DataRow("[c-a]", false, "", 1, "%")]
    [DataRow("[z-ab]", false, "b", 1, "%")]
    [DataRow("[a-b-c]", false, "c", 1, "%")]
    [DataRow("[a-c-e]", false, "e", 1, "%")]
    [DataRow("[a-b-c-e]", false, "", 2, "%")]
    [DataRow("[a-b--c]", false, "", 1, "%")]
    [DataRow("[a-b---]", false, "", 1, "%")]
    [DataRow("[a-b--]", false, "-", 1, "%")]
    [DataRow("[a-b--cd]", false, "d", 1, "%")]
    [DataRow("[a-b---cd]", false, "cd", 1, "%")]
    [DataRow("[a-b--c-e]", false, "e", 1, "%")]
    [DataRow("[éE]", false, "éE", 0, "%")]
    public async Task SearchBracket_ParsesSourceBoundaries(string keyword, bool negated, string members, int ranges, string tail)
    {
        using var f = new Fixture(false);
        f.Connection.Scalars.Enqueue(0);
        f.Connection.Readers.Enqueue([Rows(new ManagedUserDto())]);
        await f.Repository.SearchUsersAsync(new ManagedPageRequest { Keyword = keyword }, default);
        using var json = JsonDocument.Parse((string)f.Connection.Values["LikeGroups"]!);
        var group = json.RootElement[0];
        Assert.AreEqual(negated, group.GetProperty("negated").GetBoolean());
        Assert.AreEqual(members, string.Concat(group.GetProperty("members").EnumerateArray().Select(m => m.GetString())));
        Assert.AreEqual(ranges, group.GetProperty("ranges").GetArrayLength());
        Assert.AreEqual("%", group.GetProperty("prefix").GetString());
        Assert.AreEqual(tail, f.Connection.Values["LikeTail"]);
        AssertBoundary(f, 2);
    }

    /// <summary>用户/角色普通及锁读取使用明确别名，锁定分支只锁目标表且传递已有事务。</summary>
    [TestMethod]
    [DataRow("user", false)]
    [DataRow("user", true)]
    [DataRow("role", false)]
    [DataRow("role", true)]
    public async Task Read_MapsDtoAndLockScope(string kind, bool locked)
    {
        using var f = new Fixture();
        var id = Guid.NewGuid();
        if (kind == "user")
        {
            f.Connection.Readers.Enqueue([Rows(new ManagedUserDto
                { Uid = id, Version = 4, UserName = "U", NickName = "昵称", IsActive = false, IsAdmin = false })]);
            var row = locked ? await f.Repository.GetUserForUpdateAsync(id, default) : await f.Repository.GetUserAsync(id, default);
            Assert.IsNotNull(row);
            Assert.AreEqual(id, row.Uid); Assert.AreEqual(4, row.Version); Assert.AreEqual("昵称", row.NickName);
            Assert.IsNull(row.Email); Assert.IsFalse(row.IsActive);
        }
        else
        {
            f.Connection.Readers.Enqueue([Rows(new ManagedRoleDto
                { Uid = id, Version = 2, RoleCode = "R", RoleName = "角色", IsActive = true, IsSystem = true })]);
            var row = locked ? await f.Repository.GetRoleForUpdateAsync(id, default) : await f.Repository.GetRoleAsync(id, default);
            Assert.IsNotNull(row);
            Assert.AreEqual(id, row.Uid); Assert.AreEqual(2, row.Version); Assert.IsTrue(row.IsSystem);
        }
        Assert.AreEqual(locked, f.Connection.Text.Contains("FOR UPDATE OF", StringComparison.Ordinal));
        AssertBoundary(f, 1);
    }

    /// <summary>有效角色和权限使用 UUID 数组；权限可分配标志忽略输入数据库值，按目录重新计算。</summary>
    [TestMethod]
    [DataRow("roles")]
    [DataRow("permissions")]
    [DataRow("catalog")]
    public async Task ActiveSets_UseArraysAndCatalogAssignment(string operation)
    {
        using var f = new Fixture();
        var id = Guid.NewGuid();
        if (operation == "roles")
        {
            f.Connection.Readers.Enqueue([Rows(new ManagedRoleDto { Uid = id, RoleCode = "R", IsActive = true })]);
            Assert.AreEqual(id, (await f.Repository.GetActiveRolesAsync([id], default)).Single().Uid);
            CollectionAssert.AreEqual(new[] { id }, (Guid[])f.Connection.Values["RoleIds"]!);
        }
        else
        {
            var known = PermissionCatalog.All.First(item => item.IsAssignable);
            f.Connection.Readers.Enqueue([Rows(new ManagedPermissionDto
                { Uid = id, PermissionKey = known.PermissionKey, IsAssignable = false, IsActive = true })]);
            var result = operation == "catalog" ? await f.Repository.GetPermissionsAsync(default)
                : await f.Repository.GetActivePermissionsAsync([id], default);
            Assert.IsTrue(result.Single().IsAssignable);
            if (operation == "permissions") CollectionAssert.AreEqual(new[] { id }, (Guid[])f.Connection.Values["PermissionIds"]!);
            f.Connection.Readers.Enqueue([Rows(new ManagedPermissionDto { PermissionKey = "__unknown__", IsAssignable = true })]);
            Assert.IsFalse((await f.Repository.GetPermissionsAsync(default)).Single().IsAssignable);
        }
        if (operation != "catalog") StringAssert.Contains(f.Connection.Commands[0].Sql, "FOR SHARE OF");
        AssertBoundary(f, operation == "roles" ? 1 : 2);
    }

    /// <summary>空集合不产生锁或 SQL；有效关系查询仍按标识排序和活动定义过滤。</summary>
    [TestMethod]
    public async Task RelationsAndEmptySets_KeepFilters()
    {
        using var f = new Fixture();
        Assert.HasCount(0, await f.Repository.GetActiveRolesAsync([], default));
        Assert.HasCount(0, await f.Repository.GetActivePermissionsAsync([], default));
        Assert.HasCount(0, f.Connection.Commands);
        var id = Guid.NewGuid();
        f.Connection.Readers.Enqueue([Rows(id)]);
        CollectionAssert.AreEqual(new[] { id }, (await f.Repository.GetUserRoleIdsAsync(Guid.NewGuid(), default)).ToArray());
        StringAssert.Contains(f.Connection.Text, "ORDER BY ur.role_id");
        f.Connection.Readers.Enqueue([Rows(id)]);
        CollectionAssert.AreEqual(new[] { id }, (await f.Repository.GetRolePermissionIdsAsync(Guid.NewGuid(), default)).ToArray());
        StringAssert.Contains(f.Connection.Text, "p.is_active = TRUE");
        AssertBoundary(f, 2);
    }

    /// <summary>管理员锁验先角色表锁再角色、actor、关系；首条命令不读取数据，短路不再查目标。</summary>
    [TestMethod]
    [DataRow("role-missing", false, 2)]
    [DataRow("actor-missing", false, 3)]
    [DataRow("relation-missing", false, 4)]
    [DataRow("valid", true, 4)]
    public async Task Administrator_LocksRoleBeforeActor(string path, bool expected, int commands)
    {
        using var f = new Fixture();
        var actor = Guid.NewGuid();
        var role = Guid.NewGuid();
        f.Connection.Readers.Enqueue([path == "role-missing" ? EmptyId() : Rows(role)]);
        if (path != "role-missing") f.Connection.Readers.Enqueue([path == "actor-missing" ? EmptyId() : Rows(actor)]);
        f.Connection.Scalars.Enqueue(expected);
        using var cancellation = new CancellationTokenSource();
        Assert.AreEqual(expected, await f.Repository.LockAndCheckAdministratorAsync(actor, cancellation.Token));
        Assert.AreEqual("LOCK TABLE \"public\".\"sys_role\" IN SHARE ROW EXCLUSIVE MODE;", f.Connection.Commands[0].Sql);
        Assert.HasCount(0, f.Connection.Commands[0].Values);
        StringAssert.Contains(f.Connection.Commands[1].Sql, "FOR UPDATE OF r");
        if (commands > 2)
        {
            StringAssert.Contains(f.Connection.Commands[2].Sql, "FOR UPDATE OF actor");
            Assert.AreEqual(actor, f.Connection.Commands[2].Values["ActorUserId"]);
        }
        if (commands == 4) Assert.AreEqual(role, f.Connection.Commands[3].Values["AdminRoleId"]);
        Assert.AreEqual(cancellation.Token, f.Connection.LastCancellation);
        AssertBoundary(f, commands);
    }

    /// <summary>角色表等待命令取消或失败时不再查询管理员；原事务留给调用方释放，不吞提供者异常。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Administrator_TableLockFailureStopsBeforeSnapshot(bool cancelled)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        if (cancelled)
        {
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                f.Repository.LockAndCheckAdministratorAsync(Guid.NewGuid(), cancellation.Token));
        }
        else
        {
            f.Connection.BeforeExecute = sql =>
            {
                StringAssert.StartsWith(sql, "LOCK TABLE");
                throw new InvalidOperationException("锁等待失败");
            };
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                f.Repository.LockAndCheckAdministratorAsync(Guid.NewGuid(), cancellation.Token));
        }
        Assert.AreEqual(cancellation.Token, f.Connection.LastCancellation);
        AssertBoundary(f, 0);
    }

    /// <summary>两个计数保留管理员去重与未删除关系计数，角色计数不排除停用用户。</summary>
    [TestMethod]
    public async Task Counts_KeepAdministratorAndRelationshipMeaning()
    {
        using var f = new Fixture();
        f.Connection.Scalars.Enqueue(2);
        Assert.AreEqual(2, await f.Repository.CountActiveAdministratorsAsync(default));
        StringAssert.Contains(f.Connection.Text, "COUNT(DISTINCT u.uid)");
        f.Connection.Scalars.Enqueue(3);
        Assert.AreEqual(3, await f.Repository.CountRoleUsersAsync(Guid.NewGuid(), default));
        Assert.IsFalse(f.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        AssertBoundary(f, 2);
    }

    /// <summary>资料、启停和软删更新均绑定预期版本、可空参数与 timestamp，零行返回false。</summary>
    [TestMethod]
    [DataRow("user", 1)]
    [DataRow("user", 0)]
    [DataRow("role", 1)]
    [DataRow("role", 0)]
    public async Task Updates_KeepVersionAuditAndNullBranches(string operation, int rows)
    {
        using var f = new Fixture();
        f.Connection.ExecuteWrite = () => rows;
        var id = Guid.NewGuid();
        var actual = operation == "user"
            ? await f.Repository.UpdateUserAsync(id, 7, "昵称", null, null, null, default)
            : await f.Repository.UpdateRoleAsync(id, 7, null, null, false, true, default);
        Assert.AreEqual(rows == 1, actual);
        Assert.AreEqual(7, f.Connection.Values["ExpectedVersion"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, f.Connection.Types["UpdatedDate"]);
        Assert.AreEqual("映射测试", f.Connection.Values["UpdatedBy"]);
        StringAssert.Contains(f.Connection.Text, "version = version + 1");
        if (operation == "role") StringAssert.Contains(f.Connection.Text, "is_system = FALSE");
        else StringAssert.Contains(f.Connection.Text, "CAST(@IsActive AS boolean) IS NULL");
        AssertBoundary(f, 1);
    }

    /// <summary>两种整组替换先版本更新、软删，再按原集合插入；空集合清除且失败版本不继续写关系。</summary>
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public async Task Replacement_PreservesWriteOrderAndEmptyClear(bool userRoles, bool empty)
    {
        using var f = new Fixture();
        var aggregate = Guid.NewGuid();
        Guid[] ids = empty ? [] : [Guid.NewGuid(), Guid.NewGuid()];
        if (userRoles) await f.Repository.ReplaceUserRolesAsync(aggregate, 2, ids, default);
        else await f.Repository.ReplaceRolePermissionsAsync(aggregate, 2, ids, default);
        AssertBoundary(f, 2 + ids.Length);
        StringAssert.Contains(f.Connection.Commands[0].Sql, "version = version + 1");
        StringAssert.Contains(f.Connection.Commands[1].Sql, "is_deleted = TRUE");
        for (var i = 0; i < ids.Length; i++)
        {
            var insert = f.Connection.Commands[i + 2];
            Assert.AreEqual(ids[i], insert.Values[userRoles ? "RoleId" : "PermissionId"]);
            Assert.AreEqual(aggregate, insert.Values[userRoles ? "UserId" : "RoleId"]);
            Assert.AreEqual(1, insert.Values["Version"]);
            Assert.AreEqual(false, insert.Values["IsDeleted"]);
            Assert.AreNotEqual(Guid.Empty, insert.Values["Uid"]);
        }
    }

    /// <summary>陈旧版本和中途异常由外层回滚，仓储保留异常、不自行提交或清空事务。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Replacement_StopsOnStaleVersion(bool users)
    {
        using var f = new Fixture();
        f.Connection.ExecuteWrite = () => 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => users
            ? f.Repository.ReplaceUserRolesAsync(Guid.NewGuid(), 1, [Guid.NewGuid()], default)
            : f.Repository.ReplaceRolePermissionsAsync(Guid.NewGuid(), 1, [Guid.NewGuid()], default));
        AssertBoundary(f, 1);
    }

    /// <summary>创建普通角色绑定默认活动/非内置/未删除和生成标识；唯一异常不得吞掉。</summary>
    [TestMethod]
    public async Task CreateRole_BindsDefaultsAndPropagatesUnique()
    {
        using var f = new Fixture();
        var id = await f.Repository.CreateRoleAsync("NORMAL", "普通", null, default);
        Assert.AreEqual(id, f.Connection.Values["Uid"]);
        Assert.AreEqual(false, f.Connection.Values["IsSystem"]);
        Assert.AreEqual(true, f.Connection.Values["IsActive"]);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["Description"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, f.Connection.Types["CreatedDate"]);
        f.Connection.BeforeExecute = _ => throw new PostgresException("专用冲突", "ERROR", "ERROR", "23505");
        await Assert.ThrowsExactlyAsync<PostgresException>(() => f.Repository.CreateRoleAsync("NORMAL", "普通", null, default));
        AssertBoundary(f, 1);
    }

    /// <summary>无事务拒绝所有写入、锁验和聚合更新锁，不向数据库发送命令。</summary>
    [TestMethod]
    public async Task TransactionGuard_RejectsWritesAndLocks()
    {
        using var f = new Fixture(false);
        var id = Guid.NewGuid();
        Func<Task>[] calls =
        [
            () => f.Repository.LockAndCheckAdministratorAsync(id, default),
            () => f.Repository.CountActiveAdministratorsAsync(default),
            () => f.Repository.GetUserForUpdateAsync(id, default),
            () => f.Repository.GetRoleForUpdateAsync(id, default),
            () => f.Repository.UpdateUserAsync(id, 1, null, null, null, false, default),
            () => f.Repository.UpdateRoleAsync(id, 1, null, null, false, false, default),
            () => f.Repository.ReplaceUserRolesAsync(id, 1, [], default),
            () => f.Repository.ReplaceRolePermissionsAsync(id, 1, [], default),
            () => f.Repository.CreateRoleAsync("R", "R", null, default)
        ];
        foreach (var call in calls) await Assert.ThrowsExactlyAsync<InvalidOperationException>(call);
        AssertBoundary(f, 0);
    }

    /// <summary>取消在实际执行前阻断；令牌不丢失且本层不回滚调用方事务。</summary>
    [TestMethod]
    public async Task Cancellation_StopsProviderCommand()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            f.Repository.UpdateRoleAsync(Guid.NewGuid(), 1, "取消", null, null, false, cancellation.Token));
        Assert.AreEqual(cancellation.Token, f.Connection.LastCancellation);
        AssertBoundary(f, 0);
    }

    /// <summary>确认所有命令使用原事务，没有新增事务或自行提交、回滚、释放。</summary>
    private static void AssertBoundary(Fixture f, int commands)
    {
        Assert.HasCount(commands, f.Connection.Commands);
        foreach (var command in f.Connection.Commands) Assert.AreSame(f.Session.Transaction, command.Transaction);
        Assert.AreEqual(f.Session.Transaction is null ? 0 : 1, f.Connection.Begins);
        Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.Connection.TransactionDisposals);
    }

    /// <summary>构造 DTO 或标量结果表，允许真实 Dapper 处理可空值与多结果。</summary>
    private static DataTable Rows<T>(T value)
    {
        var rows = new DataTable();
        if (typeof(T) == typeof(Guid) || typeof(T) == typeof(int))
        { rows.Columns.Add("Value", typeof(T)); rows.Rows.Add(value); return rows; }
        var properties = typeof(T).GetProperties();
        foreach (var property in properties) rows.Columns.Add(property.Name, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
        rows.Rows.Add(properties.Select(p => p.GetValue(value) ?? DBNull.Value).ToArray());
        return rows;
    }

    /// <summary>模拟缺失锁定角色或操作者的零行。</summary>
    private static DataTable EmptyId()
    { var rows = new DataTable(); rows.Columns.Add("uid", typeof(Guid)); return rows; }

    /// <summary>每个测试独占模拟 Serializable 会话，保持夹具独立且无网络。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlRbacManagementRepository Repository;
        public Fixture(bool transaction = true)
        {
            Session = new DbSession(new Factory(Connection));
            if (transaction) Session.Transaction = Connection.BeginTransaction(IsolationLevel.Serializable);
            var user = new User();
            Repository = new(new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>向会话注入不连接网络的测试连接。</summary>
    private sealed class Factory(RecordingConnection connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>提供审计身份。</summary>
    private sealed class User : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;
        public string UserName => "映射测试";
        public bool IsAuthenticated => true;
    }

    /// <summary>记录 Dapper 实际发出的命令及参数，数据读取由测试显式提供。</summary>
    private sealed class RecordingConnection : DbConnection
    {
        public string Text = "";
        public Dictionary<string, object?> Values = new();
        public Dictionary<string, NpgsqlTypes.NpgsqlDbType> Types = new();
        public DbTransaction? LastTransaction;
        public readonly List<(string Sql, Dictionary<string, object?> Values, DbTransaction? Transaction)> Commands = [];
        public readonly Queue<object> Scalars = new();
        public readonly Queue<DataTable[]> Readers = new();
        public CancellationToken LastCancellation;
        public Action<string>? BeforeExecute;
        public int Begins;
        public int Commits;
        public int Rollbacks;
        public int TransactionDisposals;
        public Func<int>? ExecuteWrite;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "in-memory-test";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new Transaction(this); }
        protected override DbCommand CreateDbCommand() => new Command(this);

        /// <summary>仅提供事务身份，不执行提交或回滚到数据库。</summary>
        private sealed class Transaction(RecordingConnection connection) : DbTransaction
        {
            public override IsolationLevel IsolationLevel => IsolationLevel.Serializable;
            protected override DbConnection DbConnection => connection;
            public override void Commit() => connection.Commits++;
            public override void Rollback() => connection.Rollbacks++;
            protected override void Dispose(bool disposing)
            { if (disposing) connection.TransactionDisposals++; base.Dispose(disposing); }
        }

        /// <summary>使用提供者参数容器兼容 Dapper 参数绑定，但完全接管执行，禁止网络访问。</summary>
        private sealed class Command(RecordingConnection connection) : DbCommand
        {
            private readonly NpgsqlCommand parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
            protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery() { Capture(); return connection.ExecuteWrite?.Invoke() ?? 1; }
            public override object ExecuteScalar() { Capture(); return connection.Scalars.Dequeue(); }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            { Capture(); return new DataTableReader(connection.Readers.Dequeue()); }
            public override Task<int> ExecuteNonQueryAsync(CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteNonQuery()); }
            public override Task<object?> ExecuteScalarAsync(CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult<object?>(ExecuteScalar()); }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteDbDataReader(behavior)); }
            private void Capture()
            {
                connection.BeforeExecute?.Invoke(CommandText);
                connection.Text = CommandText;
                connection.Types = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => p.NpgsqlDbType);
                connection.LastTransaction = DbTransaction;
                connection.Values = parameters.Parameters.Cast<NpgsqlParameter>()
                    .ToDictionary(parameter => parameter.ParameterName, parameter => (object?)parameter.Value);
                connection.Commands.Add((CommandText, connection.Values, DbTransaction));
            }
            protected override void Dispose(bool disposing)
            { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}


