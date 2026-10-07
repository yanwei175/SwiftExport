using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>销售履约真实PG用例由主控串行执行；随机夹具只在ReadCommitted事务内新增并finally回滚。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed partial class PostgreSqlSalesOrderFulfillmentMigrationIntegrationTests
{
    /// <summary>草稿无来源为零/Qty，已确认缺来源为未知；缺头为空，有效空头仍返回详情。</summary>
    [TestMethod]
    public Task RealPg_EmptyDraftConfirmedAndNullableCommercial()=>InRollback(async f=>
    {
        Assert.IsNull(await f.Repository.GetFulfillmentAsync(Guid.NewGuid())); await f.Add(f.Header);
        Assert.HasCount(0,(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines); await f.Add(f.Line);
        var draft=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!; var line=draft.Lines.Single();
        Assert.AreEqual(0m,line.ShippedQty); Assert.AreEqual(10m,line.UnshippedQty); Assert.AreEqual(10m,line.AllowedFurtherDeliveryQty);
        Assert.AreEqual("待确认",line.SourceState); Assert.AreEqual("未关联",line.PurchaseState);
        Assert.AreEqual("",draft.CustomerCode); Assert.AreEqual("",draft.CustomerName); Assert.AreEqual("",draft.InvoiceNo);
        Assert.AreEqual(DateTimeKind.Unspecified,draft.OrderDate!.Value.Kind); Assert.AreEqual(f.Today,draft.OrderDate);
        await f.Execute("UPDATE public.sales_order_header SET confirmation_status=1 WHERE uid=@Uid",f.Header.Uid);
        var unknown=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines.Single();
        Assert.IsNull(unknown.ShippedQty); Assert.IsNull(unknown.UnshippedQty); Assert.IsNull(unknown.AllowedFurtherDeliveryQty);
        Assert.IsNull(unknown.GrossDemandId); Assert.AreEqual("来源需求缺失",unknown.ReviewReason); Assert.AreEqual("未知／需核实",unknown.PurchaseState);
        await f.Execute("UPDATE public.sales_order_header SET is_deleted=TRUE WHERE uid=@Uid",f.Header.Uid);
        Assert.IsNull(await f.Repository.GetFulfillmentAsync(f.Header.Uid)); Assert.AreEqual(1,await f.Scalar());
    });

    /// <summary>可信Posted计净量，Reversed仅历史；超发仍关联Gross，关闭单只把允许继续交付置零。</summary>
    [TestMethod,DataRow("posted",4,6,6),DataRow("reversed",0,10,10),DataRow("over",12,0,0),DataRow("closed",4,6,0)]
    public Task RealPg_DeliveryHistoryAndClosed(string mode,int shipped,int unshipped,int allowed)=>InRollback(async f=>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认;
        if(mode=="closed")f.Header.DocumentStatus=SalesOrderDocumentStatusEnum.Closed;
        var gross=f.Gross(); await f.Seed(gross); var head=f.PackingHead(); var packing=f.PackingLine(head,gross);
        if(mode=="reversed") { head.PostingStatus=PackingListPostingStatusEnum.Reversed; head.ReversedDate=f.Today.AddHours(1); }
        if(mode=="over")packing.Qty=12;
        await f.Add(head); await f.Add(packing); var actual=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines.Single();
        Assert.AreEqual(gross.Uid,actual.GrossDemandId); Assert.AreEqual((decimal)shipped,actual.ShippedQty);
        Assert.AreEqual((decimal)unshipped,actual.UnshippedQty); Assert.AreEqual((decimal)allowed,actual.AllowedFurtherDeliveryQty);
        Assert.AreEqual(mode=="over"?"超发需核实":"",actual.ReviewReason); Assert.AreEqual("已关联",actual.SourceState);
        var history=actual.Deliveries.Single(); Assert.AreEqual(packing.Uid,history.PackingListLineId);
        Assert.AreEqual(packing.Qty,history.Qty); Assert.AreEqual(mode!="reversed",history.CountsTowardDelivery);
        Assert.AreEqual(head.PostedDate,history.PostedDate); Assert.AreEqual(head.ReversedDate,history.ReversedDate); Assert.AreEqual(1,await f.Scalar());
    });

    /// <summary>坏来源/历史原因优先，禁止将无可信来源身份传给采购/历史；不绕约束构造坏快照。</summary>
    [TestMethod,DataRow("fingerprint","旧来源证据缺失"),DataRow("source-unit","来源身份或单位不一致"),DataRow("packing-unit","装箱过账历史异常")]
    public Task RealPg_BadSourceKeepsUnknown(string mode,string reason)=>InRollback(async f=>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; var gross=f.Gross();
        if(mode=="fingerprint")gross.DemandBasisFingerprint=new string('a',64); if(mode=="source-unit")gross.UnitSnapshot="KG";
        await f.Seed(gross); var head=f.PackingHead(); var packing=f.PackingLine(head,gross); if(mode=="packing-unit")packing.UnitSnapshot="KG";
        await f.Add(head); await f.Add(packing); var line=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines.Single();
        Assert.AreEqual(reason,line.ReviewReason); Assert.AreEqual("需核实",line.SourceState); Assert.AreEqual("未知／需核实",line.PurchaseState);
        Assert.IsNull(line.GrossDemandId); Assert.IsNull(line.ShippedQty); Assert.IsNull(line.UnshippedQty); Assert.HasCount(0,line.Deliveries);
    });

    /// <summary>整采购行数量/最新反馈不分摊；同采购行多销售来源只分别显示各自关联量，并标共享。</summary>
    [TestMethod]
    public Task RealPg_WholePurchaseLineLatestProgressAndMultiLineRouting()=>InRollback(async f=>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; var gross=f.Gross(); await f.Seed(gross); var (net,po,pol,link)=await f.Purchase(gross);
        var second=new SalesOrderLine { SalesOrderId=f.Header.Uid,OrderNo=f.Header.OrderNo,LineNumber=2,IsNonStandard=true,Sku="SKU",Qty=10,UnitSnapshot="PCS",ExpectedDeliveryDate=f.Today };
        var other=f.Gross(); other.SourceLineId=second.Uid; other.SourceLineNumber=2;
        await f.Add(second); await f.Add(other); var otherNet=f.Net(other,1); await f.Add(otherNet); await f.Add(f.Link(otherNet,po,pol,3,1));
        await f.Add(new SupplierProgressReport { PurchaseOrderLineId=pol.Uid,Sequence=1,ReadyQty=2,SupplierReportedDate=f.Today,CommandId=Guid.NewGuid() });
        await f.Add(new SupplierProgressReport { PurchaseOrderLineId=pol.Uid,Sequence=2,ReadyQty=19.1234m,ExpectedReadyDate=f.Today.AddDays(2),SupplierReportedDate=f.Today.AddHours(3),CommandId=Guid.NewGuid() });
        var detail=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!; Assert.HasCount(2,detail.Lines);
        var first=detail.Lines.Single(x=>x.SalesOrderLineId==f.Line.Uid).Purchases.Single(); var next=detail.Lines.Single(x=>x.SalesOrderLineId==second.Uid).Purchases.Single();
        Assert.AreEqual(6m,first.LinkedQty); Assert.AreEqual(2m,next.LinkedQty);
        var expectedReadyDate=f.Today.AddDays(2); var expectedReportedDate=f.Today.AddHours(3);
        foreach(var p in new[]{first,next}) { Assert.AreEqual(20.1234m,p.PurchaseLineQty); Assert.AreEqual(19.1234m,p.PurchaseLineReadyQty); Assert.IsTrue(p.HasProgress); Assert.IsTrue(p.IsSharedPurchaseLine);
            Assert.AreEqual(expectedReadyDate,p.ExpectedReadyDate); Assert.AreEqual(expectedReportedDate,p.SupplierReportedDate); Assert.AreEqual("",p.ReviewReason); }
        // 同Sequence被有效唯一索引禁止；不绕约束，源SqlGuid DESC尾键另由离线入口命令断言保留。
        Assert.AreEqual(1,await f.Scalar());
    });

    /// <summary>当前产品单位优先，根才可回落Gross；子行不能用Gross或自身旧UnitSnapshot补证据。</summary>
    [TestMethod,DataRow("root-fallback",false),DataRow("product-current",false),DataRow("product-mismatch",true),
     DataRow("child-no-product",true),DataRow("product-blank-root",false),DataRow("product-deleted-root",false),DataRow("purchase-blank",true)]
    public Task RealPg_CurrentProductAndRootFallback(string mode,bool bad)=>InRollback(async f=>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; var gross=f.Gross(); await f.Seed(gross);
        var net=f.Net(gross,1); net.UnitSnapshot="WRONG-OLD";
        var po=f.Order(); var pol=f.PurchaseLine(po); var unit=mode=="product-current"?"KG":mode=="product-blank-root"?"   ":"KG";
        if(mode.StartsWith("product",StringComparison.Ordinal)) { var p=new Product { Sku="PGSF"+Guid.NewGuid().ToString("N"),SupplyType="采购",Unit=unit,IsDeleted=mode=="product-deleted-root" }; await f.Add(p); net.ProductId=p.Uid; }
        if(mode=="product-current")pol.UnitSnapshot=" KG ";
        if(mode=="purchase-blank")pol.UnitSnapshot="   ";
        if(mode=="child-no-product") { var parent=f.Net(gross,2); await f.Add(parent); net.ParentNetDemandId=parent.Uid; net.RootNetDemandId=parent.Uid; }
        await f.Add(net); await f.Add(po); await f.Add(pol); await f.Add(f.Link(net,po,pol));
        var actual=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines.Single(); var purchase=actual.Purchases.Single();
        Assert.AreEqual(bad?(decimal?)null:6m,purchase.LinkedQty); Assert.AreEqual(bad?"采购行与物料单位需核实":"",purchase.ReviewReason);
        Assert.AreEqual(bad?"需核实":"已关联",actual.PurchaseState); Assert.AreEqual(bad?"采购关联证据异常":"",actual.PurchaseReviewReason);
    });

    /// <summary>同采购行聚合根/子净贡献，只要一条当前单位异常就整组未知；不擅自退回净单位快照。</summary>
    [TestMethod]
    public Task RealPg_RootAndChildGroupedUnitEvidence()=>InRollback(async f=>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; var gross=f.Gross(); await f.Seed(gross);
        var (root,po,pol,link)=await f.Purchase(gross); var product=new Product { Sku="PGSF"+Guid.NewGuid().ToString("N"),Unit="PCS",SupplyType="采购" }; await f.Add(product);
        var child=f.Net(gross,2); child.ParentNetDemandId=root.Uid; child.RootNetDemandId=root.Uid; child.ProductId=product.Uid; child.UnitSnapshot="OLD-WRONG";
        await f.Add(child); await f.Add(f.Link(child,po,pol,2,0));
        var before=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines.Single(); Assert.HasCount(1,before.Purchases); Assert.AreEqual(8m,before.Purchases.Single().LinkedQty);
        await f.Execute("UPDATE public.product SET unit='KG' WHERE uid=@Uid",product.Uid);
        var after=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines.Single(); Assert.HasCount(1,after.Purchases); Assert.IsNull(after.Purchases.Single().LinkedQty);
        Assert.AreEqual("采购行与物料单位需核实",after.Purchases.Single().ReviewReason); Assert.AreEqual("采购关联证据异常",after.PurchaseReviewReason);
    });

    /// <summary>日期NULL与未反馈数量保留未知，HasProgress与ReadyQty零/NULL相互独立。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public Task RealPg_NullDatesAndProgress(bool report)=>InRollback(async f=>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; f.Header.OrderDate=null; f.Header.DeliveryDate=null; f.Line.ExpectedDeliveryDate=null;
        var gross=f.Gross(); gross.RequiredDate=null; await f.Seed(gross); var (_,_,pol,_)=await f.Purchase(gross);
        if(report)await f.Add(new SupplierProgressReport { PurchaseOrderLineId=pol.Uid,Sequence=1,ReadyQty=null,ExpectedReadyDate=null,SupplierReportedDate=f.Today,CommandId=Guid.NewGuid() });
        var actual=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!; Assert.IsNull(actual.OrderDate); Assert.IsNull(actual.DeliveryDate);
        var line=actual.Lines.Single(); Assert.IsNull(line.EffectiveDueDate); var purchase=line.Purchases.Single(); Assert.AreEqual(report,purchase.HasProgress);
        Assert.IsNull(purchase.PurchaseLineReadyQty); Assert.IsNull(purchase.ExpectedReadyDate); Assert.AreEqual(report?f.Today:(DateTime?)null,purchase.SupplierReportedDate);
    });

    /// <summary>活动异常Link仍报需核实；链接身份在插入前设置，不触发不可变列更新或非法状态/删除夹具。</summary>
    [TestMethod,DataRow("missing-line"),DataRow("missing-head"),DataRow("head-voided"),DataRow("net-voided"),
     DataRow("net-version"),DataRow("source-null"),DataRow("fingerprint-null"),DataRow("line-deleted")]
    public Task RealPg_InvalidActiveLinkIsNotUnlinked(string mode)=>InRollback(async f=>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; var gross=f.Gross(); await f.Seed(gross);
        var net=f.Net(gross,1); var po=f.Order(); var pol=f.PurchaseLine(po);
        if(mode=="net-voided")net.LifecycleStatus=NetDemandLifecycleStatusEnum.Voided;
        if(mode=="net-version")net.SourceDemandVersionSnapshot=2;
        if(mode=="source-null")net.SourceVersionSnapshot=null;
        if(mode=="fingerprint-null")net.SourceDemandBasisFingerprintSnapshot=null;
        if(mode=="head-voided")po.LifecycleStatus=PurchaseOrderLifecycleStatusEnum.Voided;
        if(mode=="line-deleted")pol.IsDeleted=true;
        var link=f.Link(net,po,pol); if(mode=="missing-line")link.TargetLineId=Guid.NewGuid(); if(mode=="missing-head")link.TargetHeadId=Guid.NewGuid();
        await f.Add(net); await f.Add(po); await f.Add(pol); await f.Add(link);
        var actual=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines.Single(); Assert.HasCount(0,actual.Purchases);
        Assert.AreEqual("已关联",actual.SourceState); Assert.AreEqual("需核实",actual.PurchaseState); Assert.AreEqual("采购关联证据异常",actual.PurchaseReviewReason); Assert.AreEqual(1,await f.Scalar());
    });

    /// <summary>采购客户错误只来自采购行原因，不冒充异常Link原因；完全释放/取消的历史Link无活动关联。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public Task RealPg_CustomerReasonAndReleasedHistory(bool released)=>InRollback(async f=>
    {
        f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; var gross=f.Gross(); await f.Seed(gross); var net=f.Net(gross,1); var po=f.Order(); var pol=f.PurchaseLine(po);
        po.CustomerId=null; var link=f.Link(net,po,pol); if(released) { link.ReleasedQty=link.AllocatedQty; link.LinkStatus=DemandExecutionLinkStatusEnum.Cancelled; }
        await f.Add(net); await f.Add(po); await f.Add(pol); await f.Add(link); var actual=(await f.Repository.GetFulfillmentAsync(f.Header.Uid))!.Lines.Single();
        Assert.AreEqual("",actual.PurchaseReviewReason); Assert.AreEqual(released?"未关联":"需核实",actual.PurchaseState);
        if(released)Assert.HasCount(0,actual.Purchases); else { Assert.AreEqual("采购客户归属需核实",actual.Purchases.Single().ReviewReason); Assert.IsNull(actual.Purchases.Single().LinkedQty); }
    });

    /// <summary>真实命令边界同事务修改证明Gross重新观察、冻结数量不重算，头/行软删不增加源未有过滤。</summary>
    [TestMethod,DataRow("gross-reselect"),DataRow("header-delete"),DataRow("line-delete")]
    public async Task RealPg_ObserveCurrentHeaderLineAndReselectGross(string mode)
    {
        using var factory=new StageFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()); using var session=new DbSession(factory);
        using var transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction=transaction;
        try
        {
            var f=new Fixture(session,transaction); f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; var gross=f.Gross(); await f.Seed(gross); var next=f.Gross(); next.Version=5;
            factory.Connection.Stage=mode=="header-delete"?2:3;
            factory.Connection.Action=async()=> {
                if(mode=="gross-reselect") { await f.Execute("UPDATE public.gross_demand SET is_deleted=TRUE WHERE uid=@Uid",gross.Uid); await f.Add(next); }
                else if(mode=="header-delete")await f.Execute("UPDATE public.sales_order_header SET is_deleted=TRUE WHERE uid=@Uid",f.Header.Uid);
                else await f.Execute("UPDATE public.sales_order_line SET is_deleted=TRUE WHERE uid=@Uid",f.Line.Uid);
            };
            var detail=await f.Repository.GetFulfillmentAsync(f.Header.Uid); Assert.IsNotNull(detail); var actual=detail.Lines.Single();
            Assert.AreEqual(mode=="gross-reselect"?next.Uid:gross.Uid,actual.GrossDemandId); Assert.AreEqual(mode=="gross-reselect"?5:1,actual.GrossDemandVersion);
            Assert.AreEqual(0m,actual.ShippedQty); Assert.AreEqual(10m,actual.UnshippedQty); Assert.AreEqual(1,await f.Scalar()); Assert.AreSame(transaction,session.Transaction);
        }
        finally { try { transaction.Rollback(); } finally { session.Transaction=null; } }
    }

    /// <summary>无外层事务缺失读取可连续调用，预取消保持异常，连接无临时关系/仓储事务残留。</summary>
    [TestMethod]
    public async Task RealPg_NoOuterTransactionAndPrecancellation()
    {
        using var session=Open(); var f=new Fixture(session,null); Assert.IsNull(await f.Repository.GetFulfillmentAsync(Guid.NewGuid()));
        Assert.IsNull(await f.Repository.GetFulfillmentAsync(Guid.NewGuid())); await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetFulfillmentAsync(Guid.NewGuid(),new CancellationToken(true)));
        Assert.IsNull(session.Transaction); Assert.AreEqual(1,await f.Scalar());
        Assert.AreEqual(0L,await session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')"));
    }

    /// <summary>七读取阶段真实驱动等待取消，调用者负责回滚恢复；仓储不偷偷回滚或重连。</summary>
    [TestMethod,DataRow(0),DataRow(1),DataRow(2),DataRow(3),DataRow(4),DataRow(5),DataRow(6)]
    public async Task RealPg_StageCancellationRecovery(int stage)
    {
        using var factory=new StageFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()); using var session=new DbSession(factory);
        using var transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction=transaction;
        using var cts=new CancellationTokenSource(); Task? read=null; var rolledBack=false;
        try
        {
            var f=new Fixture(session,transaction); f.Header.ConfirmationStatus=ConfirmationStatusEnum.已确认; await f.Seed(f.Gross()); factory.Connection.Stage=stage;
            read=f.Repository.GetFulfillmentAsync(f.Header.Uid,cts.Token); await factory.Connection.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(()=>read); Assert.AreSame(transaction,session.Transaction); Assert.AreEqual(stage+1,factory.Connection.ReadCommands);
            transaction.Rollback(); rolledBack=true; session.Transaction=null; Assert.AreEqual(1,await f.Scalar());
        }
        finally { cts.Cancel(); try { if(read is not null&&!read.IsCompleted) { try { await read; } catch(OperationCanceledException){} } }
            finally { try { if(!rolledBack)transaction.Rollback(); } finally { session.Transaction=null; } } }
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
            Header=new() { OrderNo="PGSF"+Guid.NewGuid().ToString("N"),CustomerId=Guid.NewGuid(),DeliveryDate=Today,OrderDate=Today };
            Line=new() { SalesOrderId=Header.Uid,OrderNo=Header.OrderNo,LineNumber=1,IsNonStandard=true,
                Sku="SKU",Qty=10,UnitSnapshot="PCS",ExpectedDeliveryDate=Today };
        }

        /// <summary>合法且快照完全一致的来源，测试按需只调整明确的一项事实。</summary>
        public GrossDemand Gross() => new() { SourceType="SO",SourceUid=Header.Uid,SourceNo=Header.OrderNo,
            SourceLineId=Line.Uid,SourceLineNumber=Line.LineNumber,SourceVersion=1,DemandBasisFingerprint=new string('A',64),
            CustomerId=Header.CustomerId,IsNonStandard=true,SkuSnapshot=Line.Sku,UnitSnapshot=Line.UnitSnapshot,DemandQty=Line.Qty,RequiredDate=Today };

        /// <summary>随机箱单身份，不真实过账或引入库存流水；只验证已有读取口径。</summary>
        public PackingListHead PackingHead() => new() { PackingListNo="PGSF"+Guid.NewGuid().ToString("N"),CustomerId=Header.CustomerId!.Value,
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

        /// <summary>有效来源版本/指纹的根物料，行号独立且自指根身份。</summary>
        public NetDemand Net(GrossDemand gross,int number)
        {
            var uid=Guid.NewGuid(); return new() { Uid=uid,GrossDemandId=gross.Uid,RootNetDemandId=uid,DecompositionRevision=1,LineNumber=number,
                SourceDemandVersionSnapshot=gross.Version,SourceVersionSnapshot=gross.SourceVersion,SourceDemandBasisFingerprintSnapshot=gross.DemandBasisFingerprint,
                SupplyTypeSnapshot="采购",SkuSnapshot="MAT",UnitSnapshot="OLD-NET",GrossQty=10,NetQty=10 };
        }
        /// <summary>合法随机采购头，不创建真实采购业务命令。</summary>
        public PurchaseOrderHead Order()=>new() { OriginKind=1,CustomerId=Header.CustomerId,OrderNo="PGSF"+Guid.NewGuid().ToString("N"),SupplierId=Guid.NewGuid(),SupplierCode="PGSF",ExecutionRequestId=Guid.NewGuid(),OrderDate=Today };
        /// <summary>整行采购数量用于验证不按来源关联分摊。</summary>
        public PurchaseOrderLine PurchaseLine(PurchaseOrderHead po)=>new() { PurchaseOrderId=po.Uid,OrderNo=po.OrderNo,LineNumber=1,Sku="MAT",UnitSnapshot="PCS",Quantity=20.1234m };
        /// <summary>链接不可变目标在新增之前设置，正净分配符合状态约束。</summary>
        public DemandExecutionLink Link(NetDemand net,PurchaseOrderHead po,PurchaseOrderLine pol,decimal allocated=8,decimal released=2)=>new() {
            NetDemandId=net.Uid,ExecutionRequestId=Guid.NewGuid(),CommandItemId=Guid.NewGuid(),TargetType="PO",TargetHeadId=po.Uid,TargetLineId=pol.Uid,TargetNo=po.OrderNo,AllocatedQty=allocated,ReleasedQty=released };
        /// <summary>只新增本例随机采购证据。</summary>
        public async Task<(NetDemand Net,PurchaseOrderHead Head,PurchaseOrderLine Line,DemandExecutionLink Link)> Purchase(GrossDemand gross)
        { var net=Net(gross,1); var po=Order(); var pol=PurchaseLine(po); var link=Link(net,po,pol); await Add(net); await Add(po); await Add(pol); await Add(link); return(net,po,pol,link); }
        /// <summary>读取后检验调用方连接/事务仍然可用。</summary>
        public Task<int> Scalar()=>Session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:Session.Transaction);
        /// <summary>测试只修改本例已登记UID并回滚，不接受外部SQL或目标身份。</summary>
        public Task<int> Execute(string sql,Guid uid) => Session.Connection.ExecuteAsync(sql,new { Uid=uid },transaction:Transaction);
    }

    /// <summary>无业务权限的测试审计身份，不使用真实用户。</summary>
    private sealed class Identity:ICurrentUser
    {
        public Guid? UserId=>null;
        public string UserName=>"销售Fulfillment迁移实库";
        public bool IsAuthenticated=>false;
    }

    /// <summary>测试专用真实连接包装器只在命令执行边界加只读等待，不改业务SQL、参数或事务，不用于运行接线。</summary>
    private sealed class StageFactory : IDbConnectionFactory, IDisposable
    {
        public NpgsqlConnection Connection { get; }
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        /// <summary>仅由严格门禁提供连接串。</summary>
        public StageFactory(string connectionString) => Connection = new(connectionString);
        /// <summary>返回既有包装连接，真实连接由DbSession打开。</summary>
        public IDbConnection CreateConnection() => Connection;
        /// <summary>释放包装器的真实连接。</summary>
        public void Dispose() => Connection.Dispose();

        /// <summary>类型名使Dapper识别Npgsql数组能力；所有实际操作仍委托真实驱动。</summary>
        public sealed class NpgsqlConnection(string connectionString) : DbConnection
        {
            private readonly Npgsql.NpgsqlConnection inner = new(connectionString);
            public int? Stage { get; set; }
            public Func<Task>? Action { get; set; }
            public int ReadCommands { get; private set; }
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            [AllowNull] public override string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value; }
            public override string Database => inner.Database;
            public override string DataSource => inner.DataSource;
            public override string ServerVersion => inner.ServerVersion;
            public override ConnectionState State => inner.State;
            /// <summary>委托打开，不另建连接。</summary>
            public override void Open() => inner.Open();
            /// <summary>仅生命周期关闭。</summary>
            public override void Close() => inner.Close();
            /// <summary>禁止测试切库。</summary>
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            /// <summary>返回调用者拥有的真实事务。</summary>
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => inner.BeginTransaction(isolationLevel);
            /// <summary>仅包装真实命令的执行边界。</summary>
            protected override DbCommand CreateDbCommand() => new StageCommand(this, inner.CreateCommand());
            /// <summary>测试阶段施加精确同事务自写或真实只读等待，取消传播驱动异常；不模拟查询结果。</summary>
            private async Task Before(DbTransaction? transaction, CancellationToken token)
            {
                if (!Stage.HasValue) return;
                if (ReadCommands++ != Stage.Value) return;
                Stage = null;
                if (Action is not null) { await Action(); return; }
                using var wait = new Npgsql.NpgsqlCommand("SELECT pg_sleep(60)", inner, (Npgsql.NpgsqlTransaction?)transaction);
                var pending = wait.ExecuteScalarAsync(token);
                Entered.TrySetResult();
                await pending;
            }
            /// <summary>只释放真实连接，保留调用者事务恢复策略。</summary>
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }

            /// <summary>真实参数/SQL/事务完全委托，只在异步执行前施加测试只读屏障。</summary>
            private sealed class StageCommand(NpgsqlConnection owner, DbCommand innerCommand) : DbCommand
            {
                [AllowNull] public override string CommandText { get => innerCommand.CommandText; set => innerCommand.CommandText = value; }
                public override int CommandTimeout { get => innerCommand.CommandTimeout; set => innerCommand.CommandTimeout = value; }
                public override CommandType CommandType { get => innerCommand.CommandType; set => innerCommand.CommandType = value; }
                public override bool DesignTimeVisible { get => innerCommand.DesignTimeVisible; set => innerCommand.DesignTimeVisible = value; }
                public override UpdateRowSource UpdatedRowSource { get => innerCommand.UpdatedRowSource; set => innerCommand.UpdatedRowSource = value; }
                protected override DbConnection? DbConnection { get => owner; set { if (value != owner) throw new InvalidOperationException("测试不能切换连接"); } }
                protected override DbTransaction? DbTransaction { get => innerCommand.Transaction; set => innerCommand.Transaction = value; }
                protected override DbParameterCollection DbParameterCollection => innerCommand.Parameters;
                protected override DbParameter CreateDbParameter() => innerCommand.CreateParameter();
                /// <summary>取消交给真实命令。</summary>
                public override void Cancel() => innerCommand.Cancel();
                /// <summary>准备交给真实命令。</summary>
                public override void Prepare() => innerCommand.Prepare();
                /// <summary>夹具写入交给真实命令。</summary>
                public override int ExecuteNonQuery() => innerCommand.ExecuteNonQuery();
                /// <summary>标量交给真实命令。</summary>
                public override object? ExecuteScalar() => innerCommand.ExecuteScalar();
                /// <summary>同步读交给真实命令。</summary>
                protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => innerCommand.ExecuteReader(behavior);
                /// <summary>异步夹具写入不增加履约阶段。</summary>
                public override Task<int> ExecuteNonQueryAsync(CancellationToken token) => innerCommand.ExecuteNonQueryAsync(token);
                /// <summary>标量读取使用真实驱动与相同令牌。</summary>
                public override async Task<object?> ExecuteScalarAsync(CancellationToken token)
                { await owner.Before(DbTransaction, token); return await innerCommand.ExecuteScalarAsync(token); }
                /// <summary>集合读取使用真实驱动，不重写业务SQL。</summary>
                protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
                { await owner.Before(DbTransaction, token); return await innerCommand.ExecuteReaderAsync(behavior, token); }
                /// <summary>只释放当前命令。</summary>
                protected override void Dispose(bool disposing) { if (disposing) innerCommand.Dispose(); base.Dispose(disposing); }
            }
        }
    }
}
