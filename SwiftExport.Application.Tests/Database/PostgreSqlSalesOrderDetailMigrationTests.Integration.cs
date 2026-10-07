using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行验证销售Detail真实PG事实，随机新增夹具在调用方事务内回滚，不重置现有库或结构。</summary>
[TestClass, DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed partial class PostgreSqlSalesOrderDetailMigrationIntegrationTests
{
    /// <summary>无来源的开放草稿为0/Qty，确认单为未知；缺头与软删头为空，有效空头仍有详情。</summary>
    [TestMethod]
    public Task EmptyAndDraftOrConfirmed_MaintainNullVersusZero() => InRollback(async fixture =>
    {
        Assert.IsNull(await fixture.Repository.GetDetailAsync(Guid.NewGuid()));
        await fixture.Add(fixture.Header);
        var empty = await fixture.Repository.GetDetailAsync(fixture.Header.Uid);
        Assert.IsNotNull(empty); Assert.HasCount(0, empty.Lines);
        await fixture.Add(fixture.Line);
        var draft = (await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!;
        Assert.AreEqual(0m, draft.Lines.Single().ShippedQty); Assert.AreEqual(10m, draft.Lines.Single().UnshippedQty);
        Assert.IsNull(draft.Lines.Single().ReviewReason);
        await fixture.Execute("UPDATE public.sales_order_header SET confirmation_status=1 WHERE uid=@Uid", fixture.Header.Uid);
        var confirmed = (await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!.Lines.Single();
        Assert.IsNull(confirmed.ShippedQty); Assert.IsNull(confirmed.UnshippedQty); Assert.AreEqual("来源需求缺失", confirmed.ReviewReason);
        await fixture.Execute("UPDATE public.sales_order_header SET is_deleted=TRUE WHERE uid=@Uid", fixture.Header.Uid);
        Assert.IsNull(await fixture.Repository.GetDetailAsync(fixture.Header.Uid));
        Assert.AreEqual(1, await fixture.Session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: fixture.Transaction));
    });

    /// <summary>SourceVersion是正且不大于头Version；指纹严格大写64位，原因优先于后续坏历史。</summary>
    [TestMethod]
    [DataRow("older", null)] [DataRow("equal", null)]
    [DataRow("newer", "旧来源证据缺失")] [DataRow("zero", "旧来源证据缺失")]
    [DataRow("null-version", "旧来源证据缺失")] [DataRow("null-fingerprint", "旧来源证据缺失")]
    [DataRow("lower-fingerprint", "旧来源证据缺失")] [DataRow("short-fingerprint", "旧来源证据缺失")]
    [DataRow("trailing-fingerprint", "旧来源证据缺失")]
    public Task VersionAndFingerprint_KeepSourceEvidenceGate(string variant, string? reason) => InRollback(async fixture =>
    {
        fixture.Header.ConfirmationStatus = ConfirmationStatusEnum.已确认; fixture.Header.Version = 2;
        var gross = fixture.Gross();
        gross.SourceVersion = variant switch { "older" => 1, "equal" => 2, "newer" => 3, "zero" => 0, "null-version" => null, _ => 1 };
        gross.DemandBasisFingerprint = variant switch { "null-fingerprint" => null, "lower-fingerprint" => new string('a',64),
            "short-fingerprint" => new string('A',63), "trailing-fingerprint" => new string('A',64)+" ", _ => new string('A',64) };
        await fixture.Seed(gross);
        var line = (await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!.Lines.Single();
        Assert.AreEqual(reason, line.ReviewReason);
        Assert.AreEqual(reason is null ? 0m : (decimal?)null, line.ShippedQty);
        Assert.AreEqual(reason is null ? 10m : (decimal?)null, line.UnshippedQty);
    });

    /// <summary>来源优先级和合法异常各保留原错误；关闭单允许单Voided，Active加Voided不算重复。</summary>
    [TestMethod]
    [DataRow("pending-with-source", "待确认仍有关联来源")]
    [DataRow("open-voided", "来源状态不一致")]
    [DataRow("closed-voided", null)] [DataRow("active-and-voided", null)]
    [DataRow("duplicate-voided", "来源需求重复")]
    [DataRow("closed-gross-excluded", "来源需求缺失")]
    [DataRow("deleted-gross", "来源需求缺失")]
    [DataRow("customer", "来源身份或单位不一致")]
    [DataRow("unit", "来源身份或单位不一致")]
    [DataRow("quantity", "来源身份或单位不一致")]
    [DataRow("snapshot", "来源身份或单位不一致")]
    [DataRow("due-date", "来源身份或单位不一致")]
    public Task SourceIdentityAndLifecycle_KeepReasonPriority(string variant, string? reason) => InRollback(async fixture =>
    {
        fixture.Header.ConfirmationStatus = variant == "pending-with-source" ? ConfirmationStatusEnum.待确认 : ConfirmationStatusEnum.已确认;
        if (variant is "closed-voided" or "duplicate-voided") fixture.Header.DocumentStatus = SalesOrderDocumentStatusEnum.Closed;
        var gross = fixture.Gross();
        if (variant is "open-voided" or "closed-voided" or "duplicate-voided") gross.LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided;
        if (variant == "closed-gross-excluded") gross.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        if (variant == "deleted-gross") gross.IsDeleted = true;
        if (variant == "customer") gross.CustomerId = Guid.NewGuid();
        if (variant == "unit") gross.UnitSnapshot = "KG";
        if (variant == "quantity") gross.DemandQty = 11;
        if (variant == "snapshot") gross.DescriptionSnapshot = "不同快照";
        if (variant == "due-date") gross.RequiredDate = gross.RequiredDate!.Value.AddDays(1);
        await fixture.Seed(gross);
        if (variant is "active-and-voided" or "duplicate-voided")
        { var other = fixture.Gross(); other.LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided; await fixture.Add(other); }
        var line = (await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!.Lines.Single();
        Assert.AreEqual(reason, line.ReviewReason); Assert.AreEqual(reason is null ? 0m : (decimal?)null, line.ShippedQty);
        Assert.AreEqual(reason is null ? 10m : (decimal?)null, line.UnshippedQty);
    });

    /// <summary>只Posted计净交付，反向历史正量不减第二次；超发仍保留实际交付量与零未交。</summary>
    [TestMethod]
    [DataRow("unposted", 0, 10, null)] [DataRow("posted", 4, 6, null)]
    [DataRow("reversed", 0, 10, null)] [DataRow("over", 12, 0, "超发需核实")]
    public Task PackingDelivery_KeepPostedReversedAndOverDelivery(string variant, int shipped, int remaining, string? reason) => InRollback(async fixture =>
    {
        fixture.Header.ConfirmationStatus = ConfirmationStatusEnum.已确认; var gross = fixture.Gross(); await fixture.Seed(gross);
        var head = fixture.PackingHead(); var line = fixture.PackingLine(head, gross);
        if (variant == "unposted") { head.PostingStatus = PackingListPostingStatusEnum.Unposted; head.PostedDate = null; }
        if (variant == "reversed") { head.PostingStatus = PackingListPostingStatusEnum.Reversed; head.ReversedDate = fixture.Today; }
        if (variant == "over") line.Qty = 12;
        await fixture.Add(head); await fixture.Add(line);
        var actual = (await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!.Lines.Single();
        Assert.AreEqual((decimal)shipped, actual.ShippedQty); Assert.AreEqual((decimal)remaining, actual.UnshippedQty);
        Assert.AreEqual(reason, actual.ReviewReason);
    });

    /// <summary>合法坏快照、缺头和删除历史不能被忽略；未过账坏快照也使事实未知，不绕过CHECK造数。</summary>
    [TestMethod]
    [DataRow("orphan")] [DataRow("deleted-head")] [DataRow("deleted-posted-line")]
    [DataRow("customer")] [DataRow("no-posted-date")] [DataRow("no-reversed-date")]
    [DataRow("unit")] [DataRow("sku")] [DataRow("source-type")]
    [DataRow("source-no")] [DataRow("source-line")] [DataRow("unposted-unit")]
    public Task InvalidPackingHistory_LeavesUnknownEvenOutsidePosted(string variant) => InRollback(async fixture =>
    {
        fixture.Header.ConfirmationStatus = ConfirmationStatusEnum.已确认; var gross = fixture.Gross(); await fixture.Seed(gross);
        var head = fixture.PackingHead(); var packing = fixture.PackingLine(head,gross);
        if (variant == "deleted-head") head.IsDeleted = true;
        if (variant == "deleted-posted-line") packing.IsDeleted = true;
        if (variant == "customer") head.CustomerId = Guid.NewGuid();
        if (variant == "no-posted-date") head.PostedDate = null;
        if (variant == "no-reversed-date") { head.PostingStatus = PackingListPostingStatusEnum.Reversed; head.ReversedDate = null; }
        if (variant is "unit" or "unposted-unit") packing.UnitSnapshot = "KG";
        if (variant == "unposted-unit") { head.PostingStatus = PackingListPostingStatusEnum.Unposted; head.PostedDate = null; }
        if (variant == "sku") packing.SkuSnapshot = "另一个SKU";
        if (variant == "source-type") packing.SourceTypeSnapshot = "MD";
        if (variant == "source-no") packing.SourceDocumentNoSnapshot = "另一个单号";
        if (variant == "source-line") packing.SourceLineNumberSnapshot = null;
        if (variant != "orphan") await fixture.Add(head);
        await fixture.Add(packing);
        var line = (await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!.Lines.Single();
        Assert.AreEqual("装箱过账历史异常", line.ReviewReason); Assert.IsNull(line.ShippedQty); Assert.IsNull(line.UnshippedQty);
    });

    /// <summary>读取头商业NULL回落、ETA/ETD/关闭字段和行价格金额；头/来源同会话未提交写入可见。</summary>
    [TestMethod]
    public Task MappingAndOwnWrites_MaintainCommercialContract() => InRollback(async fixture =>
    {
        fixture.Header.CustomerId = null; fixture.Header.ETA = fixture.Today.AddDays(2); fixture.Header.ETD = fixture.Today;
        fixture.Header.CloseReason = "保留关闭说明"; fixture.Header.ClosedBy = "关闭人"; fixture.Header.ClosedDate = fixture.Today;
        fixture.Line.CnyPrice = 1.25m; fixture.Line.UsdPrice = 0.5m;
        fixture.Line.CnyAmount = null; fixture.Line.UsdAmount = 99m; fixture.Line.MainImage = "原图";
        await fixture.Add(fixture.Header); await fixture.Add(fixture.Line);
        var response = (await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!;
        Assert.AreEqual(Guid.Empty,response.Header.CustomerId); Assert.AreEqual(0m,response.Header.ExchangeRate);
        Assert.AreEqual(string.Empty,response.Header.Currency); Assert.AreEqual(fixture.Header.ETA,response.Header.Eta);
        Assert.AreEqual(fixture.Header.ETD,response.Header.Etd); Assert.AreEqual("保留关闭说明",response.CloseReason);
        Assert.AreEqual("关闭人",response.ClosedBy); Assert.AreEqual(fixture.Today,response.ClosedDate);
        var line = response.Lines.Single(); Assert.AreEqual(12.5m,line.CnyAmount); Assert.AreEqual(99m,line.UsdAmount);
        Assert.AreEqual("原图",line.MainImage); Assert.AreEqual(fixture.Line.Uid,line.Uid); Assert.AreEqual(1,line.Version);
        await fixture.Execute("UPDATE public.sales_order_line SET qty=8 WHERE uid=@Uid",fixture.Line.Uid);
        Assert.AreEqual(8m,(await fixture.Repository.GetDetailAsync(fixture.Header.Uid))!.Lines.Single().UnshippedQty);
        Assert.AreSame(fixture.Transaction,fixture.Session.Transaction);
    });

    /// <summary>无外层事务可连续调用，预取消保留异常；连接复用后没有局部表或仓储事务残留。</summary>
    [TestMethod]
    public async Task WithoutOuterTransactionAndPrecancellation_KeepConnectionReusable()
    {
        using var session = Open(); var fixture = new Fixture(session,null);
        Assert.IsNull(await fixture.Repository.GetDetailAsync(Guid.NewGuid()));
        Assert.IsNull(await fixture.Repository.GetDetailAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Repository.GetDetailAsync(Guid.NewGuid(),new CancellationToken(true)));
        Assert.IsNull(session.Transaction); Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        Assert.AreEqual(0L,await session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')"));
    }

    /// <summary>固定授权库，事务只新增本例随机UID并finally回滚，查询后事务仍归调用者。</summary>
    private static async Task InRollback(Func<Fixture,Task> action)
    {
        using var session = Open(); using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { await action(new Fixture(session,transaction)); Assert.AreSame(transaction,session.Transaction); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>复用已有严格数据库门禁，不提供默认连接或输出凭据。</summary>
    private static DbSession Open() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>本例实体和SQL上下文；直接按现有元数据插入以保留UID、版本和软删除的合法历史样本。</summary>
    private sealed class Fixture
    {
        public DbSession Session { get; }
        public IDbTransaction? Transaction { get; }
        public PostgreSqlSalesOrderQueryRepository Repository { get; }
        public DateTime Today { get; } = new(2026,10,5);
        public SalesOrderHeader Header { get; }
        public SalesOrderLine Line { get; }

        /// <summary>每例仅采用随机商业键和逻辑身份，不依赖当前库基础数据。</summary>
        public Fixture(DbSession session,IDbTransaction? transaction)
        {
            Session=session; Transaction=transaction; var user=new Identity();
            Repository=new(new RepositoryBaseContext(session,NullLoggerFactory.Instance,new AuditStampService(user),user));
            Header=new() { OrderNo="PGSD"+Guid.NewGuid().ToString("N"),CustomerId=Guid.NewGuid(),DeliveryDate=Today,OrderDate=Today };
            Line=new() { SalesOrderId=Header.Uid,OrderNo=Header.OrderNo,LineNumber=1,IsNonStandard=true,
                Sku="SKU",Qty=10,UnitSnapshot="PCS",ExpectedDeliveryDate=Today };
        }

        /// <summary>合法且快照完全一致的来源，测试按需只调整明确的一项事实。</summary>
        public GrossDemand Gross() => new() { SourceType="SO",SourceUid=Header.Uid,SourceNo=Header.OrderNo,
            SourceLineId=Line.Uid,SourceLineNumber=Line.LineNumber,SourceVersion=1,DemandBasisFingerprint=new string('A',64),
            CustomerId=Header.CustomerId,IsNonStandard=true,SkuSnapshot=Line.Sku,UnitSnapshot=Line.UnitSnapshot,DemandQty=Line.Qty,RequiredDate=Today };

        /// <summary>随机箱单身份，不真实过账或引入库存流水；只验证已有读取口径。</summary>
        public PackingListHead PackingHead() => new() { PackingListNo="PGSD"+Guid.NewGuid().ToString("N"),CustomerId=Header.CustomerId!.Value,
            InvoiceDate=Today,PostingStatus=PackingListPostingStatusEnum.Posted,PostedDate=Today,PostingDate=Today,CreateCommandId=Guid.NewGuid() };

        /// <summary>正量非标合法箱单快照，避免绕过数量/产品模式约束。</summary>
        public PackingListLine PackingLine(PackingListHead head,GrossDemand gross) => new() { PackingListHeadId=head.Uid,LineNumber=1,
            GrossDemandId=gross.Uid,IsNonStandardSnapshot=true,SourceTypeSnapshot="SO",SourceDocumentNoSnapshot=Header.OrderNo,
            SourceLineNumberSnapshot=Line.LineNumber,SkuSnapshot=Line.Sku,UnitSnapshot=Line.UnitSnapshot,Qty=4 };

        /// <summary>每例按头、行、来源顺序插入，其它测试不共享可变夹具。</summary>
        public async Task Seed(GrossDemand gross) { await Add(Header); await Add(Line); await Add(gross); }

        /// <summary>直接插入所有已映射属性，保持待核验历史原始UID/Version/软删值；不改变持久化实现。</summary>
        public async Task Add<T>(T entity) where T:BaseEntity
        {
            var metadata=EntityMetadataCache.GetMetadata(typeof(T));
            var table=PostgreSqlNames.Quote(metadata.SchemaName)+"."+PostgreSqlNames.Quote(metadata.TableName);
            var columns=string.Join(',',metadata.Columns.Select(column=>PostgreSqlNames.Quote(column.ColumnName)));
            var values=string.Join(',',metadata.Columns.Select(column=>"@"+column.ParameterName));
            Assert.AreEqual(1,await Session.Connection.ExecuteAsync($"INSERT INTO {table} ({columns}) VALUES ({values})",
                PostgreSqlParameters.Entity(entity,metadata),transaction:Transaction));
        }

        /// <summary>测试只修改本例已登记UID并回滚，不接受外部SQL或目标身份。</summary>
        public Task<int> Execute(string sql,Guid uid) => Session.Connection.ExecuteAsync(sql,new { Uid=uid },transaction:Transaction);
    }

    /// <summary>无业务权限的测试审计身份，不使用真实用户。</summary>
    private sealed class Identity:ICurrentUser
    {
        public Guid? UserId=>null;
        public string UserName=>"销售Detail迁移实库";
        public bool IsAuthenticated=>false;
    }
}
