using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace SwiftExport.Application.Tests.Database;

/// <summary>限制集成测试只连接用户确认的测试库；不提供默认凭据，不初始化或清理数据库。</summary>
internal static class PostgreSqlIntegrationEnvironment
{
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
