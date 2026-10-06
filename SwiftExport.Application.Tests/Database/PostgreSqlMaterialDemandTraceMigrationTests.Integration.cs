using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Services.Auth;

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Npgsql;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.DemandPlanning.Materials.Query;
namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行执行Trace实库验收；常规夹具事务回滚，跨连接专属partial提交随机夹具后按精确UID清理。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed partial class PostgreSqlMaterialDemandTraceMigrationIntegrationTests
{
    /// <summary>6+6/7按日期、来源行号与源UUID字节尾键覆盖6/1；树修订不成为覆盖排序键。</summary>
    [TestMethod]
    [DataRow("uuid")] [DataRow("date")] [DataRow("source-line")] [DataRow("null-last")]
    public async Task SharedCoverage_UsesFrozenSourceOrdering(string order)
    {
        using var f=new Fixture(); var a=f.Gross(); var b=f.Gross(); b.SourceLineNumber=a.SourceLineNumber;
        if(order=="source-line") { a.SourceLineNumber=1; b.SourceLineNumber=2; }
        if(order=="null-last") b.SourceLineNumber=null;
        await f.InsertAsync(a); await f.InsertAsync(b); var na=f.Net(a,5); var nb=f.Net(b,1); await f.InsertAsync(na); await f.InsertAsync(nb);
        var target=await Target(f,a,12m); var la=f.Link(na,target.Head.Uid,target.Line.Uid,6m,0m); var lb=f.Link(nb,target.Head.Uid,target.Line.Uid,6m,0m);
        // 两UUID的普通PG顺序与SqlGuid顺序相反；尾部字节为源优先级。
        var key=Guid.NewGuid().ToByteArray();var firstBytes=(byte[])key.Clone();var secondBytes=(byte[])key.Clone();
        for(var i=0;i<4;i++){firstBytes[i]=255;secondBytes[i]=0;}firstBytes[15]=1;secondBytes[15]=2;la.Uid=new Guid(firstBytes);lb.Uid=new Guid(secondBytes);
        if(order!="uuid")(la.Uid,lb.Uid)=(lb.Uid,la.Uid);
        if(order=="date") { la.DemandRequiredDate=new DateTime(2026,10,5); lb.DemandRequiredDate=new DateTime(2026,10,6); }
        await f.InsertAsync(la); await f.InsertAsync(lb); await Receipt(f,target.Line.Uid,7m);
        var first=await f.Repository.GetTraceAsync(na.Uid); var second=await f.Repository.GetTraceAsync(nb.Uid);
        Assert.AreEqual(6m,first!.AssociatedPurchaseQty); Assert.AreEqual(6m,first.CurrentReceiptCoverageQty);
        Assert.AreEqual(1m,second!.CurrentReceiptCoverageQty); Assert.IsTrue(first.Purchases.Single().IsSharedPurchaseLine);
        Assert.AreEqual(7m,first.Purchases.Single().PurchaseLineReceivedQty); Assert.AreEqual(0m,first.Purchases.Single().ReceiptOutsideCurrentCoverageQty);
        Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:f.Session.Transaction)));
    }

    /// <summary>覆盖一次冻结后展示重新读到8，仍保留原6/1并显示外围1；scope不阻止覆盖阶段新Link。</summary>
    [TestMethod]
    public async Task StageChanges_KeepOldCoverageAndFreshDisplay()
    {
        using var f=new Fixture(); var gross=f.Gross(); await f.InsertAsync(gross); var a=f.Net(gross,1); var b=f.Net(gross,2); await f.InsertAsync(a); await f.InsertAsync(b);
        var target=await Target(f,gross,12m); var la=f.Link(a,target.Head.Uid,target.Line.Uid,6m,0m); la.DemandRequiredDate=new DateTime(2026,10,5); await f.InsertAsync(la);
        var receipt=await Receipt(f,target.Line.Uid,7m); var hook=(HookConnection)f.Session.Connection; bool added=false,changed=false;
        hook.Before=async (sql,token)=>
        {
            if(sql.Contains("WITH eligible AS",StringComparison.Ordinal) && !added) { added=true; hook.Before=null; var lb=f.Link(b,target.Head.Uid,target.Line.Uid,6m,0m); lb.DemandRequiredDate=new DateTime(2026,10,6); await f.InsertAsync(lb); hook.Before=BeforeDisplay; }
            await Task.CompletedTask;
        };
        async Task BeforeDisplay(string sql,CancellationToken token)
        {
            if(!sql.Contains("WITH coverage AS",StringComparison.Ordinal)||changed)return;
            changed=true; hook.Before=null; await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.inbound_document_line SET quantity=8 WHERE uid=@Uid",new{Uid=receipt.Line.Uid},f.Session.Transaction,cancellationToken:token));
        }
        var result=await f.Repository.GetTraceAsync(a.Uid); Assert.IsTrue(added); Assert.IsTrue(changed);
        Assert.AreEqual(6m,result!.CurrentReceiptCoverageQty); Assert.AreEqual(8m,result.Purchases.Single().PurchaseLineReceivedQty); Assert.AreEqual(1m,result.Purchases.Single().ReceiptOutsideCurrentCoverageQty);
        // 此例只证明同一外层事务内阶段自写可见；未提交跨连接数据，不冒称并发验收。
    }

    /// <summary>来源与采购异常按源理由返回nullable量；associated超量例保留12而coverage未知。</summary>
    [TestMethod]
    [DataRow("version","来源版本或需求指纹需核实")]
    [DataRow("fingerprint","来源版本或需求指纹需核实")]
    [DataRow("md-version","来源版本或需求指纹需核实")]
    [DataRow("so-version","来源版本或需求指纹需核实")]
    [DataRow("customer","来源状态或客户需核实")]
    [DataRow("unit","物料单位需核实")]
    [DataRow("zero","物料需求数量需核实")]
    [DataRow("history","历史拆解修订")]
    [DataRow("target","采购目标、客户、单位或来源修订需核实")]
    [DataRow("purchase-customer","采购目标、客户、单位或来源修订需核实")]
    [DataRow("purchase-unit","采购目标、客户、单位或来源修订需核实")]
    [DataRow("over","采购承接超量需核实")]
    public async Task ReviewCases_KeepUnknownAndOverage(string branch,string reason)
    {
        using var f=new Fixture(); var g=f.Gross(); if(branch=="customer")g.CustomerId=null; if(branch=="so-version")g.SourceType="SO"; await f.InsertAsync(g);
        var n=f.Net(g,1); if(branch=="version")n.SourceDemandVersionSnapshot=null; if(branch=="fingerprint")n.SourceDemandBasisFingerprintSnapshot=new string('B',64);
        if(branch=="md-version")n.SourceVersionSnapshot=2; if(branch=="unit")n.UnitSnapshot="  "; if(branch=="zero")n.NetQty=0;
        if(branch=="history")n.LifecycleStatus=NetDemandLifecycleStatusEnum.RolledBack; await f.InsertAsync(n);
        if(branch=="target")await f.InsertAsync(f.Link(n,Guid.NewGuid(),Guid.NewGuid(),3m,0m));
        else if(branch.StartsWith("purchase-",StringComparison.Ordinal)||branch=="over")
        {
            var target=await Target(f,g,20m); if(branch=="purchase-customer")await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.purchase_order_head SET customer_id=@Customer WHERE uid=@Uid",new{Customer=Guid.NewGuid(),Uid=target.Head.Uid},f.Session.Transaction));
            if(branch=="purchase-unit")await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.purchase_order_line SET unit_snapshot='BOX' WHERE uid=@Uid",new{Uid=target.Line.Uid},f.Session.Transaction));
            await f.InsertAsync(f.Link(n,target.Head.Uid,target.Line.Uid,branch=="over"?12m:3m,0m));
        }
        var result=await f.Repository.GetTraceAsync(n.Uid); Assert.AreEqual(reason,result!.ReviewReason);
        if(branch=="over")Assert.AreEqual(12m,result.AssociatedPurchaseQty); else Assert.IsNull(result.AssociatedPurchaseQty); Assert.IsNull(result.CurrentReceiptCoverageQty);
    }

    /// <summary>其他贡献无效使整采购分区coverage未知；有效物料不能提前丢掉坏贡献来取得覆盖。</summary>
    [TestMethod]
    public async Task InvalidOtherContribution_NullsWholePartition()
    {
        using var f=new Fixture(); var g=f.Gross(); await f.InsertAsync(g); var a=f.Net(g,1); var b=f.Net(g,2); b.SourceDemandVersionSnapshot=null; await f.InsertAsync(a); await f.InsertAsync(b);
        var t=await Target(f,g,12m); await f.InsertAsync(f.Link(a,t.Head.Uid,t.Line.Uid,6m,0)); await f.InsertAsync(f.Link(b,t.Head.Uid,t.Line.Uid,6m,0)); await Receipt(f,t.Line.Uid,7m);
        var result=await f.Repository.GetTraceAsync(a.Uid); Assert.IsNull(result!.CurrentReceiptCoverageQty); Assert.AreEqual("采购目标、客户、单位或来源修订需核实",result.ReviewReason);
        Assert.IsNull(result.Purchases.Single().ReceiptOutsideCurrentCoverageQty);
    }

    /// <summary>整行反馈不分摊，较新修正可变小；历史取消Link仍展示，树包含未删旧修订。</summary>
    [TestMethod]
    public async Task HistoryAndReports_KeepWholeLineAndRevisionTree()
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);await f.InsertAsync(n);
        var old=f.Net(g,1);old.DecompositionRevision=2;old.LifecycleStatus=NetDemandLifecycleStatusEnum.RolledBack;await f.InsertAsync(old);
        var t=await Target(f,g,20m);await f.InsertAsync(f.Link(n,t.Head.Uid,t.Line.Uid,3m,0));var cancelled=f.Link(n,t.Head.Uid,t.Line.Uid,2m,2m);cancelled.LinkStatus=DemandExecutionLinkStatusEnum.Cancelled;await f.InsertAsync(cancelled);
        await f.InsertAsync(new SupplierProgressReport{Uid=Guid.NewGuid(),PurchaseOrderLineId=t.Line.Uid,Sequence=1,ReadyQty=8m,SupplierReportedDate=new DateTime(2026,10,5),CommandId=Guid.NewGuid()});
        await f.InsertAsync(new SupplierProgressReport{Uid=Guid.NewGuid(),PurchaseOrderLineId=t.Line.Uid,Sequence=2,ReadyQty=5m,IsCorrection=true,SupplierReportedDate=new DateTime(2026,10,6),CommandId=Guid.NewGuid()});
        var result=await f.Repository.GetTraceAsync(n.Uid);Assert.HasCount(2,result!.TreeItems);Assert.HasCount(2,result.Purchases);Assert.IsTrue(result.Purchases.All(x=>x.PurchaseLineReadyQty==5m));Assert.IsNull(result.Purchases.Single(x=>!x.IsActiveLink).CurrentReceiptCoverageQty);
        Assert.AreEqual("历史采购关联",result.Purchases.Single(x=>!x.IsActiveLink).ReviewReason);
    }

    /// <summary>唯一Sequence索引禁止并列反馈；保存源Uid尾键不构成绕过现有约束的理由。</summary>
    [TestMethod]
    public async Task SequenceTie_IsRejectedByExistingUniqueIndex()
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var t=await Target(f,g,20m);
        await f.InsertAsync(new SupplierProgressReport{Uid=Guid.NewGuid(),PurchaseOrderLineId=t.Line.Uid,Sequence=1,ReadyQty=8m,SupplierReportedDate=new DateTime(2026,10,5),CommandId=Guid.NewGuid()});
        var failure=await Assert.ThrowsAsync<PostgresException>(()=>f.InsertAsync(new SupplierProgressReport{Uid=Guid.NewGuid(),PurchaseOrderLineId=t.Line.Uid,Sequence=1,ReadyQty=5m,SupplierReportedDate=new DateTime(2026,10,5),CommandId=Guid.NewGuid()}));
        Assert.AreEqual("23505",failure.SqlState);Assert.AreEqual("ux_supplier_progress_line_sequence",failure.ConstraintName);
    }

    /// <summary>相关范围定位后检查整个入库头；无关坏反向历史不阻断，业务Guard不破坏外层事务。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)] [DataRow("whole-head")]
    public async Task GuardScope_OnlyRelevantHeadsButChecksAllLines(object kind)
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);await f.InsertAsync(n);var t=await Target(f,g,20m);await f.InsertAsync(f.Link(n,t.Head.Uid,t.Line.Uid,3m,0));
        bool relevant=kind is string || (bool)kind;
        var bad=await Receipt(f,relevant?t.Line.Uid:Guid.NewGuid(),1m);
        if(kind is string)await f.InsertAsync(new InboundDocumentLine{Uid=Guid.NewGuid(),InboundDocumentId=bad.Head.Uid,LineNumber=2,ProductId=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),Quantity=1m,OriginalLineId=Guid.NewGuid(),UnitSnapshot="KG"});
        else await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.inbound_document SET posting_status=8,reversed_date=NULL,reverse_reason=NULL WHERE uid=@Uid",new{Uid=bad.Head.Uid},f.Session.Transaction));
        var service=new MaterialDemandQueryService(new ServiceBaseContext(f.Session,NullLoggerFactory.Instance),f.Repository);var result=await service.GetTraceAsync(n.Uid);
        if(relevant){Assert.IsFalse(result.Success);Assert.AreEqual("正式入库反向历史需核实，收货量未知。",result.Message);}else Assert.IsTrue(result.Success,result.Message);
        Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:f.Session.Transaction)));
    }

    /// <summary>无头、软删头和无外层事务保留null，软删selected仍缓冲同来源tree后停止。</summary>
    [TestMethod]
    [DataRow("missing")] [DataRow("deleted")] [DataRow("no-transaction")]
    public async Task EmptyHeader_BuffersTreeAndKeepsConnectionUsable(string mode)
    {
        using var f=new Fixture(mode!="no-transaction");Guid uid=Guid.NewGuid();
        if(mode=="deleted"){var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);n.IsDeleted=true;uid=n.Uid;await f.InsertAsync(n);}
        var hook=(HookConnection)f.Session.Connection;hook.Commands.Clear();Assert.IsNull(await f.Repository.GetTraceAsync(uid));Assert.HasCount(4,hook.Commands);StringAssert.Contains(hook.Commands[3],"selected.gross_demand_id");
        Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:f.Session.Transaction)));
    }

    /// <summary>预取消和各阶段命令前取消真实入口，不改变调用者事务；服务器取消aborted另由主控串行验收。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public async Task Cancellation_PropagatesAtStageBoundary(int stage)
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);await f.InsertAsync(n);var hook=(HookConnection)f.Session.Connection;hook.Commands.Clear();using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else hook.Before=(sql,token)=>{if(hook.Commands.Count==stage)c.Cancel();token.ThrowIfCancellationRequested();return Task.CompletedTask;};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetTraceAsync(n.Uid,c.Token));hook.Before=null;Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:f.Session.Transaction)));
    }


    /// <summary>在指定阶段的真实服务器等待中取消；aborted由调用者回滚，仓储不偷偷恢复事务。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public async Task ServerWaitCancellation_LeavesRollbackToCaller(int stage)
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);await f.InsertAsync(n);var hook=(HookConnection)f.Session.Connection;hook.Commands.Clear();using var c=new CancellationTokenSource();bool waiting=false;
        hook.Before=async(sql,token)=>
        {
            if(hook.Commands.Count!=stage)return;waiting=true;hook.Before=null;c.CancelAfter(TimeSpan.FromMilliseconds(250));
            await hook.WaitOnServerAsync(f.Session.Transaction,token);
        };
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetTraceAsync(n.Uid,c.Token));Assert.IsTrue(waiting);Assert.IsNotNull(f.Session.Transaction);
        var aborted=await Assert.ThrowsAsync<PostgresException>(()=>f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:f.Session.Transaction)));Assert.AreEqual("25P02",aborted.SqlState);
        f.Session.Transaction!.Rollback();f.Session.Transaction.Dispose();f.Session.Transaction=null;Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
    }

    /// <summary>覆盖阶段重读selected的当前Link范围，起始scope为空不能吞掉后来新关系或再执行Guard。</summary>
    [TestMethod]
    public async Task NewTargetAfterGuard_IsReadWithoutSecondGuard()
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);await f.InsertAsync(n);var t=await Target(f,g,20m);await Receipt(f,t.Line.Uid,2m);
        var hook=(HookConnection)f.Session.Connection;hook.Commands.Clear();hook.Before=async(sql,token)=>
        {
            if(!sql.Contains("WITH eligible AS",StringComparison.Ordinal))return;hook.Before=null;await f.InsertAsync(f.Link(n,t.Head.Uid,t.Line.Uid,3m,0m));
        };
        var result=await f.Repository.GetTraceAsync(n.Uid);Assert.AreEqual(2m,result!.CurrentReceiptCoverageQty);Assert.HasCount(1,result.Purchases);
        Assert.AreEqual(1,hook.Commands.Count(sql=>sql.Contains("SELECT EXISTS",StringComparison.Ordinal)));Assert.HasCount(7,hook.Commands);
    }

    /// <summary>入库单位异常仅令本投影未知；未构造反向历史时不错误触发共享Guard。</summary>
    [TestMethod]
    [DataRow("BOX")] [DataRow("   ")]
    public async Task ReceiptUnitMismatch_RemainsUnknown(string unit)
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);await f.InsertAsync(n);var t=await Target(f,g,20m);await f.InsertAsync(f.Link(n,t.Head.Uid,t.Line.Uid,3m,0m));var receipt=await Receipt(f,t.Line.Uid,2m);
        await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.inbound_document_line SET unit_snapshot=@Unit WHERE uid=@Uid",new{Unit=unit,Uid=receipt.Line.Uid},f.Session.Transaction));
        var result=await f.Repository.GetTraceAsync(n.Uid);Assert.AreEqual("入库单位需核实",result!.Purchases.Single().ReviewReason);Assert.IsNull(result.Purchases.Single().CurrentReceiptCoverageQty);Assert.IsNull(result.Purchases.Single().ReceiptOutsideCurrentCoverageQty);Assert.AreEqual(2m,result.Purchases.Single().PurchaseLineReceivedQty);
    }

    /// <summary>历史取消关系仍参与起始Guard范围；相关头中软删scope行仍能定位整头坏历史。</summary>
    [TestMethod]
    [DataRow("cancelled")] [DataRow("deleted-scope")]
    public async Task HistoricalScope_StillProtectsRelatedWholeHead(string mode)
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);await f.InsertAsync(n);var t=await Target(f,g,20m);var link=f.Link(n,t.Head.Uid,t.Line.Uid,3m,mode=="cancelled"?3m:0m);if(mode=="cancelled")link.LinkStatus=DemandExecutionLinkStatusEnum.Cancelled;await f.InsertAsync(link);var r=await Receipt(f,t.Line.Uid,1m);
        if(mode=="deleted-scope")
        {
            await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.inbound_document_line SET is_deleted=TRUE WHERE uid=@Uid",new{Uid=r.Line.Uid},f.Session.Transaction));
            await f.InsertAsync(new InboundDocumentLine{Uid=Guid.NewGuid(),InboundDocumentId=r.Head.Uid,LineNumber=2,SourceLineId=Guid.NewGuid(),ProductId=Guid.NewGuid(),Quantity=1m,OriginalLineId=Guid.NewGuid()});
        }
        else await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.inbound_document SET posting_status=8,reversed_date=NULL WHERE uid=@Uid",new{Uid=r.Head.Uid},f.Session.Transaction));
        await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetTraceAsync(n.Uid));Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:f.Session.Transaction)));
    }

    /// <summary>合法空关系返回明确零，来源指纹的C#Ordinal仍能在SQL字典等值时拒绝大小写错配。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task NoLinks_ZeroOrOrdinalSourceReview(bool mismatch)
    {
        using var f=new Fixture();var g=f.Gross();await f.InsertAsync(g);var n=f.Net(g,1);if(mismatch)n.SourceDemandBasisFingerprintSnapshot=new string('a',64);await f.InsertAsync(n);
        var result=await f.Repository.GetTraceAsync(n.Uid);Assert.HasCount(0,result!.Purchases);
        if(mismatch){Assert.AreEqual("来源版本或需求指纹需核实",result.ReviewReason);Assert.IsNull(result.AssociatedPurchaseQty);}else{Assert.AreEqual(0m,result.AssociatedPurchaseQty);Assert.AreEqual(0m,result.CurrentReceiptCoverageQty);}
    }

    /// <summary>只插入合法当前采购目标，所有随机实体由同一夹具事务回滚。</summary>
    private static async Task<(PurchaseOrderHead Head,PurchaseOrderLine Line)> Target(Fixture f,GrossDemand gross,decimal quantity)
    {
        var supplier=f.Supplier();await f.InsertAsync(supplier);var head=new PurchaseOrderHead{Uid=Guid.NewGuid(),OrderNo=f.Prefix+Guid.NewGuid().ToString("N"),CustomerId=gross.CustomerId,SupplierId=supplier.Uid,SupplierCode=supplier.SupplierCode,ExecutionRequestId=Guid.NewGuid()};await f.InsertAsync(head);
        var line=new PurchaseOrderLine{Uid=Guid.NewGuid(),PurchaseOrderId=head.Uid,OrderNo=head.OrderNo,LineNumber=1,Quantity=quantity,UnitSnapshot="KG"};await f.InsertAsync(line);return(head,line);
    }

    /// <summary>正式正量采购入库，引用随机身份而不依赖主数据存在；不初始化DDL或提交夹具。</summary>
    private static async Task<(InboundDocument Head,InboundDocumentLine Line)> Receipt(Fixture f,Guid source,decimal quantity)
    {
        var head=new InboundDocument{Uid=Guid.NewGuid(),DocumentNo=f.Prefix+Guid.NewGuid().ToString("N"),DocumentType=(InboundDocumentTypeEnum)10,PostingStatus=(InboundPostingStatusEnum)1,ReceiptDate=new DateTime(2026,10,5),PostedDate=new DateTime(2026,10,5),WarehouseSiteId=Guid.NewGuid()};await f.InsertAsync(head);
        var line=new InboundDocumentLine{Uid=Guid.NewGuid(),InboundDocumentId=head.Uid,LineNumber=1,SourceLineId=source,ProductId=Guid.NewGuid(),Quantity=quantity,UnitSnapshot="KG"};await f.InsertAsync(line);return(head,line);
    }
    /// <summary>每例独立故障库事务，查询借用同一已写入夹具的事务；Dispose最终回滚，不提交/清库。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly string Prefix = "__M14T_" + Guid.NewGuid().ToString("N") + "_";
        public readonly Guid CustomerId = Guid.NewGuid();
        public readonly DbSession Session;
        public readonly RepositoryBaseContext Context;
        public readonly PostgreSqlMaterialDemandQueryRepository Repository;
        public Fixture(bool transaction = true)
        {
            Session = new(new HookFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
            try
            {
                if (transaction) Session.Transaction = Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
                var user = new Identity(); Context = new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user); Repository = new(Context);
            }
            catch { Session.Dispose(); throw; }
        }

        /// <summary>只插入随机夹具实体，复用既有PG元数据与参数，不修改结构或历史行。</summary>
        public async Task InsertAsync<T>(T entity) where T : BaseEntity
        {
            Context.AuditStamp.StampForInsert(entity); var map = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
            var columns = string.Join(", ", map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
            var parameters = string.Join(", ", map.Columns.Select(c => "@" + c.ParameterName));
            Assert.AreEqual(1, await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({parameters});", PostgreSqlParameters.Entity(entity, map), Session.Transaction)));
        }

        /// <summary>生成可证明的手工来源；客户身份不依赖当前主数据存在，保持源码只检查权威CustomerId的边界。</summary>
        public GrossDemand Gross() => new() { Uid = Guid.NewGuid(), SourceType = "MD", SourceUid = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceLineNumber = 1, SourceNo = Prefix + "SOURCE", SkuSnapshot = "GROSS", DemandQty = 10m, CustomerId = CustomerId, IsNonStandard = true, DemandBasisFingerprint = new string('A', 64) };

        /// <summary>生成单位和来源快照可信的工作台行，根指向自身，所有日期默认空。</summary>
        public NetDemand Net(GrossDemand gross, int line)
        {
            var id = Guid.NewGuid(); return new() { Uid = id, GrossDemandId = gross.Uid, RootNetDemandId = id, LineNumber = line, DecompositionRevision = 1, SourceDemandVersionSnapshot = gross.Version, SourceDemandBasisFingerprintSnapshot = gross.DemandBasisFingerprint, SkuSnapshot = "NET", SupplyTypeSnapshot = SupplyTypeEnums.采购, UnitSnapshot = "KG", GrossQty = 10m, NetQty = 10m };
        }

        /// <summary>创建独立供应商档案，未删状态不代表新业务资格。</summary>
        public Supplier Supplier() => new() { Uid = Guid.NewGuid(), SupplierCode = Prefix + Guid.NewGuid().ToString("N"), SupplierName = "供应商", DocRelateNo = Prefix + "DOC" };

        /// <summary>按现有CHECK创建合法Link；历史退出使用取消且全释放，不构造非法软删或负贡献。</summary>
        public DemandExecutionLink Link(NetDemand net, Guid head, Guid line, decimal allocated, decimal released) => new() { Uid = Guid.NewGuid(), NetDemandId = net.Uid, ExecutionRequestId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), TargetType = "PO", TargetHeadId = head, TargetLineId = line, TargetNo = Prefix + "PO", AllocatedQty = allocated, ReleasedQty = released };

        /// <summary>成功或异常均回滚唯一夹具事务；回滚失败也释放连接，不执行提交、删除或结构清理。</summary>
        public void Dispose()
        {
            try { if (Session.Transaction is { } tx) { try { tx.Rollback(); } finally { tx.Dispose(); Session.Transaction = null; } } }
            finally { Session.Dispose(); }
        }
    }

    /// <summary>夹具审计身份，不冒用真实用户或记录连接秘密。</summary>
    private sealed class Identity : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "物料工作台回滚夹具";
        public bool IsAuthenticated => false;
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
        /// <summary>测试专用单独只读等待命令，借用原事务及token，避免生产SQL/批次加入测试延迟。</summary>
        public async Task WaitOnServerAsync(IDbTransaction? transaction,CancellationToken token)
        {
            using var command=new NpgsqlCommand("SELECT pg_sleep(30)",inner,(NpgsqlTransaction?)transaction);await command.ExecuteNonQueryAsync(token);
        }
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

