using System.Data;
using System.Data.SqlTypes;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>固定failure库的入库实际查询入口；随机合法数据、单RC事务finally回滚，只编写编译供主控运行。</summary>
[DoNotParallelize]
public sealed partial class PostgreSqlInboundQueryMigrationTests
{
    /// <summary>所有状态和建立方式保留普通读取，无行头统计零，删头排除；源日期/SqlGuid顺序及超末页总数准确。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration")]
    public Task RealPg_SearchPagingAndStats() => Rollback(async f =>
    {
        await f.Seed();var ids=new List<Guid>();var uidRoot=Guid.NewGuid().ToString("N");
        // 随机共同字节避免固定身份冲突，首字节与尾字节对序相反以区分PG uuid和源SqlGuid。
        for(var i=0;i<4;i++){var h=f.Head("page"+i);h.Uid=Guid.Parse((i%2==0?"ff":"00")+uidRoot[2..31]+(i+1));h.CreationMode=(InboundCreationModeEnum)(i%2);h.PostingStatus=(InboundPostingStatusEnum)(i%2);h.LifecycleStatus=(InventoryDocumentLifecycleStatusEnum)(i%2==0?0:9);await f.Add(h);ids.Add(h.Uid);if(i>0){await f.Add(f.Line(h,1,1.1234m));var deleted=f.Line(h,2,80);deleted.IsDeleted=true;await f.Add(deleted);}}
        var gone=f.Head("deleted");gone.IsDeleted=true;await f.Add(gone);
        var request=f.Search();request.PageSize=2;var first=await f.Repository.SearchAsync(request,default);Assert.AreEqual(4,first.TotalCount);
        CollectionAssert.AreEqual(ids.OrderByDescending(x=>new SqlGuid(x)).Take(2).ToArray(),first.Items.Select(x=>x.Uid).ToArray());
        var all=await f.Repository.SearchAsync(f.Search(),default);Assert.AreEqual(0,all.Items.Single(x=>x.Uid==ids[0]).LineCount);Assert.AreEqual(0m,all.Items.Single(x=>x.Uid==ids[0]).TotalQuantity);
        foreach(var row in all.Items.Where(x=>x.Uid!=ids[0])){Assert.AreEqual(1,row.LineCount);Assert.AreEqual(1.1234m,row.TotalQuantity);}
        request.PageIndex=99;var empty=await f.Repository.SearchAsync(request,default);Assert.AreEqual(4,empty.TotalCount);Assert.HasCount(0,empty.Items);
    });

    /// <summary>三个字面字段分别匹配，反向状态/类型可空筛选；不按当前产品名称或备注命中。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration"),DataRow(0),DataRow(1),DataRow(2)]
    public Task RealPg_SearchLiteralFields(int field) => Rollback(async f =>
    {
        await f.Seed();var h=f.Head("literal");var literal=f.Prefix+"~%_[";var line=f.Line(h,1,2);
        if(field==0)h.DocumentNo=literal;else if(field==1)h.WarehouseNameSnapshot=literal;else line.SkuSnapshot=literal;
        h.Remark=f.Prefix+"remark-only";await f.Add(h);await f.Add(line);
        var request=f.Search();request.Keyword="  "+literal.ToLowerInvariant()+"  ";Assert.AreEqual(1,(await f.Repository.SearchAsync(request,default)).TotalCount);
        request.Keyword=h.Remark;Assert.AreEqual(0,(await f.Repository.SearchAsync(request,default)).TotalCount);
        request.Keyword=literal;request.PostingStatus=InboundPostingStatusEnum.Reversed;Assert.AreEqual(0,(await f.Repository.SearchAsync(request,default)).TotalCount);
        request.PostingStatus=null;request.DocumentType=(InboundDocumentTypeEnum)999;Assert.AreEqual(0,(await f.Repository.SearchAsync(request,default)).TotalCount);
    });

    /// <summary>可信正式收货按净额与草稿提示读取；已删采购目标的正式历史必须阻断，不能算成零。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration"),DataRow(false),DataRow(true)]
    public Task RealPg_DetailCommercialHistory(bool deletedMasters) => Rollback(async f =>
    {
        f.Purchase.ConfirmationStatus=SwiftExport.Contracts.SysEnums.ConfirmationStatusEnum.已确认;
        await f.Seed(deletedMasters);var current=f.Head("current");current.Remark=null;current.ReceiptDate=new(2026,10,5,13,14,15);
        await f.Add(current);await f.Add(f.Line(current,2,3));await f.Add(f.Line(current,1,2));
        var posted=f.Head("posted");posted.PostingStatus=InboundPostingStatusEnum.Posted;posted.PostedDate=f.Today;posted.LifecycleStatus=(InventoryDocumentLifecycleStatusEnum)9;await f.Add(posted);await f.Add(f.Line(posted,1,30));
        var other=f.Head("draft");other.CreationMode=InboundCreationModeEnum.Automatic;other.TriggerPackingListId=Guid.NewGuid();await f.Add(other);await f.Add(f.Line(other,1,12));await f.Add(f.Line(other,2,4));
        var inactive=f.Head("inactive");inactive.LifecycleStatus=(InventoryDocumentLifecycleStatusEnum)9;await f.Add(inactive);await f.Add(f.Line(inactive,1,40));
        var reversed=f.Head("reversed");reversed.PostingStatus=InboundPostingStatusEnum.Reversed;reversed.PostedDate=f.Today;reversed.ReversedDate=f.Today;reversed.ReverseReason="已证明反向";await f.Add(reversed);var reverseLine=f.Line(reversed,1,7);await f.Add(reverseLine);await f.Pair(reversed,reverseLine);
        if(deletedMasters){await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetDetailAsync(current.Uid,default));return;}
        var result=await f.Repository.GetDetailAsync(current.Uid,default);Assert.IsNotNull(result);Assert.AreEqual(current.ReceiptDate,result.Header.ReceiptDate);Assert.AreEqual("",result.Header.Remark);
        CollectionAssert.AreEqual(new[]{1,2},result.Lines.Select(x=>x.LineNumber).ToArray());
        foreach(var line in result.Lines){Assert.AreEqual(100m,line.OriginalQty);Assert.AreEqual(70m,line.RemainingReceivableQty);Assert.AreEqual(1,line.OtherDraftCount);Assert.AreEqual(16m,line.OtherDraftQuantity);Assert.AreEqual(7,line.SourceVersion);Assert.AreEqual(f.Purchase.OrderNo,line.SourceNo);Assert.AreEqual("采购BO",line.BoNo);Assert.AreEqual("采购关联型号",line.LinkSku);Assert.AreEqual("当前图",line.ImagePath);Assert.AreEqual("历史SKU",line.SkuSnapshot);Assert.AreEqual("历史描述",line.DescriptionSnapshot);Assert.AreEqual("PCS",line.UnitSnapshot);}
        Assert.AreSame(f.Transaction,f.Session.Transaction);
    });

    /// <summary>缺采购/头/产品或非采购类型仍保留行和原NULL默认，不强加当前主数据资格。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration"),DataRow("missingLine"),DataRow("missingHead"),DataRow("missingProduct"),DataRow("nonPurchase")]
    public Task RealPg_DetailMissingRelations(string mode) => Rollback(async f =>
    {
        await f.Seed();var h=f.Head("missing");var line=f.Line(h,1,2);
        if(mode=="missingLine")line.SourceLineId=Guid.NewGuid();
        if(mode=="nonPurchase")h.DocumentType=(InboundDocumentTypeEnum)20;
        if(mode=="missingProduct")line.ProductId=Guid.NewGuid();
        if(mode=="missingHead")await f.Session.Connection.ExecuteAsync("UPDATE public.purchase_order_line SET purchase_order_id=@Id WHERE uid=@Uid",new{Id=Guid.NewGuid(),Uid=f.PurchaseLine.Uid},f.Transaction);
        await f.Add(h);await f.Add(line);var result=await f.Repository.GetDetailAsync(h.Uid,default);Assert.IsNotNull(result);var actual=result.Lines.Single();
        if(mode is "missingLine" or "nonPurchase"){Assert.AreEqual(0,actual.SourceVersion);Assert.AreEqual(0m,actual.OriginalQty);Assert.AreEqual(0m,actual.RemainingReceivableQty);Assert.AreEqual("",actual.SourceNo);Assert.AreEqual(0,actual.OtherDraftCount);}
        if(mode=="missingHead"){Assert.AreEqual("",actual.SourceNo);Assert.AreEqual(100m,actual.OriginalQty);Assert.AreEqual("采购关联型号",actual.LinkSku);}
        if(mode=="missingProduct"){Assert.AreEqual("",actual.ImagePath);Assert.AreEqual("历史SKU",actual.SkuSnapshot);}
    });

    /// <summary>当前单正式收货也计净额，超收剩余归零；不因生命周期/建立方式删掉正式事实。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration"),DataRow(25,75),DataRow(120,0)]
    public Task RealPg_DetailOwnReceiptAndClamp(int received,int remaining) => Rollback(async f =>
    {
        f.Purchase.ConfirmationStatus=SwiftExport.Contracts.SysEnums.ConfirmationStatusEnum.已确认;
        await f.Seed();var h=f.Head("ownposted");h.PostingStatus=InboundPostingStatusEnum.Posted;h.PostedDate=f.Today;await f.Add(h);await f.Add(f.Line(h,1,received));
        var result=await f.Repository.GetDetailAsync(h.Uid,default);Assert.IsNotNull(result);Assert.AreEqual((decimal)remaining,result.Lines.Single().RemainingReceivableQty);
    });

    /// <summary>同来源正式头或行软删属于未知证据，不能忽略后把剩余数量作为可信结果。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration")]
    public Task RealPg_ReceiptAndDraftExcludedFacts() => Rollback(async f =>
    {
        await f.Seed();var current=f.Head("exclusion-current");await f.Add(current);await f.Add(f.Line(current,1,50));
        var gone=f.Head("deleted-posted");gone.PostingStatus=InboundPostingStatusEnum.Posted;gone.IsDeleted=true;await f.Add(gone);await f.Add(f.Line(gone,1,30));
        var posted=f.Head("part-posted");posted.PostingStatus=InboundPostingStatusEnum.Posted;await f.Add(posted);await f.Add(f.Line(posted,1,10));var removed=f.Line(posted,2,20);removed.IsDeleted=true;await f.Add(removed);
        var foreign=f.Head("foreign-posted");foreign.PostingStatus=InboundPostingStatusEnum.Posted;await f.Add(foreign);var foreignLine=f.Line(foreign,1,70);foreignLine.SourceLineId=Guid.NewGuid();await f.Add(foreignLine);
        var closed=f.Head("closed-draft");closed.LifecycleStatus=(InventoryDocumentLifecycleStatusEnum)8;await f.Add(closed);await f.Add(f.Line(closed,1,90));
        var draft=f.Head("open-draft");await f.Add(draft);await f.Add(f.Line(draft,1,4));var deleted=f.Line(draft,2,80);deleted.IsDeleted=true;await f.Add(deleted);
        await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetDetailAsync(current.Uid,default));
    });

    /// <summary>缺头、已删头和没有有效行都按完整头行入口读取，不擅自拒绝合法空详情。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration"),DataRow(false),DataRow(true)]
    public Task RealPg_DetailMissingAndDeleted(bool deleted) => Rollback(async f =>
    {
        await f.Seed();Assert.IsNull(await f.Repository.GetDetailAsync(Guid.NewGuid(),default));var h=f.Head("empty");h.IsDeleted=deleted;await f.Add(h);
        var result=await f.Repository.GetDetailAsync(h.Uid,default);if(deleted)Assert.IsNull(result);else{Assert.IsNotNull(result);Assert.HasCount(0,result.Lines);}
    });

    /// <summary>同头可疑历史只阻断关联采购行，无关空头和缺头照常读取；业务信号不终止父事务。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration"),DataRow(false),DataRow(true)]
    public Task RealPg_ScopedGuardVersusSearch(bool missingTarget) => Rollback(async f =>
    {
        await f.Seed();var bad=f.Head("bad-history");bad.PostingStatus=InboundPostingStatusEnum.Posted;await f.Add(bad);var suspicious=f.Line(bad,1,1);suspicious.OriginalLineId=Guid.NewGuid();await f.Add(suspicious);
        var target=f.Head("target");await f.Add(target);Assert.AreEqual(2,(await f.Repository.SearchAsync(f.Search(),default)).TotalCount);
        var result=await f.Repository.GetDetailAsync(missingTarget?Guid.NewGuid():target.Uid,default);
        if(missingTarget)Assert.IsNull(result);else Assert.IsNotNull(result);
        await f.Add(f.Line(target,1,1));
        await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetDetailAsync(target.Uid,default));
        Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:f.Transaction));
    });

    /// <summary>零/负Take与负偏移均保留技术失败，用保存点恢复父事务不重试查询或改成合法页。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration"),DataRow(1,0),DataRow(1,-1),DataRow(0,20)]
    public Task RealPg_InvalidPagingFailsTechnically(int page,int size) => Rollback(async f =>
    {
        await f.Seed();await f.Session.Connection.ExecuteAsync("SAVEPOINT inbound_invalid_page",transaction:f.Transaction);
        var request=f.Search();request.PageIndex=page;request.PageSize=size;
        var error=await Assert.ThrowsAsync<PostgresException>(()=>f.Repository.SearchAsync(request,default));Assert.AreEqual(page==0?"2201X":"2201W",error.SqlState);
        await f.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT inbound_invalid_page",transaction:f.Transaction);Assert.AreEqual(0,(await f.Repository.SearchAsync(f.Search(),default)).TotalCount);
    });

    /// <summary>已有父事务内自写可见、令牌取消后可继续查询；不是独立物理连接并发验收。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration")]
    public Task RealPg_WallTimeAndCancellation() => Rollback(async f =>
    {
        await f.Seed();var earlier=f.Head("earlier");earlier.ReceiptDate=new(2026,10,5,9,0,0);var later=f.Head("later");later.ReceiptDate=new(2026,10,5,18,0,0);await f.Add(earlier);await f.Add(later);
        var page=await f.Repository.SearchAsync(f.Search(),default);Assert.AreEqual(later.Uid,page.Items[0].Uid);Assert.AreEqual(later.ReceiptDate,page.Items[0].ReceiptDate);
        var token=new CancellationToken(true);await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchAsync(f.Search(),token));await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetDetailAsync(later.Uid,token));Assert.IsNotNull(await f.Repository.GetDetailAsync(later.Uid,default));
    });

    /// <summary>无需外层事务的普通查询和预取消，不开启写事务，不读取应用Default连接。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundQueryIntegration")]
    public async Task RealPg_NullParentTransaction()
    {
        using var session=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));var f=new InboundFixture(session,null);
        Assert.AreEqual(0,(await f.Repository.SearchAsync(f.Search(),default)).TotalCount);Assert.IsNull(await f.Repository.GetDetailAsync(Guid.NewGuid(),default));Assert.IsNull(session.Transaction);
    }

    /// <summary>使用固定failure门禁和随机身份，始终finally回滚；不初始化、提交、清库或更改结构。</summary>
    private static async Task Rollback(Func<InboundFixture,Task> action)
    {
        using var session=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));using var transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);session.Transaction=transaction;
        try{await action(new(session,transaction));Assert.AreSame(transaction,session.Transaction);}finally{try{transaction.Rollback();}finally{session.Transaction=null;}}
    }

    /// <summary>独占合法入库及历史逻辑引用夹具；仅生成查询事实，不调用过账服务或全局流水序列。</summary>
    private sealed class InboundFixture
    {
        public DbSession Session{get;} public IDbTransaction? Transaction{get;} public PostgreSqlInboundQueryRepository Repository{get;}
        public string Prefix{get;}="PGINQ"+Guid.NewGuid().ToString("N");public DateTime Today{get;}=new(2026,10,5,12,34,56);
        public Product Product{get;}public PurchaseOrderHead Purchase{get;}public PurchaseOrderLine PurchaseLine{get;}public Guid Warehouse{get;}=Guid.NewGuid();
        private long sequence;
        /// <summary>准备随机必填身份及合法SupplyType，采购元数据Quantity映射为PG quantity。</summary>
        public InboundFixture(DbSession session,IDbTransaction? transaction)
        {
            Session=session;Transaction=transaction;var user=new WorkspaceUser();Repository=new(new RepositoryBaseContext(session,NullLoggerFactory.Instance,new AuditStampService(user),user));
            Product=new(){Sku=Prefix+"product",SupplyType="采购",MainImage="当前图",DescriptionZh="当前描述",Unit="KG",IsActive=false};
            Purchase=new(){OrderNo=Prefix+"PO",BoNo="采购BO",SupplierId=Guid.NewGuid(),ExecutionRequestId=Guid.NewGuid(),OrderDate=Today};
            PurchaseLine=new(){PurchaseOrderId=Purchase.Uid,LineNumber=1,ProductId=Product.Uid,Quantity=100,Version=7,Sku=Prefix+"purchase",UnitSnapshot="PCS",LinkSku="采购关联型号"};
        }
        /// <summary>只插本例主数据；删史状态在插入前设置，不修改旧行或约束。</summary>
        public async Task Seed(bool deleted=false){Product.IsDeleted=deleted;Purchase.IsDeleted=deleted;PurchaseLine.IsDeleted=deleted;await Add(Product);await Add(Purchase);await Add(PurchaseLine);}
        /// <summary>随机编号范围限定查询，不依赖现有库内容。</summary>
        public InboundManagerSearchRequest Search()=>new(){Keyword=Prefix,PageSize=200};
        /// <summary>普通合法未过账单头，时间与全部历史快照明确。</summary>
        public InboundDocument Head(string suffix)=>new(){DocumentNo=Prefix+suffix,DocumentType=InboundDocumentTypeEnum.PurchaseReceipt,WarehouseSiteId=Warehouse,WarehouseCodeSnapshot="历史仓码",WarehouseNameSnapshot="历史仓名",ReceiptDate=Today,CommandId=Guid.NewGuid(),CreatedDate=Today,CreatedBy="查询夹具",UpdatedDate=Today,UpdatedBy="查询夹具"};
        /// <summary>允许同来源多行但同头行号唯一，保留独立历史产品快照。</summary>
        public InboundDocumentLine Line(InboundDocument head,int number,decimal quantity)=>new(){InboundDocumentId=head.Uid,LineNumber=number,ProductId=Product.Uid,SourceLineId=PurchaseLine.Uid,Quantity=quantity,SkuSnapshot="历史SKU",DescriptionSnapshot="历史描述",UnitSnapshot="PCS",CreatedDate=Today,CreatedBy="查询夹具",UpdatedDate=Today,UpdatedBy="查询夹具"};
        /// <summary>正反流水完整匹配历史Guard；直接使用当前最大序号加本例偏移，不调用nextval、不修改不可变流水。</summary>
        public async Task Pair(InboundDocument head,InboundDocumentLine line)
        {
            if(sequence==0)sequence=await Session.Connection.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(sequence),0) FROM public.inventory_ledger_entry",transaction:Transaction);
            var original=new InventoryLedgerEntry{Sequence=++sequence,WarehouseSiteId=head.WarehouseSiteId,ProductId=line.ProductId,TransactionType=InventoryTransactionTypeEnum.PurchaseReceiptInbound,QuantityDelta=line.Quantity,BalanceAfter=line.Quantity,SourceDocumentType="InboundDocument",SourceDocumentId=head.Uid,SourceLineId=line.Uid,CommandId=Guid.NewGuid(),CommandItemId=line.Uid,UnitSnapshot=line.UnitSnapshot,OccurredDate=Today};await Add(original);
            var reverse=new InventoryLedgerEntry{Sequence=++sequence,WarehouseSiteId=head.WarehouseSiteId,ProductId=line.ProductId,TransactionType=InventoryTransactionTypeEnum.Reversal,QuantityDelta=-line.Quantity,BalanceAfter=0,SourceDocumentType="InboundDocument",SourceDocumentId=head.Uid,SourceLineId=line.Uid,CommandId=Guid.NewGuid(),CommandItemId=Guid.NewGuid(),UnitSnapshot=line.UnitSnapshot,ReversalOfLedgerId=original.Uid,OccurredDate=Today};await Add(reverse);
        }
        /// <summary>按现有只读元数据写合法随机实体，完整保留UID/Version/NULL，不关闭索引CHECK或触发器。</summary>
        public async Task Add<T>(T entity) where T:BaseEntity
        {
            var map=EntityMetadataCache.GetMetadata(typeof(T));var columns=string.Join(',',map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));var values=string.Join(',',map.Columns.Select(x=>"@"+x.ParameterName));
            Assert.AreEqual(1,await Session.Connection.ExecuteAsync($"INSERT INTO public.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(entity,map),Transaction));
        }
    }
}
