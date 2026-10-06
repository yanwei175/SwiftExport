using System.Data;
using Dapper;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Services.Auth;
using Proxy = SwiftExport.Application.Tests.Shipping.PackingListPostingCoordinatorTests.Proxy;
namespace SwiftExport.Application.Tests.Shipping;

/// <summary>沿用原箱单持久化、发票分页、回滚及命令重放断言；使用现有隔离PG库新记录，保护全部旧行，不部署结构。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PackingListDraftPostgreSqlTests
{
    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows = [];
    private static long OriginalSequence;

    /// <summary>写入前冻结现有50表原身份和完整行摘要；存在其他活动事务时停止，不初始化或重置数据库。</summary>
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

    /// <summary>结束后保存全部旧行和新增身份证据；成功事实保留，旧行或库存序列变化均使验收失败。</summary>
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
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-r18-packing-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表摘要已保存。");
        Assert.AreEqual(OriginalSequence, sequence, "箱单草稿测试不能产生库存事实或推进库存序列。");
    }

    /// <summary>按冻结的原身份数组比较完整行，不按名称或时间排除原数据；表名来自本库目录并引用转义。</summary>
    private static async Task<string> SnapshotAsync(DbSession session, string table, Guid[] ids)
    {
        var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", ids);
        return await session.Connection.ExecuteScalarAsync<string>($"""
            SELECT count(*)::text||':'||md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'' ORDER BY uid),''))
            FROM public.{PostgreSqlNames.Quote(table)} t WHERE uid=ANY(@OldIds)
            """, parameters) ?? throw new AssertFailedException("原记录保护摘要为空。");
    }

    /// <summary>现有PG结构就绪且已关联来源保留；实际头行/回执验证部分关联、失败回滚和重放，原其它夹具端口保持。</summary>
    [TestMethod]
    public async Task SchemaReadyAndAssociationPersistAtomically()
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var actor = Guid.NewGuid();
        var user = Proxy.Make<ICurrentUser>((method, _) => method switch { "get_IsAuthenticated" => true, "get_UserId" => actor, "get_UserName" => "packing-pg-test", _ => throw new NotSupportedException(method) });
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var repository = new PostgreSqlPackingListRepository(context);
        await VerifyInvoiceQueryAsync(session, repository);
        var f = new PackingListImportAndAssociationTests.Fixture(session, repository, new PostgreSqlCommandReceiptRepository(context));
        f.Head.PackingListNo = "PGR18_" + Guid.NewGuid().ToString("N");
        f.Head.CreateCommandId = Guid.NewGuid();
        // SQL Server旧库升级DDL归S源部署退役；PG直接检查现有完整结构，不在测试库模拟旧结构或伪称升级幂等已验。
        var legacy = new SwiftExport.Core.Entities.Shipping.PackingListHead { CustomerId = f.Head.CustomerId, InvoiceDate = DateTime.Today, PackingListNo = "PGR18Legacy_" + Guid.NewGuid().ToString("N"), CreateCommandId = Guid.NewGuid() };
        var sourceId = f.Sources.Keys.Single();
        var legacyLine = new SwiftExport.Core.Entities.Shipping.PackingListLine { PackingListHeadId = legacy.Uid, LineNumber = 1, GrossDemandId = sourceId, IsNonStandardSnapshot = true, Qty = 1, UnitSnapshot = "PCS" };
        await Save(repository, session, legacy, [legacyLine]);
        Assert.IsTrue(await repository.IsSchemaReadyAsync());
        Assert.AreEqual(sourceId, (await repository.GetDetailAsync(legacy.Uid))!.Lines.Single().GrossDemandId);
        await Save(repository, session, f.Head, f.Rows); var parent = f.Rows.Single().Uid;
        var command = f.Command(4); f.FailLog = true;
        Assert.IsFalse((await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command)).Success);
        var rolledBack = (await repository.GetDetailAsync(f.Head.Uid))!;
        Assert.AreEqual(parent, rolledBack.Lines.Single().Uid); Assert.IsNull(rolledBack.Lines.Single().GrossDemandId);
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@CommandId", new { command.CommandId }));
        f.FailLog = false; var associated = await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command);
        Assert.IsTrue(associated.Success, associated.Message);
        Assert.AreEqual(10m, associated.Data.Detail.Lines.Sum(x => x.Qty)); Assert.AreEqual(2, associated.Data.Detail.Lines.Count);
        Assert.AreEqual(4m, associated.Data.Detail.Lines.Where(x => x.GrossDemandId.HasValue).Sum(x => x.Qty));
        Assert.IsTrue(associated.Data.Detail.Lines.All(x => x.PcsPerPackage == null));
        Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_line WHERE uid=@parent AND is_deleted=TRUE", new { parent }));
        var replay = await f.Service.AssociateSourcesAsync(f.Head.Uid, parent, command);
        Assert.IsTrue(replay.Success, replay.Message); Assert.IsTrue(replay.Data.AlreadyApplied);
        Assert.AreEqual(2, (await repository.GetDetailAsync(f.Head.Uid))!.Lines.Count);
    }
    /// <summary>真实 SQL 验证精确单号在分页前过滤，售后使用系统编号，普通候选无需采购关系。</summary>
    private static async Task VerifyInvoiceQueryAsync(DbSession session, PostgreSqlPackingListRepository repository)
    {
        var customer = Guid.NewGuid(); var so = Guid.NewGuid(); var excluded = Guid.NewGuid(); var after = Guid.NewGuid();
        var customerCode = "PGR18_" + customer.ToString("N");
        var soNo = customerCode + "SO01"; var excludedNo = customerCode + "SO02"; var afterNo = customerCode + "AFS1";
        await session.Connection.ExecuteAsync("""
            INSERT INTO public.customer(uid,customer_code,customer_name,is_active,doc_relate_no,deposit_rate,lead_time_days)
              VALUES(@customer,@customerCode,'测试客户',TRUE,'',0,0);
            INSERT INTO public.sales_order_header(uid,document_status,confirmation_status,order_no,invoice_no,customer_id,customer_po_no,lead_time_days)
              VALUES(@so,0,1,@soNo,'INV1',@customer,'',0),(@excluded,0,1,@excludedNo,'INV10',@customer,'INV1',0);
            INSERT INTO public.after_sales_header(uid,after_sales_no,customer_id,customer_code_snapshot,accepted_date,document_status,confirmation_status,issue_description)
              VALUES(@after,@afterNo,@customer,@customerCode,CURRENT_TIMESTAMP AT TIME ZONE 'UTC',0,1,'测试');
            INSERT INTO public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,source_version,customer_id,is_non_standard,sku_snapshot,unit_snapshot,demand_qty)
              VALUES(gen_random_uuid(),0,'SO',@so,@soNo,gen_random_uuid(),1,@customer,TRUE,'','PCS',10),
                    (gen_random_uuid(),0,'SO',@excluded,@excludedNo,gen_random_uuid(),1,@customer,TRUE,'','PCS',10),
                    (gen_random_uuid(),0,'CR',@after,@afterNo,gen_random_uuid(),1,@customer,TRUE,'','PCS',10);
            """, new { customer, so, excluded, after, customerCode, soNo, excludedNo, afterNo });
        var request = new SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListSourceSearchRequest
        { CustomerId = customer, IsNonStandard = true, InvoiceNumbers = ["INV1", afterNo], PageSize = 1, PageIndex = 1 };
        var first = await repository.SearchSourcesAsync(request); Assert.AreEqual(2, first.TotalCount); Assert.AreEqual(1, first.Items.Count);
        request.PageIndex = 2; var second = await repository.SearchSourcesAsync(request);
        Assert.AreEqual(2, second.TotalCount); Assert.AreNotEqual(first.Items.Single().GrossDemandId, second.Items.Single().GrossDemandId);
        request.InvoiceNumbers = []; request.PageIndex = 1;
        Assert.AreEqual(3, (await repository.SearchSourcesAsync(request)).TotalCount);
        request.ProductId = Guid.NewGuid(); Assert.AreEqual(0, (await repository.SearchSourcesAsync(request)).TotalCount);
    }
    /// <summary>在独立事务内插入新种子行，版本按真实仓储插入约定初始化。</summary>
    private static async Task Save(PostgreSqlPackingListRepository repository, DbSession session, SwiftExport.Core.Entities.Shipping.PackingListHead head, IReadOnlyList<SwiftExport.Core.Entities.Shipping.PackingListLine> lines)
    {
        // 测试种子是新行；仓储以 Version=0 区分插入与已有行的并发更新。
        foreach (var line in lines) line.Version = 0;
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.Serializable); session.Transaction = transaction;
        try { await repository.SaveAsync(head, lines, [], 0); transaction.Commit(); }
        finally { session.Transaction = null; }
    }
}
