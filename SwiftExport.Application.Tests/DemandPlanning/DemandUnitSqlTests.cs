using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Document;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Sources;
using SwiftExport.Infrastructure.Services.Auth;
using System.Text.Json;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>原单位快照场景接现有PG仓储，保护旧行，只新增随机身份，不初始化或清理测试库。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class DemandUnitPostgreSqlTests
{
    /// <summary>写入后修改当前产品单位，全部消费者仍保留物料单位；未知根不借来源或当前产品补值。</summary>
    [TestMethod]
    public async Task StoredUnitSurvivesProductChangesAndUnknownRemainsUnknown()
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var user = new UnitUser();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        IMaterialDemandDocumentRepository document = new PostgreSqlMaterialDemandDocumentRepository(context);
        IMaterialDemandQueryRepository query = new PostgreSqlMaterialDemandQueryRepository(context);
        IMaterialDemandSourceRepository sources = new PostgreSqlMaterialDemandSourceRepository(context);
        var batch = "PGS02B_" + Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid(); var productId = Guid.NewGuid(); var componentId = Guid.NewGuid();
        var grossId = Guid.NewGuid();
        // 只准备原断言使用的客户和两产品；原角色、账号、仓库与供应商均非本场景所需，不创建或修改。
        await session.Connection.ExecuteAsync("""
            INSERT INTO public.customer(uid,customer_code,customer_name,doc_relate_no,deposit_rate,lead_time_days,is_active)
            VALUES(@customerId,@batch,'原单位快照',@batch,0,0,TRUE);
            INSERT INTO public.product(uid,sku,description_zh,unit,material_type,supply_type,bom_version,color_id,is_active)
            VALUES(@productId,@RootSku,'采购成品','PCS','成品','采购',0,@EmptyId,TRUE),
                  (@componentId,@ComponentSku,'客供件','PCS','客供件','采购',0,@EmptyId,TRUE);
            INSERT INTO public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,
                customer_id,demand_basis_fingerprint,is_non_standard,product_id,sku_snapshot,unit_snapshot,demand_qty)
            VALUES(@grossId,0,'MD',@grossId,@SourceNo,@grossId,@customerId,'UNIT-BASIS',FALSE,@productId,'ROOT','SET',10);
            """, new { grossId, customerId, productId, componentId, batch, RootSku = batch + "_FG", ComponentSku = batch + "_B", SourceNo = batch + "_MD-UNIT", EmptyId = Guid.Empty });
        var rootId = Guid.NewGuid();
        var rows = new[]
        {
            Row(rootId, 1, productId, "SET"),
            Row(Guid.NewGuid(), 2, componentId, "KG"),
            Row(Guid.NewGuid(), 3, null, "BOX"),
            Row(Guid.NewGuid(), 4, productId, null)
        };
        rows[1].ParentNetDemandId = rootId;
        // 复用PG仓储原有方法局部准备，调用方没有事务；不新增全局锁或把准备塞进已有业务事务。
        var writes = await document.AddDecompositionAsync(rows);
        Assert.IsTrue(writes.All(x => x.Success));
        var stored = await document.GetActiveByGrossDemandForUpdateAsync(grossId);
        CollectionAssert.AreEqual(new string?[] { "SET", "KG", "BOX", null }, stored.Select(x => x.UnitSnapshot).ToArray());
        Assert.IsNull(session.Transaction); Assert.IsNull(session.SourceDemandLease);
        await session.Connection.ExecuteAsync("UPDATE public.product SET unit='CURRENT' WHERE uid IN (@productId,@componentId)", new { productId, componentId });
        // 关键词只将原四行与既有库隔离；不按单位或资格过滤，不隐藏未知行。
        var list = await query.SearchManagerAsync(new MaterialDemandManagerSearchRequest { PageSize = 20, Keyword = batch });
        var candidates = await sources
            .GetPurchaseCandidatesAsync(new PurchaseDraftCandidateQuery { SourceType = "MD", SourceUid = grossId });
        foreach (var row in rows)
        {
            Assert.AreEqual(row.UnitSnapshot, list.Items.Single(x => x.Uid == row.Uid).Unit);
            Assert.AreEqual(row.UnitSnapshot, candidates.Single(x => x.Uid == row.Uid).Unit);
            var trace = await query.GetTraceAsync(row.Uid);
            if (row.UnitSnapshot is null) Assert.IsTrue(string.IsNullOrWhiteSpace(trace!.Unit));
            else Assert.AreEqual(row.UnitSnapshot, trace!.Unit);
        }
        StringAssert.Contains(list.Items.Single(x => x.Uid == rows[3].Uid).ReviewReason, "单位");
        Assert.IsNull(session.Transaction); Assert.IsNull(session.SourceDemandLease);

        // 各行共享来源事实，仅物料身份与单位快照不同，未知单位不以来源 SET 或当前 CURRENT 兜底。
        NetDemand Row(Guid uid, int line, Guid? product, string? unit) => new()
        {
            Uid = uid, GrossDemandId = grossId, DecompositionRevision = 1, LineNumber = line,
            SourceDemandVersionSnapshot = 1, SourceDemandBasisFingerprintSnapshot = "UNIT-BASIS",
            RootNetDemandId = rootId, ProductId = product, UnitSnapshot = unit,
            SkuSnapshot = batch + "_ROW-" + line, SupplyTypeSnapshot = SupplyTypeEnums.采购, GrossQty = 10, NetQty = 10
        };
    }

    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows = [];
    private static long OriginalSequence;

    /// <summary>冻结原50表身份及完整摘要；其他活动事务存在时停止，不初始化或复位。</summary>
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

    /// <summary>保存每表旧行与新增身份，成功事实留存；旧行或库存序列改变则失败。</summary>
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
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-s02b-demand-unit-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表证据已保存。");
        Assert.AreEqual(OriginalSequence, sequence, "单位快照读取不能产生库存事实。");
    }

    /// <summary>按原身份数组计算完整行摘要；表名来自本库目录并进行引用。</summary>
    private static async Task<string> SnapshotAsync(DbSession session, string table, Guid[] ids)
    {
        var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", ids);
        return await session.Connection.ExecuteScalarAsync<string>($"""
            SELECT count(*)::text||':'||md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'' ORDER BY uid),''))
            FROM public.{PostgreSqlNames.Quote(table)} t WHERE uid=ANY(@OldIds)
            """, parameters) ?? throw new AssertFailedException("原记录保护摘要为空。");
    }

    /// <summary>本例仅仓储读写与审计打戳所需的测试身份，不声称验证账号或业务权限。</summary>
    private sealed class UnitUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "pg-unit-snapshot-test";
        /// <summary>仓储打戳使用固定测试身份，本场景不运行认证或授权服务。</summary>
        public bool IsAuthenticated => true;
    }
}
