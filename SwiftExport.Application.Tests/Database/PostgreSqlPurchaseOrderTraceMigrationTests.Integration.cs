using System.Collections;
using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Trace;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行真实PG采购Trace验收；随机合法事实及RC回滚，保留scope/门禁/六语句驱动协议。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlPurchaseOrderTraceMigrationIntegrationTests
{
    /// <summary>无头/删头先执行scope及ScopedGuard，真实ReadSingleOrDefault推进后释放，连接和外层事务可用。</summary>
    [TestMethod]
    [DataRow("missing")] [DataRow("deleted-head")] [DataRow("empty-lines")]
    public async Task HeaderAndEmptyRelease(string mode)
    {
        using var f=new Fixture();var h=f.Head();h.IsDeleted=mode=="deleted-head";if(mode!="missing")await f.InsertAsync(h);
        if(mode=="deleted-head"){var l=f.Line(h);await f.InsertAsync(l);await f.InsertAsync(Report(l,1,7));}
        f.Hook.Commands.Clear();var d=await f.Repository.GetAsync(h.Uid);Assert.HasCount(3,f.Hook.Commands);
        if(mode=="empty-lines"){Assert.IsNotNull(d);Assert.HasCount(0,d.Lines);Assert.AreEqual(6,f.Hook.NextResults);}
        else{Assert.IsNull(d);Assert.AreEqual(1,f.Hook.NextResults);Assert.IsTrue(f.Hook.ReadCounts.Skip(1).All(x=>x==0));}
        Assert.AreEqual(1,f.Hook.Disposals);Assert.IsTrue(f.Hook.ReaderClosed);await f.Usable();
    }

    /// <summary>scope包含删行与无有效头行；相关整头另一坏行被拒绝，无关坏历史不影响当前Trace。</summary>
    [TestMethod]
    [DataRow("deleted-purchase-line",true)] [DataRow("missing-header-line",true)] [DataRow("whole-head",true)] [DataRow("deleted-receipt-scope",true)] [DataRow("unrelated",false)]
    public async Task ScopedGuardHistory(string mode,bool rejects)
    {
        using var f=new Fixture();var h=f.Head();if(mode!="missing-header-line")await f.InsertAsync(h);
        var l=f.Line(h);l.IsDeleted=mode=="deleted-purchase-line";await f.InsertAsync(l);
        var receipt=await Receipt(f,mode=="unrelated"?Guid.NewGuid():l.Uid,2m,1,10,deletedLine:mode=="deleted-receipt-scope");
        // quantity仍为正；OriginalLineId是源Guard拒绝的修正证据，不触犯物理正量约束。
        await f.InsertAsync(new InboundDocumentLine{Uid=Guid.NewGuid(),InboundDocumentId=receipt.Head.Uid,LineNumber=3,Quantity=1m,SourceLineId=Guid.NewGuid(),ProductId=Guid.NewGuid(),SkuSnapshot="坏历史证据",UnitSnapshot="PCS",OriginalLineId=Guid.NewGuid()});
        f.Hook.Commands.Clear();
        if(rejects){await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetAsync(h.Uid));Assert.HasCount(2,f.Hook.Commands);}
        else{Assert.IsNotNull(await f.Repository.GetAsync(h.Uid));Assert.HasCount(3,f.Hook.Commands);}
        CollectionAssert.AreEqual(new[]{l.Uid},f.Hook.ScopedIds);await f.Usable();
    }

    /// <summary>完整历史采购行包含软删；OriginKind2有效无Link与非2删行不新增缺来源错误。</summary>
    [TestMethod]
    [DataRow(2,false,false)] [DataRow(1,true,false)] [DataRow(1,false,true)]
    public async Task NoLinkHistoricalLines(int origin,bool deleted,bool review)
    {
        using var f=new Fixture();var h=f.Head();h.OriginKind=origin;await f.InsertAsync(h);var l=f.Line(h);l.IsDeleted=deleted;await f.InsertAsync(l);
        var line=(await f.Repository.GetAsync(h.Uid))!.Lines.Single();Assert.AreEqual(deleted,line.IsDeleted);Assert.AreEqual(0m,line.FormalReceivedQty);Assert.HasCount(0,line.Links);
        Assert.AreEqual(review?"当前采购行未找到需求 Link，请核实来源。":null,line.ReviewReason);
    }

    /// <summary>当前Product单位优先；只根Net回落Gross，不使用Net快照，历史软删Gross/Net也不丢证据。</summary>
    [TestMethod]
    [DataRow("current",false)] [DataRow("current-mismatch",true)] [DataRow("root-fallback",false)] [DataRow("deleted-product-root",false)] [DataRow("blank-product-root",false)] [DataRow("child-no-product",true)] [DataRow("net-snapshot-only",true)] [DataRow("historical-demand",false)]
    public async Task CurrentUnitAndRootFallback(string mode,bool review)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);
        var facts=Facts(f,h,l);facts.Net.UnitSnapshot="完全不同的Net快照";
        if(mode.StartsWith("current",StringComparison.Ordinal)||mode is "deleted-product-root" or "blank-product-root")
        {var p=Product(f);p.Unit=mode=="current-mismatch"?"KG":mode=="blank-product-root"?" ":" PCS ";p.IsDeleted=mode=="deleted-product-root";await f.InsertAsync(p);facts.Net.ProductId=p.Uid;}
        if(mode=="child-no-product")facts.Net.ParentNetDemandId=Guid.NewGuid();
        if(mode=="net-snapshot-only"){facts.Gross.UnitSnapshot=null;facts.Net.UnitSnapshot="PCS";}
        if(mode=="historical-demand"){facts.Gross.IsDeleted=true;facts.Gross.LifecycleStatus=(GrossDemandLifecycleStatusEnum)9;facts.Net.IsDeleted=true;facts.Net.LifecycleStatus=(NetDemandLifecycleStatusEnum)8;}
        await InsertFacts(f,facts);
        var link=(await f.Repository.GetAsync(h.Uid))!.Lines.Single().Links.Single();Assert.AreEqual(review,link.NeedsReview);Assert.AreEqual(review?"物料与采购行单位不一致。":null,link.ReviewReason);
    }

    /// <summary>需求技术版本对当前Gross.Version、MD空版本和非MD双非空相等，字典指纹比较不要求64位算法。</summary>
    [TestMethod]
    [DataRow("md-valid",false)] [DataRow("md-source-version",true)] [DataRow("md-net-version",true)] [DataRow("nonmd-valid",false)] [DataRow("nonmd-gross-null",true)] [DataRow("nonmd-net-null",true)] [DataRow("nonmd-version-mismatch",true)] [DataRow("gross-version-mismatch",true)] [DataRow("demand-version-null",true)] [DataRow("fingerprint-null",true)] [DataRow("fingerprint-blank",true)] [DataRow("fingerprint-mismatch",true)] [DataRow("dictionary-ascii",false)]
    public async Task VersionsAndDictionaryFingerprint(string mode,bool review)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);var facts=Facts(f,h,l);
        switch(mode)
        {
            case "md-source-version":facts.Gross.SourceVersion=2;break;
            case "md-net-version":facts.Net.SourceVersionSnapshot=2;break;
            case "nonmd-valid":facts.Gross.SourceType="SO";facts.Gross.SourceVersion=4;facts.Net.SourceVersionSnapshot=4;break;
            case "nonmd-gross-null":facts.Gross.SourceType="SO";facts.Net.SourceVersionSnapshot=4;break;
            case "nonmd-net-null":facts.Gross.SourceType="SO";facts.Gross.SourceVersion=4;break;
            case "nonmd-version-mismatch":facts.Gross.SourceType="SO";facts.Gross.SourceVersion=4;facts.Net.SourceVersionSnapshot=3;break;
            case "gross-version-mismatch":facts.Gross.Version=2;break;
            case "demand-version-null":facts.Net.SourceDemandVersionSnapshot=null;break;
            case "fingerprint-null":facts.Gross.DemandBasisFingerprint=null;break;
            case "fingerprint-blank":facts.Gross.DemandBasisFingerprint=" ";break;
            case "fingerprint-mismatch":facts.Net.SourceDemandBasisFingerprintSnapshot="其他";break;
            case "dictionary-ascii":facts.Gross.SourceType="md ";facts.Gross.DemandBasisFingerprint="AbC ";facts.Net.SourceDemandBasisFingerprintSnapshot="abc";break;
        }
        await InsertFacts(f,facts);var link=(await f.Repository.GetAsync(h.Uid))!.Lines.Single().Links.Single();
        Assert.AreEqual(review,link.NeedsReview);Assert.AreEqual(review?"来源版本或需求指纹缺失，或与当前来源不一致。":null,link.ReviewReason);
        Assert.AreEqual(facts.Gross.DemandBasisFingerprint,link.DemandBasisFingerprint);Assert.AreEqual(facts.Net.SourceDemandBasisFingerprintSnapshot,link.DemandBasisFingerprintSnapshot);Assert.AreEqual(facts.Gross.SourceType,link.SourceType);
        // P03/B10/B19 Unicode等价边界未由ASCII用例替代，公共比较规则归主控。
    }

    /// <summary>理由CASE保持缺记录→身份→版本→单位→错头优先；错头Link仍通过行身份返回，合法释放量保留。</summary>
    [TestMethod]
    [DataRow("missing-net")] [DataRow("missing-gross")] [DataRow("missing-identity")] [DataRow("version-before-unit")] [DataRow("unit-before-head")] [DataRow("wrong-head")] [DataRow("released")]
    public async Task ReviewPriorityAndTargetEvidence(string mode)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);var facts=Facts(f,h,l);string? reason=null;
        if(mode=="missing-net"){facts.Link.NetDemandId=Guid.NewGuid();reason="需求来源记录缺失。";}
        if(mode=="missing-gross"){facts.Net.GrossDemandId=Guid.NewGuid();reason="需求来源记录缺失。";}
        if(mode=="missing-identity"){facts.Gross.SourceUid=Guid.Empty;facts.Net.SourceDemandVersionSnapshot=null;reason="需求来源身份缺失。";}
        if(mode=="version-before-unit"){facts.Net.SourceDemandVersionSnapshot=null;facts.Gross.UnitSnapshot="KG";reason="来源版本或需求指纹缺失，或与当前来源不一致。";}
        if(mode=="unit-before-head"){facts.Gross.UnitSnapshot="KG";facts.Link.TargetHeadId=Guid.NewGuid();reason="物料与采购行单位不一致。";}
        if(mode=="wrong-head"){facts.Link.TargetHeadId=Guid.NewGuid();reason="Link 采购头行身份不一致。";}
        if(mode=="released"){facts.Link.ReleasedQty=facts.Link.AllocatedQty;facts.Link.LinkStatus=(DemandExecutionLinkStatusEnum)8;facts.Link.CancelledReason="合法全额释放";}
        if(mode!="missing-gross")await f.InsertAsync(facts.Gross);if(mode!="missing-net")await f.InsertAsync(facts.Net);await f.InsertAsync(facts.Link);
        var link=(await f.Repository.GetAsync(h.Uid))!.Lines.Single().Links.Single();Assert.AreEqual(reason,link.ReviewReason);Assert.AreEqual(reason is not null,link.NeedsReview);Assert.AreEqual(facts.Link.AllocatedQty-facts.Link.ReleasedQty,link.EffectiveQty);
        if(mode=="released"){Assert.AreEqual(0m,link.EffectiveQty);Assert.AreEqual("合法全额释放",link.CancelledReason);}
    }

    /// <summary>行Latest读取完整最新反馈NULL不回填；后续History保持全部未删反馈、原SQL顺序与纠错快照。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task WholeLatestAndHistory(bool deleteLatest)
    {
        using var f=new Fixture();var h=f.Head();h.OriginKind=2;await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);
        var old=Report(l,1,7);old.Note="旧";await f.InsertAsync(old);var latest=Report(l,2,null);latest.IsCorrection=true;latest.CorrectionReason="修正日期";latest.ExpectedReadyDate=new DateTime(2026,10,6);latest.IsDeleted=deleteLatest;await f.InsertAsync(latest);
        var line=(await f.Repository.GetAsync(h.Uid))!.Lines.Single();Assert.AreEqual(deleteLatest?7m:null,line.LatestReadyQty);Assert.HasCount(deleteLatest?1:2,line.ProgressHistory);Assert.AreEqual(deleteLatest?old.Uid:latest.Uid,line.ProgressHistory[0].Uid);
        if(!deleteLatest){Assert.IsNull(line.ProgressHistory[0].ReadyQty);Assert.IsTrue(line.ProgressHistory[0].IsCorrection);Assert.AreEqual("修正日期",line.ProgressHistory[0].CorrectionReason);}
    }

    /// <summary>正式收货按采购行身份，Posted贡献量、完整Reversed贡献0；无Link不分摊，草稿/其他类型/软删排除。</summary>
    [TestMethod]
    [DataRow("posted",3)] [DataRow("reversed",0)] [DataRow("draft",0)] [DataRow("other-type",0)] [DataRow("deleted-head",0)] [DataRow("deleted-line",0)]
    public async Task ReceiptHistory(string mode,int expected)
    {
        using var f=new Fixture();var h=f.Head();h.OriginKind=2;await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);
        var receipt=await Receipt(f,l.Uid,3m,mode=="reversed"?8:mode=="draft"?0:1,mode=="other-type"?11:10,mode=="deleted-head",mode=="deleted-line");
        var line=(await f.Repository.GetAsync(h.Uid))!.Lines.Single();Assert.AreEqual((decimal)expected,line.FormalReceivedQty);
        if(mode is "posted" or "reversed"){Assert.HasCount(1,line.Receipts);Assert.AreEqual(receipt.Line.Uid,line.Receipts[0].InboundLineId);Assert.AreEqual(3m,line.Receipts[0].Quantity);Assert.AreEqual((decimal)expected,line.Receipts[0].NetQuantity);}
        else Assert.HasCount(0,line.Receipts);
    }

    /// <summary>原C#大小写、尾空格和Unicode空白均触发未知，保留实际收货证据并覆盖缺Link理由。</summary>
    [TestMethod]
    [DataRow("PCS",false)] [DataRow("pcs",true)] [DataRow("PCS ",true)] [DataRow("\u3000",true)]
    public async Task ReceiptUnitsRemainDotNet(string unit,bool unknown)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);await Receipt(f,l.Uid,3m,1,10,unit:unit);
        var line=(await f.Repository.GetAsync(h.Uid))!.Lines.Single();Assert.HasCount(1,line.Receipts);
        Assert.AreEqual(unknown?null:3m,line.FormalReceivedQty);Assert.AreEqual(unknown?"正式入库单位与采购行不一致，净收货量需核实。":"当前采购行未找到需求 Link，请核实来源。",line.ReviewReason);
    }

    /// <summary>同日操作日志以源SqlGuid降序，其他Module也保留；异头日志排除，不构造禁止软删日志。</summary>
    [TestMethod]
    public async Task OperationsAndSqlGuidOrder()
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);
        // 随机公共节点前缀避免复用身份；末字节01/02固定保证SqlGuid与.NET Guid对序相反。
        var suffix=Guid.NewGuid().ToString("N")[20..30];var a=Guid.Parse("ffffffff-ffff-ffff-ffff-"+suffix+"01");var b=Guid.Parse("00000000-0000-0000-0000-"+suffix+"02");
        var logs=new[]{Operation(h.Uid,a,"另一模块",new DateTime(2026,10,5)),Operation(h.Uid,b,"采购",new DateTime(2026,10,5)),Operation(h.Uid,Guid.NewGuid(),"采购",new DateTime(2026,10,4)),Operation(Guid.NewGuid(),Guid.NewGuid(),"采购",new DateTime(2026,10,6))};
        foreach(var log in logs)await f.InsertAsync(log);var d=(await f.Repository.GetAsync(h.Uid))!;Assert.HasCount(3,d.Operations);
        CollectionAssert.AreEqual(logs.Take(3).OrderByDescending(x=>x.OccurredDate).ThenByDescending(x=>new SqlGuid(x.Uid)).Select(x=>x.Uid).ToArray(),d.Operations.Select(x=>x.Uid).ToArray());Assert.IsTrue(d.Operations.Any(x=>x.OperationName=="另一模块"));
    }

    /// <summary>Guard后整批前真实变化按当前表读取；起始scope不能冻结新增行及其反馈，不冒称服务器中途跨连接。</summary>
    [TestMethod]
    public async Task GuardToBatchUsesCurrentTables()
    {
        using var f=new Fixture();var h=f.Head();h.OriginKind=2;await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);var added=f.Line(h,2);bool changed=false;
        f.Hook.Before=async(sql,token)=>{if(!sql.Contains("AS \"PurchaseOrderId\"",StringComparison.Ordinal))return;f.Hook.Before=null;changed=true;await f.InsertAsync(added);await f.InsertAsync(Report(added,1,5m));};
        var d=(await f.Repository.GetAsync(h.Uid))!;Assert.IsTrue(changed);CollectionAssert.AreEqual(new[]{l.Uid},f.Hook.ScopedIds);Assert.HasCount(2,d.Lines);Assert.AreEqual(5m,d.Lines.Single(x=>x.PurchaseOrderLineId==added.Uid).LatestReadyQty);Assert.HasCount(1,d.Lines.Single(x=>x.PurchaseOrderLineId==added.Uid).ProgressHistory);await f.Usable();
    }

    /// <summary>预取消及scope/门禁/整批/六结果推进取消；原token进真实驱动，取消后仅回滚本人外层事务。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)]
    public async Task Cancellation(int stage)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);await f.InsertAsync(f.Line(h));f.Hook.Commands.Clear();using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else if(stage<3)f.Hook.Before=(sql,token)=>{if(f.Hook.Commands.Count==stage)c.Cancel();return Task.CompletedTask;};else f.Hook.ResultAction=i=>{if(i==stage-3)c.Cancel();};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetAsync(h.Uid,c.Token));f.Hook.Before=null;f.Hook.ResultAction=null;
        Assert.IsNotNull(f.Session.Transaction);f.Session.Transaction!.Rollback();f.Session.Transaction.Dispose();f.Session.Transaction=null;await f.Usable();
    }

    /// <summary>无外层事务空scope连续查询正常释放，不创建事务或永久事实。</summary>
    [TestMethod]
    public async Task NoTransactionEmptyContinuous()
    {
        using var f=new Fixture(false);for(int i=0;i<2;i++)Assert.IsNull(await f.Repository.GetAsync(Guid.NewGuid()));Assert.IsNull(f.Session.Transaction);await f.Usable();
    }

    /// <summary>合法随机来源默认MD双方NULL，技术版本1和非64位指纹；只逻辑外键，不依赖真实业务单据。</summary>
    private static (GrossDemand Gross,NetDemand Net,DemandExecutionLink Link) Facts(Fixture f,PurchaseOrderHead h,PurchaseOrderLine l)
    {
        var gid=Guid.NewGuid();var nid=Guid.NewGuid();
        var g=new GrossDemand{Uid=gid,SourceType="MD",SourceUid=gid,SourceLineId=gid,SourceNo=f.Prefix+Guid.NewGuid().ToString("N"),SourceLineNumber=1,IsNonStandard=true,SkuSnapshot="非标",UnitSnapshot="PCS",DemandQty=5m,DemandBasisFingerprint="非64位原始指纹"};
        var n=new NetDemand{Uid=nid,GrossDemandId=gid,RootNetDemandId=nid,DecompositionRevision=1,LineNumber=1,SourceDemandVersionSnapshot=1,SourceDemandBasisFingerprintSnapshot=g.DemandBasisFingerprint,SupplyTypeSnapshot="采购",SkuSnapshot="非标",UnitSnapshot="PCS",GrossQty=5m,NetQty=5m};
        var link=new DemandExecutionLink{Uid=Guid.NewGuid(),NetDemandId=nid,ExecutionRequestId=Guid.NewGuid(),CommandItemId=Guid.NewGuid(),TargetType="PO",TargetHeadId=h.Uid,TargetLineId=l.Uid,TargetNo=h.OrderNo,AllocatedQty=5m,ReleasedQty=2m};
        return(g,n,link);
    }
    /// <summary>原始插入保留指定身份和历史版本，不调用审计Add或修复业务事实。</summary>
    private static async Task InsertFacts(Fixture f,(GrossDemand Gross,NetDemand Net,DemandExecutionLink Link) facts){await f.InsertAsync(facts.Gross);await f.InsertAsync(facts.Net);await f.InsertAsync(facts.Link);}
    /// <summary>独立操作身份保证唯一命令，模块保留用于证明查询没有新增Module筛选。</summary>
    private static BusinessOperationLog Operation(Guid head,Guid uid,string module,DateTime date)=>new(){Uid=uid,CommandId=Guid.NewGuid(),Module=module,AggregateType="采购证据",AggregateUid=head,OperationType="ReadFixture",OperationName=module,UserNameSnapshot="测试快照",OccurredDate=date};

    /// <summary>当前产品供图像读取，明确必填供货方式和独立SKU。</summary>
    private static Product Product(Fixture f)=>new(){Uid=Guid.NewGuid(),Sku=f.Prefix+Guid.NewGuid().ToString("N"),SupplyType="采购",MaterialType="零件",Unit="PCS",MainImage="当前图像"};
    /// <summary>追加完整反馈快照，NULL保持；下降量由调用用例明确提供纠错事实。</summary>
    private static SupplierProgressReport Report(PurchaseOrderLine line,int sequence,decimal? ready)=>new(){Uid=Guid.NewGuid(),PurchaseOrderLineId=line.Uid,Sequence=sequence,ReadyQty=ready,CommandId=Guid.NewGuid(),SupplierReportedDate=new DateTime(2026,10,5,12,0,0,DateTimeKind.Unspecified)};

    /// <summary>生成合法正式或草稿收货；反向头具备完整两条匹配流水，不绕全局Guard。</summary>
    private static async Task<(InboundDocument Head,InboundDocumentLine Line)> Receipt(Fixture f,Guid source,decimal quantity,int status,int type,bool deletedHead=false,bool deletedLine=false,string unit="PCS")
    {
        var h=new InboundDocument{Uid=Guid.NewGuid(),DocumentNo=f.Prefix+Guid.NewGuid().ToString("N"),DocumentType=(InboundDocumentTypeEnum)type,PostingStatus=(InboundPostingStatusEnum)status,ReceiptDate=new DateTime(2026,10,5),PostedDate=status==0?null:new DateTime(2026,10,5),WarehouseSiteId=Guid.NewGuid(),IsDeleted=deletedHead};
        if(status==8){h.ReversedDate=new DateTime(2026,10,6);h.ReverseReason="测试安全反向";}
        await f.InsertAsync(h);
        var l=new InboundDocumentLine{Uid=Guid.NewGuid(),InboundDocumentId=h.Uid,LineNumber=1,SourceLineId=source,ProductId=Guid.NewGuid(),SkuSnapshot="收货SKU",UnitSnapshot=unit,Quantity=quantity,IsDeleted=deletedLine};
        await f.InsertAsync(l);
        if(deletedLine&&!deletedHead)
        {
            // 有效正式头必须至少一有效行；另加无关合法正量行，避免无行头被Guard合法拒绝。
            await f.InsertAsync(new InboundDocumentLine{Uid=Guid.NewGuid(),InboundDocumentId=h.Uid,LineNumber=2,SourceLineId=Guid.NewGuid(),ProductId=Guid.NewGuid(),SkuSnapshot="保留行",UnitSnapshot=unit,Quantity=1m});
        }
        if(status==8)
        {
            var seq=Random.Shared.NextInt64(1_000_000_000_000,long.MaxValue-1);
            var original=new InventoryLedgerEntry{Uid=Guid.NewGuid(),Sequence=seq,WarehouseSiteId=h.WarehouseSiteId,ProductId=l.ProductId,TransactionType=(InventoryTransactionTypeEnum)10,QuantityDelta=quantity,BalanceAfter=quantity,SourceDocumentType="InboundDocument",SourceDocumentId=h.Uid,SourceLineId=l.Uid,CommandItemId=l.Uid,CommandId=Guid.NewGuid(),UnitSnapshot=l.UnitSnapshot,OccurredDate=new DateTime(2026,10,5)};
            await f.InsertAsync(original);
            await f.InsertAsync(new InventoryLedgerEntry{Uid=Guid.NewGuid(),Sequence=seq+1,WarehouseSiteId=h.WarehouseSiteId,ProductId=l.ProductId,TransactionType=(InventoryTransactionTypeEnum)900,QuantityDelta=-quantity,BalanceAfter=0m,SourceDocumentType="InboundDocument",SourceDocumentId=h.Uid,SourceLineId=l.Uid,CommandItemId=Guid.NewGuid(),CommandId=Guid.NewGuid(),UnitSnapshot=l.UnitSnapshot,ReversalOfLedgerId=original.Uid,OccurredDate=new DateTime(2026,10,6)});
        }
        return(h,l);
    }

    /// <summary>独立随机身份与事务；原始元数据插入不得通过审计Add重置指定UID。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly string Prefix="PQ"+Guid.NewGuid().ToString("N")+"X";
        public readonly Guid CustomerId=Guid.NewGuid();
        public readonly DbSession Session;
        public readonly PostgreSqlPurchaseOrderTraceRepository Repository;
        public HookConnection Hook=>(HookConnection)Session.Connection;
        public Fixture(bool transaction=true)
        {
            Session=new(new HookFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
            try{if(transaction)Session.Transaction=Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);var user=new Identity();Repository=new(new RepositoryBaseContext(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}
            catch{Session.Dispose();throw;}
        }
        /// <summary>合法头保留随机请求和供应商身份；未创建真实业务单据或主数据关联。</summary>
        public PurchaseOrderHead Head()=>new(){Uid=Guid.NewGuid(),OrderNo=Prefix+Guid.NewGuid().ToString("N"),CustomerId=CustomerId,CustomerNameSnapshot="客户旧快照",SupplierId=Guid.NewGuid(),SupplierCode="供应商编码",SupplierName="供应商旧快照",ExecutionRequestId=Guid.NewGuid(),OrderDate=new DateTime(2026,10,5,12,0,0,DateTimeKind.Unspecified)};
        /// <summary>正数量、正行号通过既有CHECK；商业金额默认NULL。</summary>
        public PurchaseOrderLine Line(PurchaseOrderHead head,int number=1)=>new(){Uid=Guid.NewGuid(),PurchaseOrderId=head.Uid,OrderNo=head.OrderNo,LineNumber=number,Quantity=10m,Sku=Prefix+"SKU",UnitSnapshot="PCS"};
        /// <summary>插入明确身份，使用现有参数类型与映射；不审计重置UID，不DDL或提交。</summary>
        public async Task InsertAsync<T>(T entity) where T:BaseEntity
        {
            var uid=entity.Uid;var map=EntityMetadataCache.GetMetadata(typeof(T));
            var columns=string.Join(',',map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));var values=string.Join(',',map.Columns.Select(x=>"@"+x.ParameterName));
            Assert.AreEqual(1,await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(entity,map),Session.Transaction)));Assert.AreEqual(uid,entity.Uid);
        }
        /// <summary>连续读取证明reader已释放且调用者事务仍可用。</summary>
        public async Task Usable()=>Assert.AreEqual(1,await Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:Session.Transaction)));
        /// <summary>任何退出均回滚自己的事务，最后释放连接；不执行清库或提交。</summary>
        public void Dispose(){try{if(Session.Transaction is {} tx){try{tx.Rollback();}finally{tx.Dispose();Session.Transaction=null;}}}finally{Session.Dispose();}}
    }
    /// <summary>独立测试审计身份，不记录凭据。</summary>
    private sealed class Identity:ICurrentUser
    {
        public Guid? UserId=>null;public string UserName=>"采购Trace回滚夹具";public bool IsAuthenticated=>false;
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
        public Action<int>? ResultAction;
        public int[] ReadCounts=new int[6];
        public Guid[]? ScopedIds;
        public int NextResults,Disposals;
        public bool ReaderClosed;
        public CancellationToken LastToken;
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
            private async Task Starting(CancellationToken token){if(owner.Before is {} before)await before(CommandText,token);token.ThrowIfCancellationRequested();owner.Commands.Add(CommandText);if(innerCommand.Parameters.Contains("Scoped"))owner.ScopedIds=(Guid[])innerCommand.Parameters["PurchaseLineIds"].Value!;}
            public override int ExecuteNonQuery()=>innerCommand.ExecuteNonQuery();public override object? ExecuteScalar()=>innerCommand.ExecuteScalar();
            /// <summary>先完成测试阶段屏障，再以真实Npgsql执行夹具写入，事务仍由夹具拥有。</summary>
            public override async Task<int> ExecuteNonQueryAsync(CancellationToken token){await Starting(token);return await innerCommand.ExecuteNonQueryAsync(token);}
            /// <summary>先完成测试阶段屏障，再执行真实Guard或标量验证，不模拟返回值。</summary>
            public override async Task<object?> ExecuteScalarAsync(CancellationToken token){await Starting(token);return await innerCommand.ExecuteScalarAsync(token);}
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>innerCommand.ExecuteReader(behavior);
            /// <summary>在reader打开之前完成阶段屏障，然后传递token并以包装reader观察六结果推进。</summary>
            protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token)
            {
                await Starting(token);var reader=await innerCommand.ExecuteReaderAsync(behavior,token);
                return CommandText.Contains("AS \"PurchaseOrderId\"",StringComparison.Ordinal)?new BatchReader(owner,reader):reader;
            }
            protected override void Dispose(bool disposing){if(disposing)innerCommand.Dispose();base.Dispose(disposing);}
        }
        /// <summary>转发全部列类型，只观察各结果Read/NextResult/Dispose；不自动排空未读取的结果。</summary>
        private sealed class BatchReader(HookConnection owner,DbDataReader inner):DbDataReader
        {
            private int phase;private bool disposed;
            public override bool Read(){owner.ResultAction?.Invoke(phase);owner.ReadCounts[phase]++;return inner.Read();}
            public override bool NextResult(){owner.NextResults++;if(phase<5)owner.ResultAction?.Invoke(phase+1);var result=inner.NextResult();if(result)phase++;return result;}
            /// <summary>观察当前结果读取边界并将取消令牌传递给真实驱动，不替代数据库返回。</summary>
            public override async Task<bool> ReadAsync(CancellationToken token){owner.LastToken=token;owner.ResultAction?.Invoke(phase);token.ThrowIfCancellationRequested();owner.ReadCounts[phase]++;return await inner.ReadAsync(token);}
            /// <summary>观察结果切换边界并使用真实异步驱动推进；取消保持技术异常。</summary>
            public override async Task<bool> NextResultAsync(CancellationToken token){owner.LastToken=token;owner.NextResults++;if(phase<5)owner.ResultAction?.Invoke(phase+1);token.ThrowIfCancellationRequested();var result=await inner.NextResultAsync(token);if(result)phase++;return result;}
            public override void Close(){inner.Close();owner.ReaderClosed=true;}
            protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;owner.Disposals++;inner.Dispose();owner.ReaderClosed=true;}base.Dispose(disposing);}
            public override int FieldCount=>inner.FieldCount;public override bool HasRows=>inner.HasRows;public override bool IsClosed=>inner.IsClosed;public override int RecordsAffected=>inner.RecordsAffected;public override int Depth=>inner.Depth;
            public override object this[int ordinal]=>inner[ordinal];public override object this[string name]=>inner[name];
            public override string GetName(int ordinal)=>inner.GetName(ordinal);public override string GetDataTypeName(int ordinal)=>inner.GetDataTypeName(ordinal);public override Type GetFieldType(int ordinal)=>inner.GetFieldType(ordinal);
            public override object GetValue(int ordinal)=>inner.GetValue(ordinal);public override int GetValues(object[] values)=>inner.GetValues(values);public override int GetOrdinal(string name)=>inner.GetOrdinal(name);public override bool IsDBNull(int ordinal)=>inner.IsDBNull(ordinal);
            public override bool GetBoolean(int ordinal)=>inner.GetBoolean(ordinal);public override byte GetByte(int ordinal)=>inner.GetByte(ordinal);public override long GetBytes(int ordinal,long offset,byte[]? buffer,int bufferOffset,int length)=>inner.GetBytes(ordinal,offset,buffer,bufferOffset,length);
            public override char GetChar(int ordinal)=>inner.GetChar(ordinal);public override long GetChars(int ordinal,long offset,char[]? buffer,int bufferOffset,int length)=>inner.GetChars(ordinal,offset,buffer,bufferOffset,length);
            public override Guid GetGuid(int ordinal)=>inner.GetGuid(ordinal);public override short GetInt16(int ordinal)=>inner.GetInt16(ordinal);public override int GetInt32(int ordinal)=>inner.GetInt32(ordinal);public override long GetInt64(int ordinal)=>inner.GetInt64(ordinal);
            public override float GetFloat(int ordinal)=>inner.GetFloat(ordinal);public override double GetDouble(int ordinal)=>inner.GetDouble(ordinal);public override string GetString(int ordinal)=>inner.GetString(ordinal);public override decimal GetDecimal(int ordinal)=>inner.GetDecimal(ordinal);public override DateTime GetDateTime(int ordinal)=>inner.GetDateTime(ordinal);
            public override IEnumerator GetEnumerator()=>((IEnumerable)inner).GetEnumerator();public override DataTable? GetSchemaTable()=>inner.GetSchemaTable();
        }
    }
}
