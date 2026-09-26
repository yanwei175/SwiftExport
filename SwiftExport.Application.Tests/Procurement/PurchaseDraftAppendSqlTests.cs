using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Interfaces.Procurement;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;
using System.Text.RegularExpressions;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>使用当前实体生成的隔离数据库验证采购预览及追加锁定共用的入库来源查询。</summary>
[TestClass, DoNotParallelize]
public sealed class PurchaseDraftAppendSqlTests
{
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
            var model = new PurchaseDraftWriteModelBuilder().Build(document, Guid.NewGuid(), db.OrderId, "TEST", createHead ? 2 : 3, createHead);
            Assert.AreEqual(0m, model.Lines.Single().UnlinkedQty);
            await db.InScopeAsync(async services =>
            {
                await services.GetRequiredService<IPurchaseOrderDraftRepository>().InsertPurchaseDraftLinesAsync(model.Lines);
                var line = await services.GetRequiredService<DbSession>().Connection.QuerySingleAsync<PurchaseOrderLine>(
                    "SELECT *,QTY AS Quantity FROM dbo.PurchaseOrderLine WHERE Uid=@Uid", new { model.Lines.Single().Uid });
                Assert.AreEqual(0m, line.UnlinkedQty);
                var gross = Guid.NewGuid();
                var facts = new ProcurementReversalFacts
                {
                    Head = new() { Uid = db.OrderId, OriginKind = 1 }, Lines = [line], Links = model.Links,
                    Sources = [new() { Uid = gross }],
                    Demands = model.Links.Select(link => new SwiftExport.Core.Entities.DemandPlanning.NetDemand
                        { Uid = link.NetDemandId, GrossDemandId = gross, DecompositionRevision = 1 }).ToList()
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
        await using var db = new AppendQueryDatabase();
        await db.InitializeAsync();
        var uid = db.OrderId;
        var sourceUid = Guid.NewGuid();
        await db.ExecuteAsync("""
            UPDATE dbo.PurchaseOrderHead SET DemandSourceType=N'SO',DemandSourceUid=@sourceUid,
                ExecutionRequestId=NEWID() WHERE Uid=@uid;
            """, new { uid, sourceUid });
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
            INSERT INTO dbo.InboundDocumentLine
                (Uid,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,Quantity,SourceLineId)
            SELECT NEWID(),NEWID(),LineNumber,ProductId,N'TEST',1,Uid
            FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid;
            """, new { uid });
        await AssertEligible(false);
        await db.ExecuteAsync("UPDATE dbo.PurchaseOrderLine SET IsDeleted=1 WHERE PurchaseOrderId=@uid", new { uid });
        await AssertEligible(false);
        await db.ExecuteAsync("""
            UPDATE dbo.InboundDocumentLine SET IsDeleted=1
            WHERE SourceLineId IN (SELECT Uid FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid);
            """, new { uid });
        await AssertEligible(true);
        // 即使入库行已软删，其不可变库存流水仍应阻断安全追加。
        await db.ExecuteAsync("""
            INSERT INTO dbo.InventoryLedgerEntry
                (Uid,Sequence,WarehouseSiteId,ProductId,TransactionType,QuantityDelta,BalanceAfter,
                 SourceDocumentType,SourceDocumentId,SourceLineId,CommandId,CommandItemId,OccurredDate)
            SELECT NEWID(),NEXT VALUE FOR dbo.InventoryLedgerSequence,@site,ProductId,10,1,1,
                N'Inbound',InboundDocumentId,Uid,NEWID(),NEWID(),SYSUTCDATETIME()
            FROM dbo.InboundDocumentLine
            WHERE SourceLineId IN (SELECT Uid FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid);
            """, new { uid, site = db.ReceiptSiteId });
        await AssertEligible(false);
    }

    /// <summary>仅装载本查询涉及的真实表定义，不依赖其他业务模块的种子或索引。</summary>
    private sealed class AppendQueryDatabase : IAsyncDisposable
    {
        private readonly string name = "SwiftExport_AppendTests_" + Guid.NewGuid().ToString("N");
        private ServiceProvider? provider;
        private bool created;
        public Guid OrderId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ReceiptSiteId { get; } = Guid.NewGuid();

        private string ConnectionString(string database) => new SqlConnectionStringBuilder
        {
            DataSource = @"(LocalDB)\MSSQLLocalDB", InitialCatalog = database,
            IntegratedSecurity = true, TrustServerCertificate = true
        }.ConnectionString;

        /// <summary>新建唯一测试库并从当前基线提取四张表，查询使用真实仓储及事务连接。</summary>
        public async Task InitializeAsync()
        {
            await using var master = new SqlConnection(ConnectionString("master"));
            await master.OpenAsync();
            await master.ExecuteAsync($"CREATE DATABASE [{name}]");
            created = true;
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            var schema = await File.ReadAllTextAsync(Path.Combine(root!.FullName,
                "SwiftExport.Infrastructure/Database/Generated/SwiftExport.Schema.sql"));
            foreach (var table in new[] { "PurchaseOrderHead", "PurchaseOrderLine", "InboundDocumentLine", "InventoryLedgerEntry" })
            {
                var definition = Regex.Match(schema, $@"CREATE TABLE \[dbo\]\.\[{table}\]\s*\(.*?\r?\n\);", RegexOptions.Singleline);
                Assert.IsTrue(definition.Success, table);
                await ExecuteAsync(definition.Value);
            }
            await ExecuteAsync("""
                CREATE SEQUENCE dbo.InventoryLedgerSequence AS bigint START WITH 1;
                INSERT dbo.PurchaseOrderHead
                    (Uid,OriginKind,ConfirmationStatus,LifecycleStatus,OrderNo,Currency,SupplierId,SupplierCode,ExecutionRequestId,OrderDate)
                VALUES(@OrderId,0,0,0,N'TEST',N'CNY',@SupplierId,N'TEST',NEWID(),SYSUTCDATETIME());
                INSERT dbo.PurchaseOrderLine(Uid,PurchaseOrderId,LineNumber,OrderNo,ProductId,Sku,QTY)
                VALUES(NEWID(),@OrderId,1,N'TEST',NEWID(),N'TEST',1);
                """, new { OrderId, SupplierId });
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Default"] = ConnectionString(name) }).Build());
            services.AddScoped<DbSession>();
            services.AddScoped(p => new RepositoryBaseContext(p.GetRequiredService<DbSession>(), NullLoggerFactory.Instance,
                new AuditStampService(new TestUser()), new TestUser()));
            services.AddScoped<IPurchaseOrderDraftRepository, PurchaseOrderDraftRepository>();
            provider = services.BuildServiceProvider();
        }

        public async Task<int> ExecuteAsync(string sql, object? args = null)
        {
            await using var connection = new SqlConnection(ConnectionString(name));
            return await connection.ExecuteAsync(sql, args);
        }

        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = provider!.CreateScope();
            return await action(scope.ServiceProvider);
        }

        /// <summary>只清理本实例自行生成并创建的测试库。</summary>
        public async ValueTask DisposeAsync()
        {
            if (provider is not null) await provider.DisposeAsync();
            if (!created) return;
            await using var master = new SqlConnection(ConnectionString("master"));
            await master.OpenAsync();
            await master.ExecuteAsync($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
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
