using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>五个库存查询的真实PG用例由主控串行运行；随机身份只在ReadCommitted事务中新增并finally回滚。</summary>
[DoNotParallelize]
public sealed partial class PostgreSqlInventoryQueryMigrationTests
{
    /// <summary>余额快照不跟随产品名称改动，只有图片当前投影；缺失/软删产品仍返回余额。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration"),DataRow("live"),DataRow("missing-product"),DataRow("deleted-product"),DataRow("deleted-supplier"),DataRow("missing-supplier"),DataRow("deleted-warehouse"),DataRow("inactive-warehouse")]
    public Task RealPg_BalanceJoinsAndImage(string mode)=>Rollback(async f=>
    {
        f.Warehouse.IsDeleted=mode=="deleted-warehouse";f.Warehouse.IsActive=mode!="inactive-warehouse";f.Supplier.IsDeleted=mode=="deleted-supplier";
        f.Product.IsDeleted=mode=="deleted-product";await f.Seed(product:mode!="missing-product",supplier:mode!="missing-supplier");
        var item=await f.Repository.GetBalanceDtoAsync(f.Balance.Uid);Assert.IsNotNull(item);Assert.AreEqual(f.Balance.Uid,item.Uid);Assert.AreEqual(f.Balance.Version,item.Version);
        Assert.AreEqual(f.Warehouse.Code,item.WarehouseCode);Assert.AreEqual(f.Warehouse.Name,item.WarehouseName);Assert.AreEqual(f.Supplier.Uid,item.SupplierId);
        Assert.AreEqual(mode=="missing-supplier"?null:f.Supplier.SupplierName,item.SupplierName);Assert.AreEqual(mode=="missing-supplier"?null:f.Supplier.SupplierCode,item.SupplierCode);
        Assert.AreEqual("旧SKU",item.Sku);Assert.AreEqual("旧品名",item.ProductName);Assert.AreEqual(" OLD UNIT ",item.Unit);Assert.AreEqual(8.1234m,item.OnHandQty);Assert.AreEqual(item.OnHandQty,item.AvailableQty);
        Assert.AreEqual(mode is "missing-product" or "deleted-product"?null:"当前图片",item.ImagePath);Assert.AreEqual(f.Product.Uid,item.ProductId);
        Assert.AreEqual(1,(await f.Repository.SearchBalancesAsync(f.BalanceRequest())).TotalCount);
        if(mode=="live"){await f.Execute("UPDATE public.product SET main_image='改变图片',description_zh='新名称' WHERE uid=@Uid",f.Product.Uid);var changed=await f.Repository.GetBalanceDtoAsync(f.Balance.Uid);Assert.AreEqual("改变图片",changed!.ImagePath);Assert.AreEqual("旧品名",changed.ProductName);}
    });

    /// <summary>nullable余额快照/时间和缺供应商资料保持NULL，不补空串；产品仅逻辑身份。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public Task RealPg_BalanceNulls()=>Rollback(async f=>
    {
        f.Balance.SkuSnapshot=null;f.Balance.ProductNameSnapshot=null;f.Balance.UnitSnapshot=null;f.Warehouse.SupplierId=null;await f.Seed(product:false,supplier:false);
        var row=await f.Repository.GetBalanceDtoAsync(f.Balance.Uid);Assert.IsNotNull(row);Assert.IsNull(row.Sku);Assert.IsNull(row.ProductName);Assert.IsNull(row.Unit);Assert.IsNull(row.ImagePath);Assert.IsNull(row.SupplierId);Assert.IsNull(row.SupplierCode);Assert.IsNull(row.SupplierName);Assert.IsNull(row.UpdatedDate);
    });

    /// <summary>余额软删时详情/搜索/对账不返回，但按余额键的历史Trace仍返回，缺失余额为0/NULL。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public Task RealPg_DeletedAndMissingBalance()=>Rollback(async f=>
    {
        f.Balance.IsDeleted=true;await f.Seed();var ledger=f.Ledger(1,3);await f.Add(ledger);
        Assert.IsNull(await f.Repository.GetBalanceDtoAsync(f.Balance.Uid));Assert.IsNull(await f.Repository.GetReconciliationAsync(f.Balance.Uid));Assert.AreEqual(0,(await f.Repository.SearchBalancesAsync(f.BalanceRequest())).TotalCount);
        var trace=await f.Repository.GetLedgerByBalanceAsync(f.Balance.Uid,1,20);Assert.AreEqual(1,trace.TotalCount);Assert.AreEqual(ledger.Uid,trace.Items.Single().Uid);
        var missing=Guid.NewGuid();Assert.IsNull(await f.Repository.GetBalanceDtoAsync(missing));Assert.IsNull(await f.Repository.GetReconciliationAsync(missing));var empty=await f.Repository.GetLedgerByBalanceAsync(missing,1,20);Assert.AreEqual(0,empty.TotalCount);Assert.HasCount(0,empty.Items);
    });

    /// <summary>必需仓库不存在使搜索/详情/Trace无行；独立对账只依余额与流水键，不擅自加主数据依赖。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public Task RealPg_MissingWarehouse()=>Rollback(async f=>
    {
        await f.Add(f.Balance);await f.Add(f.Ledger(1,4));Assert.IsNull(await f.Repository.GetBalanceDtoAsync(f.Balance.Uid));Assert.AreEqual(0,(await f.Repository.SearchBalancesAsync(f.BalanceRequest())).TotalCount);Assert.AreEqual(0,(await f.Repository.SearchLedgerAsync(f.LedgerRequest())).TotalCount);Assert.AreEqual(0,(await f.Repository.GetLedgerByBalanceAsync(f.Balance.Uid,1,20)).TotalCount);
        var reconciliation=await f.Repository.GetReconciliationAsync(f.Balance.Uid);Assert.IsNotNull(reconciliation);Assert.AreEqual(4m,reconciliation.LedgerNetQty);
    });

    /// <summary>流水所有名称/单号/单位坚持不可变快照，当前仓库仅用于Supplier过滤；改名不刷新历史。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public Task RealPg_LedgerHistoricalFields()=>Rollback(async f=>
    {
        await f.Seed();var ledger=f.Ledger(1,8.1234m);ledger.SourceDocumentId=Guid.NewGuid();ledger.SourceLineId=Guid.NewGuid();ledger.CorrectionGroupId=Guid.NewGuid();ledger.Reason="历史原因";ledger.CreatedBy="历史人";await f.Add(ledger);
        await f.Execute("UPDATE public.warehouse_site SET code=code || '-NEW',name='仓库改名' WHERE uid=@Uid",f.Warehouse.Uid);
        var row=(await f.Repository.SearchLedgerAsync(f.LedgerRequest())).Items.Single();Assert.AreEqual(ledger.Uid,row.Uid);Assert.AreEqual(ledger.Sequence,row.Sequence);Assert.AreEqual(ledger.WarehouseCodeSnapshot,row.WarehouseCode);Assert.AreEqual(ledger.WarehouseNameSnapshot,row.WarehouseName);
        Assert.AreEqual(ledger.SkuSnapshot,row.Sku);Assert.AreEqual(ledger.ProductNameSnapshot,row.ProductName);Assert.AreEqual(ledger.UnitSnapshot,row.Unit);Assert.AreEqual(ledger.TransactionType,row.TransactionType);Assert.AreEqual(ledger.QuantityDelta,row.QuantityDelta);Assert.AreEqual(ledger.BalanceAfter,row.BalanceAfter);
        Assert.AreEqual(ledger.SourceDocumentType,row.SourceDocumentType);Assert.AreEqual(ledger.SourceDocumentId,row.SourceDocumentId);Assert.AreEqual(ledger.SourceLineId,row.SourceLineId);Assert.AreEqual(ledger.SourceDocumentNoSnapshot,row.SourceDocumentNo);Assert.AreEqual(ledger.CorrectionGroupId,row.CorrectionGroupId);Assert.AreEqual(ledger.Reason,row.Reason);Assert.AreEqual(ledger.CreatedBy,row.CreatedBy);Assert.AreEqual(ledger.OccurredDate,row.OccurredDate);
        var r=f.LedgerRequest();r.SupplierId=Guid.NewGuid();Assert.AreEqual(0,(await f.Repository.SearchLedgerAsync(r)).TotalCount);r.SupplierId=f.Supplier.Uid;Assert.AreEqual(1,(await f.Repository.SearchLedgerAsync(r)).TotalCount);
    });

    /// <summary>反向和替换各自LEFT JOIN，原行与两后续行都返回；不做净额去重或新历史算法。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public Task RealPg_ReversalReplacementJoins()=>Rollback(async f=>
    {
        await f.Seed();var original=f.Ledger(1,8);var reversal=f.Ledger(2,-8);reversal.TransactionType=(InventoryTransactionTypeEnum)900;reversal.ReversalOfLedgerId=original.Uid;
        var replacement=f.Ledger(3,6);replacement.ReplacementOfLedgerId=original.Uid;var group=Guid.NewGuid();reversal.CorrectionGroupId=group;replacement.CorrectionGroupId=group;
        await f.Add(original);await f.Add(reversal);await f.Add(replacement);var page=await f.Repository.GetLedgerByBalanceAsync(f.Balance.Uid,1,20);Assert.AreEqual(3,page.TotalCount);
        CollectionAssert.AreEqual(new[]{replacement.Uid,reversal.Uid,original.Uid},page.Items.Select(x=>x.Uid).ToArray());var first=page.Items.Single(x=>x.Uid==original.Uid);Assert.AreEqual(reversal.Uid,first.ReversedByLedgerId);Assert.AreEqual(replacement.Uid,first.ReplacedByLedgerId);
        Assert.AreEqual(original.Uid,page.Items.Single(x=>x.Uid==reversal.Uid).ReversalOfLedgerId);Assert.AreEqual(original.Uid,page.Items.Single(x=>x.Uid==replacement.Uid).ReplacementOfLedgerId);
        Assert.AreEqual(6m,(await f.Repository.GetReconciliationAsync(f.Balance.Uid))!.LedgerNetQty);
    });

    /// <summary>NULL历史快照和引用不回落当前仓库/产品，Mandatory SourceDocumentType可为空字符串。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public Task RealPg_LedgerNulls()=>Rollback(async f=>
    {
        await f.Seed();var ledger=f.Ledger(1,1);ledger.WarehouseCodeSnapshot=null;ledger.WarehouseNameSnapshot=null;ledger.SkuSnapshot=null;ledger.ProductNameSnapshot=null;ledger.UnitSnapshot=null;ledger.SourceDocumentNoSnapshot=null;ledger.SourceDocumentType="";await f.Add(ledger);
        var row=(await f.Repository.SearchLedgerAsync(f.LedgerRequest())).Items.Single();Assert.IsNull(row.WarehouseCode);Assert.IsNull(row.WarehouseName);Assert.IsNull(row.Sku);Assert.IsNull(row.ProductName);Assert.IsNull(row.Unit);Assert.IsNull(row.SourceDocumentNo);Assert.IsNull(row.SourceDocumentId);Assert.IsNull(row.SourceLineId);Assert.IsNull(row.ReversalOfLedgerId);Assert.IsNull(row.ReversedByLedgerId);Assert.IsNull(row.ReplacementOfLedgerId);Assert.IsNull(row.ReplacedByLedgerId);Assert.IsNull(row.CorrectionGroupId);Assert.IsNull(row.Reason);Assert.IsNull(row.CreatedBy);Assert.AreEqual("",row.SourceDocumentType);
    });

    /// <summary>对账要求数量与最后序列同时相等，空流水与正负零净额独立，不能只比较最新余额。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration"),DataRow("matched",true),DataRow("quantity",false),DataRow("sequence",false),DataRow("zero-net",true),DataRow("none",true)]
    public Task RealPg_Reconciliation(string mode,bool matched)=>Rollback(async f=>
    {
        f.Balance.OnHandQty=mode is "zero-net" or "none"?0:mode=="quantity"?9:8;
        f.Balance.LastLedgerSequence=mode is "none" or "sequence"?0:f.SequenceBase+(mode=="zero-net"?2:1);await f.Seed();
        if(mode!="none"){await f.Add(f.Ledger(1,mode=="zero-net"?6:8));if(mode=="zero-net")await f.Add(f.Ledger(2,-6));}
        var row=await f.Repository.GetReconciliationAsync(f.Balance.Uid);Assert.IsNotNull(row);Assert.AreEqual(f.Balance.OnHandQty,row.BalanceQty);Assert.AreEqual(mode is "zero-net" or "none"?0m:8m,row.LedgerNetQty);
        Assert.AreEqual(f.Balance.LastLedgerSequence,row.BalanceLastLedgerSequence);Assert.AreEqual(mode=="none"?0:f.SequenceBase+(mode=="zero-net"?2:1),row.LedgerLastSequence);Assert.AreEqual(matched,row.IsReconciled);
    });

    /// <summary>两个搜索回落50，Trace直接clamp1；实际count和页按原两个独立语句观察。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration"),DataRow(0,0,50),DataRow(1,0,50),DataRow(2,0,1),DataRow(0,-1,50),DataRow(1,-1,50),DataRow(2,-1,1),DataRow(0,999,200),DataRow(1,999,200),DataRow(2,999,200)]
    public Task RealPg_PagingDefaults(int method,int size,int expected)=>Rollback(async f=>
    {
        await f.Seed();await f.Add(f.Ledger(1,1));await f.Add(f.Ledger(2,2));
        if(method==0){var r=f.BalanceRequest();r.PageSize=size;r.PageIndex=0;var page=await f.Repository.SearchBalancesAsync(r);Assert.AreEqual(expected,page.PageSize);Assert.AreEqual(1,page.PageIndex);Assert.AreEqual(1,page.TotalCount);}
        else{var r=f.LedgerRequest();r.PageSize=size;r.PageIndex=0;var page=method==1?await f.Repository.SearchLedgerAsync(r):await f.Repository.GetLedgerByBalanceAsync(f.Balance.Uid,0,size);Assert.AreEqual(expected,page.PageSize);Assert.AreEqual(1,page.PageIndex);Assert.AreEqual(2,page.TotalCount);Assert.HasCount(Math.Min(expected,2),page.Items);Assert.AreEqual(f.SequenceBase+2,page.Items[0].Sequence);}
    });

    /// <summary>余额Supplier/Warehouse/NonZero和SKU/品名/仓名字面筛选保留，负数量不新增业务屏蔽。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration"),DataRow(0),DataRow(-3),DataRow(3)]
    public Task RealPg_BalanceFilters(int inputQuantity)=>Rollback(async f=>
    {
        decimal quantity=inputQuantity;
        f.Balance.OnHandQty=quantity;f.Balance.SkuSnapshot="A\\%_[";await f.Seed();var request=f.BalanceRequest();request.NonZeroOnly=true;request.SupplierId=f.Supplier.Uid;request.Keyword=" A\\%_[ ";Assert.AreEqual(quantity==0?0:1,(await f.Repository.SearchBalancesAsync(request)).TotalCount);
        request.NonZeroOnly=null;Assert.AreEqual(1,(await f.Repository.SearchBalancesAsync(request)).TotalCount);request.SupplierId=Guid.NewGuid();Assert.AreEqual(0,(await f.Repository.SearchBalancesAsync(request)).TotalCount);request.SupplierId=null;request.WarehouseSiteId=Guid.NewGuid();Assert.AreEqual(0,(await f.Repository.SearchBalancesAsync(request)).TotalCount);
    });

    /// <summary>时间下界原输入包含、上界原输入加一天排他，含时分不截Date。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration"),DataRow(-1,false),DataRow(0,true),DataRow(1,true),DataRow(86400,false)]
    public Task RealPg_LedgerDateBounds(int seconds,bool included)=>Rollback(async f=>
    {
        await f.Seed();var start=f.Today.AddHours(13);var ledger=f.Ledger(1,1);ledger.OccurredDate=start.AddSeconds(seconds);await f.Add(ledger);var request=f.LedgerRequest();request.OccurredDateFrom=start;request.OccurredDateTo=start;
        Assert.AreEqual(included?1:0,(await f.Repository.SearchLedgerAsync(request)).TotalCount);
    });

    /// <summary>流水筛选用历史SKU/名称/类型/仓码和来源单号字面，不随当前主数据改变。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public Task RealPg_LedgerLiteralFilters()=>Rollback(async f=>
    {
        await f.Seed();var ledger=f.Ledger(1,1);ledger.SourceDocumentNoSnapshot="NO\\%_[";ledger.SkuSnapshot="SKU\\%_[";await f.Add(ledger);
        var r=f.LedgerRequest();r.Keyword=" SKU\\%_[ ";r.SourceDocumentNo=" NO\\%_[ ";r.TransactionType=ledger.TransactionType;Assert.AreEqual(1,(await f.Repository.SearchLedgerAsync(r)).TotalCount);
        r.TransactionType=(InventoryTransactionTypeEnum)900;Assert.AreEqual(0,(await f.Repository.SearchLedgerAsync(r)).TotalCount);r.TransactionType=null;r.Keyword="当前产品";Assert.AreEqual(0,(await f.Repository.SearchLedgerAsync(r)).TotalCount);
    });

    /// <summary>不可变流水软删受到CHECK拒绝，负LastSequence也被拒绝；保存点恢复测试事务，不绕约束。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration"),DataRow(false),DataRow(true)]
    public Task RealPg_ConstraintsArePreserved(bool ledger)=>Rollback(async f=>
    {
        await f.Seed();await f.Session.Connection.ExecuteAsync("SAVEPOINT inventory_invalid",transaction:f.Transaction);
        var badLedger=f.Ledger(1,1);badLedger.IsDeleted=true;var badBalance=new InventoryBalance{WarehouseSiteId=f.Warehouse.Uid,ProductId=Guid.NewGuid(),LastLedgerSequence=-1};
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>ledger?f.Add(badLedger):f.Add(badBalance));Assert.AreEqual(PostgresErrorCodes.CheckViolation,ex.SqlState);
        await f.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT inventory_invalid",transaction:f.Transaction);Assert.IsNotNull(await f.Repository.GetBalanceDtoAsync(f.Balance.Uid));
    });

    /// <summary>同事务当前页反映后续新增，旧总数结果不变；不声称此用例为count/页间物理跨连接屏障。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public Task RealPg_LaterAppendIsVisible()=>Rollback(async f=>
    {
        await f.Seed();await f.Add(f.Ledger(1,1));var first=await f.Repository.SearchLedgerAsync(f.LedgerRequest());await f.Add(f.Ledger(2,2));var second=await f.Repository.SearchLedgerAsync(f.LedgerRequest());Assert.AreEqual(1,first.TotalCount);Assert.HasCount(1,first.Items);Assert.AreEqual(2,second.TotalCount);Assert.HasCount(2,second.Items);
    });

    /// <summary>五入口可空事务，预取消三分页仍执行count后取消行，单对象预取消不执行；连接后续可用。</summary>
    [TestMethod,TestCategory("PostgreSqlInventoryQueryIntegration")]
    public async Task RealPg_OptionalTransactionAndCancellation()
    {
        using var session=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));var f=new InventoryFixture(session,null);var uid=Guid.NewGuid();
        Assert.IsNull(await f.Repository.GetBalanceDtoAsync(uid));Assert.IsNull(await f.Repository.GetReconciliationAsync(uid));Assert.AreEqual(0,(await f.Repository.GetLedgerByBalanceAsync(uid,1,20)).TotalCount);Assert.AreEqual(0,(await f.Repository.SearchBalancesAsync(f.BalanceRequest())).TotalCount);Assert.AreEqual(0,(await f.Repository.SearchLedgerAsync(f.LedgerRequest())).TotalCount);
        var token=new CancellationToken(true);await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchBalancesAsync(f.BalanceRequest(),token));await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchLedgerAsync(f.LedgerRequest(),token));await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetLedgerByBalanceAsync(uid,1,20,token));await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetBalanceDtoAsync(uid,token));await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetReconciliationAsync(uid,token));
        Assert.IsNull(session.Transaction);Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
    }

    /// <summary>限定固定failure库和随机实体，父ReadCommitted事务始终finally回滚，不初始化/提交/消耗全局序列。</summary>
    private static async Task Rollback(Func<InventoryFixture,Task> action)
    {
        using var session=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));using var transaction=session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);session.Transaction=transaction;
        try{await action(new(session,transaction));Assert.AreSame(transaction,session.Transaction);}finally{try{transaction.Rollback();}finally{session.Transaction=null;}}
    }

    /// <summary>合法随机库存键/正序号，不共享可变数据，不通过过账服务创造查询前置。</summary>
    private sealed class InventoryFixture
    {
        public DbSession Session{get;} public IDbTransaction? Transaction{get;} public PostgreSqlInventoryQueryRepository Repository{get;}
        public DateTime Today{get;}=new(2026,10,5);
        public long SequenceBase{get;}=(BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue)/2+1;
        public Supplier Supplier{get;} public WarehouseSite Warehouse{get;} public Product Product{get;} public InventoryBalance Balance{get;}
        /// <summary>主数据只是本例逻辑身份，余额保留独立商业快照。</summary>
        public InventoryFixture(DbSession session,IDbTransaction? transaction)
        {
            Session=session;Transaction=transaction;var user=new WorkspaceUser();Repository=new(new RepositoryBaseContext(session,NullLoggerFactory.Instance,new AuditStampService(user),user));
            // SupplyType在源与PG均为必填；合法主数据夹具不应在查询前被非空约束拒绝。
            Supplier=new(){SupplierCode="PGIQ"+Guid.NewGuid().ToString("N"),SupplierName="供应商当前名",DocRelateNo="PGIQ"+Guid.NewGuid().ToString("N")};Warehouse=new(){Code="PGIQ"+Guid.NewGuid().ToString("N"),Name="仓库当前名",SupplierId=Supplier.Uid,IsActive=true};Product=new(){Sku="PGIQ"+Guid.NewGuid().ToString("N"),DescriptionZh="当前产品",MainImage="当前图片",SupplyType="采购"};
            Balance=new(){WarehouseSiteId=Warehouse.Uid,ProductId=Product.Uid,SkuSnapshot="旧SKU",ProductNameSnapshot="旧品名",UnitSnapshot=" OLD UNIT ",OnHandQty=8.1234m};
        }
        /// <summary>按需省略产品/供应商以验证逻辑引用缺失，真实必需仓库仍由本例插入。</summary>
        public async Task Seed(bool product=true,bool supplier=true){if(supplier)await Add(Supplier);await Add(Warehouse);if(product)await Add(Product);await Add(Balance);}
        /// <summary>限定本例仓库避免读取其他历史。</summary>
        public InventoryBalanceSearchRequest BalanceRequest()=>new(){WarehouseSiteId=Warehouse.Uid,PageSize=200};
        /// <summary>限定本例仓库，显式分页不依赖测试库其他流水。</summary>
        public InventoryLedgerSearchRequest LedgerRequest()=>new(){WarehouseSiteId=Warehouse.Uid,PageSize=200};
        /// <summary>严格递增独立随机序号及命令身份，快照故意不同当前主数据，不消耗数据库序列。</summary>
        public InventoryLedgerEntry Ledger(int number,decimal quantity)=>new(){WarehouseSiteId=Warehouse.Uid,ProductId=Product.Uid,Sequence=SequenceBase+number,TransactionType=(InventoryTransactionTypeEnum)10,QuantityDelta=quantity,BalanceAfter=quantity,
            WarehouseCodeSnapshot="历史仓码",WarehouseNameSnapshot="历史仓名",SkuSnapshot="历史SKU",ProductNameSnapshot="历史品名",UnitSnapshot="历史单位",SourceDocumentType="InboundDocument",SourceDocumentNoSnapshot="历史单号",CommandId=Guid.NewGuid(),CommandItemId=Guid.NewGuid(),OccurredDate=Today};
        /// <summary>直接元数据INSERT原UID/Version/NULL，不打戳，不更改CHECK/索引/不可变触发器。</summary>
        public async Task Add<T>(T entity) where T:BaseEntity
        {
            var m=PostgreSqlEntityMetadata.GetMetadata(typeof(T));var columns=string.Join(',',m.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));var values=string.Join(',',m.Columns.Select(x=>"@"+x.ParameterName));
            Assert.AreEqual(1,await Session.Connection.ExecuteAsync($"INSERT INTO public.{PostgreSqlNames.Quote(m.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(entity,m),Transaction));
        }
        /// <summary>只更改本例主数据身份以检验当前/历史投影，不修改不可变流水。</summary>
        public Task<int> Execute(string sql,Guid uid)=>Session.Connection.ExecuteAsync(sql,new{Uid=uid},Transaction);
    }
}
