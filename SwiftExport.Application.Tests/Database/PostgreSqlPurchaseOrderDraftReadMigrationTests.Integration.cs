using System.Data;
using System.Data.SqlTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dapper;
using Npgsql;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅在既有Failure库门禁下验两个普通读取；随机夹具全在父事务中，退出回滚并用独立连接按UID核对零留存。</summary>
[TestClass, DoNotParallelize]
public sealed class PostgreSqlPurchaseOrderDraftReadMigrationIntegrationTests
{
    /// <summary>向主控显示每例初始化前的随机UID清单，不输出连接或凭据。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>空执行请求仍是候选，未删金额NULL/零及软删行依原快照合计，无行金额为零。</summary>
    [TestMethod]
    public async Task Candidate_AmountsAndEmptyExecutionRequest()
    {
        await using var f = new Fixture(TestContext);
        var empty = f.Head(); empty.ExecutionRequestId = Guid.Empty; await f.InsertAsync(empty);
        var total = f.Head(); await f.InsertAsync(total);
        var a = f.Line(total,1); a.UnitAmount = 12.25m; a.TaxAmount = null; await f.InsertAsync(a);
        var b = f.Line(total,2); b.UnitAmount = null; b.TaxAmount = 0m; await f.InsertAsync(b);
        var deleted = f.Line(total,3); deleted.IsDeleted = true; deleted.UnitAmount = 999m; deleted.TaxAmount = 999m; await f.InsertAsync(deleted);
        var rows = await f.Candidates([empty.SupplierId,total.SupplierId]);
        Assert.HasCount(2,rows); Assert.AreEqual(0m,rows.Single(x=>x.PurchaseOrderId==empty.Uid).UntaxedTotalAmount);
        Assert.AreEqual(12.25m,rows.Single(x=>x.PurchaseOrderId==total.Uid).UntaxedTotalAmount);
        Assert.IsTrue(rows.All(x=>x.TaxIncludedTotalAmount==0m));
        Assert.HasCount(0,await f.Candidates([f.Id()]));
    }

    /// <summary>候选仅原来源/供应商/草稿状态资格，不新增OriginKind/客户或请求约束。</summary>
    [TestMethod]
    public async Task Candidate_KeepQualificationsAndOrder()
    {
        await using var f = new Fixture(TestContext);
        var a = f.Head(); a.OrderNo=f.Prefix+"A"; a.OriginKind=2; a.CustomerId=null; await f.InsertAsync(a);
        var b = f.Head(); b.OrderNo=f.Prefix+"B"; b.DemandSourceType="sO "; await f.InsertAsync(b);
        var confirmed = f.Head(); confirmed.ConfirmationStatus=ConfirmationStatusEnum.已确认; await f.InsertAsync(confirmed);
        var closed = f.Head(); closed.LifecycleStatus=PurchaseOrderLifecycleStatusEnum.Closed; await f.InsertAsync(closed);
        var deleted = f.Head(); deleted.IsDeleted=true; await f.InsertAsync(deleted);
        var other = f.Head(); other.DemandSourceUid=f.Id(); await f.InsertAsync(other);
        var nullType = f.Head(); nullType.DemandSourceType=null; await f.InsertAsync(nullType);
        var nullSource = f.Head(); nullSource.DemandSourceUid=null; await f.InsertAsync(nullSource);
        var ids=new[]{a.SupplierId,b.SupplierId,confirmed.SupplierId,closed.SupplierId,deleted.SupplierId,other.SupplierId,nullType.SupplierId,nullSource.SupplierId};
        var rows=await f.Repository.GetSafeAppendCandidatesAsync("  SO  ",f.Source,ids);
        CollectionAssert.AreEqual(new[]{a.Uid,b.Uid},rows.Select(x=>x.PurchaseOrderId).ToArray());
    }

    /// <summary>采购行/入库行软删和Ledger历史按原关系保护；无入库头不解除未删行资格阻断。</summary>
    [TestMethod]
    [DataRow(false,false,false,false)]
    [DataRow(false,true,false,true)]
    [DataRow(true,false,false,false)]
    [DataRow(true,true,false,true)]
    [DataRow(false,false,true,false)]
    [DataRow(false,true,true,false)]
    [DataRow(true,false,true,false)]
    [DataRow(true,true,true,false)]
    public async Task SafeFacts_History(bool purchaseDeleted,bool receiveDeleted,bool ledger,bool eligible)
    {
        await using var f = new Fixture(TestContext);
        var head=f.Head(); await f.InsertAsync(head);
        var line=f.Line(head,1); line.IsDeleted=purchaseDeleted; await f.InsertAsync(line);
        var receive=f.Receive(line); receive.IsDeleted=receiveDeleted; await f.InsertAsync(receive);
        if(ledger) await f.InsertLedger(receive);
        Assert.AreEqual(eligible?1:0,(await f.Candidates([head.SupplierId])).Count);
    }

    /// <summary>反向历史不解除安全追加，原流水和反向流水均只在本父事务中新增并回滚。</summary>
    [TestMethod]
    public async Task SafeFacts_ReversalDoesNotRestoreQualification()
    {
        await using var f=new Fixture(TestContext); var head=f.Head(); await f.InsertAsync(head);
        var line=f.Line(head,1); line.IsDeleted=true; await f.InsertAsync(line);
        var receive=f.Receive(line); receive.IsDeleted=true; await f.InsertAsync(receive);
        var original=await f.InsertLedger(receive); await f.InsertLedger(receive,original);
        Assert.HasCount(0,await f.Candidates([head.SupplierId]));
    }

    /// <summary>批量完整44列映射，独立/确认/关闭仍返回；软删和缺失省略，三个额外字段不读取。</summary>
    [TestMethod]
    public async Task Batch_44ColumnsIndependentAndMissing()
    {
        await using var f=new Fixture(TestContext); var head=f.Head();
        head.OriginKind=2; head.ConfirmationStatus=ConfirmationStatusEnum.已确认; head.LifecycleStatus=PurchaseOrderLifecycleStatusEnum.Closed;
        head.DemandSourceType=null; head.DemandSourceUid=null; head.DemandSourceNo=null; head.CustomerId=null;
        head.ActiveConfirmationId=f.Id(); head.PackagingRequirements="不应读取";
        head.CreatedDate=new DateTime(2026,10,5,11,12,13,DateTimeKind.Unspecified); head.CreatedBy="创建甲";
        head.UpdatedDate=head.CreatedDate.Value.AddMinutes(1); head.UpdatedBy="更新乙";
        head.CustomerPoNo="客户PO"; head.BoNo="关联单"; head.CustomerCode="历史客户代码"; head.CustomerNameSnapshot="历史客户";
        head.Currency="USD"; head.DiscountRate=0.95m; head.DiscountAmount=null; head.SupplierName="历史供应商";
        head.SoPerson="业务员"; head.PoPerson="采购员"; head.InletrHoseType="软管"; head.AeratorType="起泡";
        head.PalletType="托盘"; head.BoxType="盒"; head.LabelType="标贴"; head.IMType="说明";
        head.CartonType="外箱"; head.LogoType="标识"; head.ReviewedBy="复核"; head.PoFolder="路径";
        head.ZheKouBeiZhu="商务备注"; head.Remark="中文备注"; head.ExpectedDate=null; head.ReplyDate=head.OrderDate.AddDays(1);
        head.ConfirmedDate=head.OrderDate.AddMinutes(2); head.ConfirmedBy="确认人"; await f.InsertAsync(head);
        var deleted=f.Head(); deleted.IsDeleted=true; await f.InsertAsync(deleted); var missing=f.Id();
        var row=(await f.Repository.GetPurchaseDraftsByIdsAsync([Guid.Empty,head.Uid,head.Uid,deleted.Uid,missing])).Single();
        var omitted=new[]{"OriginKind","ActiveConfirmationId","PackagingRequirements"};
        foreach(var property in typeof(PurchaseOrderHead).GetProperties().Where(x=>!omitted.Contains(x.Name)))
        {
            Assert.AreEqual(property.GetValue(head),property.GetValue(row),property.Name);
            if(property.GetValue(row) is DateTime time) Assert.AreEqual(DateTimeKind.Unspecified,time.Kind,property.Name);
        }
        Assert.AreEqual(0,row.OriginKind); Assert.IsNull(row.ActiveConfirmationId); Assert.IsNull(row.PackagingRequirements);
    }

    /// <summary>可空来源优先、来源UUID/供应商UUID/头UUID均按SqlGuid；合法唯一订单号不为排序拆索引。</summary>
    [TestMethod]
    public async Task Batch_NullableAndSqlGuidOrder()
    {
        await using var f=new Fixture(TestContext);
        var a=f.Head(); a.DemandSourceType=null; a.DemandSourceUid=null; await f.InsertAsync(a);
        var b=f.Head(); b.DemandSourceUid=null; await f.InsertAsync(b);
        var c=f.Head(); c.DemandSourceUid=Guid.Parse("ffffffff-ffff-ffff-ffff-000000000001"); await f.InsertAsync(c);
        var d=f.Head(); d.DemandSourceUid=Guid.Parse("00000000-0000-0000-0000-000000000002"); await f.InsertAsync(d);
        var e=f.Head(); e.DemandSourceUid=d.DemandSourceUid; await f.InsertAsync(e);
        var sameSupplier=f.Head(); sameSupplier.DemandSourceUid=d.DemandSourceUid; sameSupplier.SupplierId=d.SupplierId; await f.InsertAsync(sameSupplier);
        var heads=new[]{a,b,c,d,e,sameSupplier};
        var expected=heads.OrderBy(x=>x.DemandSourceType is null?0:1)
            .ThenBy(x=>x.DemandSourceUid.HasValue?1:0)
            .ThenBy(x=>x.DemandSourceUid.HasValue?new SqlGuid(x.DemandSourceUid.Value):SqlGuid.Null)
            .ThenBy(x=>new SqlGuid(x.SupplierId)).ThenBy(x=>new SqlGuid(x.Uid)).Select(x=>x.Uid).ToArray();
        var rows=await f.Repository.GetPurchaseDraftsByIdsAsync(heads.Select(x=>x.Uid));
        CollectionAssert.AreEqual(expected,rows.Select(x=>x.Uid).ToArray());
        var sample=new[]{Guid.Parse("ffffffff-ffff-ffff-ffff-000000000001"),Guid.Parse("00000000-0000-0000-0000-000000000002")};
        // 同OrderNo的真实头受有效唯一索引禁止；以无持久化VALUES表达式独立核对候选尾键权重。
        var key="ARRAY["+string.Join(',',new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(x=>$"get_byte(uuid_send(v.uid),{x})"))+"]";
        var order=(await f.Session.Connection.QueryAsync<Guid>(new CommandDefinition($"SELECT v.uid FROM (VALUES (@A::uuid),(@B::uuid)) v(uid) ORDER BY {key}",new{A=sample[0],B=sample[1]},f.Session.Transaction))).ToArray();
        CollectionAssert.AreEqual(sample.OrderBy(x=>new SqlGuid(x)).ToArray(),order);
    }

    /// <summary>真正无父事务仅普通查询缺失随机身份；不会为测试提交夹具或创建租约。</summary>
    [TestMethod]
    public async Task NoParent_ContinuousEmptyReads()
    {
        await using var f=new Fixture(TestContext,false);
        for(var i=0;i<2;i++)
        {
            Assert.HasCount(0,await f.Candidates([f.Id()]));
            Assert.HasCount(0,await f.Repository.GetPurchaseDraftsByIdsAsync([f.Id()]));
            Assert.IsNull(f.Session.Transaction);
        }
    }

    /// <summary>普通查询预取消仍由驱动传播；调用者父事务保留且可继续使用。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Cancellation_PreservesParent(bool candidate)
    {
        await using var f=new Fixture(TestContext); using var cancel=new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>{
            if(candidate) await f.Repository.GetSafeAppendCandidatesAsync("SO",f.Source,[f.Id()],cancel.Token);
            else await f.Repository.GetPurchaseDraftsByIdsAsync([f.Id()],cancel.Token);
        });
        Assert.IsNotNull(f.Session.Transaction);
        Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:f.Session.Transaction)));
    }

    /// <summary>所有可能插入UID提前公示；夹具只使用已有表/约束，父回滚后独立observer核对实际插入UID。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string connection;
        private readonly Queue<Guid> reserved=new(Enumerable.Range(0,96).Select(_=>Guid.NewGuid()));
        private readonly Dictionary<Type,List<Guid>> inserted=[];
        public readonly string Prefix="DR"+Guid.NewGuid().ToString("N");
        public readonly Guid Source;
        public readonly DbSession Session;
        public readonly PostgreSqlPurchaseOrderDraftRepository Repository;
        /// <summary>先通过专用库门禁并显示身份清单，随后打开会话；父事务仅归本测试夹具所有。</summary>
        public Fixture(TestContext output,bool parent=true)
        {
            connection=PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
            output.WriteLine("初始化前随机UID清单："+string.Join(',',reserved));
            Source=Id(); Session=new(new NativeFactory(connection));
            try
            {
                if(parent)Session.Transaction=Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
                var identity=new Identity(); Repository=new(new RepositoryBaseContext(Session,NullLoggerFactory.Instance,new AuditStampService(identity),identity));
            }
            catch { Session.Dispose(); throw; }
        }
        /// <summary>从初始化前公示集合取身份，不修改既有数据。</summary>
        public Guid Id()=>reserved.Dequeue();
        /// <summary>独立订单号/请求/供应商满足当前唯一范围；来源与状态只用于本普通查询夹具。</summary>
        public PurchaseOrderHead Head()=>new(){Uid=Id(),OriginKind=1,OrderNo=Prefix+Id().ToString("N"),SupplierId=Id(),SupplierCode="随机供应商",
            ExecutionRequestId=Id(),DemandSourceType="SO",DemandSourceUid=Source,OrderDate=new DateTime(2026,10,5,13,14,15,DateTimeKind.Unspecified)};
        /// <summary>正行号/正数量通过真实CHECK，金额默认NULL；不新增非法历史数量。</summary>
        public PurchaseOrderLine Line(PurchaseOrderHead head,int number)=>new(){Uid=Id(),PurchaseOrderId=head.Uid,OrderNo=head.OrderNo,LineNumber=number,Quantity=1m,Sku=Prefix+"SKU",UnitSnapshot="PCS"};
        /// <summary>仅需要原SQL关系；故意不建入库头以证明其存在性不是SafeFacts资格。</summary>
        public InboundDocumentLine Receive(PurchaseOrderLine line)=>new(){Uid=Id(),InboundDocumentId=Id(),LineNumber=1,ProductId=Id(),SkuSnapshot=Prefix+"SKU",UnitSnapshot="PCS",Quantity=1m,SourceLineId=line.Uid};
        /// <summary>所有插入使用同一父事务及明确实体列；无事务夹具禁止写，不改约束、提交或清理旧行。</summary>
        public async Task InsertAsync<T>(T entity) where T:BaseEntity
        {
            Assert.IsNotNull(Session.Transaction); var map=EntityMetadataCache.GetMetadata(typeof(T));
            if(!inserted.TryGetValue(typeof(T),out var ids)) inserted.Add(typeof(T),ids=[]);
            ids.Add(entity.Uid);
            var columns=string.Join(',',map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));
            var values=string.Join(',',map.Columns.Select(x=>"@"+x.ParameterName));
            Assert.AreEqual(1,await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {Table(map)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(entity,map),Session.Transaction)));
        }
        /// <summary>流水仅新增本父事务事实，Sequence保留数据库非回滚消耗；不UPDATE/DELETE不可变事实。</summary>
        public async Task<InventoryLedgerEntry> InsertLedger(InboundDocumentLine receive,InventoryLedgerEntry? original=null)
        {
            var sequence=await Session.Connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT nextval('public.inventory_ledger_sequence')",transaction:Session.Transaction));
            var fact=new InventoryLedgerEntry{Uid=Id(),Sequence=sequence,WarehouseSiteId=original?.WarehouseSiteId??Id(),ProductId=receive.ProductId,
                TransactionType=original is null?InventoryTransactionTypeEnum.PurchaseReceiptInbound:InventoryTransactionTypeEnum.Reversal,
                QuantityDelta=original is null?1m:-1m,BalanceAfter=original is null?1m:0m,SourceDocumentType="Inbound",
                SourceDocumentId=receive.InboundDocumentId,SourceLineId=receive.Uid,ReversalOfLedgerId=original?.Uid,
                CommandId=Id(),CommandItemId=Id(),OccurredDate=new DateTime(2026,10,5,13,14,15,DateTimeKind.Unspecified),UnitSnapshot="PCS"};
            await InsertAsync(fact); return fact;
        }
        /// <summary>通过本例独立来源与供应商身份读取，保留可空父事务。</summary>
        public Task<IReadOnlyList<SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.SafePurchaseDraftCandidateDto>> Candidates(IEnumerable<Guid> suppliers)=>
            Repository.GetSafeAppendCandidatesAsync("SO",Source,suppliers);
        /// <summary>先回滚/释放父事务，再以独立连接检查四类真实写入UID零残留；不执行DELETE或DDL。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if(Session.Transaction is {} tx){try{tx.Rollback();}finally{tx.Dispose();Session.Transaction=null;}}
            }
            finally
            {
                // 即使显式回滚/事务释放失败，也先关闭会话，再尝试独立核对，不能跳过零留存检查。
                try { Session.Dispose(); }
                finally { await ObserveRollbackAsync(); }
            }
        }
        /// <summary>独立连接只读本例实际插入UID，失败也不以清理写入掩盖残留。</summary>
        private async Task ObserveRollbackAsync()
        {
            await using var observer=new NpgsqlConnection(connection); await observer.OpenAsync();
            foreach(var pair in inserted)
            {
                var map=EntityMetadataCache.GetMetadata(pair.Key);
                var remaining=await observer.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COUNT(*) FROM {Table(map)} WHERE {PostgreSqlNames.Quote(map.KeyName)}=ANY(@Uids)",PostgreSqlParameters.Ids(pair.Value)));
                Assert.AreEqual(0L,remaining,pair.Key.Name+"父回滚后有残留");
            }
        }
        /// <summary>仅引用真实实体元数据，不拼外部表名。</summary>
        private static string Table(EntityMetadata map)=>PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName);
    }
    /// <summary>门禁已核验的连接由DbSession管理，不提供默认凭据或初始化。</summary>
    private sealed class NativeFactory(string connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection()=>new NpgsqlConnection(connection);
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;
    }
    /// <summary>固定测试身份，不读取桌面登录。</summary>
    private sealed class Identity : ICurrentUser
    {
        public string UserName=>"采购草稿普通读取回滚夹具";
        public Guid? UserId=>null;
        public bool IsAuthenticated=>false;
    }
}
