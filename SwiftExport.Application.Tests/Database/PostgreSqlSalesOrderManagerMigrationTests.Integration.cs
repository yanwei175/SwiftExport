using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>Manager真实PG验收独立夹具；只在已授权故障库新增随机身份并回滚，不执行DDL或提交。</summary>
[TestClass, DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlSalesOrderManagerMigrationIntegrationTests
{
    /// <summary>九种QuickView与未知值在八种业务事实中按源定义取交集；混合单位不自动进入核实视图。</summary>
    [TestMethod]
    [DataRow(0,8)] [DataRow(1,3)] [DataRow(2,4)] [DataRow(3,1)] [DataRow(4,1)]
    [DataRow(5,1)] [DataRow(6,1)] [DataRow(7,1)] [DataRow(8,2)] [DataRow(99,0)]
    public Task QuickViews_KeepAllSourcePredicates(int quick,int count) => InRollback(async f =>
    {
        var customer=f.Header.CustomerId;
        foreach(var mode in new[]{"draft","none","partial","full","missing","closed","empty","mixed"})
        {
            var x=new Fixture(f.Session,f.Transaction);x.Header.CustomerId=customer;
            if(mode is not ("draft" or "empty" or "mixed"))x.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认;
            if(mode=="closed")x.Header.DocumentStatus=SalesOrderDocumentStatusEnum.Closed;
            if(mode=="none")x.Line.ExpectedDeliveryDate=x.Today.AddDays(7);
            if(mode is "partial" or "full")x.Line.ExpectedDeliveryDate=x.Today.AddDays(-1);
            await x.Add(x.Header);if(mode=="empty")continue;await x.Add(x.Line);
            if(mode=="mixed")await x.Add(new SalesOrderLine{Uid=Guid.NewGuid(),SalesOrderId=x.Header.Uid,OrderNo=x.Header.OrderNo,
                LineNumber=2,IsNonStandard=true,Sku="SKU2",Qty=2,UnitSnapshot="KG",ExpectedDeliveryDate=x.Today});
            if(mode is "draft" or "missing" or "mixed")continue;
            var gross=x.Gross();gross.RequiredDate=x.Line.ExpectedDeliveryDate;
            if(mode=="closed")gross.LifecycleStatus=GrossDemandLifecycleStatusEnum.Voided;
            await x.Add(gross);
            if(mode is "partial" or "full")
            {var head=x.PackingHead();var line=x.PackingLine(head,gross);line.Qty=mode=="full"?10:4;await x.Add(head);await x.Add(line);}
        }
        var result=await f.Repository.SearchManagerAsync(new(){CustomerId=customer,QuickView=(SalesOrderManagerQuickView)quick,PageSize=200},f.Today.AddHours(19));
        Assert.AreEqual(count,result.TotalCount);Assert.HasCount(count,result.Items);
        if(quick==8)CollectionAssert.AreEquivalent(new[]{"来源需求缺失","无有效销售行"},result.Items.Select(x=>x.ReviewReason).ToArray());
    });

    /// <summary>数量门槛与核实门槛分别生效；超发保留12/0，多单位、缺单位或未知不能汇总。</summary>
    [TestMethod]
    [DataRow("over")] [DataRow("mixed")] [DataRow("missing-unit")] [DataRow("unknown")] [DataRow("empty")] [DataRow("draft")]
    public Task QuantitiesAndReview_KeepDistinctGates(string mode) => InRollback(async f =>
    {
        if(mode is "over" or "unknown")f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认;
        if(mode=="missing-unit")f.Line.UnitSnapshot="   ";
        await f.Add(f.Header);if(mode!="empty")await f.Add(f.Line);
        if(mode=="mixed")await f.Add(new SalesOrderLine{Uid=Guid.NewGuid(),SalesOrderId=f.Header.Uid,OrderNo=f.Header.OrderNo,
            LineNumber=2,IsNonStandard=true,Sku="SKU2",Qty=2,UnitSnapshot="KG"});
        if(mode=="over")
        {var gross=f.Gross();await f.Add(gross);var head=f.PackingHead();var line=f.PackingLine(head,gross);line.Qty=12;await f.Add(head);await f.Add(line);}
        var item=(await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId},f.Today)).Items.Single();
        if(mode is "draft" or "over")
        {Assert.AreEqual(10m,item.TotalQty);Assert.AreEqual(mode=="over"?12m:0m,item.TotalShippedQty);Assert.AreEqual(mode=="over"?0m:10m,item.TotalUnshippedQty);}
        else {Assert.IsNull(item.TotalQty);Assert.IsNull(item.TotalShippedQty);Assert.IsNull(item.TotalUnshippedQty);Assert.IsNull(item.UnitSnapshot);}
        Assert.AreEqual(mode is "over" or "unknown" or "empty",item.NeedsReview);
        if(mode=="over"){Assert.AreEqual("超发需核实",item.ReviewReason);Assert.IsNull(item.FulfillmentStatus);}
        if(mode=="empty"){Assert.AreEqual("无有效销售行",item.ReviewReason);Assert.IsFalse(item.HasUndatedOpenLine);}
        Assert.AreEqual(0m,item.ExchangeRate);Assert.AreEqual(string.Empty,item.Currency);Assert.AreEqual(0m,item.TotalAmountLocalCurrency);
    });

    /// <summary>页排序使用SqlGuid源序，独立总数保留超末页；空候选仍返回合法空页。</summary>
    [TestMethod]
    public Task Paging_KeepSqlGuidTieOrderAndCountBeyondLastPage() => InRollback(async f =>
    {
        var ids=new List<Guid>();
        for(var i=0;i<3;i++)
        {var x=new Fixture(f.Session,f.Transaction);x.Header.CustomerId=f.Header.CustomerId;x.Header.CreatedDate=f.Today;ids.Add(x.Header.Uid);await x.Add(x.Header);}
        var expected=ids.OrderByDescending(x=>new SqlGuid(x)).ToArray();
        for(var page=1;page<=3;page++)
        {var result=await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId,PageIndex=page,PageSize=1},f.Today);Assert.AreEqual(3,result.TotalCount);Assert.AreEqual(expected[page-1],result.Items.Single().Uid);}
        var beyond=await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId,PageIndex=99,PageSize=1},f.Today);
        Assert.AreEqual(3,beyond.TotalCount);Assert.HasCount(0,beyond.Items);
        var empty=await f.Repository.SearchManagerAsync(new(){CustomerId=Guid.NewGuid()},f.Today);Assert.AreEqual(0,empty.TotalCount);Assert.HasCount(0,empty.Items);
    });

    /// <summary>通用搜索覆盖头、当前客户与有效行字段，删除行和删除客户不能贡献搜索匹配。</summary>
    [TestMethod]
    [DataRow("order")] [DataRow("code")] [DataRow("po")] [DataRow("invoice")] [DataRow("customer")]
    [DataRow("sku")] [DataRow("customer-sku")] [DataRow("link-sku")] [DataRow("description")]
    [DataRow("deleted-line")] [DataRow("deleted-customer")]
    public Task Keyword_KeepEveryColumnAndDeletionBoundary(string mode) => InRollback(async f =>
    {
        var keyword="KEY"+Guid.NewGuid().ToString("N");
        if(mode=="order")f.Header.OrderNo+=keyword;
        if(mode=="code")f.Header.CustomerCode=keyword;
        if(mode=="po")f.Header.CustomerPoNo=keyword;
        if(mode=="invoice")f.Header.InvoiceNo=keyword;
        if(mode is "customer" or "deleted-customer")await f.Add(new Customer{Uid=f.Header.CustomerId!.Value,
            CustomerCode=Guid.NewGuid().ToString("N"),DocRelateNo=Guid.NewGuid().ToString("N"),CustomerName=keyword,IsDeleted=mode=="deleted-customer"});
        if(mode is "sku" or "deleted-line")f.Line.Sku=keyword;
        if(mode=="customer-sku")f.Line.CustomerSku=keyword;
        if(mode=="link-sku")f.Line.LinkSku=keyword;
        if(mode=="description")f.Line.DescriptionEn=keyword;
        f.Line.IsDeleted=mode=="deleted-line";await f.Add(f.Header);await f.Add(f.Line);
        var result=await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId,Keyword=keyword.ToLowerInvariant()},f.Today);
        Assert.AreEqual(mode is "deleted-line" or "deleted-customer"?0:1,result.TotalCount);
    });

    /// <summary>原因在SQL内去重和字典排序，不按行顺序拼接，也不因部分已知数量消除未知总量。</summary>
    [TestMethod]
    public Task MultipleReviewReasons_KeepDistinctDictionaryOrder() => InRollback(async f =>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认;await f.Add(f.Header);await f.Add(f.Line);
        for(var number=2;number<=3;number++)
        {await f.Add(new SalesOrderLine{Uid=Guid.NewGuid(),SalesOrderId=f.Header.Uid,OrderNo=f.Header.OrderNo,LineNumber=number,
            IsNonStandard=true,Sku="SKU",Qty=10,UnitSnapshot="PCS",ExpectedDeliveryDate=f.Today});}
        var gross=f.Gross();gross.SourceVersion=0;await f.Add(gross);
        var item=(await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId},f.Today)).Items.Single();
        var expected=await f.Session.Connection.ExecuteScalarAsync<string>("SELECT string_agg(reason,'、' ORDER BY reason COLLATE public.erp_dictionary_ci) FROM (VALUES ('旧来源证据缺失'),('来源需求缺失')) reasons(reason)",transaction:f.Transaction);
        Assert.AreEqual(expected,item.ReviewReason);Assert.IsTrue(item.NeedsReview);Assert.IsNull(item.TotalQty);Assert.IsNull(item.TotalShippedQty);
    });

    /// <summary>单位大小写和尾ASCII空格按源字典相等；零订单量在源与PG均非法，只验证约束拒绝，不伪称查询通过。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public Task UnitComparisonAndZero_KeepDictionaryAndUndatedFlags(bool zero) => InRollback(async f =>
    {
        f.Header.DeliveryDate=null;f.Line.ExpectedDeliveryDate=null;if(zero)f.Line.Qty=0;
        await f.Add(f.Header);
        if(zero)
        {
            var failure=await Assert.ThrowsAsync<PostgresException>(()=>f.Add(f.Line));
            Assert.AreEqual("23514",failure.SqlState);Assert.AreEqual("ck_sales_order_line_qty",failure.ConstraintName);
            return; // CHECK失败后的事务交由夹具finally回滚，不放宽数据库规则或继续查询已失败事务。
        }
        await f.Add(f.Line);
        await f.Add(new SalesOrderLine{Uid=Guid.NewGuid(),SalesOrderId=f.Header.Uid,OrderNo=f.Header.OrderNo,LineNumber=2,
            IsNonStandard=true,Sku="SKU2",Qty=2,UnitSnapshot="pcs ",ExpectedDeliveryDate=null});
        var item=(await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId},f.Today)).Items.Single();
        Assert.AreEqual(12m,item.TotalQty);Assert.AreEqual(0m,item.TotalShippedQty);Assert.AreEqual(12m,item.TotalUnshippedQty);
        Assert.IsFalse(item.NeedsReview);Assert.IsTrue(item.HasUndatedOpenLine);Assert.IsNull(item.EarliestOpenDueDate);
    });

    /// <summary>合法正订单量全部交付后未交为零，即使无交期也不是未定开放行；不是以非法零Qty替代业务事实。</summary>
    [TestMethod]
    public Task FullyDeliveredPositiveQuantity_KeepsZeroOutstandingAndNoUndatedOpenLine() => InRollback(async f =>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认;
        f.Header.DeliveryDate=null;f.Line.ExpectedDeliveryDate=null;
        await f.Add(f.Header);await f.Add(f.Line);
        var gross=f.Gross();gross.RequiredDate=null;await f.Add(gross);
        var packing=f.PackingHead();var packed=f.PackingLine(packing,gross);packed.Qty=f.Line.Qty;
        await f.Add(packing);await f.Add(packed);
        var item=(await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId},f.Today)).Items.Single();
        Assert.AreEqual(10m,item.TotalQty);Assert.AreEqual(10m,item.TotalShippedQty);Assert.AreEqual(0m,item.TotalUnshippedQty);
        Assert.IsFalse(item.NeedsReview);Assert.IsFalse(item.HasUndatedOpenLine);Assert.IsNull(item.EarliestOpenDueDate);
    });

    /// <summary>最终客户名取当前值，NULL客户身份回落Guid.Empty且不改变冻结草稿数量。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public Task CustomerProjection_KeepCurrentNameAndEmptyIdentity(bool emptyIdentity) => InRollback(async f =>
    {
        var customer=f.Header.CustomerId!.Value;
        if(emptyIdentity)f.Header.CustomerId=null;
        else await f.Add(new Customer{Uid=customer,CustomerCode=Guid.NewGuid().ToString("N"),DocRelateNo=Guid.NewGuid().ToString("N"),CustomerName="候选客户"});
        await f.Add(f.Header);await f.Add(f.Line);var hook=(HookConnection)f.Session.Connection;var stage=0;
        hook.Before=async (_,_)=>{if(stage++!=3||emptyIdentity)return;hook.Before=null;await f.Execute("UPDATE public.customer SET customer_name='当前客户' WHERE uid=@Uid",customer);};
        var item=(await f.Repository.SearchManagerAsync(new(){CustomerId=emptyIdentity?Guid.Empty:customer,OrderNo=f.Header.OrderNo},f.Today)).Items.Single();hook.Before=null;
        Assert.AreEqual(emptyIdentity?Guid.Empty:customer,item.CustomerId);Assert.AreEqual(emptyIdentity?string.Empty:"当前客户",item.CustomerName);Assert.AreEqual(10m,item.TotalQty);
    });

    /// <summary>候选普通筛选取交集，字面LIKE不Trim；ASCII空格与问号保留通配，特殊字符按源转义。</summary>
    [TestMethod]
    [DataRow("[ab]","[ab]",true)] [DataRow("100%_~","100%_~",true)]
    [DataRow("ABCXDEF","abc?def",true)] [DataRow("ABC-middle-DEF","ABC DEF",true)]
    [DataRow("ABC"," ABC ",true)] [DataRow("ABC","\tABC\t",false)] [DataRow("safe","' OR TRUE --",false)]
    public Task LiteralFilters_KeepCaseAndSourceWildcards(string text,string search,bool matches) => InRollback(async f =>
    {
        f.Header.CustomerPoNo=text;f.Header.CustomerCode="CODE";f.Line.CustomerSku="CSKU";await f.Add(f.Header);await f.Add(f.Line);
        var result=await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId,CustomerPoNo=search,
            CustomerCodes=[" code ","CODE"," "],CustomerSkus=[" csku ","CSKU"],
            DocumentStatuses=[f.Header.DocumentStatus],ConfirmationStatuses=[f.Header.ConfirmationStatus]},f.Today);
        Assert.AreEqual(matches?1:0,result.TotalCount);
    });

    /// <summary>订单日按日截断，有效交期行优先；仅起点/终点参数也具有明确PG类型。</summary>
    [TestMethod]
    [DataRow("both",true)] [DataRow("from",true)] [DataRow("to",true)] [DataRow("head-only",false)]
    public Task DateFilters_KeepDayBoundsAndLinePriority(string mode,bool matches) => InRollback(async f =>
    {
        f.Header.OrderDate=f.Today.AddHours(23);f.Header.DeliveryDate=f.Today.AddDays(-2);f.Line.ExpectedDeliveryDate=f.Today.AddDays(7);
        await f.Add(f.Header);await f.Add(f.Line);var due=mode=="head-only"?f.Today.AddDays(-2):f.Today.AddDays(7);
        var request=new SalesOrderManagerSearchRequest{CustomerId=f.Header.CustomerId,OrderDateFrom=f.Today.AddHours(19),OrderDateTo=f.Today.AddHours(1)};
        if(mode!="to")request.EffectiveDeliveryDateFrom=due.AddHours(12);
        if(mode!="from")request.EffectiveDeliveryDateTo=due.AddHours(12);
        Assert.AreEqual(matches?1:0,(await f.Repository.SearchManagerAsync(request,f.Today)).TotalCount);
    });

    /// <summary>四阶段允许混合观察：行事实看到候选后的写入，count保留冻结状态，最终页读取当前软删头。</summary>
    [TestMethod]
    public Task StageChanges_KeepFrozenFactsAndCurrentCommercialPage() => InRollback(async f =>
    {
        await f.Add(f.Header);await f.Add(f.Line);var hook=(HookConnection)f.Session.Connection;var stage=0;
        Func<string,CancellationToken,Task>? barrier=null;
        barrier=async (_,_)=>
        {
            var current=stage++;hook.Before=null;
            try
            {
                if(current==1)await f.Execute("UPDATE public.sales_order_line SET qty=8 WHERE uid=@Uid",f.Line.Uid);
                if(current==2)
                {await f.Execute("UPDATE public.sales_order_line SET qty=17 WHERE uid=@Uid",f.Line.Uid);await f.Execute("UPDATE public.sales_order_header SET confirmation_status=1 WHERE uid=@Uid",f.Header.Uid);}
                if(current==3)await f.Execute("UPDATE public.sales_order_header SET is_deleted=TRUE,invoice_no='当前商业页' WHERE uid=@Uid",f.Header.Uid);
            }
            finally{hook.Before=barrier;}
        };
        hook.Before=barrier;
        var response=await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId,QuickView=SalesOrderManagerQuickView.PendingConfirmation},f.Today);
        hook.Before=null;Assert.AreEqual(4,stage);Assert.AreEqual(1,response.TotalCount);var item=response.Items.Single();
        Assert.AreEqual(8m,item.TotalQty);Assert.AreEqual(8m,item.TotalUnshippedQty);Assert.AreEqual(0m,item.TotalShippedQty);
        Assert.AreEqual(ConfirmationStatusEnum.已确认,item.ConfirmationStatus);Assert.AreEqual("当前商业页",item.InvoiceNo);
        Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:f.Transaction));
    });

    /// <summary>有效头进入count后物理移除不会回算count；起始软删头仍被候选阶段排除。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public Task DeletedHeads_KeepCandidateVersusPageBoundary(bool initiallyDeleted) => InRollback(async f =>
    {
        f.Header.IsDeleted=initiallyDeleted;await f.Add(f.Header);var hook=(HookConnection)f.Session.Connection;var stage=0;
        hook.Before=async (_,_)=>{if(stage++!=3||initiallyDeleted)return;hook.Before=null;await f.Execute("DELETE FROM public.sales_order_header WHERE uid=@Uid",f.Header.Uid);};
        var result=await f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId},f.Today);hook.Before=null;
        Assert.AreEqual(initiallyDeleted?0:1,result.TotalCount);Assert.HasCount(0,result.Items);
    });

    /// <summary>取消原样传播且外层事务继续可用，仓储不取消、提交或释放调用方事务。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public Task Cancellation_KeepEachBoundaryAndCallerOwnership(int boundary) => InRollback(async f =>
    {
        await f.Add(f.Header);await f.Add(f.Line);var hook=(HookConnection)f.Session.Connection;var stage=0;using var c=new CancellationTokenSource();
        if(boundary<0)c.Cancel();else hook.Before=(_,_)=>{if(stage++==boundary)c.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchManagerAsync(new(){CustomerId=f.Header.CustomerId},f.Today,c.Token));
        hook.Before=null;Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:f.Transaction));
    });

    /// <summary>无外层事务借用连接可连续读取空身份；仓储不创建临时表或遗留事务。</summary>
    [TestMethod]
    public async Task WithoutOuterTransaction_KeepReusableConnection()
    {
        using var session=Open();var f=new Fixture(session,null);var request=new SalesOrderManagerSearchRequest{CustomerId=Guid.NewGuid()};
        for(var i=0;i<2;i++){var result=await f.Repository.SearchManagerAsync(request,f.Today);Assert.AreEqual(0,result.TotalCount);Assert.HasCount(0,result.Items);}
        Assert.IsNull(session.Transaction);Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        Assert.AreEqual(0L,await session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')"));
    }

    /// <summary>固定授权库、单例随机夹具；finally只回滚本事务，不清库或删除他人身份。</summary>
    private static async Task InRollback(Func<Fixture,Task> action)
    {
        using var session=Open();using var transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);session.Transaction=transaction;
        try{await action(new Fixture(session,transaction));Assert.AreSame(transaction,session.Transaction);}
        finally{try{transaction.Rollback();}finally{session.Transaction=null;}}
    }

    /// <summary>只通过现有严格故障库门禁打开真实包装连接，不输出或覆盖连接配置。</summary>
    private static DbSession Open()=>new(new HookFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

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
            Header=new() { Uid=Guid.NewGuid(), OrderNo="PGSD"+Guid.NewGuid().ToString("N"),CustomerId=Guid.NewGuid(),DeliveryDate=Today,OrderDate=Today };
            Line=new() { Uid=Guid.NewGuid(), SalesOrderId=Header.Uid,OrderNo=Header.OrderNo,LineNumber=1,IsNonStandard=true,
                Sku="SKU",Qty=10,UnitSnapshot="PCS",ExpectedDeliveryDate=Today };
        }

        /// <summary>合法且快照完全一致的来源，测试按需只调整明确的一项事实。</summary>
        public GrossDemand Gross() => new() { Uid=Guid.NewGuid(), SourceType="SO",SourceUid=Header.Uid,SourceNo=Header.OrderNo,
            SourceLineId=Line.Uid,SourceLineNumber=Line.LineNumber,SourceVersion=1,DemandBasisFingerprint=new string('A',64),
            CustomerId=Header.CustomerId,IsNonStandard=true,SkuSnapshot=Line.Sku,UnitSnapshot=Line.UnitSnapshot,DemandQty=Line.Qty,RequiredDate=Today };

        /// <summary>随机箱单身份，不真实过账或引入库存流水；只验证已有读取口径。</summary>
        public PackingListHead PackingHead() => new() { Uid=Guid.NewGuid(), PackingListNo="PGSD"+Guid.NewGuid().ToString("N"),CustomerId=Header.CustomerId!.Value,
            InvoiceDate=Today,PostingStatus=PackingListPostingStatusEnum.Posted,PostedDate=Today,PostingDate=Today,CreateCommandId=Guid.NewGuid() };

        /// <summary>正量非标合法箱单快照，避免绕过数量/产品模式约束。</summary>
        public PackingListLine PackingLine(PackingListHead head,GrossDemand gross) => new() { Uid=Guid.NewGuid(), PackingListHeadId=head.Uid,LineNumber=1,
            GrossDemandId=gross.Uid,IsNonStandardSnapshot=true,SourceTypeSnapshot="SO",SourceDocumentNoSnapshot=Header.OrderNo,
            SourceLineNumberSnapshot=Line.LineNumber,SkuSnapshot=Line.Sku,UnitSnapshot=Line.UnitSnapshot,Qty=4 };

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
        public string UserName=>"销售Manager迁移实库";
        public bool IsAuthenticated=>false;
    }
    /// <summary>仅替代连接入口以观察真实命令，仍使用原Npgsql连接及错误分类。</summary>
    private sealed class HookFactory(string connectionString):IDbConnectionFactory
    {
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;
        public IDbConnection CreateConnection()=>new HookConnection(new NpgsqlConnection(connectionString));
    }

    /// <summary>包装真实驱动连接，阶段屏障在命令执行前且没有活动reader时运行。</summary>
    private sealed class HookConnection(NpgsqlConnection inner):DbConnection
    {
        public Func<string,CancellationToken,Task>? Before;
        public readonly List<string> Commands=[];
        [AllowNull] public override string ConnectionString{get=>inner.ConnectionString;set=>inner.ConnectionString=value;}
        public override string Database=>inner.Database; public override string DataSource=>inner.DataSource;public override string ServerVersion=>inner.ServerVersion;public override ConnectionState State=>inner.State;
        public override void Open()=>inner.Open();public override void Close()=>inner.Close();public override void ChangeDatabase(string name)=>inner.ChangeDatabase(name);
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>inner.BeginTransaction(level);
        protected override DbCommand CreateDbCommand()=>new HookCommand(this,inner.CreateCommand());
        protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
        /// <summary>完整转发参数、事务与token；屏障用于修改随机夹具或取消，不模拟查询返回。</summary>
        private sealed class HookCommand(HookConnection owner,NpgsqlCommand innerCommand):DbCommand
        {
            [AllowNull]public override string CommandText{get=>innerCommand.CommandText;set=>innerCommand.CommandText=value;}
            public override int CommandTimeout{get=>innerCommand.CommandTimeout;set=>innerCommand.CommandTimeout=value;}public override CommandType CommandType{get=>innerCommand.CommandType;set=>innerCommand.CommandType=value;}
            public override bool DesignTimeVisible{get=>innerCommand.DesignTimeVisible;set=>innerCommand.DesignTimeVisible=value;}public override UpdateRowSource UpdatedRowSource{get=>innerCommand.UpdatedRowSource;set=>innerCommand.UpdatedRowSource=value;}
            protected override DbConnection? DbConnection{get=>owner;set{}}protected override DbTransaction? DbTransaction{get=>innerCommand.Transaction;set=>innerCommand.Transaction=(NpgsqlTransaction?)value;}
            protected override DbParameterCollection DbParameterCollection=>innerCommand.Parameters;protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
            public override void Cancel()=>innerCommand.Cancel();public override void Prepare()=>innerCommand.Prepare();
            /// <summary>在没有活动reader时先执行测试屏障，再记录实际SQL；取消不转换为业务Guard。</summary>
            private async Task Starting(CancellationToken token){if(owner.Before is {} before)await before(CommandText,token);token.ThrowIfCancellationRequested();owner.Commands.Add(CommandText);}
            public override int ExecuteNonQuery()=>innerCommand.ExecuteNonQuery();public override object? ExecuteScalar()=>innerCommand.ExecuteScalar();
            /// <summary>先完成测试阶段屏障，再以真实Npgsql执行夹具写入，事务仍由夹具拥有。</summary>
            public override async Task<int> ExecuteNonQueryAsync(CancellationToken token){await Starting(token);return await innerCommand.ExecuteNonQueryAsync(token);}
            /// <summary>先完成测试阶段屏障，再执行真实Guard或标量验证，不模拟返回值。</summary>
            public override async Task<object?> ExecuteScalarAsync(CancellationToken token){await Starting(token);return await innerCommand.ExecuteScalarAsync(token);}
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>innerCommand.ExecuteReader(behavior);
            /// <summary>在reader打开之前完成阶段屏障，然后原样传递token和驱动reader。</summary>
            protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){await Starting(token);return await innerCommand.ExecuteReaderAsync(behavior,token);}
            protected override void Dispose(bool disposing){if(disposing)innerCommand.Dispose();base.Dispose(disposing);}
        }
    }
}
