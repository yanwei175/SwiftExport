using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace SwiftExport.Application.Tests.Database;

/// <summary>限制集成测试只连接用户确认的测试库；不提供默认凭据，不初始化或清理数据库。</summary>
internal static class PostgreSqlIntegrationEnvironment
{
    /// <summary>仅接受已明确批准的故障隔离库或本轮入库隔离库，不能降级到原库、备份或任意新库。</summary>
    internal static string GetFailureConnectionString()
    {
        var connection = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_FAILURE_TESTS") != "1"
            || string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("未配置已保留的专用测试库；本项未运行。");
        NpgsqlConnectionStringBuilder settings;
        try { settings = new NpgsqlConnectionStringBuilder(connection); }
        catch (ArgumentException)
        {
            Assert.Fail("专用故障测试连接格式无效。");
            throw;
        }
        Assert.AreEqual("192.168.3.12", settings.Host);
        Assert.AreEqual(5432, settings.Port);
        Assert.IsTrue(settings.Database is "dv_next_pg_failure_tests_20261004" or "dv_next_pg_inbound_tests_20261021", "数据库必须属于已明确批准的隔离白名单。");
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        return connection!;
    }

    /// <summary>提交回执的实库场景只允许用户批准的专用库；与原 dv_next 测试入口严格分离。</summary>
    internal static string GetCommandConnectionString()
    {
        var connection = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_COMMAND_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_COMMAND_TESTS") != "1"
            || string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("未显式配置本批专用命令测试库；本项未运行。");
        NpgsqlConnectionStringBuilder settings;
        try { settings = new NpgsqlConnectionStringBuilder(connection); }
        catch (ArgumentException)
        {
            Assert.Fail("专用命令测试连接格式无效。");
            throw;
        }
        Assert.AreEqual("192.168.3.12", settings.Host, "专用测试实例不匹配。");
        Assert.AreEqual(5432, settings.Port, "专用测试端口不匹配。");
        Assert.AreEqual("dv_next_pg_command_tests_20261004", settings.Database, "提交回执测试必须使用本批获批专用库。");
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public", "专用测试不能替换 schema。");
        return connection!;
    }

    /// <summary>读取显式启用的测试连接并校验目标；未配置时标记未运行，目标不符时失败且不输出连接内容。</summary>
    internal static string GetConnectionString()
    {
        var connection = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_TESTS") != "1"
            || string.IsNullOrWhiteSpace(connection))
        {
            Assert.Inconclusive("未配置已授权的 PostgreSQL 集成测试连接或未显式启用；本项未运行。");
        }

        NpgsqlConnectionStringBuilder settings;
        try
        {
            settings = new NpgsqlConnectionStringBuilder(connection);
        }
        catch (ArgumentException)
        {
            // 驱动解析错误可能携带输入片段，只返回固定诊断，避免泄露连接密钥。
            Assert.Fail("PostgreSQL 测试连接配置格式无效。");
            throw;
        }

        Assert.AreEqual("192.168.3.12", settings.Host, "测试连接必须指向已授权实例。");
        Assert.AreEqual(5432, settings.Port, "测试连接必须使用已授权端口。");
        Assert.AreEqual("dv_next", settings.Database, "测试连接必须指向已授权测试库。");
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath)
            || settings.SearchPath == "public", "测试连接不允许覆盖为其他 schema。");
        return connection!;
    }
}
