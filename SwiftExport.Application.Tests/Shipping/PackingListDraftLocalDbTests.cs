using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Services.Auth;
using Proxy = SwiftExport.Application.Tests.Shipping.PackingListPostingCoordinatorTests.Proxy;
namespace SwiftExport.Application.Tests.Shipping;

/// <summary>独立随机 LocalDB 数据库验证迁移、实际持久化、回滚和命令回执；不读取应用连接配置。</summary>
[TestClass]
public sealed class PackingListDraftLocalDbTests
{
    /// <summary>既有来源不丢失，迁移幂等；部分关联及失败回滚通过真实头行仓储和回执验证。</summary>
    [TestMethod, TestCategory("SqlServerIntegration")]
    public async Task MigrationAndAssociationPersistAtomically()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PACKING_LOCALDB_TESTS") != "1")
            Assert.Inconclusive("未显式启用本机独立随机数据库测试。");
        var database = "SwiftExport_PackingImport_Test_" + Guid.NewGuid().ToString("N");
        const string masterConnection = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15";
        await using var master = new SqlConnection(masterConnection); await master.OpenAsync();
        await master.ExecuteAsync($"CREATE DATABASE [{database}]");
        try
        {
            var builder = new SqlConnectionStringBuilder(masterConnection) { InitialCatalog = database, Pooling = false };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = builder.ConnectionString }).Build();
            using var session = new DbSession(configuration);
            await session.Connection.ExecuteAsync(File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure/Database/Generated/SwiftExport.Schema.sql")), commandTimeout: 180);
            // 基线脚本的 NOCOUNT 仅供部署；测试复用连接时恢复仓储需要的受影响行数。
            await session.Connection.ExecuteAsync("SET NOCOUNT OFF;");
            var actor = Guid.NewGuid();
            var user = Proxy.Make<ICurrentUser>((method, _) => method switch { "get_IsAuthenticated" => true, "get_UserId" => actor, "get_UserName" => "packing-sql-test", _ => throw new NotSupportedException(method) });
            var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            var repository = new PackingListRepository(context);
            await VerifyInvoiceQueryAsync(session, repository);
            var f = new PackingListImportAndAssociationTests.Fixture(session, repository, new CommandReceiptRepository(context));
            // 先模拟已部署旧库：源列非空，存在一条历史关联草稿，然后执行独立迁移两次。
            var legacy = new SwiftExport.Core.Entities.Shipping.PackingListHead { CustomerId = f.Head.CustomerId, InvoiceDate = DateTime.Today, PackingListNo = "LEGACY", CreateCommandId = Guid.NewGuid() };
            var sourceId = f.Sources.Keys.Single();
            var legacyLine = new SwiftExport.Core.Entities.Shipping.PackingListLine { PackingListHeadId = legacy.Uid, LineNumber = 1, GrossDemandId = sourceId, IsNonStandardSnapshot = true, Qty = 1, UnitSnapshot = "PCS" };
            await Save(repository, session, legacy, [legacyLine]);
            await session.Connection.ExecuteAsync("DROP INDEX IX_PackingListLine_GrossDemand ON dbo.PackingListLine; ALTER TABLE dbo.PackingListLine ALTER COLUMN GrossDemandId uniqueidentifier NOT NULL; CREATE INDEX IX_PackingListLine_GrossDemand ON dbo.PackingListLine(GrossDemandId,PackingListHeadId) WHERE IsDeleted=0;");
            Assert.IsFalse(await repository.IsSchemaReadyAsync());
            var migration = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure/Database/PackingList未关联草稿迁移.sql"));
            await session.Connection.ExecuteAsync(migration); await session.Connection.ExecuteAsync(migration);
            Assert.IsTrue(await repository.IsSchemaReadyAsync());
            Assert.AreEqual(sourceId, (await repository.GetDetailAsync(legacy.Uid))!.Lines.Single().GrossDemandId);
            await Save(repository, session, f.Head, f.Rows); var parent = f.Rows.Single().Uid;
            var command = f.Command(4); f.FailLog = true;
            Assert.IsFalse((await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command)).Success);
            var rolledBack = (await repository.GetDetailAsync(f.Head.Uid))!;
            Assert.AreEqual(parent, rolledBack.Lines.Single().Uid); Assert.IsNull(rolledBack.Lines.Single().GrossDemandId);
            Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.CommandReceipt WHERE CommandId=@CommandId", new { command.CommandId }));
            f.FailLog = false; var associated = await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command);
            Assert.IsTrue(associated.Success, associated.Message);
            Assert.AreEqual(10m, associated.Data.Detail.Lines.Sum(x => x.Qty)); Assert.AreEqual(2, associated.Data.Detail.Lines.Count);
            Assert.AreEqual(4m, associated.Data.Detail.Lines.Where(x => x.GrossDemandId.HasValue).Sum(x => x.Qty));
            Assert.IsTrue(associated.Data.Detail.Lines.All(x => x.PcsPerPackage == null));
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.PackingListLine WHERE Uid=@parent AND IsDeleted=1", new { parent }));
            var replay = await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command);
            Assert.IsTrue(replay.Success, replay.Message); Assert.IsTrue(replay.Data.AlreadyApplied);
            Assert.AreEqual(2, (await repository.GetDetailAsync(f.Head.Uid))!.Lines.Count);
        }
        finally
        {
            // 只删除本测试刚创建、名称由固定前缀及随机 Guid 组成的数据库。
            if (!database.StartsWith("SwiftExport_PackingImport_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("测试库名异常。");
            await master.ExecuteAsync($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
        }
    }
    /// <summary>真实 SQL 验证精确单号在分页前过滤，售后使用系统编号，普通候选无需采购关系。</summary>
    private static async Task VerifyInvoiceQueryAsync(DbSession session, PackingListRepository repository)
    {
        var customer = Guid.NewGuid(); var so = Guid.NewGuid(); var excluded = Guid.NewGuid(); var after = Guid.NewGuid();
        await session.Connection.ExecuteAsync("""
            INSERT dbo.Customer(Uid,CustomerCode,CustomerName,IsActive,DocRelateNo,DepositRate,LeadTimeDays)
              VALUES(@customer,N'TEST',N'测试客户',1,N'',0,0);
            INSERT dbo.SalesOrderHeader(Uid,DocumentStatus,ConfirmationStatus,OrderNo,InvoiceNo,CustomerId,CustomerPoNo,LeadTimeDays)
              VALUES(@so,0,1,N'SO01',N'INV1',@customer,N'',0),(@excluded,0,1,N'SO02',N'INV10',@customer,N'INV1',0);
            INSERT dbo.AfterSalesHeader(Uid,AfterSalesNo,CustomerId,CustomerCodeSnapshot,AcceptedDate,DocumentStatus,ConfirmationStatus,IssueDescription)
              VALUES(@after,N'AFS1',@customer,N'TEST',GETDATE(),0,1,N'测试');
            INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,SourceVersion,CustomerId,IsNonStandard,SkuSnapshot,UnitSnapshot,DemandQty)
              VALUES(NEWID(),0,N'SO',@so,N'SO01',NEWID(),1,@customer,1,N'',N'PCS',10),
                    (NEWID(),0,N'SO',@excluded,N'SO02',NEWID(),1,@customer,1,N'',N'PCS',10),
                    (NEWID(),0,N'CR',@after,N'AFS1',NEWID(),1,@customer,1,N'',N'PCS',10);
            """, new { customer, so, excluded, after });
        var request = new SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListSourceSearchRequest
        { CustomerId = customer, IsNonStandard = true, InvoiceNumbers = ["INV1", "AFS1"], PageSize = 1, PageIndex = 1 };
        var first = await repository.SearchSourcesAsync(request); Assert.AreEqual(2, first.TotalCount); Assert.AreEqual(1, first.Items.Count);
        request.PageIndex = 2; var second = await repository.SearchSourcesAsync(request);
        Assert.AreEqual(2, second.TotalCount); Assert.AreNotEqual(first.Items.Single().GrossDemandId, second.Items.Single().GrossDemandId);
        request.InvoiceNumbers = []; request.PageIndex = 1;
        Assert.AreEqual(3, (await repository.SearchSourcesAsync(request)).TotalCount);
        request.ProductId = Guid.NewGuid(); Assert.AreEqual(0, (await repository.SearchSourcesAsync(request)).TotalCount);
    }
    /// <summary>在独立事务内插入新种子行，版本按真实仓储插入约定初始化。</summary>
    private static async Task Save(PackingListRepository repository, DbSession session, SwiftExport.Core.Entities.Shipping.PackingListHead head, IReadOnlyList<SwiftExport.Core.Entities.Shipping.PackingListLine> lines)
    {
        // 测试种子是新行；仓储以 Version=0 区分插入与已有行的并发更新。
        foreach (var line in lines) line.Version = 0;
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.Serializable); session.Transaction = transaction;
        try { await repository.SaveAsync(head, lines, [], 0); transaction.Commit(); }
        finally { session.Transaction = null; }
    }
    private static string Root()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "AGENTS.md"))) return folder.FullName;
        throw new InvalidOperationException("无法定位测试数据库基线。");
    }
}
