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
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>出库选品真实PG用例仅编写编译，主控严格故障库串行执行；随机合法实体全在父事务回滚，不创建结构或提交。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlOutboundItemsQueryMigrationIntegrationTests
{
    /// <summary>十字段来自当前产品/余额，历史余额快照不覆盖，SKU/单位原文和NULL及小数保持。</summary>
    [TestMethod]
    public async Task FullProjectionAndCurrentFacts()
    {
        using var f=new Fixture();var w=f.Warehouse();await f.Insert(w);var p=f.Product();p.Unit=" PCS ";p.DescriptionZh="当前说明";p.LinkSku=null;p.MainImage=null;await f.Insert(p);var b=f.Balance(w,p);b.SkuSnapshot="历史SKU";b.UnitSnapshot="历史单位";await f.Insert(b);
        var q=f.Request(w);var d=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(1,d.TotalCount);var r=d.Items.Single();Assert.AreEqual(b.Uid,r.Uid);Assert.AreNotEqual(p.Uid,r.Uid);Assert.AreEqual(w.Uid,r.WarehouseSiteId);Assert.AreEqual(p.Uid,r.ProductId);Assert.AreEqual(p.Sku,r.Sku);Assert.AreEqual("当前说明",r.Description);Assert.IsNull(r.LinkSku);Assert.IsNull(r.ImagePath);Assert.AreEqual(" PCS ",r.Unit);Assert.AreEqual(3.1250m,r.OnHandQty);Assert.IsNull(r.DefaultWarehouseName);await f.Usable();
    }

    /// <summary>真实INNER JOIN及余额/启用/软删资格，不新增仓库类型或默认仓限制。</summary>
    [TestMethod]
    [DataRow("valid",true)] [DataRow("zero",false)] [DataRow("negative",false)] [DataRow("balance-deleted",false)]
    [DataRow("product-missing",false)] [DataRow("product-deleted",false)] [DataRow("product-inactive",false)]
    [DataRow("warehouse-missing",false)] [DataRow("warehouse-deleted",false)] [DataRow("warehouse-inactive",false)] [DataRow("other-warehouse",false)] [DataRow("type-zero",true)]
    public async Task Qualification(string mode,bool expected)
    {
        using var f=new Fixture();var w=f.Warehouse();var p=f.Product();var b=f.Balance(w,p);
        if(mode=="zero")b.OnHandQty=0;if(mode=="negative")b.OnHandQty=-1;if(mode=="balance-deleted")b.IsDeleted=true;
        if(mode=="product-deleted")p.IsDeleted=true;if(mode=="product-inactive")p.IsActive=false;if(mode=="warehouse-deleted")w.IsDeleted=true;if(mode=="warehouse-inactive")w.IsActive=false;if(mode=="type-zero")w.SiteType=(WarehouseSiteTypeEnum)0;
        if(mode!="warehouse-missing")await f.Insert(w);if(mode!="product-missing")await f.Insert(p);await f.Insert(b);
        var q=f.Request(w);if(mode=="other-warehouse")q.WarehouseSiteId=Guid.NewGuid();var d=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(expected?1:0,d.TotalCount);Assert.HasCount(expected?1:0,d.Items);await f.Usable();
    }

    /// <summary>单位资格只去U+0020两端空格，tab/NBSP仍符合源SQL条件，输出不Trim。</summary>
    [TestMethod]
    [DataRow(null,false)] [DataRow("",false)] [DataRow("   ",false)] [DataRow("\t",true)] [DataRow("\u00a0",true)] [DataRow(" PCS ",true)] [DataRow("KG",true)]
    public async Task AsciiUnitAndRawValue(string? unit,bool expected)
    {
        using var f=new Fixture();var w=f.Warehouse();var p=f.Product();p.Unit=unit;await f.Insert(w);await f.Insert(p);await f.Insert(f.Balance(w,p));var d=await f.Repository.SearchItemsAsync(f.Request(w),default);Assert.AreEqual(expected?1:0,d.TotalCount);Assert.HasCount(expected?1:0,d.Items);if(expected)Assert.AreEqual(unit,d.Items.Single().Unit);
    }

    /// <summary>默认仓仅LEFT JOIN未删，停用仍显示名称，缺失/未配置/软删为NULL而候选资格保持。</summary>
    [TestMethod]
    [DataRow("none",false)] [DataRow("missing",false)] [DataRow("deleted",false)] [DataRow("inactive",true)] [DataRow("active",true)]
    public async Task DefaultWarehouseBoundary(string mode,bool hasName)
    {
        using var f=new Fixture();var w=f.Warehouse();var dw=f.Warehouse();dw.Name="原默认仓名称";dw.IsActive=mode!="inactive";dw.IsDeleted=mode=="deleted";var p=f.Product();if(mode!="none")p.DefaultWarehouseSiteId=dw.Uid;
        await f.Insert(w);if(mode is not ("none" or "missing"))await f.Insert(dw);await f.Insert(p);await f.Insert(f.Balance(w,p));var d=await f.Repository.SearchItemsAsync(f.Request(w),default);Assert.AreEqual(1,d.TotalCount);Assert.AreEqual(hasName?dw.Name:null,d.Items.Single().DefaultWarehouseName);
    }

    /// <summary>实际uuid[]排除局部null/empty结果相同，重复不改语义，Empty非业务身份不扩大资格。</summary>
    [TestMethod]
    [DataRow("null",2)] [DataRow("empty",2)] [DataRow("one",1)] [DataRow("duplicates",1)] [DataRow("empty-guid",2)]
    public async Task Exclusions(string mode,int count)
    {
        using var f=new Fixture();var w=f.Warehouse();var a=f.Product();var b=f.Product();await f.Insert(w);foreach(var p in new[]{a,b}){await f.Insert(p);await f.Insert(f.Balance(w,p));}var q=f.Request(w);
        q.ExcludedProductIds=mode switch{"null"=>null!,"empty"=>[],"one"=>[a.Uid],"duplicates"=>[a.Uid,a.Uid,Guid.Empty],_=>[Guid.Empty]};var d=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(count,d.TotalCount);Assert.HasCount(count,d.Items);if(count==1)Assert.AreEqual(b.Uid,d.Items.Single().ProductId);
    }

    /// <summary>原三列字面LIKE与.NET Trim，通配符/转义字符不能扩大到伪匹配产品。</summary>
    [TestMethod]
    [DataRow("sku"," ~%_[ ")] [DataRow("link"," ~%_[ ")] [DataRow("description"," ~%_[ ")]
    [DataRow("ascii"," mixedCaseWordZZZZZ ")] [DataRow("chinese"," 中文库存 ")]
    public async Task LiteralKeywordColumns(string field,string keyword)
    {
        using var f=new Fixture();var w=f.Warehouse();var p=f.Product();var foil=f.Product();
        var value=field=="ascii"?"MIXEDCASEWORDZZZZZ":field=="chinese"?"中文库存":"~%_[";
        if(field=="link")p.LinkSku=value;else if(field is "description" or "chinese")p.DescriptionZh=value;else p.Sku+=value;
        foil.LinkSku="~xA";foil.DescriptionZh="通配不应命中";await f.Insert(w);foreach(var product in new[]{p,foil}){await f.Insert(product);await f.Insert(f.Balance(w,product));}
        var q=f.Request(w);q.Keyword=keyword;var d=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(1,d.TotalCount);Assert.AreEqual(p.Uid,d.Items.Single().ProductId);
    }

    /// <summary>正常第一页/末页/超末页保持总数和原页值，当前库存不会成为默认出库数量。</summary>
    [TestMethod]
    public async Task PagingAndNoCandidates()
    {
        using var f=new Fixture();var w=f.Warehouse();await f.Insert(w);var rows=new List<Product>();foreach(var suffix in new[]{"C","A","B"}){var p=f.Product();p.Sku=f.Prefix+suffix;rows.Add(p);await f.Insert(p);await f.Insert(f.Balance(w,p));}
        var q=f.Request(w);q.PageSize=2;var first=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(3,first.TotalCount);CollectionAssert.AreEqual(new[]{rows[1].Uid,rows[2].Uid},first.Items.Select(x=>x.ProductId).ToArray());
        q.PageIndex=2;var last=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(3,last.TotalCount);Assert.AreEqual(rows[0].Uid,last.Items.Single().ProductId);
        q.PageIndex=4;var beyond=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(3,beyond.TotalCount);Assert.HasCount(0,beyond.Items);Assert.AreEqual(4,beyond.PageIndex);Assert.AreEqual(2,beyond.PageSize);
        q.ExcludedProductIds=rows.Select(x=>x.Uid).ToList();var none=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(0,none.TotalCount);Assert.HasCount(0,none.Items);await f.Usable();
    }

    /// <summary>生产排序片段在合法VALUES关系证明SKU局部尾空格/case等值后按SqlGuid；不为了重复SKU禁产品唯一索引。</summary>
    [TestMethod]
    public async Task FrozenOrderKeyAndSqlGuidTailWithoutIllegalProductRows()
    {
        using var f=new Fixture();var (a,b)=OppositeIds();Assert.IsTrue(a.CompareTo(b)>0);Assert.IsTrue(new SqlGuid(a).CompareTo(new SqlGuid(b))<0);
        var order=(string)typeof(PostgreSqlOutboundQueryRepository).GetMethod("ItemOrder",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,["c"])!;
        var ids=(await f.Session.Connection.QueryAsync<Guid>(new CommandDefinition("SELECT c.\"ProductId\" FROM (VALUES (@A,'CaseKey '),(@B,'casekey')) c(\"ProductId\",\"Sku\") ORDER BY "+order,new{A=a,B=b},f.Session.Transaction))).ToArray();CollectionAssert.AreEqual(new[]{a,b},ids);
    }

    /// <summary>候选空或非空也必须对非法FETCH/负OFFSET产生技术失败；外层回滚aborted事务，不包装成业务Validation。</summary>
    [TestMethod]
    [DataRow(1,0,false)] [DataRow(1,0,true)] [DataRow(1,-1,false)] [DataRow(1,-1,true)]
    [DataRow(0,20,false)] [DataRow(0,20,true)] [DataRow(-1,20,false)] [DataRow(-1,20,true)]
    public async Task InvalidPaging(int index,int size,bool candidate)
    {
        using var f=new Fixture();var w=f.Warehouse();if(candidate){var p=f.Product();await f.Insert(w);await f.Insert(p);await f.Insert(f.Balance(w,p));}var q=f.Request(w);q.PageIndex=index;q.PageSize=size;
        var error=await Assert.ThrowsExactlyAsync<PostgresException>(()=>f.Repository.SearchItemsAsync(q,default));Assert.AreEqual(DatabaseFailureKind.Unknown,f.Session.Errors.Classify(error));Assert.IsNotNull(f.Session.Transaction);f.Rollback();await f.Usable();
    }

    /// <summary>直接仓储原int减1回绕及极大正OFFSET可成功空页，不能因服务不接受就裁剪或重写公式。</summary>
    [TestMethod]
    [DataRow(int.MinValue,20)] [DataRow(int.MaxValue,200)] [DataRow(1,int.MaxValue)]
    public async Task OriginalExtremePages(int index,int size)
    {
        using var f=new Fixture();var w=f.Warehouse();var p=f.Product();await f.Insert(w);await f.Insert(p);await f.Insert(f.Balance(w,p));var q=f.Request(w);q.PageIndex=index;q.PageSize=size;var d=await f.Repository.SearchItemsAsync(q,default);Assert.AreEqual(1,d.TotalCount);Assert.AreEqual(index==1?1:0,d.Items.Count);Assert.AreEqual(index,d.PageIndex);Assert.AreEqual(size,d.PageSize);await f.Usable();
    }

    /// <summary>空候选也执行真实SQL，预取消原样传播；错误/取消之后父事务由夹具结束。</summary>
    [TestMethod]
    public async Task PreCanceledEmptyQuery()
    {using var f=new Fixture();using var c=new CancellationTokenSource();c.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchItemsAsync(new(){WarehouseSiteId=Guid.NewGuid()},c.Token));Assert.IsNotNull(f.Session.Transaction);f.Rollback();await f.Usable();}

    /// <summary>无事务连续两次空读取保持连接可用，不创建父事务。</summary>
    [TestMethod]
    public async Task NoTransactionReads()
    {using var f=new Fixture(false);for(var i=0;i<2;i++){var d=await f.Repository.SearchItemsAsync(new(){WarehouseSiteId=Guid.NewGuid()},default);Assert.AreEqual(0,d.TotalCount);Assert.HasCount(0,d.Items);}Assert.IsNull(f.Session.Transaction);await f.Usable();}

    /// <summary>实际已执行生产命令再EXPLAIN ANALYZE同SQL/参数；候选CTE只生成一次，两个消费者读取同关系而非第二次生产表查询。</summary>
    [TestMethod]
    public async Task ActualPlanMaterializesCandidateOnce()
    {
        using var f=new Fixture();var w=f.Warehouse();await f.Insert(w);for(var i=0;i<3;i++){var p=f.Product();await f.Insert(p);await f.Insert(f.Balance(w,p));}var q=f.Request(w);q.PageSize=2;
        Assert.AreEqual(3,(await f.Repository.SearchItemsAsync(q,default)).TotalCount);var sent=f.Hook.Items.Single();Assert.HasCount(5,sent.Parameters);
        using var command=new NpgsqlCommand("EXPLAIN (ANALYZE,BUFFERS,FORMAT JSON) "+sent.Sql,f.Hook.Inner,(NpgsqlTransaction?)f.Session.Transaction);
        foreach(var parameter in sent.Parameters)command.Parameters.Add(parameter.Clone());var json=(string)(await command.ExecuteScalarAsync())!;using var document=JsonDocument.Parse(json);var root=document.RootElement[0].GetProperty("Plan");
        var nodes=Plans(root).ToArray();var producer=nodes.Single(x=>x.TryGetProperty("Subplan Name",out var n)&&n.GetString()=="CTE candidates");Assert.AreEqual(1L,producer.GetProperty("Actual Loops").GetInt64());
        Assert.IsTrue(nodes.Count(x=>x.TryGetProperty("CTE Name",out var n)&&n.GetString()=="candidates")>=2);
        // count/page生产表只属于唯一候选子树，不能仅凭CTE关键字存在就宣称没有第二次生产读取。
        var producedRelations=Plans(producer).Where(x=>x.TryGetProperty("Relation Name",out _)).Select(x=>x.GetRawText()).ToHashSet();
        foreach(var node in nodes.Where(x=>x.TryGetProperty("Relation Name",out var n)&&n.GetString() is "inventory_balance" or "product" or "warehouse_site"))Assert.IsTrue(producedRelations.Contains(node.GetRawText()));
        Assert.HasCount(1,f.Hook.Items);await f.Usable();
    }

    /// <summary>仅测试库临时事务表锁制造真实SELECT服务器等待，观察未授予relation锁后才取消；不加生产锁/DDL，三连接全部回滚。</summary>
    [TestMethod,Timeout(60000)]
    [DataRow(false)] [DataRow(true)]
    public async Task RealServerWaitCancellationAndConnectionReuse(bool parent)
    {
        using var blocker=new Fixture();using var reader=new Fixture(parent);using var observer=new Fixture(false);using var canceled=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await blocker.Session.Connection.ExecuteAsync(new CommandDefinition("LOCK TABLE public.inventory_balance IN ACCESS EXCLUSIVE MODE",transaction:blocker.Session.Transaction));
        var pending=reader.Repository.SearchItemsAsync(new(){WarehouseSiteId=Guid.NewGuid()},canceled.Token);
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(!await observer.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE l.pid=@Pid AND l.locktype='relation' AND l.relation='public.inventory_balance'::regclass AND NOT l.granted AND a.wait_event_type='Lock')",new{Pid=reader.Hook.Inner.ProcessID},cancellationToken:deadline.Token)))await Task.Delay(25,deadline.Token);
            Assert.IsFalse(pending.IsCompleted);canceled.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>pending);
            Assert.AreEqual(parent,reader.Session.Transaction is not null);reader.Rollback();await reader.Usable();Assert.HasCount(1,reader.Hook.Items);
        }
        finally
        {canceled.Cancel();blocker.Rollback();try{await pending;}catch(OperationCanceledException){}reader.Rollback();}
    }

    /// <summary>递归检查真实JSON计划节点，分析本次生产SQL而非虚构预期计划。</summary>
    private static IEnumerable<JsonElement> Plans(JsonElement node)
    {yield return node;if(node.TryGetProperty("Plans",out var children))foreach(var child in children.EnumerateArray())foreach(var nested in Plans(child))yield return nested;}
    /// <summary>随机节点使两UID真实SqlGuid顺序与.NET Guid相反，不插重复SKU产品。</summary>
    private static (Guid A,Guid B) OppositeIds(){var node=Guid.NewGuid().ToString("N")[20..30];return(Guid.Parse("ffffffff-ffff-ffff-ffff-"+node+"01"),Guid.Parse("00000000-0000-0000-0000-"+node+"02"));}

    /// <summary>真实RC父事务只承载本人随机合法夹具；无事务模式只读，结束不提交/清库。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly string Prefix="OI"+Guid.NewGuid().ToString("N")+"X";public readonly DbSession Session;public readonly PostgreSqlOutboundQueryRepository Repository;public CaptureConnection Hook=>(CaptureConnection)Session.Connection;
        /// <summary>先严格故障库门禁才打开连接；默认父事务用于所有随机写入回滚。</summary>
        public Fixture(bool transaction=true){Session=new(new CaptureFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));try{if(transaction)Session.Transaction=Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);var user=new User();Repository=new(new RepositoryBaseContext(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}catch{Session.Dispose();throw;}}
        /// <summary>合法库存仓随机code，必需类型/启用/版本明确。</summary>
        public WarehouseSite Warehouse()=>new(){Uid=Guid.NewGuid(),Code=Prefix+Guid.NewGuid().ToString("N"),Name="随机库存仓",IsActive=true,SiteType=(WarehouseSiteTypeEnum)1};
        /// <summary>合法当前产品补必需采购供应类型，SKU唯一、不取号。</summary>
        public Product Product()=>new(){Uid=Guid.NewGuid(),Sku=Prefix+Guid.NewGuid().ToString("N"),SupplyType="采购",MaterialType="零件",IsActive=true,Unit="PCS"};
        /// <summary>正余额及合法序号；软删/负值只用于受测源资格边界，不制造非法CHECK。</summary>
        public InventoryBalance Balance(WarehouseSite w,Product p)=>new(){Uid=Guid.NewGuid(),WarehouseSiteId=w.Uid,ProductId=p.Uid,OnHandQty=3.125m,LastLedgerSequence=0};
        /// <summary>固定本例实际仓库及正常页请求，不借默认仓作为真实余额仓。</summary>
        public OutboundItemSearchRequest Request(WarehouseSite w)=>new(){WarehouseSiteId=w.Uid,PageSize=20};
        /// <summary>元数据原始INSERT保留UID，所有写入必须处于本例父事务，禁止无事务偷偷提交夹具。</summary>
        public async Task Insert<T>(T row)where T:BaseEntity
        {Assert.IsNotNull(Session.Transaction);var map=EntityMetadataCache.GetMetadata(typeof(T));var columns=string.Join(',',map.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)));var values=string.Join(',',map.Columns.Select(c=>"@"+c.ParameterName));Assert.AreEqual(1,await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(row,map),Session.Transaction)));}
        /// <summary>只有夹具结束本人父事务，仓储错误不接管事务生命周期。</summary>
        public void Rollback(){if(Session.Transaction is {} tx){try{tx.Rollback();}finally{Session.Transaction=null;tx.Dispose();}}}
        /// <summary>同连接SELECT1验证无活动reader或已回滚错误事务。</summary>
        public async Task Usable()=>Assert.AreEqual(1,await Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1",transaction:Session.Transaction)));
        /// <summary>任意退出都先回滚，再释放本人连接，无DELETE/DDL/备份或序列操作。</summary>
        public void Dispose(){try{Rollback();}finally{Session.Dispose();}}
    }
    /// <summary>只用于审计上下文的测试身份，不创建账户或记录凭据。</summary>
    private sealed class User:ICurrentUser{public Guid? UserId=>null;public string UserName=>"出库选品回滚夹具";public bool IsAuthenticated=>false;}
    /// <summary>保留真实Npgsql与分类，只装饰发送记录，不替换驱动结果。</summary>
    private sealed class CaptureFactory(string connection):IDbConnectionFactory
    {public IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;public IDbConnection CreateConnection()=>new CaptureConnection(new NpgsqlConnection(connection));}
    /// <summary>截取真实已发送Items命令供计划验收；不加入阶段屏障/生产睡眠，不伪造reader。</summary>
    private sealed class CaptureConnection(NpgsqlConnection inner):DbConnection
    {
        public NpgsqlConnection Inner=>inner;public readonly List<(string Sql,NpgsqlParameter[] Parameters)> Items=[];
        [AllowNull]public override string ConnectionString{get=>inner.ConnectionString;set=>inner.ConnectionString=value;}public override string Database=>inner.Database;public override string DataSource=>inner.DataSource;public override string ServerVersion=>inner.ServerVersion;public override ConnectionState State=>inner.State;
        public override void Open()=>inner.Open();public override void Close()=>inner.Close();public override void ChangeDatabase(string name)=>inner.ChangeDatabase(name);protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>inner.BeginTransaction(level);protected override DbCommand CreateDbCommand()=>new CaptureCommand(this,inner.CreateCommand());protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
        /// <summary>完整转发连接/事务/五参数和取消令牌，记录的参数克隆只用于相同SQL计划命令。</summary>
        private sealed class CaptureCommand(CaptureConnection owner,NpgsqlCommand command):DbCommand
        {
            [AllowNull]public override string CommandText{get=>command.CommandText;set=>command.CommandText=value;}public override int CommandTimeout{get=>command.CommandTimeout;set=>command.CommandTimeout=value;}public override CommandType CommandType{get=>command.CommandType;set=>command.CommandType=value;}public override bool DesignTimeVisible{get=>command.DesignTimeVisible;set=>command.DesignTimeVisible=value;}public override UpdateRowSource UpdatedRowSource{get=>command.UpdatedRowSource;set=>command.UpdatedRowSource=value;}
            protected override DbConnection? DbConnection{get=>owner;set{}}protected override DbTransaction? DbTransaction{get=>command.Transaction;set=>command.Transaction=(NpgsqlTransaction?)value;}protected override DbParameterCollection DbParameterCollection=>command.Parameters;protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
            public override void Cancel()=>command.Cancel();public override void Prepare()=>command.Prepare();public override int ExecuteNonQuery()=>command.ExecuteNonQuery();public override object? ExecuteScalar()=>command.ExecuteScalar();public override Task<int> ExecuteNonQueryAsync(CancellationToken token)=>command.ExecuteNonQueryAsync(token);public override Task<object?> ExecuteScalarAsync(CancellationToken token)=>command.ExecuteScalarAsync(token);
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>command.ExecuteReader(behavior);
            /// <summary>记录本次真实Items SQL与独立参数，返回原驱动reader；token也直接传入原Npgsql。</summary>
            protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token)
            {if(CommandText.StartsWith("WITH candidates AS MATERIALIZED",StringComparison.Ordinal))owner.Items.Add((CommandText,command.Parameters.Select(x=>x.Clone()).ToArray()));return await command.ExecuteReaderAsync(behavior,token);}
            protected override void Dispose(bool disposing){if(disposing)command.Dispose();base.Dispose(disposing);}
        }
    }
}
