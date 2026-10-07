using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Infrastructure.Repositories.ForExcel;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>来源交付普通真实PG用例仅编写编译，由主控严格故障库串行执行；所有合法随机事实在父事务回滚，不创建结构或提交。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlSourceDeliveryQueryMigrationIntegrationTests
{
    /// <summary>集中验证来源工作台新增列表与采购Excel四方法实际PG及可空产品关系，全部随机主数据回滚。</summary>
    [TestMethod]
    public async Task RealPg_ResumedWorkspaceAndPurchaseImport()
    {
        using var f=new Fixture();var(c,p,g)=await f.Seed();var user=new User();var context=new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(user),user);
        var workspace=new PostgreSqlGrossDemandQueryRepository(context);var import=new PostgreSqlForExcelPurchaseOrderRepository(context);
        var page=await workspace.SearchWorkspaceAsync(new GrossDemandSearchRequest{SourceUid=g.SourceUid,SourceTypes=["SO"]});Assert.AreEqual(1,page.TotalCount);Assert.AreEqual(g.Uid,page.Items.Single().Uid);Assert.AreEqual(g.DemandQty,page.Items.Single().DemandQty);
        Assert.AreEqual(c.Uid,(await import.FindCustomersAsync(c.CustomerCode.ToLowerInvariant()+"   ",default)).Single().Uid);Assert.IsNull(await import.FindSupplierAsync(Guid.NewGuid(),default));
        var relation=new CustomerProduct{CustomerId=c.Uid,CustomerCode=c.CustomerCode,SkuId=p.Uid,CustomerSku=f.Prefix+"SKU",IsActive=true};await f.Insert(relation);
        var matches=await import.MatchProductsAsync(c.Uid,[relation.CustomerSku.ToUpperInvariant()],default);Assert.AreEqual(1,matches.Count);Assert.AreEqual(p.Uid,matches.Single().ProductId);Assert.IsTrue(matches.Single().IsUsable);
        await f.Insert(new SysDictData{DictCode="Unit",DataNameEn=f.Prefix,IsEnabled=true});Assert.AreEqual(f.Prefix,await import.FindUnitAsync(f.Prefix.ToLowerInvariant()+"   ",default));
        await f.Usable();
    }

    /// <summary>集中验证装箱三个接口及执行/事实仓储的实际PG、行号交换和历史范围，所有随机事实由既有夹具回滚。</summary>
    [TestMethod]
    public async Task RealPg_ResumedPackingListRepositories()
    {
        using var f=new Fixture();var(c,p,g)=await f.Seed();var user=new User();
        var context=new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(user),user);
        var repository=new PostgreSqlPackingListRepository(context);var executions=new PostgreSqlPackingListExecutionRepository(context);var facts=new PostgreSqlPackingListPostingFactsRepository(context);
        Assert.IsTrue(await repository.IsSchemaReadyAsync());Assert.IsTrue(await executions.IsSchemaReadyAsync(default));
        var h=f.Head(c);h.PostingStatus=PackingListPostingStatusEnum.Unposted;var first=f.Line(h,g,2m);var second=f.Line(h,g,3m);second.LineNumber=2;first.Version=0;second.Version=0;
        first.InvoiceNo="InvB";second.InvoiceNo="InvA";first.PackingGroupId=Guid.NewGuid();
        Assert.IsFalse(await repository.HasForeignPackingGroupsAsync(h.Uid,[first.PackingGroupId.Value]));
        await repository.SaveAsync(h,[first,second],[],0);Assert.AreEqual(1,h.Version);Assert.AreEqual(1,first.Version);
        Assert.AreEqual(2,(await repository.LockLinesAsync(h.Uid)).Count);Assert.AreEqual(h.Uid,(await repository.LockHeaderAsync(h.Uid,false))?.Uid);
        Assert.AreEqual(2,(await repository.LockLineOwnersAsync([first.Uid,second.Uid])).Count);
        var detail=await repository.GetDetailAsync(h.Uid);Assert.IsNotNull(detail);Assert.AreEqual("InvB, InvA",detail.Header.InvoiceNosSummary);
        var page=await repository.SearchAsync(new PackingListManagerSearchRequest{Keyword=h.PackingListNo});Assert.AreEqual(1,page.TotalCount);
        first.LineNumber=2;second.LineNumber=1;await repository.SaveAsync(h,[first,second],[],h.Version);Assert.AreEqual(2,h.Version);
        Assert.AreEqual("InvA, InvB",(await repository.GetDetailAsync(h.Uid))?.Header.InvoiceNosSummary);
        await repository.SaveAsync(h,[second],[first],h.Version);Assert.AreEqual(2,first.Version);Assert.IsFalse(first.IsDeleted);
        Assert.AreEqual(1,(await repository.GetDetailAsync(h.Uid))?.Lines.Count);
        Assert.AreEqual(1,(await repository.AssessSourceExitForUpdateAsync([g.Uid])).ActiveDraftCount);
        var sourcePage=await repository.SearchSourcesAsync(new PackingListSourceSearchRequest{CustomerId=c.Uid,Keyword=f.Prefix});Assert.AreEqual(1,sourcePage.TotalCount);Assert.AreEqual(g.Uid,sourcePage.Items.Single().GrossDemandId);
        Assert.AreEqual(g.Uid,(await repository.GetSourceEnrichmentsAsync([g.Uid])).Single().GrossDemandId);
        await facts.LockSourcesAsync([g.Uid],default);var observed=await facts.ReadAsync(h.Uid,default);Assert.AreEqual(1,observed.Lines.Count);Assert.AreEqual(g.Uid,observed.Sources.Single().Uid);Assert.IsTrue(observed.CustomerExists);
        var execution=new PackingListPostingExecution{PackingListHeadId=h.Uid,CommandId=Guid.NewGuid(),Operation=1,BusinessDate=new DateTime(2026,10,5),PackingListNo=h.PackingListNo,ExpectedVersion=h.Version,RequestFingerprint=new string('a',64),PreviewToken=new string('b',64),ResultJson="{}"};
        Assert.IsNull(await executions.FindCommandAsync(execution.CommandId,default));
        var evidence=new PackingListPostingExecutionLine{ExecutionId=execution.Uid,LineNumber=1,PackingListLineId=second.Uid,GrossDemandId=g.Uid,ProductId=g.ProductId,Qty=second.Qty,ActionKind="Delivery"};
        await executions.AppendAsync(execution,[evidence],default);Assert.AreEqual(execution.Uid,(await executions.FindCommandAsync(execution.CommandId,default))?.Uid);
        var history=await executions.ReadAsync(h.Uid,default);Assert.AreEqual(1,history.Heads.Count);Assert.AreEqual(evidence.Uid,history.Lines.Single().Uid);
        h.PostCommandId=execution.CommandId;h.PostingDate=execution.BusinessDate;h.PostedDate=execution.BusinessDate;h.PostedBy=user.UserName;
        await executions.MarkAsync(h,h.Version,false,default);Assert.AreEqual(PackingListPostingStatusEnum.Posted,(await executions.ReadReverseFactsAsync(h.Uid,default)).Head?.PostingStatus);
        Assert.AreEqual(1,(await repository.AssessSourceExitForUpdateAsync([g.Uid])).PostedPackingListCount);
        h.ReverseCommandId=Guid.NewGuid();h.ReversedDate=execution.BusinessDate;h.ReversedBy=user.UserName;h.ReverseReason="原反向理由";
        await executions.MarkAsync(h,h.Version,true,default);Assert.AreEqual(PackingListPostingStatusEnum.Reversed,(await executions.ReadReverseFactsAsync(h.Uid,default)).Head?.PostingStatus);
        Assert.AreEqual(0,(await repository.AssessSourceExitForUpdateAsync([g.Uid])).InvalidHistoryCount);
        var other=f.Head(c);other.PostingStatus=PackingListPostingStatusEnum.Unposted;Assert.IsTrue(await repository.HasForeignPackingGroupsAsync(other.Uid,[first.PackingGroupId.Value]));
        await repository.SaveAsync(other,[],[],0);await repository.DeleteAsync(other);Assert.IsNull(await repository.LockHeaderAsync(other.Uid,false));
        await f.Usable();
    }

    /// <summary>验证新增锁定方法真实SQL、现有Posted净额和输入范围；无事务仍遵守原门禁，随机头行全部回滚。</summary>
    [TestMethod]
    public async Task RealPg_CompletedSourceDeliveryLockRead()
    {
        using(var noParent=new Fixture(false))
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async()=>await noParent.Repository.GetForUpdateAsync([]));
        using var f=new Fixture();var (c,p,g)=await f.Seed();var h=f.Head(c);await f.Insert(h);await f.Insert(f.Line(h,g,2.125m));
        Assert.AreEqual(0,(await f.Repository.GetForUpdateAsync([Guid.Empty])).Count);
        var rows=await f.Repository.GetForUpdateAsync([g.Uid,g.Uid,Guid.Empty,Guid.NewGuid()]);
        Assert.AreEqual(1,rows.Count);Assert.AreEqual(g.Uid,rows[0].GrossDemandId);Assert.AreEqual(2.125m,rows[0].DeliveredQty);Assert.AreEqual(7.875m,rows[0].RemainingQty);
        await f.Usable();
    }

    /// <summary>只验证当前真实目录已部署结果，可空父事务保持；缺表/缺列/视图不在已有库DDL制造。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ExistingSchema(bool parent)
    {using var f=new Fixture(parent);Assert.IsTrue(await f.Repository.IsSchemaReadyAsync());Assert.AreEqual(parent,f.Session.Transaction is not null);await f.Usable();}

    /// <summary>27字段实际映射由Gross原快照提供，当前停用产品/客户仍关联；原文NULL与小数不重新计算或Trim。</summary>
    [TestMethod]
    public async Task FullCurrentSnapshotProjection()
    {
        using var f=new Fixture();var c=f.Customer();c.IsActive=false;c.CustomerName=null;var p=f.Product();p.IsActive=false;p.Sku+="current";p.Unit="当前单位";var g=f.Gross(c,p);g.SkuSnapshot=" 原SKU ";g.SourceType=" SO ";g.SourceNo=f.Prefix+" 原单号 ";g.LinkSkuSnapshot=null;g.DescriptionSnapshot=null;g.SpecificationSnapshot="规格";g.ImagePathSnapshot="图片";g.ColorSnapshot="颜色";g.CustomerSkuSnapshot="客型";g.UnitSnapshot=" PCS ";g.SourceVersion=null;g.SourceLineNumber=null;g.RequiredDate=new DateTime(2026,10,5);await f.Insert(c);await f.Insert(p);await f.Insert(g);var h=f.Head(c);await f.Insert(h);await f.Insert(f.Line(h,g,2.125m));
        var expected=new Dictionary<string,object?>{["GrossDemandId"]=g.Uid,["DemandVersion"]=g.Version,["SourceType"]=g.SourceType,["SourceUid"]=g.SourceUid,["SourceNo"]=g.SourceNo,["SourceLineId"]=g.SourceLineId,["SourceLineNumber"]=g.SourceLineNumber,["SourceVersion"]=g.SourceVersion,["CustomerId"]=c.Uid,["CustomerCode"]=c.CustomerCode,["CustomerName"]=null,["ProductId"]=p.Uid,["IsNonStandard"]=false,["Sku"]=g.SkuSnapshot,["LinkSku"]=null,["CustomerSku"]=g.CustomerSkuSnapshot,["Description"]=null,["Color"]=g.ColorSnapshot,["Specification"]=g.SpecificationSnapshot,["ImagePath"]=g.ImagePathSnapshot,["Unit"]=g.UnitSnapshot,["DemandQty"]=10m,["DeliveredQty"]=2.125m,["RemainingQty"]=7.875m,["LifecycleStatus"]=g.LifecycleStatus,["IsActive"]=true,["RequiredDate"]=g.RequiredDate};
        Assert.HasCount(27,expected);var detail=await f.Repository.GetAsync(g.Uid);Assert.IsNotNull(detail);var search=await f.Repository.SearchAsync(f.Request(c));Assert.AreEqual(1,search.TotalCount);foreach(var row in new[]{detail,search.Items.Single()})foreach(var field in expected)Assert.AreEqual(field.Value,typeof(SourceDemandDeliveryItemDto).GetProperty(field.Key)!.GetValue(row),field.Key);await f.Usable();
    }

    /// <summary>共同准入与Search专用活动/客户资格分离，不新增单位、产品/客户启用、来源类型白名单。</summary>
    [TestMethod]
    [DataRow("valid",true,true)] [DataRow("product-missing",false,false)] [DataRow("product-deleted",false,false)] [DataRow("product-inactive",true,true)]
    [DataRow("nonstandard",true,true)] [DataRow("gross-deleted",false,false)] [DataRow("closed",true,false)] [DataRow("voided",true,false)]
    [DataRow("customer-null",true,false)] [DataRow("customer-missing",true,false)] [DataRow("customer-deleted",true,false)] [DataRow("customer-inactive",true,true)]
    [DataRow("unit-null",true,true)] [DataRow("arbitrary-type",true,true)]
    public async Task Qualification(string mode,bool get,bool search)
    {
        using var f=new Fixture();var c=f.Customer();var p=f.Product();var g=f.Gross(c,p);if(mode=="product-deleted")p.IsDeleted=true;if(mode=="product-inactive")p.IsActive=false;if(mode=="nonstandard"){g.IsNonStandard=true;g.ProductId=null;}if(mode=="gross-deleted")g.IsDeleted=true;if(mode=="closed")g.LifecycleStatus=GrossDemandLifecycleStatusEnum.Closed;if(mode=="voided")g.LifecycleStatus=GrossDemandLifecycleStatusEnum.Voided;if(mode=="customer-null")g.CustomerId=null;if(mode=="customer-deleted")c.IsDeleted=true;if(mode=="customer-inactive")c.IsActive=false;if(mode=="unit-null")g.UnitSnapshot=null;if(mode=="arbitrary-type")g.SourceType="自定义来源";
        if(mode!="customer-missing")await f.Insert(c);if(mode!="product-missing")await f.Insert(p);await f.Insert(g);var detail=await f.Repository.GetAsync(g.Uid);Assert.AreEqual(get,detail is not null);var q=f.Request(c);var rows=await f.Repository.SearchAsync(q);Assert.AreEqual(search?1:0,rows.TotalCount);Assert.HasCount(search?1:0,rows.Items);if(detail is not null&&mode.StartsWith("customer-",StringComparison.Ordinal)&&mode!="customer-inactive")Assert.IsNull(detail.CustomerId);
    }

    /// <summary>正式SUM仅有效Posted头行，多头多行按同Gross合计；缺头/软删/反向/草稿不计，头生命周期不额外限制。</summary>
    [TestMethod]
    [DataRow("none",0)] [DataRow("draft",0)] [DataRow("posted",2)] [DataRow("reversed",0)] [DataRow("head-deleted",0)] [DataRow("line-deleted",0)] [DataRow("head-missing",0)]
    [DataRow("closed-posted",2)] [DataRow("voided-posted",2)] [DataRow("multiple",6)] [DataRow("empty-head",0)] [DataRow("unbound",0)]
    public async Task DeliveredHistory(string mode,int delivered)
    {
        using var f=new Fixture();var (c,p,g)=await f.Seed();if(mode!="none"){var h=f.Head(c);if(mode=="draft")h.PostingStatus=PackingListPostingStatusEnum.Unposted;if(mode=="reversed")h.PostingStatus=PackingListPostingStatusEnum.Reversed;if(mode=="head-deleted")h.IsDeleted=true;if(mode=="closed-posted")h.LifecycleStatus=PackingListLifecycleStatusEnum.Closed;if(mode=="voided-posted")h.LifecycleStatus=PackingListLifecycleStatusEnum.Voided;if(mode!="head-missing")await f.Insert(h);if(mode!="empty-head"){var l=f.Line(h,g,2m);if(mode=="line-deleted")l.IsDeleted=true;if(mode=="unbound")l.GrossDemandId=null;await f.Insert(l);}if(mode=="multiple"){var l=f.Line(h,g,1m);l.LineNumber=2;await f.Insert(l);var another=f.Head(c);await f.Insert(another);await f.Insert(f.Line(another,g,3m));}}
        var detail=await f.Repository.GetAsync(g.Uid);Assert.IsNotNull(detail);Assert.AreEqual((decimal)delivered,detail.DeliveredQty);Assert.AreEqual(10m-delivered,detail.RemainingQty);Assert.AreEqual(1,(await f.Repository.SearchAsync(f.Request(c))).TotalCount);
    }

    /// <summary>正Qty合法超额构造零/负剩余，Get保留、Search只正剩余，不拆CHECK或钳制。</summary>
    [TestMethod]
    [DataRow(2.125)] [DataRow(10.0)] [DataRow(12.125)]
    public async Task RemainingBoundary(double qty)
    {using var f=new Fixture();var (c,p,g)=await f.Seed();var h=f.Head(c);await f.Insert(h);await f.Insert(f.Line(h,g,(decimal)qty));var d=await f.Repository.GetAsync(g.Uid);Assert.IsNotNull(d);Assert.AreEqual((decimal)qty,d.DeliveredQty);Assert.AreEqual(10m-(decimal)qty,d.RemainingQty);Assert.AreEqual(qty<10?1:0,(await f.Repository.SearchAsync(f.Request(c))).TotalCount);}

    /// <summary>其他正式客户出库头行数量不重复扣SUM；原始关系夹具不充完整过账/流水验收。</summary>
    [TestMethod]
    public async Task CustomerOutboundDoesNotDoubleDeduct()
    {using var f=new Fixture();var(c,p,g)=await f.Seed();var h=f.Head(c);await f.Insert(h);await f.Insert(f.Line(h,g,2m));var w=new WarehouseSite{Uid=Guid.NewGuid(),Code=f.Prefix+"W",Name="随机仓",IsActive=true,SiteType=(WarehouseSiteTypeEnum)1};await f.Insert(w);var o=new OutboundDocument{Uid=Guid.NewGuid(),DocumentNo=f.Prefix+"O",CommandId=Guid.NewGuid(),DocumentType=OutboundDocumentTypeEnum.FinalCustomerShipment,PostingStatus=OutboundPostingStatusEnum.Posted,WarehouseSiteId=w.Uid,ShipmentDate=new DateTime(2026,10,5),TriggerPackingListId=h.Uid};await f.Insert(o);await f.Insert(new OutboundDocumentLine{Uid=Guid.NewGuid(),OutboundDocumentId=o.Uid,LineNumber=1,ProductId=p.Uid,Quantity=9m,SkuSnapshot=p.Sku,UnitSnapshot="PCS",CommandItemId=Guid.NewGuid()});Assert.AreEqual(2m,(await f.Repository.GetAsync(g.Uid))!.DeliveredQty);}

    /// <summary>筛选沿原AND关系，SourceType字典尾ASCII与大小写等值，客户Empty不等于无过滤。</summary>
    [TestMethod]
    [DataRow("all",1)] [DataRow("type-ci",1)] [DataRow("type-miss",0)] [DataRow("customer-empty",0)] [DataRow("customer-other",0)] [DataRow("and-match",1)] [DataRow("and-miss",0)]
    public async Task Filters(string mode,int total)
    {using var f=new Fixture();var(c,p,g)=await f.Seed();var q=f.Request(c);if(mode=="type-ci")q.SourceType=" so  ";if(mode=="type-miss")q.SourceType="CR";if(mode=="customer-empty")q.CustomerId=Guid.Empty;if(mode=="customer-other")q.CustomerId=Guid.NewGuid();if(mode.StartsWith("and-",StringComparison.Ordinal)){q.SourceNo=g.SourceNo;q.Sku=mode=="and-match"?g.SkuSnapshot:"缺失型号";q.Keyword=g.SkuSnapshot;}Assert.AreEqual(total,(await f.Repository.SearchAsync(q)).TotalCount);}

    /// <summary>六Keyword列只作字面匹配，规格/单位/客户名不能扩入，原NULL和.NET Trim沿生产。</summary>
    [TestMethod]
    [DataRow("SourceNo",true)] [DataRow("SkuSnapshot",true)] [DataRow("LinkSkuSnapshot",true)] [DataRow("CustomerSkuSnapshot",true)] [DataRow("DescriptionSnapshot",true)] [DataRow("ColorSnapshot",true)]
    [DataRow("SpecificationSnapshot",false)] [DataRow("UnitSnapshot",false)] [DataRow("CustomerName",false)]
    public async Task LiteralKeywordScope(string field,bool expected)
    {using var f=new Fixture();var c=f.Customer();var p=f.Product();var g=f.Gross(c,p);if(field=="CustomerName")c.CustomerName="~%_[";else typeof(GrossDemand).GetProperty(field)!.SetValue(g,"~%_[");await f.Insert(c);await f.Insert(p);await f.Insert(g);var foil=f.Gross(c,p);foil.LinkSkuSnapshot="~xA";await f.Insert(foil);var q=f.Request(c);q.Keyword=" ~%_[ ";var d=await f.Repository.SearchAsync(q);Assert.AreEqual(expected?1:0,d.TotalCount);if(expected)Assert.AreEqual(g.Uid,d.Items.Single().GrossDemandId);}

    /// <summary>SourceNo/Sku独立文本过滤沿相同字面转义，不只靠Keyword路径覆盖。</summary>
    [TestMethod]
    [DataRow("number")] [DataRow("sku")]
    public async Task DedicatedLiteralFilters(string field)
    {using var f=new Fixture();var c=f.Customer();var p=f.Product();var g=f.Gross(c,p);if(field=="number")g.SourceNo="~%_[";else g.SkuSnapshot="~%_[";await f.Insert(c);await f.Insert(p);await f.Insert(g);var foil=f.Gross(c,p);foil.SourceNo="~xA";foil.SkuSnapshot="~xA";await f.Insert(foil);var q=f.Request(c);if(field=="number")q.SourceNo=" ~%_[ ";else q.Sku=" ~%_[ ";var d=await f.Repository.SearchAsync(q);Assert.AreEqual(1,d.TotalCount);Assert.AreEqual(g.Uid,d.Items.Single().GrossDemandId);}

    /// <summary>多Gross/多输入关系合计相互隔离，其他Gross正式行不泄漏到当前来源。</summary>
    [TestMethod]
    public async Task DifferentGrossTotalsRemainIndependent()
    {using var f=new Fixture();var(c,p,g)=await f.Seed();var other=f.Gross(c,p);await f.Insert(other);var h=f.Head(c);await f.Insert(h);await f.Insert(f.Line(h,g,2m));var l=f.Line(h,other,4m);l.LineNumber=2;await f.Insert(l);var d=await f.Repository.SearchAsync(f.Request(c));Assert.AreEqual(2,d.TotalCount);Assert.AreEqual(2m,d.Items.Single(x=>x.GrossDemandId==g.Uid).DeliveredQty);Assert.AreEqual(4m,d.Items.Single(x=>x.GrossDemandId==other.Uid).DeliveredQty);Assert.AreEqual(8m,(await f.Repository.GetAsync(g.Uid))!.RemainingQty);Assert.AreEqual(6m,(await f.Repository.GetAsync(other.Uid))!.RemainingQty);}

    /// <summary>来源单号、来源类型、行号三层排序优先于UID，分页使用完整原顺序。</summary>
    [TestMethod]
    public async Task SourceSortHierarchy()
    {using var f=new Fixture();var c=f.Customer();var p=f.Product();await f.Insert(c);await f.Insert(p);var values=new[]{("B","AA",1),("A","ZZ",1),("A","AA",2),("A","AA",1)};var rows=new List<GrossDemand>();foreach(var(no,type,line)in values){var g=f.Gross(c,p);g.SourceNo=f.Prefix+no;g.SourceType=type;g.SourceLineNumber=line;rows.Add(g);await f.Insert(g);}var d=await f.Repository.SearchAsync(f.Request(c));CollectionAssert.AreEqual(new[]{rows[3].Uid,rows[2].Uid,rows[1].Uid,rows[0].Uid},d.Items.Select(x=>x.GrossDemandId).ToArray());}

    /// <summary>中文/ASCII大小写及Unicode空白在实际LIKE路径中验证，不能硬编码样本返回。</summary>
    [TestMethod]
    [DataRow(" MixedCaseZX ","MIXEDCASEZX")] [DataRow(" 中文交付 ","中文交付")] [DataRow("	 ",null)]
    public async Task KeywordTrim(string keyword,string? value)
    {using var f=new Fixture();var(c,p,g)=await f.Seed();if(value is not null){g.ColorSnapshot=value;await f.Update(g,"ColorSnapshot");}var q=f.Request(c);q.Keyword=keyword;Assert.AreEqual(1,(await f.Repository.SearchAsync(q)).TotalCount);}

    /// <summary>真实Gross允许相同来源显示排序键；NULL行号与intMax等键后按SqlGuid，页内及最终传输都验证。</summary>
    [TestMethod]
    public async Task FrozenOrderAndPage()
    {
        using var f=new Fixture();var c=f.Customer();var p=f.Product();await f.Insert(c);await f.Insert(p);var node=Guid.NewGuid().ToString("N")[20..30];var a=Guid.Parse("ffffffff-ffff-ffff-ffff-"+node+"01");var b=Guid.Parse("00000000-0000-0000-0000-"+node+"02");Assert.IsTrue(a.CompareTo(b)>0);Assert.IsTrue(new SqlGuid(a).CompareTo(new SqlGuid(b))<0);
        var ga=f.Gross(c,p);ga.Uid=a;ga.SourceNo=f.Prefix+"casekey ";ga.SourceType="SO ";ga.SourceLineNumber=null;var gb=f.Gross(c,p);gb.Uid=b;gb.SourceNo=f.Prefix+"CASEKEY";gb.SourceType="so";gb.SourceLineNumber=int.MaxValue;await f.Insert(ga);await f.Insert(gb);var q=f.Request(c);q.PageSize=1;var first=await f.Repository.SearchAsync(q);Assert.AreEqual(2,first.TotalCount);Assert.AreEqual(a,first.Items.Single().GrossDemandId);Assert.AreEqual(ga.SourceNo,first.Items.Single().SourceNo);q.PageIndex=2;Assert.AreEqual(b,(await f.Repository.SearchAsync(q)).Items.Single().GrossDemandId);q.PageIndex=3;var beyond=await f.Repository.SearchAsync(q);Assert.AreEqual(2,beyond.TotalCount);Assert.HasCount(0,beyond.Items);q.PageIndex=1;q.PageSize=2;CollectionAssert.AreEqual(new[]{a,b},(await f.Repository.SearchAsync(q)).Items.Select(x=>x.GrossDemandId).ToArray());
    }

    /// <summary>缺Gross及Empty直接Get为null、空候选Count包为0，连续无事务读取不创建业务事务。</summary>
    [TestMethod]
    public async Task NoTransactionEmptyReads()
    {using var f=new Fixture(false);for(var i=0;i<2;i++){Assert.IsNull(await f.Repository.GetAsync(i==0?Guid.Empty:Guid.NewGuid()));var d=await f.Repository.SearchAsync(new(){CustomerId=Guid.NewGuid()});Assert.AreEqual(0,d.TotalCount);Assert.HasCount(0,d.Items);}Assert.IsNull(f.Session.Transaction);await f.Usable();}

    /// <summary>空/非空候选都对非正Take及负Offset产生技术异常；父事务由外层回滚，同连接恢复。</summary>
    [TestMethod]
    [DataRow(1,0,false)] [DataRow(1,0,true)] [DataRow(1,-1,false)] [DataRow(1,-1,true)] [DataRow(0,20,false)] [DataRow(0,20,true)] [DataRow(-1,20,false)] [DataRow(-1,20,true)]
    public async Task InvalidPaging(int index,int size,bool candidate)
    {using var f=new Fixture();var q=new SourceDemandDeliverySearchRequest{CustomerId=Guid.NewGuid(),PageIndex=index,PageSize=size};if(candidate){var(c,p,g)=await f.Seed();q.CustomerId=c.Uid;}var e=await Assert.ThrowsExactlyAsync<PostgresException>(()=>f.Repository.SearchAsync(q));Assert.AreEqual(DatabaseFailureKind.Unknown,f.Session.Errors.Classify(e));Assert.IsNotNull(f.Session.Transaction);f.Rollback();await f.Usable();}

    /// <summary>源int减一回绕及极大正页保留请求值，不能按服务规范裁剪直接仓储输入。</summary>
    [TestMethod]
    [DataRow(int.MinValue,20)] [DataRow(int.MaxValue,200)] [DataRow(1,int.MaxValue)]
    public async Task ExtremePaging(int index,int size)
    {using var f=new Fixture();var(c,p,g)=await f.Seed();var q=f.Request(c);q.PageIndex=index;q.PageSize=size;var d=await f.Repository.SearchAsync(q);Assert.AreEqual(1,d.TotalCount);Assert.HasCount(index==1?1:0,d.Items);Assert.AreEqual(index,d.PageIndex);Assert.AreEqual(size,d.PageSize);}

    /// <summary>原始三方法预取消沿令牌；取消后外层事务释放并验证连接。</summary>
    [TestMethod]
    [DataRow("schema")] [DataRow("search")] [DataRow("get")]
    public async Task PreCancellation(string method)
    {using var f=new Fixture();using var c=new CancellationTokenSource();c.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(async()=>{if(method=="schema")await f.Repository.IsSchemaReadyAsync(c.Token);else if(method=="get")await f.Repository.GetAsync(Guid.NewGuid(),c.Token);else await f.Repository.SearchAsync(new(),c.Token);});Assert.IsNotNull(f.Session.Transaction);f.Rollback();await f.Usable();}

    /// <summary>先执行生产搜索，再同SQL九参数EXPLAIN ANALYZE；候选producer只一次、两个消费者读取同关系。</summary>
    [TestMethod]
    public async Task ActualPlanMaterializesOnce()
    {using var f=new Fixture();var(c,p,g)=await f.Seed();await f.Insert(f.Gross(c,p));var q=f.Request(c);q.PageSize=1;Assert.AreEqual(2,(await f.Repository.SearchAsync(q)).TotalCount);var sent=f.Hook.Items.Single();Assert.HasCount(9,sent.Parameters);using var command=new NpgsqlCommand("EXPLAIN (ANALYZE,BUFFERS,FORMAT JSON) "+sent.Sql,f.Hook.Inner,(NpgsqlTransaction?)f.Session.Transaction);foreach(var parameter in sent.Parameters)command.Parameters.Add(parameter.Clone());using var json=JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);var nodes=Plans(json.RootElement[0].GetProperty("Plan")).ToArray();var producer=nodes.Single(x=>x.TryGetProperty("Subplan Name",out var n)&&n.GetString()=="CTE candidates");Assert.AreEqual(1L,producer.GetProperty("Actual Loops").GetInt64());Assert.IsTrue(nodes.Count(x=>x.TryGetProperty("CTE Name",out var n)&&n.GetString()=="candidates")>=2);Assert.HasCount(1,f.Hook.Items);await f.Usable();}

    /// <summary>测试事务临时表锁制造真正服务器等待，观察未授予锁后取消Search/Get；不添加生产锁或DDL。</summary>
    [TestMethod,Timeout(60000)]
    [DataRow(false,"search")] [DataRow(true,"search")] [DataRow(false,"get")] [DataRow(true,"get")]
    public async Task RealServerWaitCancellation(bool parent,string method)
    {
        using var blocker=new Fixture();using var reader=new Fixture(parent);using var observer=new Fixture(false);using var canceled=new CancellationTokenSource(TimeSpan.FromSeconds(15));await blocker.Session.Connection.ExecuteAsync(new CommandDefinition("LOCK TABLE public.gross_demand IN ACCESS EXCLUSIVE MODE",transaction:blocker.Session.Transaction));
        var pending=Read();async Task Read(){if(method=="get")await reader.Repository.GetAsync(Guid.NewGuid(),canceled.Token);else await reader.Repository.SearchAsync(new(){CustomerId=Guid.NewGuid()},canceled.Token);}
        try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));while(!await observer.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE l.pid=@Pid AND l.locktype='relation' AND l.relation='public.gross_demand'::regclass AND NOT l.granted AND a.wait_event_type='Lock')",new{Pid=reader.Hook.Inner.ProcessID},cancellationToken:deadline.Token)))await Task.Delay(25,deadline.Token);Assert.IsFalse(pending.IsCompleted);canceled.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>pending);Assert.AreEqual(parent,reader.Session.Transaction is not null);reader.Rollback();await reader.Usable();}
        finally{canceled.Cancel();blocker.Rollback();try{await pending;}catch(OperationCanceledException){}reader.Rollback();}
    }

    /// <summary>递归真实计划节点，不伪造计划或用两行假结果代替候选观察证明。</summary>
    private static IEnumerable<JsonElement> Plans(JsonElement node)
    {yield return node;if(node.TryGetProperty("Plans",out var children))foreach(var child in children.EnumerateArray())foreach(var nested in Plans(child))yield return nested;}

    /// <summary>严格故障库随机合法事实，默认父RC事务回滚；无事务模式只读，没有结构/序列/提交/清库操作。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly string Prefix="DQ"+Guid.NewGuid().ToString("N")+"X";public readonly DbSession Session;public readonly PostgreSqlSourceDeliveryQueryRepository Repository;public CaptureConnection Hook=>(CaptureConnection)Session.Connection;
        /// <summary>门禁通过才连接指定库，事务及连接只由本夹具拥有。</summary>
        public Fixture(bool parent=true){Session=new(new CaptureFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));try{if(parent)Session.Transaction=Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);var user=new User();Repository=new(new RepositoryBaseContext(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}catch{Session.Dispose();throw;}}
        /// <summary>随机客户补required编码及资料关联编号，不复用内置数据。</summary>
        public Customer Customer()=>new(){Uid=Guid.NewGuid(),CustomerCode=Prefix+Guid.NewGuid().ToString("N"),DocRelateNo=Prefix+Guid.NewGuid().ToString("N"),CustomerName="客户",IsActive=true};
        /// <summary>随机唯一SKU补采购供应类型，不借非法产品制造排序样本。</summary>
        public Product Product()=>new(){Uid=Guid.NewGuid(),Sku=Prefix+Guid.NewGuid().ToString("N"),SupplyType="采购",MaterialType="零件",IsActive=true,Unit="PCS"};
        /// <summary>合法正需求及产品模式、随机独立来源/行身份，历史快照单独明确。</summary>
        public GrossDemand Gross(Customer c,Product p)=>new(){Uid=Guid.NewGuid(),SourceType="SO",SourceUid=Guid.NewGuid(),SourceNo=Prefix+Guid.NewGuid().ToString("N"),SourceLineId=Guid.NewGuid(),SourceLineNumber=1,SourceVersion=2,CustomerId=c.Uid,ProductId=p.Uid,IsNonStandard=false,SkuSnapshot=Prefix+"快照",UnitSnapshot="PCS",DemandQty=10m};
        /// <summary>合法随机箱单头与创建命令，Posted原始关系仅证明查询数量定义。</summary>
        public PackingListHead Head(Customer c)=>new(){Uid=Guid.NewGuid(),PackingListNo=Prefix+Guid.NewGuid().ToString("N"),CustomerId=c.Uid,CreateCommandId=Guid.NewGuid(),InvoiceDate=new DateTime(2026,10,5),PostingStatus=PackingListPostingStatusEnum.Posted};
        /// <summary>合法正Qty/行号及快照，不生产负Qty或绕过唯一行号约束。</summary>
        public PackingListLine Line(PackingListHead h,GrossDemand g,decimal qty)=>new(){Uid=Guid.NewGuid(),PackingListHeadId=h.Uid,LineNumber=1,GrossDemandId=g.Uid,ProductId=g.ProductId,IsNonStandardSnapshot=g.IsNonStandard,SourceTypeSnapshot=g.SourceType,SourceDocumentNoSnapshot=g.SourceNo,SkuSnapshot=g.SkuSnapshot,UnitSnapshot=g.UnitSnapshot,Qty=qty};
        /// <summary>隔离随机客户作为Search范围，无默认全库候选假定。</summary>
        public SourceDemandDeliverySearchRequest Request(Customer c)=>new(){CustomerId=c.Uid,PageSize=20};
        /// <summary>原始合法三个实体共享父事务，所有后续历史也在此事务回滚。</summary>
        public async Task<(Customer C,Product P,GrossDemand G)> Seed(){var c=Customer();var p=Product();var g=Gross(c,p);await Insert(c);await Insert(p);await Insert(g);return(c,p,g);}
        /// <summary>INSERT使用PG元数据及既有显式参数，必须父事务，UID与物理Qty映射不猜测。</summary>
        public async Task Insert<T>(T row)where T:BaseEntity
        {Assert.IsNotNull(Session.Transaction);var map=EntityMetadataCache.GetMetadata(typeof(T));var columns=string.Join(',',map.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)));var values=string.Join(',',map.Columns.Select(c=>"@"+c.ParameterName));Assert.AreEqual(1,await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(row,map),Session.Transaction)));}
        /// <summary>只改本例随机Gross一个固定属性用于真实文本资格输入，不改结构/历史库数据。</summary>
        public async Task Update(GrossDemand row,string property)
        {Assert.IsNotNull(Session.Transaction);var map=EntityMetadataCache.GetMetadata(typeof(GrossDemand));var column=map.Columns.Single(x=>x.PropertyName==property);Assert.AreEqual(1,await Session.Connection.ExecuteAsync(new CommandDefinition($"UPDATE {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} SET {PostgreSqlNames.Quote(column.ColumnName)}=@{column.ParameterName} WHERE uid=@Uid",PostgreSqlParameters.Entity(row,map),Session.Transaction)));}
        /// <summary>外层夹具拥有回滚，生产仓储异常不接管父事务。</summary>
        public void Rollback(){if(Session.Transaction is {} tx){try{tx.Rollback();}finally{Session.Transaction=null;tx.Dispose();}}}
        /// <summary>相同连接SELECT1确认reader与错误事务已清理。</summary>
        public async Task Usable()=>Assert.AreEqual(1,await Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:Session.Transaction)));
        /// <summary>任意退出先回滚本人事实再释放连接，无DELETE/DDL/取号。</summary>
        public void Dispose(){try{Rollback();}finally{Session.Dispose();}}
    }
    /// <summary>只用于审计上下文的测试身份，不创建账户或记录凭据。</summary>
    private sealed class User:ICurrentUser{public Guid? UserId=>null;public string UserName=>"来源交付回滚夹具";public bool IsAuthenticated=>false;}
    /// <summary>保留真实Npgsql与分类，只装饰发送记录，不替换驱动结果。</summary>
    private sealed class CaptureFactory(string connection):IDbConnectionFactory
    {public IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;public IDbConnection CreateConnection()=>new CaptureConnection(new NpgsqlConnection(connection));}
    /// <summary>截取真实已发送来源搜索命令供计划验收；不加入阶段屏障/生产睡眠，不伪造reader。</summary>
    private sealed class CaptureConnection(NpgsqlConnection inner):DbConnection
    {
        public NpgsqlConnection Inner=>inner;public readonly List<(string Sql,NpgsqlParameter[] Parameters)> Items=[];
        [AllowNull]public override string ConnectionString{get=>inner.ConnectionString;set=>inner.ConnectionString=value;}public override string Database=>inner.Database;public override string DataSource=>inner.DataSource;public override string ServerVersion=>inner.ServerVersion;public override ConnectionState State=>inner.State;
        public override void Open()=>inner.Open();public override void Close()=>inner.Close();public override void ChangeDatabase(string name)=>inner.ChangeDatabase(name);protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>inner.BeginTransaction(level);protected override DbCommand CreateDbCommand()=>new CaptureCommand(this,inner.CreateCommand());protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
        /// <summary>完整转发连接/事务/九参数和取消令牌，记录的参数克隆只用于相同SQL计划命令。</summary>
        private sealed class CaptureCommand(CaptureConnection owner,NpgsqlCommand command):DbCommand
        {
            [AllowNull]public override string CommandText{get=>command.CommandText;set=>command.CommandText=value;}public override int CommandTimeout{get=>command.CommandTimeout;set=>command.CommandTimeout=value;}public override CommandType CommandType{get=>command.CommandType;set=>command.CommandType=value;}public override bool DesignTimeVisible{get=>command.DesignTimeVisible;set=>command.DesignTimeVisible=value;}public override UpdateRowSource UpdatedRowSource{get=>command.UpdatedRowSource;set=>command.UpdatedRowSource=value;}
            protected override DbConnection? DbConnection{get=>owner;set{}}protected override DbTransaction? DbTransaction{get=>command.Transaction;set=>command.Transaction=(NpgsqlTransaction?)value;}protected override DbParameterCollection DbParameterCollection=>command.Parameters;protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
            public override void Cancel()=>command.Cancel();public override void Prepare()=>command.Prepare();public override int ExecuteNonQuery()=>command.ExecuteNonQuery();public override object? ExecuteScalar()=>command.ExecuteScalar();public override Task<int> ExecuteNonQueryAsync(CancellationToken token)=>command.ExecuteNonQueryAsync(token);public override Task<object?> ExecuteScalarAsync(CancellationToken token)=>command.ExecuteScalarAsync(token);
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>command.ExecuteReader(behavior);
            /// <summary>记录本次真实来源搜索SQL与独立参数，返回原驱动reader；token也直接传入原Npgsql。</summary>
            protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token)
            {if(CommandText.Contains("candidates AS MATERIALIZED",StringComparison.Ordinal))owner.Items.Add((CommandText,command.Parameters.Select(x=>x.Clone()).ToArray()));return await command.ExecuteReaderAsync(behavior,token);}
            protected override void Dispose(bool disposing){if(disposing)command.Dispose();base.Dispose(disposing);}
        }
    }
}
