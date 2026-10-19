using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Interfaces.Procurement;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;
using System.Text.Json;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>沿用两个追加行为场景，显式使用既有隔离PG库和真实仓储；保护全部原记录，不部署结构或清理数据。</summary>
[TestClass, DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PurchaseDraftAppendSqlTests
{
    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows = [];
    private static long OriginalSequence;

    /// <summary>写入前冻结现有50表原身份和完整行摘要；其他活动事务存在时停止，不初始化或重置数据库。</summary>
    [ClassInitialize]
    public static async Task CaptureOriginalRowsAsync(TestContext context)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend' AND (state<>'idle' OR xact_start IS NOT NULL)"), "隔离写入必须串行且没有其他活动事务。");
        var tables = (await session.Connection.QueryAsync<string>("SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY table_name")).ToArray();
        Assert.AreEqual(50, tables.Length, "使用既有结构，禁止初始化测试库。");
        foreach (var table in tables)
        {
            var ids = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)}")).ToArray();
            OriginalRows.Add(table, (ids, await SnapshotAsync(session, table, ids)));
        }
        OriginalSequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
    }

    /// <summary>结束后记录全部原行及新身份、库存序列；只保留本批新增事实，任何原行变化均使验收失败。</summary>
    [ClassCleanup]
    public static async Task VerifyOriginalRowsAsync()
    {
        if (OriginalRows.Count == 0) return;
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var evidence = new List<object>();
        var unchanged = true;
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
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-r17-draft-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表摘要已保存。");
    }

    /// <summary>仅按原身份数组比较完整行，不按时间或名称排除原数据；表名来自本库目录并引用转义。</summary>
    private static async Task<string> SnapshotAsync(DbSession session, string table, Guid[] ids)
    {
        var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", ids);
        return await session.Connection.ExecuteScalarAsync<string>($"""
            SELECT count(*)::text||':'||md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'' ORDER BY uid),''))
            FROM public.{PostgreSqlNames.Quote(table)} t WHERE uid=ANY(@OldIds)
            """, parameters) ?? throw new AssertFailedException("原记录保护摘要为空。");
    }

    /// <summary>新建和追加的合并行均写入零未关联数量；读回后贡献守恒，缺失和释放引用仍阻断。</summary>
    [TestMethod]
    public async Task DemandLines_ShouldPersistZeroUnlinkedQuantityAndValidateContributions()
    {
        await using var db = new AppendQueryDatabase();
        await db.InitializeAsync();
        var document = new PurchaseDraftPreviewDocumentDto
        {
            SourceType = "SO", SourceUid = Guid.NewGuid(), SourceNo = "SO-TEST",
            Lines = [new() { Sku = "MERGED", PurchaseQty = 5m, Contributions =
                [new() { NetDemandId = Guid.NewGuid(), PurchaseQty = 2m },
                 new() { NetDemandId = Guid.NewGuid(), PurchaseQty = 3m }] }]
        };
        foreach (var createHead in new[] { true, false })
        {
            var model = new PurchaseDraftWriteModelBuilder().Build(document, Guid.NewGuid(), db.OrderId, db.OrderNo, createHead ? 2 : 3, createHead);
            Assert.AreEqual(0m, model.Lines.Single().UnlinkedQty);
            await db.InScopeAsync(async services =>
            {
                await services.GetRequiredService<IPurchaseOrderDraftRepository>().InsertPurchaseDraftLinesAsync(model.Lines);
                var session = services.GetRequiredService<DbSession>();
                var columns = string.Join(',', EntityMetadataCache.GetMetadata(typeof(PurchaseOrderLine)).Columns.Select(column =>
                    PostgreSqlNames.Quote(column.ColumnName) + " AS " + PostgreSqlNames.Quote(column.PropertyName)));
                var line = await session.Connection.QuerySingleAsync<PurchaseOrderLine>(
                    $"SELECT {columns} FROM public.purchase_order_line WHERE uid=@Uid", new { model.Lines.Single().Uid }, session.Transaction);
                Assert.AreEqual(0m, line.UnlinkedQty);
                var gross = Guid.NewGuid();
                var facts = new ProcurementReversalFacts
                {
                    Head = new() { Uid = db.OrderId, OriginKind = 1,CustomerId=Guid.Parse("5e4b1a50-20ed-4ae3-a6d8-b8f9a8492faa") }, Lines = [line], Links = model.Links,
                    Sources = [new() { Uid = gross,DemandBasisFingerprint="basis",CustomerId=Guid.Parse("5e4b1a50-20ed-4ae3-a6d8-b8f9a8492faa"),SourceType="MD",SourceUid=gross,IsNonStandard=!line.ProductId.HasValue }],
                    Demands = model.Links.Select(link => new SwiftExport.Core.Entities.DemandPlanning.NetDemand
                        { Uid = link.NetDemandId, GrossDemandId = gross, DecompositionRevision = 1,SourceDemandBasisFingerprintSnapshot="basis",ProductId=line.ProductId,
                            NetQty=line.Quantity,UnitSnapshot=line.UnitSnapshot,MaterialTypeSnapshot=line.MaterialType??string.Empty,SupplyTypeSnapshot="采购" }).ToList()
                };
                ProcurementConfirmationBuilder.RequireSourceReferences(facts);
                line.UnlinkedQty = null;
                var missing = Assert.ThrowsExactly<InvalidOperationException>(() => ProcurementConfirmationBuilder.RequireSourceReferences(facts));
                StringAssert.Contains(missing.Message, "缺少未关联数量依据");
                line.UnlinkedQty = 0;
                facts.Links[0].ReleasedQty = 1;
                Assert.ThrowsExactly<InvalidOperationException>(() => ProcurementConfirmationBuilder.RequireSourceReferences(facts));
                facts.Links.Clear();
                Assert.ThrowsExactly<InvalidOperationException>(() => ProcurementConfirmationBuilder.RequireSourceReferences(facts));
                return true;
            });
        }
    }

    /// <summary>无收货草稿可追加；采购行入库即阻断，软删采购行不能隐藏其历史收货。</summary>
    [TestMethod]
    public async Task AppendQueries_ShouldUsePurchaseLineSourceAndPreserveHistory()
    {
        var sourceUid = Guid.NewGuid();
        await using var db = new AppendQueryDatabase();
        await db.InitializeAsync(sourceUid);
        var uid = db.OrderId;
        async Task AssertEligible(bool eligible)
        {
            await db.InScopeAsync(async services =>
            {
                var repository = services.GetRequiredService<IPurchaseOrderDraftRepository>();
                var candidates = await repository.GetSafeAppendCandidatesAsync("SO", sourceUid, [db.SupplierId]);
                Assert.AreEqual(eligible ? 1 : 0, candidates.Count);
                var head = await repository.GetSafeDraftForUpdateAsync(uid, 1);
                Assert.AreEqual(eligible, head is not null);
                return true;
            });
        }
        await AssertEligible(true);
        await db.ExecuteAsync("""
            INSERT INTO public.inbound_document_line
                (uid,inbound_document_id,line_number,product_id,sku_snapshot,quantity,source_line_id)
            SELECT gen_random_uuid(),gen_random_uuid(),line_number,product_id,'TEST',1,uid
            FROM public.purchase_order_line WHERE purchase_order_id=@uid;
            """, new { uid });
        await AssertEligible(false);
        await db.ExecuteAsync("UPDATE public.purchase_order_line SET is_deleted=TRUE WHERE purchase_order_id=@uid", new { uid });
        await AssertEligible(false);
        await db.ExecuteAsync("""
            UPDATE public.inbound_document_line SET is_deleted=TRUE
            WHERE source_line_id IN (SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@uid);
            """, new { uid });
        await AssertEligible(true);
        // 即使入库行已软删，其不可变库存流水仍应阻断安全追加。
        await db.ExecuteAsync("""
            INSERT INTO public.inventory_ledger_entry
                (uid,sequence,warehouse_site_id,product_id,transaction_type,quantity_delta,balance_after,
                 source_document_type,source_document_id,source_line_id,command_id,command_item_id,occurred_date)
            SELECT gen_random_uuid(),nextval('public.inventory_ledger_sequence'),@site,product_id,10,1,1,
                'Inbound',inbound_document_id,uid,gen_random_uuid(),gen_random_uuid(),CURRENT_TIMESTAMP AT TIME ZONE 'UTC'
            FROM public.inbound_document_line
            WHERE source_line_id IN (SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@uid);
            """, new { uid, site = db.ReceiptSiteId });
        await AssertEligible(false);
    }

    /// <summary>仅新增原场景使用的采购头和行，复用现有PG结构；调用作用域持有事务，不删除本批成功事实。</summary>
    private sealed class AppendQueryDatabase : IAsyncDisposable
    {
        private readonly string connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        private ServiceProvider? provider;
        public Guid OrderId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ReceiptSiteId { get; } = Guid.NewGuid();
        public string OrderNo { get; } = "PGR17_" + Guid.NewGuid().ToString("N");

        /// <summary>使用独立随机头/行和唯一单号；来源可在INSERT时确定，不更新SQL Server与PG共同保护的成立身份。</summary>
        public async Task InitializeAsync(Guid? sourceUid = null)
        {
            await ExecuteAsync("""
                INSERT INTO public.purchase_order_head
                    (uid,origin_kind,confirmation_status,lifecycle_status,order_no,currency,supplier_id,supplier_code,execution_request_id,order_date,demand_source_type,demand_source_uid)
                VALUES(@OrderId,0,0,0,@OrderNo,'CNY',@SupplierId,'TEST',gen_random_uuid(),CURRENT_TIMESTAMP AT TIME ZONE 'UTC',@SourceType,@SourceUid);
                INSERT INTO public.purchase_order_line(uid,purchase_order_id,line_number,order_no,product_id,sku,quantity)
                VALUES(gen_random_uuid(),@OrderId,1,@OrderNo,gen_random_uuid(),'TEST',1);
                """, new { OrderId, SupplierId, OrderNo, SourceType = sourceUid.HasValue ? "SO" : null, SourceUid = sourceUid });
            var services = new ServiceCollection();
            services.AddScoped(_ => new DbSession(new PostgreSqlConnectionFactory(connectionString)));
            services.AddScoped(p => new RepositoryBaseContext(p.GetRequiredService<DbSession>(), NullLoggerFactory.Instance,
                new AuditStampService(new TestUser()), new TestUser()));
            services.AddScoped<IPurchaseOrderDraftRepository, PostgreSqlPurchaseOrderDraftRepository>();
            provider = services.BuildServiceProvider();
        }

        /// <summary>在本库执行原场景种子及只按本批身份限定的修改，复用服务器默认值及序列。</summary>
        public async Task<int> ExecuteAsync(string sql, object? args = null)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            return await connection.ExecuteAsync(sql, args);
        }

        /// <summary>每次原断言使用独立scoped真实仓储和调用方事务；失败回滚，不让数据库锁跨断言泄漏。</summary>
        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = provider!.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            using var transaction = session.Connection.BeginTransaction();
            session.Transaction = transaction;
            try
            {
                var result = await action(scope.ServiceProvider);
                transaction.Commit();
                return result;
            }
            catch { transaction.Rollback(); throw; }
            finally { session.Transaction = null; }
        }

        /// <summary>仅释放本夹具服务容器和连接，不清理成功事实或重置任何数据库。</summary>
        public async ValueTask DisposeAsync()
        {
            if (provider is not null) await provider.DisposeAsync();
        }
    }

    /// <summary>隔离数据库写入使用固定测试身份，不读取桌面登录信息。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public string UserName => "append-test";
        public Guid? UserId => null;
        public bool IsAuthenticated => true;
    }
}
