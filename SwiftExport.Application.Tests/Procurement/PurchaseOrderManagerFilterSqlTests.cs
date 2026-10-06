using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.WebApi.Configuration;
using System.Text.Json;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>原采购管理组合筛选场景接当前PG服务，仅新增随机身份，保护既有数据，不初始化或清理库。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PurchaseOrderManagerFilterPostgreSqlTests
{
    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows = [];
    private static long OriginalSequence;

    /// <summary>冻结原50表身份及完整摘要；其他活动事务存在时停止，不重置测试库。</summary>
    [ClassInitialize]
    public static async Task CaptureOriginalRowsAsync(TestContext context)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend' AND (state<>'idle' OR xact_start IS NOT NULL)"), "隔离写入必须串行。");
        var tables = (await session.Connection.QueryAsync<string>("SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY table_name")).ToArray();
        Assert.AreEqual(50, tables.Length, "使用既有完整结构，不初始化。");
        foreach (var table in tables)
        {
            var ids = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)}")).ToArray();
            OriginalRows.Add(table, (ids, await SnapshotAsync(session, table, ids)));
        }
        OriginalSequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
    }

    /// <summary>保存每表旧行及新增身份；不删除本批事实，旧行或库存序列改变则失败。</summary>
    [ClassCleanup]
    public static async Task VerifyOriginalRowsAsync()
    {
        if (OriginalRows.Count == 0) return;
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var evidence = new List<object>(); var unchanged = true;
        foreach (var (table, original) in OriginalRows)
        {
            var after = await SnapshotAsync(session, table, original.Ids);
            var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", original.Ids);
            var added = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)} WHERE NOT(uid=ANY(@OldIds)) ORDER BY uid", parameters)).ToArray();
            evidence.Add(new { Table = table, OldCount = original.Ids.Length, Before = original.Snapshot, After = after, AddedIds = added });
            unchanged &= original.Snapshot == after;
        }
        var sequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
        Directory.CreateDirectory("TestResults/postgresql-migration");
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-s02a-manager-filter-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表证据已保存。");
        Assert.AreEqual(OriginalSequence, sequence, "管理筛选不能产生库存事实。");
    }

    /// <summary>按原身份数组计算完整行摘要，表名只取本库目录并进行标识符引用。</summary>
    private static async Task<string> SnapshotAsync(DbSession session, string table, Guid[] ids)
    {
        var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", ids);
        return await session.Connection.ExecuteScalarAsync<string>($"""
            SELECT count(*)::text||':'||md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'' ORDER BY uid),''))
            FROM public.{PostgreSqlNames.Quote(table)} t WHERE uid=ANY(@OldIds)
            """, parameters) ?? throw new AssertFailedException("原记录保护摘要为空。");
    }

    /// <summary>客户代码精确匹配历史快照；关键词包含头行来源，结束日期包含当日末尾并排除次日。</summary>
    [TestMethod]
    public async Task CustomerSupplierKeywordAndInclusiveDatesFilterCorrectOrders()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        // 只读取既有有效管理员，正常权限服务仍查询实际资格；不创建或修改账号、角色及授权。
        var actor = await observer.Connection.QueryFirstAsync<ManagerUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
            WHERE u.is_active AND NOT u.is_deleted AND EXISTS(
                SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                WHERE ur.user_id=u.uid AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN')
            ORDER BY u.uid;
            """);
        var batch = "PGS02A_" + Guid.NewGuid().ToString("N");
        var year = 4000 + Math.Abs(Guid.NewGuid().GetHashCode() % 5000);
        // 随机独立年月既不推进原号段，也避免日期单独筛选命中其他批次；冲突时停止而非重置或清理。
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("""
            SELECT (SELECT count(*) FROM public.document_sequence WHERE doc_type='PO' AND year_month=@Month)
                 + (SELECT count(*) FROM public.purchase_order_head WHERE order_date>=@From AND order_date<@To)
            """, new { Month = $"{year:D4}08", From = new DateTime(year, 9, 15), To = new DateTime(year, 9, 17) }));
        var customer = new Customer { CustomerCode = batch, CustomerName = "原管理筛选", DocRelateNo = batch, IsActive = true };
        var supplier = new Supplier { SupplierCode = batch, SupplierName = "原实际供应商", DocRelateNo = batch, IsActive = true };
        var receiptSupplier = new Supplier { SupplierCode = batch + "_RECEIPT", SupplierName = "原收货供应商", DocRelateNo = batch, IsActive = true };
        var unit = new SysDictData { DictCode = "Unit", DataNameEn = batch, IsEnabled = true };
        var product = new Product { Sku = batch, DescriptionZh = "原采购成品", Unit = batch, MaterialType = "成品", SupplyType = "采购", ColorId = Guid.Empty, IsActive = true };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(Root(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        // 每次命令/读取保留真实request scope，事务和租约由原服务拥有，不借用测试事务。
        async Task<T> Run<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = app.Services.CreateScope();
            var result = await action(scope.ServiceProvider);
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease);
            return result;
        }
        Assert.IsTrue((await Run(p => p.GetRequiredService<IRepository<Customer>>().AddAsync(customer))).Success);
        Assert.IsTrue((await Run(p => p.GetRequiredService<IRepository<Supplier>>().AddAsync(supplier))).Success);
        Assert.IsTrue((await Run(p => p.GetRequiredService<IRepository<Supplier>>().AddAsync(receiptSupplier))).Success);
        Assert.IsTrue((await Run(p => p.GetRequiredService<IRepository<SysDictData>>().AddAsync(unit))).Success);
        Assert.IsTrue((await Run(p => p.GetRequiredService<IRepository<Product>>().AddAsync(product))).Success);
        CreateIndependentPurchaseOrderCommand Command() => new()
        {
            CommandId = Guid.NewGuid(), CustomerId = customer.Uid, SupplierId = supplier.Uid,
            OrderDate = new DateTime(year, 8, 31), PackagingRequirements = "客户包装要求",
            Lines = [new() { ProductId = product.Uid, Quantity = 10m, UnitPrice = 2m, TaxPrice = 2.5m }]
        };
        var first = await Run(p => p.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(Command()));
        var second = await Run(p => p.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(Command()));
        Assert.IsTrue(first.Success, first.Message); Assert.IsTrue(second.Success, second.Message);
        var uid = first.Data!.PurchaseOrderId; var other = second.Data!.PurchaseOrderId;
        var history = batch + "_C-HISTORY"; var headSource = batch + "_SOURCE-HEAD-UNIQUE";
        var lineSource = batch + "_SOURCE-LINE-UNIQUE"; var sku = batch + "_SKU-UNIQUE";
        var day = new DateTime(year, 9, 15);
        // 仍使用原最后一个100ns输入；Npgsql按PG timestamp(6)精度存储，避免七位SQL字面量被服务器四舍五入到次日。
        await observer.Connection.ExecuteAsync("""
            UPDATE public.purchase_order_head SET customer_code=@history,order_date=@LastInstant,demand_source_no=@headSource WHERE uid=@uid;
            UPDATE public.purchase_order_head SET customer_code=@OtherHistory,order_date=@NextDay WHERE uid=@other;
            UPDATE public.purchase_order_line SET source_no=@lineSource,sku=@sku WHERE purchase_order_id=@uid;
            """, new { uid, other, history, headSource, lineSource, sku, OtherHistory = history + "-X", LastInstant = day.AddDays(1).AddTicks(-1), NextDay = day.AddDays(1) });
        async Task AssertMatches(PurchaseOrderManagerSearchRequest request, int count)
        {
            var result = await Run(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>().SearchAsync(request));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(count, result.Data!.TotalCount);
            if (count == 1) Assert.AreEqual(uid, result.Data.Items.Single().PurchaseOrderId);
        }
        await AssertMatches(new() { CustomerCode = history }, 1);
        await AssertMatches(new() { CustomerCode = batch + "_C-HIST" }, 0);
        await AssertMatches(new() { CustomerCode = history, SupplierId = supplier.Uid }, 1);
        await AssertMatches(new() { SupplierId = receiptSupplier.Uid }, 0);
        await AssertMatches(new() { OrderDateFrom = day, OrderDateTo = day }, 1);
        foreach (var keyword in new[] { first.Data.OrderNo, headSource, lineSource, sku })
            await AssertMatches(new() { Keyword = keyword }, 1);
        var invalid = await Run(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>().SearchAsync(new()
            { OrderDateFrom = day.AddDays(1), OrderDateTo = day }));
        Assert.IsFalse(invalid.Success);
    }

    /// <summary>实际既有管理员的当前请求身份；只读取资格，不伪造权限服务或修改账号。</summary>
    private sealed class ManagerUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string UserName { get; set; } = "";
        /// <summary>该身份已由本库有效管理员查询取得，后续命令仍须通过正常权限核验。</summary>
        public bool IsAuthenticated => true;
    }

    /// <summary>定位实际宿主配置；连接在内存明确覆盖为专用库，不写配置文件。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.WebApi", "appsettings.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("无法定位 WebApi 配置。");
    }
}
