using System.Collections;
using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅编写编译的四查询真实PG用例，门禁/随机RC回滚沿原环境；不实现SearchItems或接运行DI。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlOutboundQueryMigrationIntegrationTests
{
    /// <summary>精确nullable类型/状态，终态与PL自动单保留，未删行COUNT/SUM不倍增且无行零。</summary>
    [TestMethod]
    [DataRow(null,null,3)] [DataRow(22,null,1)] [DataRow(null,8,1)] [DataRow(20,8,0)]
    public async Task SearchEnumsAndCommercialFacts(int? type,int? status,int expected)
    {
        using var f=new Fixture();var heads=new List<OutboundDocument>();
        for(int i=0;i<3;i++){var h=f.Head();h.DocumentType=(OutboundDocumentTypeEnum)(20+i);h.PostingStatus=(OutboundPostingStatusEnum)new[]{0,1,8}[i];h.LifecycleStatus=(InventoryDocumentLifecycleStatusEnum)9;h.TriggerPackingListId=Guid.NewGuid();await f.InsertAsync(h);heads.Add(h);}
        var a=f.Line(heads[0]);a.Quantity=3.25m;await f.InsertAsync(a);var b=f.Line(heads[0],2);b.Quantity=4.5m;await f.InsertAsync(b);var deleted=f.Line(heads[0],3);deleted.IsDeleted=true;await f.InsertAsync(deleted);
        var dead=f.Head();dead.IsDeleted=true;await f.InsertAsync(dead);
        var d=await f.Repository.SearchAsync(new(){Keyword=f.Prefix,DocumentType=type.HasValue?(OutboundDocumentTypeEnum)type:null,PostingStatus=status.HasValue?(OutboundPostingStatusEnum)status:null},default);
        Assert.AreEqual(expected,d.TotalCount);Assert.HasCount(expected,d.Items);
        if(!type.HasValue&&!status.HasValue){var item=d.Items.Single(x=>x.Uid==heads[0].Uid);Assert.AreEqual(2,item.LineCount);Assert.AreEqual(7.75m,item.TotalQuantity);Assert.AreEqual("",item.Remark);Assert.AreEqual("",item.IssueReason);Assert.AreEqual(0m,d.Items.Single(x=>x.Uid==heads[1].Uid).TotalQuantity);}
        await f.Usable();
    }

    /// <summary>关键词只搜头号、仓名和有效行快照SKU，当前产品匹配不能扩大条件；字面特殊符号与ASCII大小写保持。</summary>
    [TestMethod]
    [DataRow("document",true)] [DataRow("warehouse",true)] [DataRow("line",true)] [DataRow("deleted-line",false)] [DataRow("product",false)] [DataRow("literal",true)] [DataRow("ascii",true)]
    public async Task SearchKeywordBoundary(string mode,bool matches)
    {
        using var f=new Fixture();var h=f.Head();var keyword=f.Prefix+"Special";var p=Product(f);var l=f.Line(h);l.ProductId=p.Uid;
        switch(mode){case "document":h.DocumentNo=keyword;break;case "warehouse":h.WarehouseNameSnapshot=keyword;break;case "line":l.SkuSnapshot=keyword;break;case "deleted-line":l.SkuSnapshot=keyword;l.IsDeleted=true;break;case "product":p.Sku=keyword;break;case "literal":keyword="%_[~";h.DocumentNo=f.Prefix+keyword;break;case "ascii":h.DocumentNo=f.Prefix+"AbC";keyword=f.Prefix+"abc";break;}
        await f.InsertAsync(p);await f.InsertAsync(h);await f.InsertAsync(l);var d=await f.Repository.SearchAsync(new(){Keyword=keyword},default);Assert.AreEqual(matches?1:0,d.TotalCount);Assert.HasCount(matches?1:0,d.Items);
    }

    /// <summary>同日头尾键保留SqlGuid降序，越界空页及响应页值原样，无C#重新排序。</summary>
    [TestMethod]
    public async Task SearchSortingAndPageValues()
    {
        using var f=new Fixture();var (a,b)=OppositeIds();var heads=new[]{f.Head(),f.Head(),f.Head()};heads[0].Uid=a;heads[1].Uid=b;heads[2].ShipmentDate=new DateTime(2026,10,4);
        foreach(var h in heads)await f.InsertAsync(h);
        var d=await f.Repository.SearchAsync(new(){Keyword=f.Prefix,PageIndex=1,PageSize=2},default);Assert.AreEqual(3,d.TotalCount);Assert.AreEqual(1,d.PageIndex);Assert.AreEqual(2,d.PageSize);
        CollectionAssert.AreEqual(heads.OrderByDescending(x=>x.ShipmentDate).ThenByDescending(x=>new SqlGuid(x.Uid)).Take(2).Select(x=>x.Uid).ToArray(),d.Items.Select(x=>x.Uid).ToArray());
        var empty=await f.Repository.SearchAsync(new(){Keyword=f.Prefix,PageIndex=4,PageSize=2},default);Assert.AreEqual(3,empty.TotalCount);Assert.HasCount(0,empty.Items);Assert.AreEqual(4,empty.PageIndex);
    }

    /// <summary>非法FETCH<=0及负OFFSET通过生产SQL真实技术拒绝，SQLSTATE不与SqlException号码强行等同。</summary>
    [TestMethod]
    [DataRow(1,0)] [DataRow(1,-1)] [DataRow(0,20)] [DataRow(-1,20)] [DataRow(1,int.MinValue)]
    public async Task RawPageTechnicalFailure(int index,int size)
    {
        using var f=new Fixture();var error=await Assert.ThrowsAsync<PostgresException>(()=>f.Repository.SearchAsync(new(){Keyword=f.Prefix,PageIndex=index,PageSize=size},default));
        Assert.AreEqual(DatabaseFailureKind.Unknown,f.Session.Errors.Classify(error));Assert.IsNotNull(f.Session.Transaction);
        // 不把PG原错误转换成业务验证；外层事务可能aborted，只有调用者负责回滚。
        f.Rollback();await f.Usable();
    }

    /// <summary>超大页只为空页，int.MinValue减1原unchecked提升顺序不被修复；仓储不裁剪超服务页大小。</summary>
    [TestMethod]
    [DataRow(int.MaxValue,200)] [DataRow(int.MinValue,20)] [DataRow(1,201)]
    public async Task RawPageRetainsOriginalValues(int index,int size)
    {
        using var f=new Fixture();var d=await f.Repository.SearchAsync(new(){Keyword=f.Prefix,PageIndex=index,PageSize=size},default);Assert.AreEqual(0,d.TotalCount);Assert.HasCount(0,d.Items);Assert.AreEqual(index,d.PageIndex);Assert.AreEqual(size,d.PageSize);
    }

    /// <summary>真实缺头/删头仍推进并读取第二结果后释放；删头有效行保留在第二阶段，连接及父事务可用。</summary>
    [TestMethod]
    [DataRow("missing")] [DataRow("deleted-head")] [DataRow("empty-lines")]
    public async Task DetailMissingHeaderBuffersLines(string mode)
    {
        using var f=new Fixture();var h=f.Head();h.IsDeleted=mode=="deleted-head";if(mode!="missing")await f.InsertAsync(h);if(mode=="deleted-head")await f.InsertAsync(f.Line(h));
        f.Hook.Commands.Clear();var d=await f.Repository.GetDetailAsync(h.Uid,default);
        if(mode=="empty-lines"){Assert.IsNotNull(d);Assert.HasCount(0,d.Lines);}else Assert.IsNull(d);
        Assert.AreEqual(2,f.Hook.NextResults);Assert.AreEqual(1,f.Hook.Disposals);Assert.IsTrue(f.Hook.ReaderClosed);Assert.IsTrue(f.Hook.ReadCounts[1]>0);if(mode=="deleted-head")Assert.IsTrue(f.Hook.ReadCounts[1]>1);await f.Usable();
    }

    /// <summary>头24/行18字段日期审计与NULL默认齐全，旧快照不由当前产品覆盖，行IsDeleted未投影默认false。</summary>
    [TestMethod]
    public async Task DetailRoundTripProjection()
    {
        using var f=new Fixture();var h=f.Head();h.Version=3;h.WarehouseCodeSnapshot="旧仓码";h.WarehouseNameSnapshot="旧仓名";h.Remark=null;h.IssueReason=null;h.PostedDate=new DateTime(2026,10,5,13,14,15);h.PostedBy="旧过账人";h.ReversedDate=new DateTime(2026,10,6,1,2,3);h.ReversedBy="旧反向人";h.ReverseReason="旧原因";h.TriggerPackingListId=Guid.NewGuid();h.CreatedDate=new DateTime(2026,10,1);h.CreatedBy="创建人";h.UpdatedDate=new DateTime(2026,10,2);h.UpdatedBy="更新人";await f.InsertAsync(h);
        var p=Product(f);p.LinkSku="当前关联";p.MainImage=null;await f.InsertAsync(p);var l=f.Line(h);l.Version=2;l.ProductId=p.Uid;l.SkuSnapshot="旧SKU";l.DescriptionSnapshot="旧描述";l.UnitSnapshot=null;l.OriginalLineId=Guid.NewGuid();l.CreatedDate=new DateTime(2026,10,1);l.CreatedBy="行创建";l.UpdatedDate=new DateTime(2026,10,2);l.UpdatedBy="行更新";await f.InsertAsync(l);
        var d=(await f.Repository.GetDetailAsync(h.Uid,default))!;var row=d.Lines.Single();
        Assert.AreEqual(h.Uid,d.Header.Uid);Assert.AreEqual(3,d.Header.Version);Assert.AreEqual(h.CommandId,d.Header.CommandId);Assert.AreEqual("旧仓码",d.Header.WarehouseCodeSnapshot);Assert.AreEqual("旧仓名",d.Header.WarehouseNameSnapshot);Assert.AreEqual(h.ShipmentDate,d.Header.ShipmentDate);Assert.AreEqual("",d.Header.Remark);Assert.AreEqual("",d.Header.IssueReason);
        Assert.AreEqual(h.PostedDate,d.Header.PostedDate);Assert.AreEqual(h.PostedBy,d.Header.PostedBy);Assert.AreEqual(h.ReversedDate,d.Header.ReversedDate);Assert.AreEqual(h.ReversedBy,d.Header.ReversedBy);Assert.AreEqual(h.ReverseReason,d.Header.ReverseReason);Assert.AreEqual(h.TriggerPackingListId,d.Header.TriggerPackingListId);Assert.AreEqual(h.CreatedDate,d.Header.CreatedDate);Assert.AreEqual(h.CreatedBy,d.Header.CreatedBy);Assert.AreEqual(h.UpdatedDate,d.Header.UpdatedDate);Assert.AreEqual(h.UpdatedBy,d.Header.UpdatedBy);
        Assert.AreEqual(l.Uid,row.Uid);Assert.AreEqual(2,row.Version);Assert.AreEqual(h.Uid,row.OutboundDocumentId);Assert.AreEqual(l.CommandItemId,row.CommandItemId);Assert.AreEqual(l.OriginalLineId,row.OriginalLineId);Assert.AreEqual(l.ProductId,row.ProductId);Assert.AreEqual(l.LineNumber,row.LineNumber);Assert.AreEqual(l.Quantity,row.Quantity);Assert.AreEqual("旧SKU",row.SkuSnapshot);Assert.AreEqual("旧描述",row.DescriptionSnapshot);Assert.IsNull(row.UnitSnapshot);Assert.AreEqual("当前关联",row.LinkSku);Assert.IsNull(row.ImagePath);Assert.AreEqual(0m,row.OnHandQty);Assert.IsFalse(row.IsDeleted);
        Assert.AreEqual(l.CreatedDate,row.CreatedDate);Assert.AreEqual(l.CreatedBy,row.CreatedBy);Assert.AreEqual(l.UpdatedDate,row.UpdatedDate);Assert.AreEqual(l.UpdatedBy,row.UpdatedBy);
    }

    /// <summary>详情当前产品不排软删/停用，缺产品NULL；缺/删余额0、负余额原值，不要求实际仓库记录。</summary>
    [TestMethod]
    [DataRow("deleted-product",-2)] [DataRow("inactive-product",-2)] [DataRow("missing-product",-2)] [DataRow("missing-balance",0)] [DataRow("deleted-balance",0)]
    public async Task DetailCurrentProductAndBalance(string mode,int balance)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var p=Product(f);p.IsDeleted=mode=="deleted-product";p.IsActive=mode!="inactive-product";if(mode!="missing-product")await f.InsertAsync(p);
        var l=f.Line(h);l.ProductId=p.Uid;await f.InsertAsync(l);if(mode!="missing-balance")await f.InsertAsync(new InventoryBalance{Uid=Guid.NewGuid(),WarehouseSiteId=h.WarehouseSiteId,ProductId=p.Uid,OnHandQty=-2m,IsDeleted=mode=="deleted-balance"});
        var row=(await f.Repository.GetDetailAsync(h.Uid,default))!.Lines.Single();Assert.AreEqual((decimal)balance,row.OnHandQty);Assert.AreEqual(mode=="missing-product"?null:"当前图像",row.ImagePath);Assert.AreEqual(l.SkuSnapshot,row.SkuSnapshot);
    }

    /// <summary>未删行按LineNumber读取，已删行排除；物理唯一行号禁止构造非法并列UID夹具。</summary>
    [TestMethod]
    public async Task DetailLineOrderAndDeletedRows()
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var b=f.Line(h,2);var a=f.Line(h,1);var dead=f.Line(h,3);dead.IsDeleted=true;await f.InsertAsync(b);await f.InsertAsync(a);await f.InsertAsync(dead);
        CollectionAssert.AreEqual(new[]{a.Uid,b.Uid},(await f.Repository.GetDetailAsync(h.Uid,default))!.Lines.Select(x=>x.Uid).ToArray());
    }

    /// <summary>六投影只未删、停用产品仍读取，NULL/原单位及候选其他默认字段保留，重复ID不展开数组。</summary>
    [TestMethod]
    public async Task ProductsAndDefaults()
    {
        using var f=new Fixture();var p=Product(f);p.IsActive=false;p.Unit=" PCS ";p.MainImage=null;p.LinkSku=null;await f.InsertAsync(p);var deleted=Product(f);deleted.IsDeleted=true;await f.InsertAsync(deleted);var unknown=Guid.NewGuid();
        var rows=await f.Repository.GetProductsAsync(new[]{p.Uid,p.Uid,deleted.Uid,unknown},default);Assert.HasCount(1,rows);var row=rows.Single();Assert.AreEqual(p.Uid,row.ProductId);Assert.AreEqual(p.Sku,row.Sku);Assert.AreEqual(" PCS ",row.Unit);Assert.IsNull(row.ImagePath);Assert.IsNull(row.LinkSku);Assert.AreEqual(Guid.Empty,row.Uid);Assert.AreEqual(Guid.Empty,row.WarehouseSiteId);Assert.AreEqual(0m,row.OnHandQty);Assert.IsNull(row.DefaultWarehouseName);await f.Usable();
    }

    /// <summary>空产品ID即使预取消也成功且无SQL，null保留原技术异常。</summary>
    [TestMethod]
    public async Task ProductsEmptyWithCanceledToken()
    {
        using var f=new Fixture();f.Hook.Commands.Clear();using var c=new CancellationTokenSource();c.Cancel();Assert.HasCount(0,await f.Repository.GetProductsAsync([],c.Token));Assert.HasCount(0,f.Hook.Commands);
        await Assert.ThrowsAsync<NullReferenceException>(()=>f.Repository.GetProductsAsync(null!,default));Assert.HasCount(0,f.Hook.Commands);await f.Usable();
    }

    /// <summary>活跃身份检查不要求Unit、余额或仓库，只排删/停用/未知产品。</summary>
    [TestMethod]
    [DataRow("active",true)] [DataRow("inactive",false)] [DataRow("deleted",false)] [DataRow("missing",false)]
    public async Task ActiveIdentity(string mode,bool expected)
    {
        using var f=new Fixture();var p=Product(f);p.Unit=null;p.IsActive=mode!="inactive";p.IsDeleted=mode=="deleted";if(mode!="missing")await f.InsertAsync(p);
        Assert.AreEqual(expected,await f.Repository.IsProductActiveAsync(p.Uid,default));await f.Usable();
    }

    /// <summary>预取消和两结果推进边界保留真实驱动token及reader释放；不冒称服务器等待取消已验。</summary>
    [TestMethod]
    [DataRow("search",-1)] [DataRow("search",0)] [DataRow("search",1)] [DataRow("detail",-1)] [DataRow("detail",0)] [DataRow("detail",1)] [DataRow("products",-1)] [DataRow("active",-1)]
    public async Task Cancellation(string method,int stage)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);await f.InsertAsync(f.Line(h));using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else f.Hook.ResultAction=i=>{if(i==stage)c.Cancel();};
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>{switch(method){case "search":await f.Repository.SearchAsync(new(){Keyword=f.Prefix},c.Token);break;case "detail":await f.Repository.GetDetailAsync(h.Uid,c.Token);break;case "products":await f.Repository.GetProductsAsync([Guid.NewGuid()],c.Token);break;default:await f.Repository.IsProductActiveAsync(Guid.NewGuid(),c.Token);break;}});
        f.Hook.ResultAction=null;Assert.IsNotNull(f.Session.Transaction);f.Rollback();await f.Usable();
    }

    /// <summary>命令派发边界取消传递原token，未发送生产SQL且调用者事务仍可用；不是服务器等待取消证明。</summary>
    [TestMethod]
    [DataRow("search")] [DataRow("detail")] [DataRow("products")] [DataRow("active")]
    public async Task CancellationAtCommandStart(string method)
    {
        using var f=new Fixture();f.Hook.Commands.Clear();using var c=new CancellationTokenSource();
        f.Hook.Before=(sql,token)=>{c.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>{switch(method){case "search":await f.Repository.SearchAsync(new(){Keyword=f.Prefix},c.Token);break;case "detail":await f.Repository.GetDetailAsync(Guid.NewGuid(),c.Token);break;case "products":await f.Repository.GetProductsAsync([Guid.NewGuid()],c.Token);break;default:await f.Repository.IsProductActiveAsync(Guid.NewGuid(),c.Token);break;}});
        f.Hook.Before=null;Assert.HasCount(0,f.Hook.Commands);Assert.IsNotNull(f.Session.Transaction);await f.Usable();
    }

    /// <summary>无外层事务四普通方法可连续读取独立随机空身份，不新增事务或写持久事实。</summary>
    [TestMethod]
    public async Task NoTransactionContinuousQueries()
    {
        using var f=new Fixture(false);Assert.AreEqual(0,(await f.Repository.SearchAsync(new(){Keyword=f.Prefix},default)).TotalCount);Assert.IsNull(await f.Repository.GetDetailAsync(Guid.NewGuid(),default));Assert.HasCount(0,await f.Repository.GetProductsAsync([Guid.NewGuid()],default));Assert.IsFalse(await f.Repository.IsProductActiveAsync(Guid.NewGuid(),default));Assert.IsNull(f.Session.Transaction);await f.Usable();
    }

    /// <summary>合法产品显式填充必填SupplyType，身份/SKU独立随机，不借测试创建真实业务关系。</summary>
    private static Product Product(Fixture f)=>new(){Uid=Guid.NewGuid(),Sku=f.Prefix+Guid.NewGuid().ToString("N"),SupplyType="采购",MaterialType="零件",Unit="PCS",MainImage="当前图像",LinkSku="当前关联"};
    /// <summary>随机公共节点，01/02末字节保证SqlGuid与.NET Guid序相反，避免固定业务身份。</summary>
    private static (Guid A,Guid B) OppositeIds(){var node=Guid.NewGuid().ToString("N")[20..30];return(Guid.Parse("ffffffff-ffff-ffff-ffff-"+node+"01"),Guid.Parse("00000000-0000-0000-0000-"+node+"02"));}

    /// <summary>独立RC夹具，所有写入属于本人事务并回滚；不建表、取序列或提交。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly string Prefix="OQ"+Guid.NewGuid().ToString("N")+"X";
        public readonly DbSession Session;
        public readonly PostgreSqlOutboundQueryRepository Repository;
        public HookConnection Hook=>(HookConnection)Session.Connection;
        /// <summary>通过固定故障库门禁打开独立连接，可选RC事务只用于回滚本例事实。</summary>
        public Fixture(bool transaction=true)
        {
            Session=new(new HookFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
            try{if(transaction)Session.Transaction=Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);var user=new Identity();Repository=new(new RepositoryBaseContext(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}
            catch{Session.Dispose();throw;}
        }
        /// <summary>独立合法出库头保存历史仓库快照，不要求当前仓库存在。</summary>
        public OutboundDocument Head()=>new(){Uid=Guid.NewGuid(),DocumentNo=Prefix+Guid.NewGuid().ToString("N"),DocumentType=OutboundDocumentTypeEnum.StockIssue,WarehouseSiteId=Guid.NewGuid(),WarehouseNameSnapshot=Prefix+"仓名",ShipmentDate=new DateTime(2026,10,5),CommandId=Guid.NewGuid()};
        /// <summary>正数量/正行号及随机CommandItemId，不构造非法重复有效行号。</summary>
        public OutboundDocumentLine Line(OutboundDocument h,int number=1)=>new(){Uid=Guid.NewGuid(),OutboundDocumentId=h.Uid,LineNumber=number,ProductId=Guid.NewGuid(),SkuSnapshot=Prefix+"历史SKU",UnitSnapshot="PCS",Quantity=10m,CommandItemId=Guid.NewGuid()};
        /// <summary>原始元数据插入保留指定UID/Version，不调用审计Add重置身份。</summary>
        public async Task InsertAsync<T>(T entity) where T:BaseEntity
        {
            var uid=entity.Uid;var map=EntityMetadataCache.GetMetadata(typeof(T));var columns=string.Join(',',map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));var values=string.Join(',',map.Columns.Select(x=>"@"+x.ParameterName));
            Assert.AreEqual(1,await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(entity,map),Session.Transaction)));Assert.AreEqual(uid,entity.Uid);
        }
        /// <summary>错误/取消后调用者显式回滚本人事务，不让仓储接管。</summary>
        public void Rollback(){if(Session.Transaction is {} tx){try{tx.Rollback();}finally{tx.Dispose();Session.Transaction=null;}}}
        /// <summary>连续读取证明reader释放，若事务仍存在则验证其可用。</summary>
        public async Task Usable()=>Assert.AreEqual(1,await Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:Session.Transaction)));
        /// <summary>所有退出回滚本人事务并释放连接，不清库。</summary>
        public void Dispose(){try{Rollback();}finally{Session.Dispose();}}
    }

    /// <summary>独立测试审计身份，不记录凭据。</summary>
    private sealed class Identity:ICurrentUser
    {
        public Guid? UserId=>null;public string UserName=>"出库四查询回滚夹具";public bool IsAuthenticated=>false;
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
        public int[] ReadCounts=new int[2];
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
            private async Task Starting(CancellationToken token){if(owner.Before is {} before)await before(CommandText,token);token.ThrowIfCancellationRequested();owner.Commands.Add(CommandText);}
            public override int ExecuteNonQuery()=>innerCommand.ExecuteNonQuery();public override object? ExecuteScalar()=>innerCommand.ExecuteScalar();
            /// <summary>先完成测试阶段屏障，再以真实Npgsql执行夹具写入，事务仍由夹具拥有。</summary>
            public override async Task<int> ExecuteNonQueryAsync(CancellationToken token){await Starting(token);return await innerCommand.ExecuteNonQueryAsync(token);}
            /// <summary>先完成测试阶段屏障，再执行真实Guard或标量验证，不模拟返回值。</summary>
            public override async Task<object?> ExecuteScalarAsync(CancellationToken token){await Starting(token);return await innerCommand.ExecuteScalarAsync(token);}
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>innerCommand.ExecuteReader(behavior);
            /// <summary>在reader打开之前完成阶段屏障，然后原样传递token和驱动reader。</summary>
            protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token)
            {
                await Starting(token);var reader=await innerCommand.ExecuteReaderAsync(behavior,token);
                return (CommandText.Contains("AS \"WarehouseCodeSnapshot\"",StringComparison.Ordinal)||CommandText.StartsWith("SELECT COUNT(*)::integer ",StringComparison.Ordinal))?new BatchReader(owner,reader):reader;
            }
            protected override void Dispose(bool disposing){if(disposing)innerCommand.Dispose();base.Dispose(disposing);}
        }
        /// <summary>转发全部列类型，只观察各结果Read/NextResult/Dispose；不自动排空未读取的结果。</summary>
        private sealed class BatchReader(HookConnection owner,DbDataReader inner):DbDataReader
        {
            private int phase;private bool disposed;
            /// <summary>观察当前结果读取次数，原行和列仍由内部reader提供。</summary>
            public override bool Read(){owner.ResultAction?.Invoke(phase);owner.ReadCounts[phase]++;return inner.Read();}
            /// <summary>观察结果推进边界，不主动排空未读结果或改变驱动返回。</summary>
            public override bool NextResult(){owner.NextResults++;if(phase<1)owner.ResultAction?.Invoke(phase+1);var result=inner.NextResult();if(result)phase++;return result;}
            /// <summary>观察当前结果读取边界并将取消令牌传递给真实驱动，不替代数据库返回。</summary>
            public override async Task<bool> ReadAsync(CancellationToken token){owner.LastToken=token;owner.ResultAction?.Invoke(phase);token.ThrowIfCancellationRequested();owner.ReadCounts[phase]++;return await inner.ReadAsync(token);}
            /// <summary>观察结果切换边界并使用真实异步驱动推进；取消保持技术异常。</summary>
            public override async Task<bool> NextResultAsync(CancellationToken token){owner.LastToken=token;owner.NextResults++;if(phase<1)owner.ResultAction?.Invoke(phase+1);token.ThrowIfCancellationRequested();var result=await inner.NextResultAsync(token);if(result)phase++;return result;}
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
