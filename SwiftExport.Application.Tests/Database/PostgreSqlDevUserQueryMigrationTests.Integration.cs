using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auth.Development;

namespace SwiftExport.Application.Tests.Database;

/// <summary>固定failure库真实开发身份查询；仅父RC内随机合法账号，finally回滚并按UID确认零残留。</summary>
[TestClass, DoNotParallelize]
public sealed class PostgreSqlDevUserQueryMigrationIntegrationTests
{
    /// <summary>输出无凭据夹具UID和回滚策略。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>资格、缺失及ASCII比较遵循原读取边界；不写既有管理员、角色、目录或回执。</summary>
    [TestMethod, TestCategory("PostgreSqlDevUserQueryIntegration")]
    [DataRow("active")]
    [DataRow("inactive")]
    [DataRow("deleted")]
    [DataRow("missing")]
    [DataRow("case")]
    [DataRow("stored-trailing")]
    [DataRow("input-trailing")]
    [DataRow("leading-preserved")]
    [DataRow("leading-mismatch")]
    [DataRow("tab-mismatch")]
    [DataRow("null")]
    [DataRow("injection")]
    public async Task NativeLookup_PreservesEligibilityAndName(string mode)
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var settings = new NpgsqlConnectionStringBuilder(connection);
        Assert.IsFalse(settings.Multiplexing); Assert.IsFalse(settings.NoResetOnClose);
        var uid = Guid.NewGuid(); var name = "P06DU" + uid.ToString("N");
        var stored = mode == "stored-trailing" ? name + "   " : mode == "leading-preserved" ? " " + name : name;
        string? input = mode switch
        {
            "missing" => "missing" + Guid.NewGuid().ToString("N"),
            "case" => name.ToLowerInvariant(),
            "input-trailing" => name + "   ",
            "leading-preserved" => stored,
            "leading-mismatch" => " " + name,
            "tab-mismatch" => name + "\t",
            "null" => null,
            "injection" => name + "' OR TRUE;--",
            _ => name
        };
        TestContext.WriteLine(JsonSerializer.Serialize(new { Phase = "初始化前", Database = settings.Database, User = uid, Mode = mode, Policy = "父RC回滚并精确UID确认零残留" }));
        using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
        try
        {
            // 仅本例新账号的随机BCrypt哈希，不查询或输出任何既有凭据。
            var parameters = new DynamicParameters();
            parameters.Add("Uid", uid, DbType.Guid); parameters.Add("UserName", stored, DbType.String);
            parameters.Add("PasswordHash", BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")), DbType.String);
            parameters.Add("Active", mode != "inactive", DbType.Boolean); parameters.Add("Deleted", mode == "deleted", DbType.Boolean);
            Assert.AreEqual(1, await session.Connection.ExecuteAsync("""
                INSERT INTO public.sys_user
                    (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,
                     user_name,nick_name,password_hash,credential_version,email,phone_number,is_active)
                VALUES (@Uid,1,@Deleted,NULL,NULL,NULL,NULL,@UserName,NULL,@PasswordHash,1,NULL,NULL,@Active)
                """, parameters, transaction));
            var result = await new PostgreSqlDevUserQuery(session).FindUidAsync(input!);
            var found = mode is "active" or "case" or "stored-trailing" or "input-trailing" or "leading-preserved";
            Assert.AreEqual(found ? (Guid?)uid : null, result);
            Assert.AreSame(transaction, session.Transaction);
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM public.sys_user WHERE uid=@Uid", new { Uid = uid }, transaction));
        }
        finally
        {
            // 即使查询异常或断言失败也先回滚；不以DELETE掩盖未知提交或不可回滚错误。
            try { transaction.Rollback(); }
            finally { session.Transaction = null; }
            using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM public.sys_user WHERE uid=@Uid", new { Uid = uid }));
            TestContext.WriteLine(JsonSerializer.Serialize(new { Phase = "回滚后零残留", User = uid }));
        }
    }

    /// <summary>空字符串/纯空格输入不默认dev；存在既有同名活动账号时不读其身份，改为未运行。</summary>
    [TestMethod, TestCategory("PostgreSqlDevUserQueryIntegration")]
    [DataRow("")]
    [DataRow("   ")]
    public async Task BlankInput_IsNotDefaulted(string input)
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var settings = new NpgsqlConnectionStringBuilder(connection);
        Assert.IsFalse(settings.Multiplexing); Assert.IsFalse(settings.NoResetOnClose);
        var uid = Guid.NewGuid(); var name = "P06DU" + uid.ToString("N");
        TestContext.WriteLine(JsonSerializer.Serialize(new { Phase = "空白用例初始化前", Database = settings.Database, User = uid, Policy = "父RC回滚并精确UID确认零残留" }));
        using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
        // 不假定真实库没有空白活动账号；只计数，不获取其UID或凭据。
        if (await session.Connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM public.sys_user WHERE rtrim(user_name,' ')='' AND is_active=TRUE AND COALESCE(is_deleted,FALSE)=FALSE)"))
            Assert.Inconclusive("已有空白活动账号；不使用既有身份验证本例。");
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
        try
        {
            Assert.AreEqual(1, await session.Connection.ExecuteAsync("""
                INSERT INTO public.sys_user(uid,version,is_deleted,user_name,password_hash,credential_version,is_active)
                VALUES (@Uid,1,FALSE,@Name,@Hash,1,TRUE)
                """, new { Uid = uid, Name = name, Hash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")) }, transaction));
            Assert.IsNull(await new PostgreSqlDevUserQuery(session).FindUidAsync(input));
        }
        finally
        {
            try { transaction.Rollback(); } finally { session.Transaction = null; }
            using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_user WHERE uid=@Uid", new { Uid = uid }));
            TestContext.WriteLine(JsonSerializer.Serialize(new { Phase = "回滚后零残留", User = uid }));
        }
    }
}

