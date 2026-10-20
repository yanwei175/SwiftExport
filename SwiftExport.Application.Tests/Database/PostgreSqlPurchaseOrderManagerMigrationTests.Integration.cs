using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Infrastructure.Repositories.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>管理查询真实PG证据交主控串行运行；每例随机身份、ReadCommitted且finally回滚，不初始化/提交。</summary>
[DoNotParallelize]
public sealed partial class PostgreSqlPurchaseOrderManagerMigrationTests
{
    /// <summary>全局坏反向或孤儿历史即使与请求无关也阻断两入口，第一道异常保持优先。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration"),DataRow(false,false),DataRow(true,false),DataRow(false,true),DataRow(true,true)]
    public Task RealPg_GlobalGates(bool lines,bool reversed)=>Rollback(async f=>
    {
        await f.Seed();await f.Receipt(Guid.NewGuid(),1,"PCS",reversed,proof:false);
        Func<Task> read=()=>lines?f.Repository.GetLinesAsync(Guid.NewGuid()):f.Repository.SearchAsync(new(){CustomerCodes=["不存在"]});
        if(reversed)await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(read);
        else Assert.AreEqual("存在无法定位采购行的正式采购入库历史，请先完成数据核实与迁移。",(await Assert.ThrowsAsync<InvalidOperationException>(read)).Message);
    });

    /// <summary>已删但存在的采购行仍能定位正式历史，不要求采购行未删；空活动头状态保持0。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    public Task RealPg_DeletedPurchaseLineIsTraceable()=>Rollback(async f=>
    {
        f.Line.IsDeleted=true;await f.Seed();await f.Receipt(f.Line.Uid,2);
        var item=(await f.Search()).Single();Assert.AreEqual(0,item.LineCount);Assert.AreEqual((PurchaseReadyStatusEnum)0,item.ReadyStatus);Assert.AreEqual((PurchaseReceiptStatusEnum)0,item.ReceiptStatus);
        Assert.IsNull(item.ReadyQty);Assert.IsNull(item.TotalQty);Assert.IsNull(item.ReceiptProgress);Assert.HasCount(0,await f.Repository.GetLinesAsync(f.Head.Uid));
    });

    /// <summary>所有货好CASE优先级和数量遮罩在真实SQL执行，NULL报告和无报告是不同状态。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    [DataRow("none",1)][DataRow("null",2)][DataRow("zero",3)][DataRow("part",4)][DataRow("full",5)][DataRow("over",9)]
    public Task RealPg_ReadyStates(string mode,int status)=>Rollback(async f=>
    {
        await f.Seed(); decimal? ready=mode switch{"null"=>null,"zero"=>0,"part"=>4,"full"=>10,"over"=>11,_=>null};
        if(mode!="none")await f.Report(f.Line,ready,1);
        var item=(await f.Search()).Single();Assert.AreEqual((PurchaseReadyStatusEnum)status,item.ReadyStatus);Assert.AreEqual(mode=="none"?0:1,item.ReportedLineCount);
        Assert.AreEqual(mode is "none" or "null" or "over"?null:ready,item.ReadyQty);Assert.AreEqual(10m,item.TotalQty);Assert.AreEqual(0m,item.ReceiptProgress);
        Assert.AreEqual((PurchaseReceiptStatusEnum)1,item.ReceiptStatus);Assert.AreEqual(mode=="none"?"部分采购行尚未反馈货好数量。":mode=="null"?"部分采购行尚未反馈货好数量。":mode=="over"?"采购数量、单位、货好或收货事实不一致，需逐行核实。":null,item.ReviewReason);
        var filtered=await f.Repository.SearchAsync(f.Request(readyStatus:status));Assert.AreEqual(1,filtered.TotalCount);
        var excluded=await f.Repository.SearchAsync(f.Request(readyStatus:status==1?5:1));Assert.AreEqual(0,excluded.TotalCount);
    });

    /// <summary>正式Posted贡献、完整Reversed零贡献，超量/单位异常只遮罩头，从表真实数量不隐藏。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    [DataRow("none",1)][DataRow("part",2)][DataRow("full",3)][DataRow("over",9)][DataRow("mismatch",9)][DataRow("reversed",1)][DataRow("nullunit",9)]
    public Task RealPg_ReceiptStates(string mode,int status)=>Rollback(async f=>
    {
        await f.Seed();var qty=mode=="full"?10m:mode=="over"?11m:4m;
        if(mode!="none")await f.Receipt(f.Line.Uid,qty,mode=="mismatch"?"KG":mode=="nullunit"?null:"PCS",mode=="reversed");
        var item=(await f.Search()).Single();var received=mode is "none" or "reversed"?0:qty;
        Assert.AreEqual((PurchaseReceiptStatusEnum)status,item.ReceiptStatus);Assert.AreEqual(status==9?null:(decimal?)(received/10),item.ReceiptProgress);
        Assert.AreEqual(10m,item.TotalQty);var actual=(await f.Repository.GetLinesAsync(f.Head.Uid)).Single();
        Assert.AreEqual(received,actual.ReceivedQty);Assert.AreEqual(Math.Max(10-received,0),actual.UnreceivedQty);Assert.AreEqual(10m,actual.Quantity);Assert.IsNull(actual.ReadyQty);
        Assert.AreEqual(1,(await f.Repository.SearchAsync(f.Request(receiptStatus:status))).TotalCount);
    });

    /// <summary>单位差异遮罩三个跨行合计，NULL基础单位优先；NULL三值不擅自当单位差异。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration"),DataRow("different"),DataRow("null"),DataRow("space"),DataRow("trailing")]
    public Task RealPg_UnitMasks(string mode)=>Rollback(async f=>
    {
        await f.Seed();var second=f.NewLine(2);second.UnitSnapshot=mode switch{"different"=>"KG","null"=>null,"space"=>"   ",_=>"PCS "};await f.Add(second);
        await f.Report(f.Line,10,1);await f.Report(second,10,1);var item=(await f.Search()).Single();
        if(mode=="trailing"){Assert.AreEqual(20m,item.TotalQty);Assert.AreEqual(20m,item.ReadyQty);Assert.AreEqual(0m,item.ReceiptProgress);}
        else{Assert.IsNull(item.TotalQty);Assert.IsNull(item.ReadyQty);Assert.IsNull(item.ReceiptProgress);Assert.IsNull(item.UnitSnapshot);}
        Assert.AreEqual((PurchaseReadyStatusEnum)(mode is "null" or "space"?9:5),item.ReadyStatus);Assert.HasCount(2,await f.Repository.GetLinesAsync(f.Head.Uid));
    });

    /// <summary>最新整行报告不被多个需求Link倍增，已删更新序号忽略；独立采购不依赖需求链。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    public Task RealPg_LatestWholeLineAndMultipleLinks()=>Rollback(async f=>
    {
        await f.Seed();await f.Report(f.Line,2,1);await f.Report(f.Line,7,2);await f.Report(f.Line,10,3,deleted:true);await f.Receipt(f.Line.Uid,3);
        var gross=new GrossDemand{SourceType="MD",SourceUid=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),SourceNo=f.Head.OrderNo,IsNonStandard=true,SkuSnapshot="MAT",DemandQty=10};await f.Add(gross);
        var uid=Guid.NewGuid();var net=new NetDemand{Uid=uid,GrossDemandId=gross.Uid,RootNetDemandId=uid,LineNumber=1,DecompositionRevision=1,SupplyTypeSnapshot="采购",SkuSnapshot="MAT",GrossQty=10,NetQty=10};await f.Add(net);
        foreach(var allocated in new[]{2m,3m})await f.Add(new DemandExecutionLink{NetDemandId=net.Uid,ExecutionRequestId=Guid.NewGuid(),CommandItemId=Guid.NewGuid(),TargetType="PO",TargetHeadId=f.Head.Uid,TargetLineId=f.Line.Uid,TargetNo=f.Head.OrderNo,AllocatedQty=allocated});
        var item=(await f.Search()).Single();Assert.AreEqual(10m,item.TotalQty);Assert.AreEqual(7m,item.ReadyQty);Assert.AreEqual(.3m,item.ReceiptProgress);Assert.AreEqual(1,item.LineCount);
        var line=(await f.Repository.GetLinesAsync(f.Head.Uid)).Single();Assert.AreEqual(7m,line.ReadyQty);Assert.AreEqual(3m,line.ReceivedQty);Assert.AreEqual(7m,line.UnreceivedQty);Assert.AreEqual("反馈人",line.FollowUpUserName);
    });

    /// <summary>Unknown先于零/全量计数，全部零/全部满与混合分别得到源整单状态；预计完成取最新整行报告的MAX。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration"),DataRow("unknown",2),DataRow("zero",3),DataRow("full",5),DataRow("mixed",4)]
    public Task RealPg_MixedReadyPriority(string mode,int status)=>Rollback(async f=>
    {
        await f.Seed();var second=f.NewLine(2);await f.Add(second);
        var first=mode=="zero"?0m:10m;decimal? next=mode=="unknown"?null:mode=="full"?10m:0m;
        await f.Report(f.Line,first,1,f.Today.AddDays(1));await f.Report(second,next,1,f.Today.AddDays(2));
        var item=(await f.Search()).Single();Assert.AreEqual((PurchaseReadyStatusEnum)status,item.ReadyStatus);Assert.AreEqual(20m,item.TotalQty);
        var expectedDate=f.Today.AddDays(2);Assert.AreEqual(mode=="unknown"?null:(decimal?)(first+next),item.ReadyQty);Assert.AreEqual(expectedDate,item.ExpectedCompletionDate);Assert.AreEqual(2,item.ReportedLineCount);
    });

    /// <summary>北京时间Due精确窗口、活动已确认门槛和未知Ready风险，满量货好不命中。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    [DataRow(-1,1,true)][DataRow(0,1,false)][DataRow(0,2,true)][DataRow(7,2,true)][DataRow(8,2,false)]
    public Task RealPg_DueBoundaries(int day,int view,bool hit)=>Rollback(async f=>
    {
        f.Line.PlannedDeliveryDate=f.Today.AddDays(day);await f.Seed();var request=f.Request();request.DueView=(PurchaseDueViewEnum)view;
        var first=await f.Repository.SearchAsync(request);Assert.AreEqual(hit?1:0,first.TotalCount);
        if(hit){var row=first.Items.Single();Assert.AreEqual(1,row.DueHitLineCount);Assert.IsTrue(row.HasUnconfirmedReadyRisk);Assert.AreEqual(f.Line.PlannedDeliveryDate,row.EarliestDueHitDate);}
        await f.Report(f.Line,10,1);Assert.AreEqual(0,(await f.Repository.SearchAsync(request)).TotalCount);
    });

    /// <summary>交期风险不包含待确认或关闭/作废头；商业按钮依源活动/确认CASE。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration"),DataRow(0,0),DataRow(1,8),DataRow(1,9)]
    public Task RealPg_StatusAndUnlinkedEligibility(int confirmation,int lifecycle)=>Rollback(async f=>
    {
        f.Head.ConfirmationStatus=(ConfirmationStatusEnum)confirmation;f.Head.LifecycleStatus=(PurchaseOrderLifecycleStatusEnum)lifecycle;f.Line.PlannedDeliveryDate=f.Today.AddDays(-1);await f.Seed();
        var manager=new PostgreSqlPurchaseOrderManagerQueryRepository(new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(new WorkspaceUser()),new WorkspaceUser()));
        var item=(await manager.SearchAsync(new(){CustomerCode=f.Head.CustomerCode})).Items.Single();Assert.AreEqual(lifecycle==0&&confirmation==0,item.CanConfirm);Assert.AreEqual(lifecycle==0&&confirmation==1,item.CanCancelConfirmation);
        Assert.AreEqual(lifecycle==0?null:"采购单不是活动状态。",item.CommandBlockedReason);var request=f.Request();request.DueView=(PurchaseDueViewEnum)1;Assert.AreEqual(0,(await f.Repository.SearchAsync(request)).TotalCount);
        var managerRequest=new PurchaseOrderManagerSearchRequest{CustomerCode=f.Head.CustomerCode,OnlyWithUnlinkedQuantity=true};Assert.AreEqual(lifecycle==0?1:0,(await manager.SearchAsync(managerRequest)).TotalCount);
        await f.Execute("UPDATE public.purchase_order_line SET unlinked_qty=0 WHERE uid=@Uid",f.Line.Uid);Assert.AreEqual(0,(await manager.SearchAsync(managerRequest)).TotalCount);
    });

    /// <summary>OrderDate自然日包含，计划/完成原DateTime闭区间；不套进度页次日排他。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    public Task RealPg_TimeBoundsAndLiteralSearch()=>Rollback(async f=>
    {
        var time=f.Today.AddHours(16);f.Head.OrderDate=time;f.Line.PlannedDeliveryDate=time;f.Line.Sku="A~%_[";await f.Seed();await f.Report(f.Line,4,1,time);
        var r=f.Request();r.OrderDateFrom=f.Today;r.OrderDateTo=f.Today;r.PlannedDeliveryDateFrom=time;r.PlannedDeliveryDateTo=time;r.ExpectedCompletionDateFrom=time;r.ExpectedCompletionDateTo=time;r.Keyword=" A~%_[ ";
        Assert.AreEqual(1,(await f.Repository.SearchAsync(r)).TotalCount);r.PlannedDeliveryDateTo=time.AddTicks(-10);Assert.AreEqual(0,(await f.Repository.SearchAsync(r)).TotalCount);
        r.PlannedDeliveryDateTo=time;r.ExpectedCompletionDateFrom=time.AddTicks(10);Assert.AreEqual(0,(await f.Repository.SearchAsync(r)).TotalCount);
        r.ExpectedCompletionDateFrom=time;r.OrderDateTo=DateTime.MaxValue;Assert.AreEqual(1,(await f.Repository.SearchAsync(r)).TotalCount);
    });

    /// <summary>同时间/单号条件的源UUID排序及整工作集分页，返回后同事务变化不能改旧计数/商业结果。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    public Task RealPg_FrozenPagesAndLaterChange()=>Rollback(async f=>
    {
        await f.Seed();var other=f.NewHead();other.CustomerCode=f.Head.CustomerCode;other.OrderDate=f.Head.OrderDate;await f.Add(other);
        var r=f.Request();r.PageIndex=2;r.PageSize=1;var frozen=await f.Repository.SearchAsync(r);Assert.AreEqual(2,frozen.TotalCount);Assert.HasCount(1,frozen.Items);
        var held=frozen.Items.Single();await f.Execute("UPDATE public.purchase_order_head SET is_deleted=TRUE WHERE uid=@Uid",held.PurchaseOrderId);
        Assert.AreEqual(2,frozen.TotalCount);Assert.AreEqual(held.OrderNo,frozen.Items.Single().OrderNo);Assert.AreEqual(1,(await f.Repository.SearchAsync(r)).TotalCount);
    });

    /// <summary>图片来自当前产品且软删后NULL，非标ProductId为空返回Guid.Empty；送达名只客供件。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    public Task RealPg_CurrentImageAndNullableLineFields()=>Rollback(async f=>
    {
        // 源与PG产品SupplyType均非空；图片夹具补齐合法采购事实，不能靠非法NULL检验查询。
        var p=new Product{Sku="PGPM"+Guid.NewGuid().ToString("N"),SupplyType="采购",MainImage="原图片"};await f.Add(p);f.Line.ProductId=p.Uid;f.Line.MaterialType="客供件";f.Line.DeliverySupplierNameSnapshot="送达名";await f.Seed();
        var line=(await f.Repository.GetLinesAsync(f.Head.Uid)).Single();Assert.AreEqual("原图片",line.MainImage);Assert.AreEqual("送达名",line.DeliverySupplierName);Assert.IsNull(line.ReadyQty);Assert.IsNull(line.UnitPrice);
        await f.Execute("UPDATE public.product SET main_image='新图片' WHERE uid=@Uid",p.Uid);Assert.AreEqual("新图片",(await f.Repository.GetLinesAsync(f.Head.Uid)).Single().MainImage);
        await f.Execute("UPDATE public.product SET is_deleted=TRUE WHERE uid=@Uid",p.Uid);Assert.IsNull((await f.Repository.GetLinesAsync(f.Head.Uid)).Single().MainImage);
        var other=f.NewLine(2);other.DeliverySupplierNameSnapshot="不可返回";await f.Add(other);var nonstandard=(await f.Repository.GetLinesAsync(f.Head.Uid)).Single(x=>x.PurchaseOrderLineId==other.Uid);Assert.AreEqual(Guid.Empty,nonstandard.ProductId);Assert.IsNull(nonstandard.DeliverySupplierName);
    });

    /// <summary>0数量属于现有CHECK拒绝证据，不绕约束造夹具；保存点仅恢复已中止的本测试事务。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    public Task RealPg_ZeroQuantityIsConstraintEvidence()=>Rollback(async f=>
    {
        await f.Seed();var invalid=f.NewLine(2);invalid.Quantity=0;await f.Session.Connection.ExecuteAsync("SAVEPOINT manager_quantity",transaction:f.Transaction);
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>f.Add(invalid));Assert.AreEqual(PostgresErrorCodes.CheckViolation,ex.SqlState);StringAssert.Contains(ex.ConstraintName!,"quantity");
        await f.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT manager_quantity",transaction:f.Transaction);Assert.AreEqual(1,(await f.Search()).Single().LineCount);
    });

    /// <summary>可空事务以及预取消复用真实驱动；不声称服务器等待或物理跨连接取消已验。</summary>
    [TestMethod,TestCategory("PostgreSqlPurchaseManagerIntegration")]
    public async Task RealPg_OptionalTransactionAndPreCancellation()
    {
        using var session=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));var f=new ManagerFixture(session,null);
        Assert.HasCount(0,await f.Repository.GetLinesAsync(Guid.NewGuid()));Assert.AreEqual(0,(await f.Repository.SearchAsync(f.Request())).TotalCount);
        var token=new CancellationToken(true);await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetLinesAsync(Guid.NewGuid(),token));await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchAsync(f.Request(),token));
        Assert.IsNull(session.Transaction);Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
    }

    /// <summary>严格故障库门禁，随机夹具只在现有结构中写读；始终finally回滚且释放父事务。</summary>
    private static async Task Rollback(Func<ManagerFixture,Task> action)
    {
        using var session=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));using var transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);session.Transaction=transaction;
        try{await action(new(session,transaction));Assert.AreSame(transaction,session.Transaction);}finally{try{transaction.Rollback();}finally{session.Transaction=null;}}
    }

    /// <summary>本测试独占合法随机实体，直接元数据INSERT保留NULL/软删/版本，不通过业务API制造额外事实。</summary>
    private sealed class ManagerFixture
    {
        public DbSession Session{get;} public IDbTransaction? Transaction{get;} public TrackingRepository Repository{get;}
        public DateTime Today{get;}=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"China Standard Time").Date;
        public PurchaseOrderHead Head{get;} public PurchaseOrderLine Line{get;}
        /// <summary>随机客户编码限定本例工作集，客户/供应商仅逻辑身份。</summary>
        public ManagerFixture(DbSession session,IDbTransaction? transaction)
        {
            Session=session;Transaction=transaction;var user=new WorkspaceUser();Repository=new(new RepositoryBaseContext(session,NullLoggerFactory.Instance,new AuditStampService(user),user));
            Head=NewHead();Line=NewLine(1);
        }
        /// <summary>独立采购合法唯一请求/单号，不依赖需求Link。</summary>
        public PurchaseOrderHead NewHead()=>new(){OriginKind=2,OrderNo="PGPM"+Guid.NewGuid().ToString("N"),CustomerCode="PGPM"+Guid.NewGuid().ToString("N"),SupplierId=Guid.NewGuid(),SupplierCode="PGPM",SupplierName="供应商",ExecutionRequestId=Guid.NewGuid(),OrderDate=Today,ConfirmationStatus=ConfirmationStatusEnum.已确认};
        /// <summary>合法正数非标行，未关联量等于采购量。</summary>
        public PurchaseOrderLine NewLine(int number)=>new(){PurchaseOrderId=Head.Uid,OrderNo=Head.OrderNo,LineNumber=number,Quantity=10,UnlinkedQty=10,Sku="MAT",UnitSnapshot="PCS",TaxAmount=12.3456m};
        /// <summary>头先于行插入，未初始化其他模块。</summary>
        public async Task Seed(){await Add(Head);await Add(Line);}
        /// <summary>只限定本例客户编码；可选真实商业状态筛选。</summary>
        public PurchaseOrderTrackingSearchRequest Request(int? readyStatus=null,int? receiptStatus=null)=>new(){CustomerCodes=[Head.CustomerCode!],PageSize=200,ReadyStatuses=readyStatus.HasValue?[(PurchaseReadyStatusEnum)readyStatus.Value]:[],ReceiptStatuses=receiptStatus.HasValue?[(PurchaseReceiptStatusEnum)receiptStatus.Value]:[]};
        /// <summary>读本例全工作集，不C#模拟数量算法。</summary>
        public async Task<List<PurchaseOrderTrackingItemDto>> Search()=> (await Repository.SearchAsync(Request())).Items;
        /// <summary>唯一序号及命令的整行报告；软删样本仍遵守索引。</summary>
        public Task Report(PurchaseOrderLine line,decimal? ready,int sequence,DateTime? expected=null,bool deleted=false)=>Add(new SupplierProgressReport{PurchaseOrderLineId=line.Uid,ReadyQty=ready,Sequence=sequence,SupplierReportedDate=Today,ExpectedReadyDate=expected,CommandId=Guid.NewGuid(),CreatedBy="反馈人",IsDeleted=deleted});
        /// <summary>正式正数入库，可选合法成对反向流水；坏证明只省略证明，不绕CHECK。</summary>
        public async Task Receipt(Guid source,decimal quantity,string? unit="PCS",bool reversed=false,bool proof=true)
        {
            var h=new InboundDocument{DocumentNo="PGPM"+Guid.NewGuid().ToString("N"),DocumentType=InboundDocumentTypeEnum.PurchaseReceipt,PostingStatus=reversed?InboundPostingStatusEnum.Reversed:InboundPostingStatusEnum.Posted,
                WarehouseSiteId=Guid.NewGuid(),ReceiptDate=Today,PostedDate=Today,ReversedDate=reversed?Today.AddHours(1):null,ReverseReason=reversed?"测试反向":null};
            var l=new InboundDocumentLine{InboundDocumentId=h.Uid,LineNumber=1,ProductId=Guid.NewGuid(),SkuSnapshot="MAT",UnitSnapshot=unit,Quantity=quantity,SourceLineId=source};await Add(h);await Add(l);
            if(!reversed||!proof)return;
            var original=new InventoryLedgerEntry{WarehouseSiteId=h.WarehouseSiteId,ProductId=l.ProductId,TransactionType=(InventoryTransactionTypeEnum)10,QuantityDelta=quantity,BalanceAfter=quantity,SourceDocumentType="InboundDocument",SourceDocumentId=h.Uid,SourceLineId=l.Uid,CommandId=Guid.NewGuid(),CommandItemId=l.Uid,UnitSnapshot=unit,OccurredDate=Today};
            await Add(original);await Add(new InventoryLedgerEntry{WarehouseSiteId=h.WarehouseSiteId,ProductId=l.ProductId,TransactionType=(InventoryTransactionTypeEnum)900,QuantityDelta=-quantity,SourceDocumentType="InboundDocument",SourceDocumentId=h.Uid,SourceLineId=l.Uid,ReversalOfLedgerId=original.Uid,CommandId=Guid.NewGuid(),CommandItemId=Guid.NewGuid(),UnitSnapshot=unit,OccurredDate=Today});
        }
        /// <summary>直接按冻结映射插入合法本例实体，不打戳或更改约束。</summary>
        public async Task Add<T>(T entity) where T:BaseEntity
        {
            // 流水Sequence有唯一索引，随机正值仅用于本例历史证明，不消耗数据库序列或复用0。
            if(entity is InventoryLedgerEntry ledger && ledger.Sequence==0)ledger.Sequence=BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
            var m=EntityMetadataCache.GetMetadata(typeof(T));var columns=string.Join(',',m.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));var values=string.Join(',',m.Columns.Select(x=>"@"+x.ParameterName));
            Assert.AreEqual(1,await Session.Connection.ExecuteAsync($"INSERT INTO public.{PostgreSqlNames.Quote(m.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(entity,m),Transaction));
        }
        /// <summary>只修改本例已登记UID，所有变化由父事务回滚。</summary>
        public Task<int> Execute(string sql,Guid uid)=>Session.Connection.ExecuteAsync(sql,new{Uid=uid},Transaction);
    }
}
