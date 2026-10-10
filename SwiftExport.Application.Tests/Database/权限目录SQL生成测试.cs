using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Tools.DbSeed;

namespace SwiftExport.Application.Tests.Database;

/// <summary>离线校验可交付 SQL 的目录完整性及写入边界，不把文本核对当作 PG 实际执行验收。</summary>
[TestClass]
public sealed class PermissionCatalogSqlGenerationTests
{
    /// <summary>相同代码目录生成相同文件，所有条目及顺序完整，输出不依赖运行时间或连接环境。</summary>
    [TestMethod]
    public void Generation_IsDeterministicAndContainsWholeCatalog()
    {
        var sql = PermissionCatalogSqlGenerator.Generate();
        Assert.AreEqual(sql, PermissionCatalogSqlGenerator.Generate());
        Assert.IsFalse(sql.Contains('\r'));
        var rows = Regex.Matches(sql, @"^    \('([^']+)', '((?:[^']|'')*)', '((?:[^']|'')*)', (\d+)\)[,;]$", RegexOptions.Multiline);
        Assert.AreEqual(PermissionCatalog.All.Count, rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var definition = PermissionCatalog.All[index];
            Assert.AreEqual(definition.PermissionKey, rows[index].Groups[1].Value);
            Assert.AreEqual(definition.DisplayName, rows[index].Groups[2].Value.Replace("''", "'"));
            Assert.AreEqual(definition.GroupKey, rows[index].Groups[3].Value.Replace("''", "'"));
            Assert.AreEqual(index + 1, int.Parse(rows[index].Groups[4].Value));
        }
        Assert.IsFalse(sql.Contains("ConnectionStrings", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("SWIFTEXPORT_DBSEED_CONNECTION", StringComparison.Ordinal));
    }

    /// <summary>定义更新只能写三展示列；插入不得复活软删除定义，不存在账号或授权关系 DML。</summary>
    [TestMethod]
    public void Sql_PreservesExistingIdentityStatesAuditAndRelations()
    {
        var sql = PermissionCatalogSqlGenerator.Generate();
        var update = Regex.Match(sql, """UPDATE "public"\."sys_permission" AS p\s+SET (.*?)\s+FROM""", RegexOptions.Singleline);
        Assert.IsTrue(update.Success);
        CollectionAssert.AreEquivalent(new[] { "display_name", "group_key", "sort_order" },
            Regex.Matches(update.Groups[1].Value, "\"([^\"]+)\"\\s*=").Select(match => match.Groups[1].Value).ToArray());
        var inserts = Regex.Matches(sql, """INSERT INTO ("[^"\r\n]+"\."[^"\r\n]+"|pg_temp\.\w+)""");
        CollectionAssert.AreEquivalent(new[] { "pg_temp.swift_export_permission_catalog", "\"public\".\"sys_permission\"" },
            inserts.Select(match => match.Groups[1].Value).ToArray());
        Assert.IsFalse(Regex.IsMatch(sql, @"\b(DELETE|TRUNCATE|ALTER)\b"));
        Assert.IsFalse(sql.Contains("sys_user", StringComparison.Ordinal));
        StringAssert.Contains(sql, "WHERE NOT EXISTS (SELECT 1 FROM \"public\".\"sys_permission\" AS p WHERE p.\"permission_key\" COLLATE \"C\" = c.permission_key)");
        StringAssert.Contains(sql, "SELECT gen_random_uuid(), 1, FALSE");
        StringAssert.Contains(sql, "clock_timestamp() AT TIME ZONE 'UTC', 'DbSeed'");
        StringAssert.Contains(sql, "c.permission_key, c.display_name, c.group_key, c.sort_order, TRUE");
    }

    /// <summary>先按既有稳定锁序排队，再验证重复/拼写；所有 DML 位于同一完整事务内。</summary>
    [TestMethod]
    public void Sql_RejectsIdentityDriftBeforeDmlAndKeepsLockOrder()
    {
        var sql = PermissionCatalogSqlGenerator.Generate();
        var roleLock = sql.IndexOf("LOCK TABLE \"public\".\"sys_role\"", StringComparison.Ordinal);
        var permissionLock = sql.IndexOf("LOCK TABLE \"public\".\"sys_permission\"", StringComparison.Ordinal);
        var firstSnapshot = sql.IndexOf("CREATE TEMP TABLE", StringComparison.Ordinal);
        var duplicateGuard = sql.IndexOf("GROUP BY lower", StringComparison.Ordinal);
        var spellingGuard = sql.IndexOf("大小写与代码不一致", StringComparison.Ordinal);
        var update = sql.IndexOf("UPDATE \"public\".\"sys_permission\"", StringComparison.Ordinal);
        Assert.IsTrue(roleLock > 0 && roleLock < permissionLock && permissionLock < firstSnapshot);
        Assert.IsTrue(duplicateGuard > firstSnapshot && duplicateGuard < spellingGuard && spellingGuard < update);
        StringAssert.Contains(sql, "BEGIN TRANSACTION ISOLATION LEVEL SERIALIZABLE;");
        StringAssert.Contains(sql, "ON COMMIT DROP;");
        Assert.IsTrue(sql.EndsWith("COMMIT;\n", StringComparison.Ordinal));
    }
}
