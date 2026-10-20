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
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行验收采购进度列表；随机合法夹具在单RC事务finally回滚，本窗口仅编写编译。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlPurchaseOrderProgressSearchMigrationIntegrationTests
{
    /// <summary>无行头仍列出，空和超末页count保持独立于页；本轮不读取Detail。</summary>
    [TestMethod]
    [DataRow("empty")] [DataRow("no-lines")] [DataRow("beyond")] [DataRow("deleted-head")]
    public async Task EmptyHeadsAndPaging(string mode)
    {
        using var f=new Fixture();var h=f.Head();h.IsDeleted=mode=="deleted-head";if(mode!="empty")await f.InsertAsync(h);
        f.Hook.Commands.Clear();var d=await f.Repository.SearchAsync(new(){OrderNo=f.Prefix,PageIndex=mode=="beyond"?99:1,PageSize=1});
        Assert.AreEqual(mode is "empty" or "deleted-head"?0:1,d.TotalCount);Assert.HasCount(mode=="no-lines"?1:0,d.Items);
        Assert.HasCount(1,f.Hook.Commands);if(d.Items.Count>0){var row=d.Items.Single();Assert.AreEqual(0,row.LineCount);}
        await f.Usable();
    }

    /// <summary>反馈有无、未知、零或超量不改变订单选单身份及行数，选单不读取报告。</summary>
    [TestMethod]
    [DataRow("unreported")] [DataRow("null")] [DataRow("zero")] [DataRow("partial")] [DataRow("over")]
    public async Task FeedbackQuantities_DoNotAffectOrderSelection(string mode)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);
        decimal? ready=mode switch{"zero"=>0m,"partial"=>3m,"over"=>17m,_=>null};
        if(mode!="unreported")await f.InsertAsync(Report(l,1,ready));
        f.Hook.Commands.Clear();var row=(await Search(f)).Items.Single();Assert.AreEqual(1,row.LineCount);Assert.AreEqual(h.Uid,row.PurchaseOrderId);Assert.AreEqual(h.BoNo,row.BoNo);
        Assert.IsFalse(f.Hook.Commands.Single().Contains("supplier_progress_report",StringComparison.Ordinal));
    }

    /// <summary>选单行数只取有效采购行；新旧反馈及反馈软删不参与选单。</summary>
    [TestMethod]
    [DataRow("smaller")] [DataRow("null")] [DataRow("deleted-report")] [DataRow("deleted-line")]
    public async Task LineCount_IgnoresFeedbackHistory(string mode)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);l.IsDeleted=mode=="deleted-line";await f.InsertAsync(l);
        var old=Report(l,1,8m);old.ExpectedReadyDate=new DateTime(2026,10,7);await f.InsertAsync(old);
        var next=Report(l,2,mode=="null"?null:2m);next.ExpectedReadyDate=null;next.IsCorrection=true;next.CorrectionReason="供应商修正";next.IsDeleted=mode=="deleted-report";await f.InsertAsync(next);
        var row=(await Search(f)).Items.Single();
        Assert.AreEqual(mode=="deleted-line"?0:1,row.LineCount);Assert.AreEqual(h.Uid,row.PurchaseOrderId);
    }

    /// <summary>跨单位不影响选单行数；不汇总跨单位数量，孤立行不归属有效订单。</summary>
    [TestMethod]
    public async Task MixedUnitsAndOrphan_KeepOrderLineCount()
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var a=f.Line(h);a.Quantity=4;a.UnitSnapshot="PCS";await f.InsertAsync(a);var b=f.Line(h,2);b.Quantity=6;b.UnitSnapshot="KG";await f.InsertAsync(b);
        await f.InsertAsync(Report(a,1,3));await f.InsertAsync(Report(b,1,7));var orphan=f.Line(f.Head());await f.InsertAsync(orphan);
        var row=(await Search(f)).Items.Single();Assert.AreEqual(2,row.LineCount);Assert.AreEqual(h.Uid,row.PurchaseOrderId);
    }

    /// <summary>确认和生命周期状态数组独立限制；筛选不依赖反馈安排状态。</summary>
    [TestMethod]
    public async Task StateSets_UseOrderStatusesOnly()
    {
        using var f=new Fixture();var a=f.Head();var b=f.Head();b.ConfirmationStatus=ConfirmationStatusEnum.已确认;b.LifecycleStatus=PurchaseOrderLifecycleStatusEnum.Closed;
        await f.InsertAsync(a);await f.InsertAsync(b);var la=f.Line(a);var lb=f.Line(b);await f.InsertAsync(la);await f.InsertAsync(lb);var report=Report(lb,1,1);report.ArrangementStatus=SupplierArrangementStatusEnum.Arranged;await f.InsertAsync(report);
        var r=new PurchaseOrderProgressSearchRequest{OrderNo=f.Prefix,PageIndex=1,PageSize=20,ConfirmationStatuses=[ConfirmationStatusEnum.待确认,ConfirmationStatusEnum.已确认,ConfirmationStatusEnum.已确认],LifecycleStatuses=[PurchaseOrderLifecycleStatusEnum.Active,PurchaseOrderLifecycleStatusEnum.Closed]};
        Assert.AreEqual(2,(await f.Repository.SearchAsync(r)).TotalCount);
        r.LifecycleStatuses=[PurchaseOrderLifecycleStatusEnum.Closed];Assert.AreEqual(b.Uid,(await f.Repository.SearchAsync(r)).Items.Single().PurchaseOrderId);
        r.ConfirmationStatuses=[ConfirmationStatusEnum.待确认];Assert.AreEqual(0,(await f.Repository.SearchAsync(r)).TotalCount);
    }

    /// <summary>只按活动及已确认给反馈能力，关闭/作废理由优先于未确认。</summary>
    [TestMethod]
    [DataRow(0,0)] [DataRow(1,0)] [DataRow(0,8)] [DataRow(1,8)] [DataRow(0,9)] [DataRow(1,9)]
    public async Task CanReport_KeepCasePriority(int confirmation,int lifecycle)
    {
        using var f=new Fixture();var h=f.Head();h.ConfirmationStatus=(ConfirmationStatusEnum)confirmation;h.LifecycleStatus=(PurchaseOrderLifecycleStatusEnum)lifecycle;await f.InsertAsync(h);
        var row=(await Search(f)).Items.Single();Assert.AreEqual(confirmation==1&&lifecycle==0,row.CanReportProgress);
        Assert.AreEqual(lifecycle!=0?"仅活动采购单允许登记供应商反馈。":confirmation!=1?"仅已确认采购单允许登记供应商反馈。":"",row.ReadOnlyReason);
    }

    /// <summary>计划交期保留时分秒；下界含等号，上界原输入次日排他。</summary>
    [TestMethod]
    [DataRow(false,-1,false)] [DataRow(false,0,true)] [DataRow(true,86399,true)] [DataRow(true,86400,false)]
    public async Task Dates_WallTimeAndExclusiveNextDay(bool upper,int seconds,bool match)
    {
        using var f=new Fixture();var bound=new DateTime(2026,10,5,13,14,15,DateTimeKind.Unspecified);var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);
        l.PlannedDeliveryDate=bound.AddSeconds(seconds);await f.InsertAsync(l);
        var r=new PurchaseOrderProgressSearchRequest{OrderNo=f.Prefix,PageIndex=1,PageSize=20};
        if(upper)r.PlannedDeliveryDateTo=bound;else r.PlannedDeliveryDateFrom=bound;
        Assert.AreEqual(match?1:0,(await f.Repository.SearchAsync(r)).TotalCount);
    }

    /// <summary>反馈预计日期不再影响选单；保留订单身份及有效行数。</summary>
    [TestMethod]
    public async Task ExpectedFeedbackDates_DoNotAffectSelection()
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var a=f.Line(h);var b=f.Line(h,2);await f.InsertAsync(a);await f.InsertAsync(b);
        var ra=Report(a,1,null);ra.ExpectedReadyDate=new DateTime(2026,10,5);var rb=Report(b,1,null);rb.ExpectedReadyDate=new DateTime(2026,10,8);await f.InsertAsync(ra);await f.InsertAsync(rb);
        f.Hook.Commands.Clear();var row=(await Search(f)).Items.Single();Assert.AreEqual(2,row.LineCount);Assert.AreEqual(h.Uid,row.PurchaseOrderId);
        Assert.IsFalse(f.Hook.Commands.Single().Contains("supplier_progress_report",StringComparison.Ordinal));
    }

    /// <summary>四筛选路径使用字面特殊字符；固定隔离前缀不含本例字符以避免随机自命中。</summary>
    [TestMethod]
    [DataRow("%")] [DataRow("_")] [DataRow("~")] [DataRow("[ab]")] [DataRow("?")] [DataRow("A B")]
    public async Task LiteralFilters_AllCommercialPaths(string literal)
    {
        using var f=new Fixture();var h=f.Head();h.OrderNo=f.Prefix+"LEFT"+literal+"RIGHT";h.BoNo="BO"+literal;h.SupplierCode="SUP"+literal;h.SupplierName="NAME"+literal;await f.InsertAsync(h);
        var l=f.Line(h);l.Sku="SKU"+literal;await f.InsertAsync(l);var other=f.Head();other.OrderNo=f.Prefix+"LEFTaxxbRIGHT";other.BoNo="BOaxxb";other.SupplierName="NAMEaxxb";await f.InsertAsync(other);
        foreach(var field in new[]{"Keyword","OrderNo","SupplierKeyword","Sku"})
        {
            var r=new PurchaseOrderProgressSearchRequest{OrderNo=f.Prefix,PageIndex=1,PageSize=20};
            if(field=="Keyword")r.Keyword=" "+literal+" ";if(field=="OrderNo")r.OrderNo=h.OrderNo;if(field=="SupplierKeyword")r.SupplierKeyword=literal;if(field=="Sku")r.Sku=literal;
            Assert.AreEqual(h.Uid,(await f.Repository.SearchAsync(r)).Items.Single().PurchaseOrderId);
        }
    }

    /// <summary>keyword分别命中BO/采购行SKU/头供应商名，不读取当前供应商主数据替代快照。</summary>
    [TestMethod]
    [DataRow("bo")] [DataRow("sku")] [DataRow("supplier")]
    public async Task Keyword_UsesSourceCommercialFields(string field)
    {
        using var f=new Fixture();var h=f.Head();var term=Guid.NewGuid().ToString("N");if(field=="bo")h.BoNo=term;if(field=="supplier")h.SupplierName=term;await f.InsertAsync(h);
        var l=f.Line(h);if(field=="sku")l.Sku=term;await f.InsertAsync(l);
        Assert.AreEqual(h.Uid,(await f.Repository.SearchAsync(new(){OrderNo=f.Prefix,Keyword=term,PageIndex=1,PageSize=20})).Items.Single().PurchaseOrderId);
    }

    /// <summary>SQL完成冻结后改变头及追加反馈，返回仍保留旧头/count，不再次读商业事实。</summary>
    [TestMethod]
    public async Task FrozenAfterRead_DoesNotRefreshOrderSelection()
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);await f.InsertAsync(Report(l,1,7));
        var oldName=h.SupplierName;bool changed=false;f.Hook.Commands.Clear();
        f.Hook.AfterFacts=async token=>{f.Hook.AfterFacts=null;changed=true;await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.purchase_order_head SET order_no=@Outside,supplier_name='新供应商名',is_deleted=true WHERE uid=@Uid",new{Uid=h.Uid,Outside="OUT"+Guid.NewGuid().ToString("N")},f.Session.Transaction,cancellationToken:token));var correction=Report(l,2,0);correction.IsCorrection=true;correction.CorrectionReason="供应商修正";await f.InsertAsync(correction);};
        var d=await Search(f);Assert.IsTrue(changed);Assert.AreEqual(1,d.TotalCount);Assert.AreEqual(h.OrderNo,d.Items.Single().OrderNo);Assert.AreEqual(oldName,d.Items.Single().SupplierName);Assert.AreEqual(1,d.Items.Single().LineCount);
        Assert.AreEqual(1,f.Hook.Commands.Count(x=>x.Contains("WITH filtered AS",StringComparison.Ordinal)));await f.Usable();
        // 同事务真实SQL冻结后自写，只证明不回查；物理跨连接并发另由主控授权。
    }

    /// <summary>反馈选单不读取无关收货历史；坏入库头不影响有效采购单与计数。</summary>
    [TestMethod]
    public async Task InvalidReceipt_DoesNotBlockFeedbackSearch()
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);
        await f.InsertAsync(new InboundDocument{Uid=Guid.NewGuid(),DocumentNo=f.Prefix+"BAD",DocumentType=InboundDocumentTypeEnum.PurchaseReceipt,PostingStatus=InboundPostingStatusEnum.Posted,ReceiptDate=new DateTime(2026,10,5),PostedDate=new DateTime(2026,10,5),WarehouseSiteId=Guid.NewGuid()});
        f.Hook.Commands.Clear();Assert.AreEqual(h.Uid,(await Search(f)).Items.Single().PurchaseOrderId);Assert.HasCount(1,f.Hook.Commands);Assert.IsFalse(f.Hook.Commands[0].Contains("inbound_document",StringComparison.Ordinal));await f.Usable();
    }

    /// <summary>预取消、反馈命令前和结果缓冲后取消保持外层事务，服务器中途取消另验。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)]
    public async Task Cancellation_Boundaries(int stage)
    {
        using var f=new Fixture();using var c=new CancellationTokenSource();f.Hook.Commands.Clear();
        if(stage<0)c.Cancel();else if(stage==1)f.Hook.AfterFacts=token=>{c.Cancel();return Task.CompletedTask;};else f.Hook.Before=(sql,token)=>{if(f.Hook.Commands.Count==stage)c.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchAsync(new(){OrderNo=f.Prefix,PageIndex=1,PageSize=1},c.Token));
        f.Hook.Before=null;Assert.IsNotNull(f.Session.Transaction);await f.Usable();
    }

    /// <summary>可空事务连续只读同连接，SQL排序原日期/单号；实际UUID尾键对照独立SqlGuid参照。</summary>
    [TestMethod]
    public async Task SourceOrderAndUuidKey()
    {
        using var f=new Fixture();var a=f.Head();var b=f.Head();var c=f.Head();a.OrderNo=f.Prefix+"A";b.OrderNo=f.Prefix+"B";c.OrderDate=a.OrderDate.AddDays(1);
        await f.InsertAsync(a);await f.InsertAsync(b);await f.InsertAsync(c);f.Hook.Commands.Clear();var d=await Search(f);
        CollectionAssert.AreEqual(new[]{c.Uid,b.Uid,a.Uid},d.Items.Select(x=>x.PurchaseOrderId).ToArray());
        var key=Regex.Match(f.Hook.Commands[0],@"ARRAY\[get_byte\(uuid_send\(facts\.""PurchaseOrderId""\).*?\]").Value.Replace("facts.\"PurchaseOrderId\"","v.uid");Assert.IsFalse(string.IsNullOrWhiteSpace(key));
        var ids=new[]{Guid.Parse("ffffffff-ffff-ffff-ffff-000000000001"),Guid.Parse("00000000-0000-0000-0000-000000000002")};
        var actual=(await f.Session.Connection.QueryAsync<Guid>(new CommandDefinition($"SELECT v.uid FROM (VALUES (@A::uuid),(@B::uuid)) v(uid) ORDER BY {key} DESC",new{A=ids[0],B=ids[1]},f.Session.Transaction))).ToArray();
        CollectionAssert.AreEqual(ids.OrderByDescending(x=>new SqlGuid(x)).ToArray(),actual);
        // 有效OrderNo与Line Sequence唯一索引限制合法并列；不绕约束制造排序夹具。
    }

    /// <summary>不写入的无外层事务调用可连续读取，查询不隐式开事务。</summary>
    [TestMethod]
    public async Task NoTransaction_ContinuousRead()
    {
        using var f=new Fixture(false);for(int i=0;i<2;i++)Assert.AreEqual(0,(await Search(f)).TotalCount);Assert.IsNull(f.Session.Transaction);await f.Usable();
    }

    /// <summary>随机隔离采购单号的合法分页请求。</summary>
    private static Task<SwiftExport.Contracts.Wrappers.PagedResponseBase<PurchaseOrderProgressListItemDto>> Search(Fixture f)=>f.Repository.SearchAsync(new(){OrderNo=f.Prefix,PageIndex=1,PageSize=20});
    /// <summary>追加式整行反馈，保留NULL或明确零，不覆盖旧报告。</summary>
    private static SupplierProgressReport Report(PurchaseOrderLine line,int sequence,decimal? ready)=>new(){Uid=Guid.NewGuid(),PurchaseOrderLineId=line.Uid,Sequence=sequence,ReadyQty=ready,ArrangementStatus=SupplierArrangementStatusEnum.Unknown,CommandId=Guid.NewGuid(),SupplierReportedDate=new DateTime(2026,10,5,12,0,0,DateTimeKind.Unspecified)};

    /// <summary>独立随机身份与事务；原始元数据插入不得通过审计Add重置指定UID。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly string Prefix="PQ"+Guid.NewGuid().ToString("N")+"X";
        public readonly Guid CustomerId=Guid.NewGuid();
        public readonly DbSession Session;
        public readonly PostgreSqlPurchaseOrderProgressQueryRepository Repository;
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
        public Guid? UserId=>null;public string UserName=>"采购进度列表回滚夹具";public bool IsAuthenticated=>false;
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
        public Func<CancellationToken,Task>? AfterFacts;
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
                if(owner.AfterFacts is {} after && CommandText.Contains("WITH filtered AS",StringComparison.Ordinal))
                {
                    // 真实PG完整结果先缓冲并关闭reader，再修改同事务夹具；不伪造商业返回或注入生产延迟。
                    var table=new DataTable();using(reader)table.Load(reader);await after(token);return table.CreateDataReader();
                }
                return reader;
            }
            protected override void Dispose(bool disposing){if(disposing)innerCommand.Dispose();base.Dispose(disposing);}
        }
    }
}

