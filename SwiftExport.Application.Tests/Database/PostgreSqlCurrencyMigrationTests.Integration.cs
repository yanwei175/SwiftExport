using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Systems.Currencies;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Core.Entities.Systems.Currencies;
using SwiftExport.Infrastructure.Repositories.Systems.Currencies;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Core.Entities.Systems.ExchangeRates;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Systems.ExchangeRates;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证真实PG汇率查询和本位币切换；切换仅使用获批单库，其他场景仍由父事务回滚。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlCurrencyMigrationIntegrationTests
{
    /// <summary>将切换场景关联到单库的完整事实和结构导出。</summary>
    public TestContext TestContext { get; set; } = default!;

    /// <summary>真实服务切换后全部汇率为1:1且提示重设；旧版本拒绝时完整回滚并允许同会话继续切换。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BaseCurrencySwitch_ResetsRatesAndRemindsUser(bool rejectStaleVersionFirst)
    {
        await using var database = await PostgreSqlLegacyTestDatabase.AcquireAsync(TestContext);
        using var session = new DbSession(new PostgreSqlConnectionFactory(database.ConnectionString));
        var user = new CurrencyIntegrationUser("base-switch-test");
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var repository = new PostgreSqlSysDicCurrencyRepository(context);
        var service = new SysDicCurrencyService(repository, new ServiceBaseContext(session, NullLoggerFactory.Instance),
            new PostgreSqlSysDicCurrencyExchangeRepository(context));
        var currencies = new[]
        {
            new SysDicCurrency { CurrencyCode = "CNY", IsBase = true, RateToBase = 1m },
            new SysDicCurrency { CurrencyCode = "USD", IsEnabled = false, RateToBase = 7.2m },
            new SysDicCurrency { CurrencyCode = "EUR", IsEnabled = false, RateToBase = 0.88m },
            new SysDicCurrency { CurrencyCode = "GBP", IsDeleted = true, RateToBase = 5.6m }
        };
        foreach (var currency in currencies) Assert.IsTrue((await repository.AddAsync(currency)).Success);
        // 通用新增会归一化IsDeleted=false，显式准备本测试自有软删行，避免把有效行误当软删覆盖。
        await session.Connection.ExecuteAsync("UPDATE public.sys_dic_currency SET is_deleted=TRUE WHERE uid=@Uid;",
            new { currencies[3].Uid });
        var request = currencies[1].Adapt<SysDicCurrencyDto>();
        if (rejectStaleVersionFirst)
        {
            var before = await session.Connection.QuerySingleAsync<string>(
                "SELECT jsonb_agg(to_jsonb(c) ORDER BY uid)::text FROM public.sys_dic_currency c;");
            request.Version++;
            var rejected = await service.SetNewBaseCurrencyAsync(request);
            Assert.IsFalse(rejected.Success);
            Assert.IsNull(session.Transaction);
            // Reset已经写过全部汇率；目标版本冲突必须将标识、汇率和审计事实一起恢复。
            Assert.AreEqual(before, await session.Connection.QuerySingleAsync<string>(
                "SELECT jsonb_agg(to_jsonb(c) ORDER BY uid)::text FROM public.sys_dic_currency c;"));
            request.Version = currencies[1].Version;
        }
        var result = await service.SetNewBaseCurrencyAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Data);
        StringAssert.Contains(result.Message, "1:1");
        StringAssert.Contains(result.Message, "请重新设置汇率");
        Assert.IsNull(session.Transaction);
        var rows = (await session.Connection.QueryAsync<SysDicCurrency>("""
            SELECT uid AS "Uid",rate_to_base AS "RateToBase",is_base AS "IsBase",is_enabled AS "IsEnabled",is_deleted AS "IsDeleted",version AS "Version"
            FROM public.sys_dic_currency;
            """)).ToArray();
        Assert.HasCount(4, rows);
        Assert.IsTrue(rows.All(row => row.RateToBase == 1m), "启用、停用及软删币种均按全量重置规则处理。");
        Assert.AreEqual(currencies[1].Uid, rows.Single(row => row.IsBase).Uid);
        Assert.IsTrue(rows.Single(row => row.IsBase).IsEnabled);
        Assert.IsFalse(rows.Single(row => row.Uid == currencies[2].Uid).IsEnabled);
        Assert.IsTrue(rows.Single(row => row.Uid == currencies[3].Uid).IsDeleted);
        Assert.AreEqual(currencies[1].Version + 1, rows.Single(row => row.IsBase).Version);
    }

    /// <summary>验证真实可空参数、分页投影和引用锁读取，停用关系也参与阻断；最终回滚并核对测试 Uid 无残留。</summary>
    [TestMethod]
    public async Task ExchangeQueries_UseRealNullableParametersAndRollbackTestRows()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var session = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        var marker = "__pgtest_" + Guid.NewGuid().ToString("N");
        var user = new CurrencyIntegrationUser(marker);
        var repository = new PostgreSqlSysDicCurrencyExchangeRepository(
            new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        var first = new SysDicCurrencyExchange();
        var second = new SysDicCurrencyExchange();
        session.Transaction = session.Connection.BeginTransaction();
        try
        {
            // 只选择现有有效启用币种，不补造币种；引用为空才能证明停用测试关系贡献了阻断结果。
            var codes = (await session.Connection.QueryAsync<string>(@"
                SELECT currency_code FROM public.sys_dic_currency
                WHERE is_deleted = FALSE AND is_enabled = TRUE AND currency_code IS NOT NULL
                    AND btrim(currency_code) <> ''
                ORDER BY currency_code, uid LIMIT 2;", transaction: session.Transaction)).ToArray();
            if (codes.Length != 2 || string.Equals(codes[0], codes[1], StringComparison.OrdinalIgnoreCase))
                Assert.Inconclusive("需要两个不同的现有有效启用币种；本测试不新增或修改币种。");
            var collisions = await session.Connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM public.sys_dic_currency_exchange
                WHERE (is_deleted = FALSE OR is_deleted IS NULL)
                    AND (rtrim(from_currency_code, ' ') IN (@FromCode, @ToCode)
                        OR rtrim(to_currency_code, ' ') IN (@FromCode, @ToCode));",
                new { FromCode = codes[0], ToCode = codes[1] }, session.Transaction);
            if (collisions != 0)
                Assert.Inconclusive("所选币种已有有效汇率引用，无法隔离证明停用测试关系的阻断贡献；不修改既有关系。");

            first.FromCurrencyCode = codes[0];
            first.ToCurrencyCode = codes[1];
            first.IsEnabled = false;
            first.Rate = null;
            first.Remark = marker;
            second.FromCurrencyCode = codes[1];
            second.ToCurrencyCode = codes[0];
            second.IsEnabled = false;
            second.Rate = 7.123456m;
            second.Remark = null;
            Assert.IsTrue((await repository.AddAsync(first)).Success);
            Assert.IsTrue((await repository.AddAsync(second)).Success);
            Assert.AreNotEqual(Guid.Empty, first.Uid);
            Assert.AreNotEqual(first.Uid, second.Uid);

            // 不假设目标表为空或计数恰好为测试数量；随机备注定位自己的记录，断言 Uid 和实际字段。
            var page = await repository.GetPagedAsync(new SysDicCurrencyExchangeSearchRequest
            { PageIndex = 1, PageSize = 200, Keyword = marker });
            Assert.IsTrue(page.TotalCount >= 1);
            var projected = page.Items.Single(row => row.Uid == first.Uid);
            Assert.AreEqual(codes[0], projected.FromCurrencyCode);
            Assert.AreEqual(codes[1], projected.ToCurrencyCode);
            Assert.AreEqual(marker, projected.Remark);
            Assert.IsNull(projected.Rate);
            Assert.IsFalse(projected.IsEnabled);
            Assert.AreEqual(1, projected.Version);
            Assert.AreEqual(marker, projected.CreatedBy);

            var locked = await repository.GetAllForUpdateAsync();
            var persistedFirst = locked.Single(row => row.Uid == first.Uid);
            var persistedSecond = locked.Single(row => row.Uid == second.Uid);
            Assert.IsNull(persistedFirst.Rate);
            Assert.AreEqual(marker, persistedFirst.Remark);
            Assert.AreEqual(7.123456m, persistedSecond.Rate);
            Assert.IsNull(persistedSecond.Remark);
            Assert.IsFalse(persistedFirst.IsEnabled);
            Assert.IsFalse(persistedSecond.IsEnabled);

            // 两条停用反向关系均引用这两个币种，输出必须去重；单端点请求不能带出另一端。
            var referenced = await repository.GetReferencedCurrencyCodesAsync([codes[0], codes[1], codes[0]]);
            CollectionAssert.AreEquivalent(codes, referenced.ToArray());
            var oneEndpoint = await repository.GetReferencedCurrencyCodesAsync([codes[0]]);
            CollectionAssert.AreEqual(new[] { codes[0] }, oneEndpoint.ToArray());
            Assert.HasCount(0, await repository.GetReferencedCurrencyCodesAsync([]));
        }
        finally
        {
            // INSERT、断言失败或 Inconclusive 均回滚同一事务；没有提交，因此无需删除任何已有记录。
            try { session.Transaction?.Rollback(); }
            finally
            {
                session.Transaction?.Dispose();
                session.Transaction = null;
            }
        }

        var remaining = await session.Connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM public.sys_dic_currency_exchange WHERE uid = @FirstUid OR uid = @SecondUid;",
            new { FirstUid = first.Uid, SecondUid = second.Uid });
        Assert.AreEqual(0, remaining, "回滚后测试汇率 Uid 不得残留。");
    }

    /// <summary>仅为测试记录提供随机审计标记，不依赖或修改真实账号和授权。</summary>
    private sealed class CurrencyIntegrationUser(string marker) : ICurrentUser
    {
        /// <summary>提供可识别的测试记录审计前缀。</summary>
        public string UserName => marker;

        /// <summary>没有对应真实用户关系，不创建或修改账号。</summary>
        public Guid? UserId => null;

        /// <summary>仅满足审计接口，不触发登录或权限写入。</summary>
        public bool IsAuthenticated => true;
    }
}
