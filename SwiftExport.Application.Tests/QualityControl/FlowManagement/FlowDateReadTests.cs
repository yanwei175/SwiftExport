using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.QualityControl.FlowManagement;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.QualityControl.FlowManagement;

/// <summary>在显式指定的 PostgreSQL 上只读验证真实日期类型和流量仓储，不初始化、修改或清理数据。</summary>
[TestClass]
public sealed class FlowDateReadTests
{
    /// <summary>分页、详情及维护前读取共享正确日期投影，保持午夜及无时区语义。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlReadOnly")]
    public async Task RealDateColumnMapsAcrossAllHeaderReads()
    {
        var configured = Environment.GetEnvironmentVariable("SWIFTEXPORT_FLOW_READONLY_PG");
        if (string.IsNullOrWhiteSpace(configured)) Assert.Inconclusive("未显式配置流量只读 PostgreSQL 连接。");
        var builder = new NpgsqlConnectionStringBuilder(configured)
        {
            Pooling = false,
            // 连接建立时即强制只读，测试代码也不能意外进行业务写入。
            Options = "-c default_transaction_read_only=on"
        };
        using var session = new DbSession(new PostgreSqlConnectionFactory(builder.ConnectionString));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.RepeatableRead);
        session.Transaction = transaction;
        Assert.AreEqual("on", await session.Connection.ExecuteScalarAsync<string>("SHOW transaction_read_only", transaction: transaction));
        var user = new ReadUser();
        var repository = new PostgreSqlFlowRepository(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        var page = await repository.GetPagedAsync(new FlowSearchRequest { PageIndex = 1, PageSize = 1 }, default);
        if (page.Items.Count == 0) Assert.Inconclusive("当前库没有可读取的测试组；本测试不会创建数据。");
        var item = page.Items[0];
        var rawDate = await session.Connection.ExecuteScalarAsync<DateOnly>(
            "SELECT test_date FROM public.flow_header WHERE uid=@Id", new { Id = item.Uid }, transaction);
        var expected = rawDate.ToDateTime(TimeOnly.MinValue);
        Assert.AreEqual(expected, item.TestDate);
        Assert.AreEqual(DateTimeKind.Unspecified, item.TestDate.Kind);
        var detail = await repository.GetDetailAsync(item.Uid, default);
        Assert.IsNotNull(detail); Assert.AreEqual(expected, detail.TestDate);
        var header = await repository.ReadHeaderAsync(item.Uid, false, default);
        Assert.IsNotNull(header); Assert.AreEqual(expected, header.TestDate);
        Assert.AreEqual("on", await session.Connection.ExecuteScalarAsync<string>("SHOW transaction_read_only", transaction: transaction));
    }

    /// <summary>提供仓储依赖所需的只读审计身份；本测试不调用维护方法。</summary>
    private sealed class ReadUser : ICurrentUser
    {
        /// <summary>标识只读测试会话，不用于业务写入。</summary>
        public string UserName => "flow-date-read-only";
        /// <summary>不模拟实际业务用户身份。</summary>
        public Guid? UserId => null;
        /// <summary>满足仓储审计依赖，不经过 HTTP 授权。</summary>
        public bool IsAuthenticated => true;
    }
}
