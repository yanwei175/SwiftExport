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
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行真实PG详情验收；保留Npgsql完整三语句批次与reader推进，不改生产SQL或提前拆查询。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlPurchaseOrderProgressDetailMigrationIntegrationTests
{
    /// <summary>真实Npgsql空头推进后Dispose，未读结果释放且连接/外层事务可继续使用。</summary>
    [TestMethod]
    [DataRow("missing")] [DataRow("deleted-head")] [DataRow("empty-lines")]
    public async Task HeaderAndEmptyRelease(string mode)
    {
        using var f=new Fixture();var h=f.Head();h.IsDeleted=mode=="deleted-head";if(mode!="missing")await f.InsertAsync(h);
        if(mode=="deleted-head"){var l=f.Line(h);await f.InsertAsync(l);await f.InsertAsync(Report(l,1,null));}
        f.Hook.Commands.Clear();var d=await f.Repository.GetDetailAsync(h.Uid);Assert.HasCount(2,f.Hook.Commands);
        if(mode=="empty-lines"){Assert.IsNotNull(d);Assert.HasCount(0,d.Lines);Assert.AreEqual(3,f.Hook.NextResults);}
        else{Assert.IsNull(d);Assert.AreEqual(1,f.Hook.NextResults);Assert.AreEqual(0,f.Hook.ReadCounts[1]);Assert.AreEqual(0,f.Hook.ReadCounts[2]);}
        Assert.AreEqual(1,f.Hook.Disposals);Assert.IsTrue(f.Hook.ReaderClosed);await f.Usable();
    }

    /// <summary>当前未删图像按源COALESCE空串；标准/非标不增加其他准入，商业NULL保留。</summary>
    [TestMethod]
    [DataRow("current")] [DataRow("null-image")] [DataRow("deleted-product")] [DataRow("missing-product")] [DataRow("nonstandard")]
    public async Task ImageAndNullableCommercialFields(string mode)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);
        if(mode!="nonstandard"){var p=Product(f);l.ProductId=p.Uid;if(mode=="null-image")p.MainImage=null;if(mode=="deleted-product")p.IsDeleted=true;if(mode!="missing-product")await f.InsertAsync(p);}
        l.SupplierSku=null;l.DescriptionZh=null;l.PlannedDeliveryDate=null;await f.InsertAsync(l);
        var line=(await f.Repository.GetDetailAsync(h.Uid))!.Lines.Single();Assert.AreEqual(mode=="current"?"当前图像":"",line.MainImage);
        Assert.IsNull(line.SupplierSku);Assert.IsNull(line.DescriptionZh);Assert.IsNull(line.PlannedDeliveryDate);Assert.AreEqual(10m,line.Quantity);Assert.AreEqual(0m,line.ReceivedQty);Assert.IsNull(line.LatestProgress);Assert.HasCount(0,line.ProgressHistory);
    }

    /// <summary>正式采购Posted贡献量，Reversed贡献零，草稿/其他类型/软删头排除；不依赖Link。</summary>
    [TestMethod]
    [DataRow("posted",3)] [DataRow("reversed",0)] [DataRow("draft",0)] [DataRow("other-type",0)] [DataRow("deleted-head",0)] [DataRow("deleted-line",0)]
    public async Task ReceiptStates_UsePurchaseLineIdentity(string mode,int expected)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);
        var receipt=await Receipt(f,l.Uid,3m,mode=="reversed"?8:mode=="draft"?0:1,mode=="other-type"?11:10,mode=="deleted-head",mode=="deleted-line");
        var d=await f.Repository.GetDetailAsync(h.Uid);Assert.AreEqual((decimal)expected,d!.Lines.Single().ReceivedQty);await f.Usable();
    }

    /// <summary>多个Link共享同采购行，收货仍单次按SourceLineId汇总，且无单位遮罩。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(2)]
    public async Task SharedLinks_DoNotMultiplyReceipt(int links)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);await f.InsertAsync(l);await Receipt(f,l.Uid,4m,1,10);
        for(int i=0;i<links;i++)
        {
            var gid=Guid.NewGuid();var g=new GrossDemand{Uid=gid,SourceType="MD",SourceUid=gid,SourceLineId=gid,SourceNo=f.Prefix+"G"+i,SourceVersion=1,SourceLineNumber=1,CustomerId=h.CustomerId,IsNonStandard=true,SkuSnapshot="非标",UnitSnapshot="PCS",DemandQty=5m,DemandBasisFingerprint=new string('A',64)};
            await f.InsertAsync(g);var nid=Guid.NewGuid();await f.InsertAsync(new NetDemand{Uid=nid,GrossDemandId=gid,RootNetDemandId=nid,LineNumber=1,DecompositionRevision=1,SourceDemandVersionSnapshot=1,SourceVersionSnapshot=1,SourceDemandBasisFingerprintSnapshot=g.DemandBasisFingerprint,SupplyTypeSnapshot="采购",SkuSnapshot="非标",UnitSnapshot="PCS",GrossQty=5m,NetQty=5m});
            await f.InsertAsync(new DemandExecutionLink{Uid=Guid.NewGuid(),NetDemandId=nid,ExecutionRequestId=Guid.NewGuid(),CommandItemId=Guid.NewGuid(),TargetType="PO",TargetHeadId=h.Uid,TargetLineId=l.Uid,TargetNo=h.OrderNo,AllocatedQty=5m,ReleasedQty=0m});
        }
        var d=await f.Repository.GetDetailAsync(h.Uid);Assert.AreEqual(4m,d!.Lines.Single().ReceivedQty);
    }

    /// <summary>旧行/旧反馈软删按源排除，完整未删历史保留最新NULL及纠错快照，不补旧值。</summary>
    [TestMethod]
    [DataRow("history")] [DataRow("deleted-report")] [DataRow("deleted-line")]
    public async Task HistoryAndSoftDeletion(string mode)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);l.IsDeleted=mode=="deleted-line";await f.InsertAsync(l);
        var a=Report(l,1,7m);a.Note="旧备注";await f.InsertAsync(a);var b=Report(l,2,null);b.ExpectedReadyDate=new DateTime(2026,10,6,15,14,13);b.Note="新日期";b.IsDeleted=mode=="deleted-report";await f.InsertAsync(b);
        var d=await f.Repository.GetDetailAsync(h.Uid);Assert.IsNotNull(d);
        if(mode=="deleted-line")Assert.HasCount(0,d.Lines);
        else{var line=d.Lines.Single();Assert.HasCount(mode=="deleted-report"?1:2,line.ProgressHistory);Assert.AreEqual(mode=="deleted-report"?a.Uid:b.Uid,line.LatestProgress.Uid);
            Assert.AreEqual(mode=="deleted-report"?7m:null,line.LatestProgress.ReadyQty);Assert.AreEqual(mode=="deleted-report"?"旧备注":"新日期",line.LatestProgress.Note);}
    }

    /// <summary>行按SQL行号排序、反馈按完整Sequence组装，源UUID表达式仍在真实PG执行且以独立SqlGuid比对。</summary>
    [TestMethod]
    public async Task OrderingAndSourceUuidExpression()
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var b=f.Line(h,2);var a=f.Line(h,1);await f.InsertAsync(b);await f.InsertAsync(a);
        var old=Report(a,1,7);var latest=Report(a,2,3);latest.IsCorrection=true;latest.CorrectionReason="反馈修正";await f.InsertAsync(latest);await f.InsertAsync(old);f.Hook.Commands.Clear();
        var d=await f.Repository.GetDetailAsync(h.Uid);CollectionAssert.AreEqual(new[]{a.Uid,b.Uid},d!.Lines.Select(x=>x.PurchaseOrderLineId).ToArray());
        CollectionAssert.AreEqual(new[]{latest.Uid,old.Uid},d.Lines[0].ProgressHistory.Select(x=>x.Uid).ToArray());Assert.AreEqual(3m,d.Lines[0].LatestProgress.ReadyQty);
        var key=Regex.Match(f.Hook.Commands[1],@"ARRAY\[get_byte\(uuid_send\(l\.""uid""\).*?\]").Value.Replace("l.\"uid\"","v.uid");Assert.IsFalse(string.IsNullOrWhiteSpace(key));
        var ids=new[]{Guid.Parse("ffffffff-ffff-ffff-ffff-000000000001"),Guid.Parse("00000000-0000-0000-0000-000000000002")};
        var actual=(await f.Session.Connection.QueryAsync<Guid>(new CommandDefinition($"SELECT v.uid FROM (VALUES (@A::uuid),(@B::uuid)) v(uid) ORDER BY {key} ASC",new{A=ids[0],B=ids[1]},f.Session.Transaction))).ToArray();
        CollectionAssert.AreEqual(ids.OrderBy(x=>new SqlGuid(x)).ToArray(),actual);
        // 反馈Sequence和有效行号唯一约束不允许合法同序号/同行号并列，.NET Guid并列仅离线验证。
    }

    /// <summary>Guard完成到实际详情批次开始之间修改图像/行/反馈；三SQL按当前表条件读取，不冻结Guard时身份。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ChangeBeforeBatch_UsesCurrentTables(bool deleteHead)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var p=Product(f);await f.InsertAsync(p);var l=f.Line(h);l.ProductId=p.Uid;await f.InsertAsync(l);await f.InsertAsync(Report(l,1,7m));bool changed=false;
        f.Hook.Before=async(sql,token)=>{if(!sql.Contains("WITH ReceiptFacts AS",StringComparison.Ordinal))return;f.Hook.Before=null;changed=true;
            if(deleteHead)await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.purchase_order_head SET is_deleted=true WHERE uid=@Uid",new{Uid=h.Uid},f.Session.Transaction,cancellationToken:token));
            else{await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.purchase_order_line SET quantity=17 WHERE uid=@Uid",new{Uid=l.Uid},f.Session.Transaction,cancellationToken:token));await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.product SET main_image='更新图像' WHERE uid=@Uid",new{Uid=p.Uid},f.Session.Transaction,cancellationToken:token));
                var latest=Report(l,2,0m);latest.IsCorrection=true;latest.CorrectionReason="供应商修正";await f.InsertAsync(latest);}};
        var d=await f.Repository.GetDetailAsync(h.Uid);Assert.IsTrue(changed);
        if(deleteHead)Assert.IsNull(d);else{Assert.AreEqual(17m,d!.Lines.Single().Quantity);Assert.AreEqual("更新图像",d.Lines.Single().MainImage);Assert.AreEqual(0m,d.Lines.Single().LatestProgress.ReadyQty);}
        await f.Usable();
        // 这是Guard→整批前真实阶段自写，不冒称三语句服务器执行中间的跨连接修改。
    }

    /// <summary>预取消及真实Npgsql命令/结果推进边界取消；不改SQL/拆批，读者释放和外层回滚由原边界处理。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public async Task Cancellation_RealReaderBoundaries(int stage)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);await f.InsertAsync(f.Line(h));f.Hook.Commands.Clear();using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else if(stage<2)f.Hook.Before=(sql,token)=>{if(f.Hook.Commands.Count==stage)c.Cancel();return Task.CompletedTask;};
        else f.Hook.ResultAction=i=>{if(i==stage-2)c.Cancel();};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetDetailAsync(h.Uid,c.Token));
        f.Hook.Before=null;f.Hook.ResultAction=null;Assert.IsNotNull(f.Session.Transaction);
        // 取消可能使真实事务aborted；先回滚释放本例事务，再验证同连接可继续读取，不把事务中SELECT1强加给驱动。
        f.Session.Transaction!.Rollback();f.Session.Transaction.Dispose();f.Session.Transaction=null;await f.Usable();
    }

    /// <summary>全局无关坏正式采购收货历史先拒绝；缺头请求也不绕过公共门禁。</summary>
    [TestMethod]
    public async Task GlobalGuard_RejectsEvenMissingHeader()
    {
        using var f=new Fixture();await f.InsertAsync(new InboundDocument{Uid=Guid.NewGuid(),DocumentNo=f.Prefix+"BAD",DocumentType=InboundDocumentTypeEnum.PurchaseReceipt,PostingStatus=InboundPostingStatusEnum.Posted,ReceiptDate=new DateTime(2026,10,5),PostedDate=new DateTime(2026,10,5),WarehouseSiteId=Guid.NewGuid()});
        f.Hook.Commands.Clear();await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetDetailAsync(Guid.NewGuid()));Assert.HasCount(1,f.Hook.Commands);await f.Usable();
    }

    /// <summary>无外层事务真实空头批次正常释放，连续读取同连接，不隐式创建事务。</summary>
    [TestMethod]
    public async Task NoTransaction_EmptyContinuous()
    {
        using var f=new Fixture(false);for(int i=0;i<2;i++)Assert.IsNull(await f.Repository.GetDetailAsync(Guid.NewGuid()));Assert.IsNull(f.Session.Transaction);await f.Usable();
    }

    /// <summary>当前产品供图像读取，明确必填供货方式和独立SKU。</summary>
    private static Product Product(Fixture f)=>new(){Uid=Guid.NewGuid(),Sku=f.Prefix+Guid.NewGuid().ToString("N"),SupplyType="采购",MaterialType="零件",Unit="PCS",MainImage="当前图像"};
    /// <summary>追加完整反馈快照，NULL保持；下降量由调用用例明确提供纠错事实。</summary>
    private static SupplierProgressReport Report(PurchaseOrderLine line,int sequence,decimal? ready)=>new(){Uid=Guid.NewGuid(),PurchaseOrderLineId=line.Uid,Sequence=sequence,ReadyQty=ready,CommandId=Guid.NewGuid(),SupplierReportedDate=new DateTime(2026,10,5,12,0,0,DateTimeKind.Unspecified)};

    /// <summary>生成合法正式或草稿收货；反向头具备完整两条匹配流水，不绕全局Guard。</summary>
    private static async Task<(InboundDocument Head,InboundDocumentLine Line)> Receipt(Fixture f,Guid source,decimal quantity,int status,int type,bool deletedHead=false,bool deletedLine=false)
    {
        var h=new InboundDocument{Uid=Guid.NewGuid(),DocumentNo=f.Prefix+Guid.NewGuid().ToString("N"),DocumentType=(InboundDocumentTypeEnum)type,PostingStatus=(InboundPostingStatusEnum)status,ReceiptDate=new DateTime(2026,10,5),PostedDate=status==0?null:new DateTime(2026,10,5),WarehouseSiteId=Guid.NewGuid(),IsDeleted=deletedHead};
        if(status==8){h.ReversedDate=new DateTime(2026,10,6);h.ReverseReason="测试安全反向";}
        await f.InsertAsync(h);
        var l=new InboundDocumentLine{Uid=Guid.NewGuid(),InboundDocumentId=h.Uid,LineNumber=1,SourceLineId=source,ProductId=Guid.NewGuid(),SkuSnapshot="收货SKU",UnitSnapshot="KG",Quantity=quantity,IsDeleted=deletedLine};
        await f.InsertAsync(l);
        if(deletedLine&&!deletedHead)
        {
            // 有效正式头必须至少一有效行；另加无关合法正量行，避免无行头被Guard合法拒绝。
            await f.InsertAsync(new InboundDocumentLine{Uid=Guid.NewGuid(),InboundDocumentId=h.Uid,LineNumber=2,SourceLineId=Guid.NewGuid(),ProductId=Guid.NewGuid(),SkuSnapshot="保留行",UnitSnapshot="KG",Quantity=1m});
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
            var uid=entity.Uid;var map=PostgreSqlEntityMetadata.GetMetadata(typeof(T));
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
        public Guid? UserId=>null;public string UserName=>"采购进度详情回滚夹具";public bool IsAuthenticated=>false;
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
        public int[] ReadCounts=new int[3];
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
                return CommandText.Contains("WITH ReceiptFacts AS",StringComparison.Ordinal)?new BatchReader(owner,reader):reader;
            }
            protected override void Dispose(bool disposing){if(disposing)innerCommand.Dispose();base.Dispose(disposing);}
        }
        /// <summary>转发全部列类型，只观察各结果Read/NextResult/Dispose；不自动排空未读取的结果。</summary>
        private sealed class BatchReader(HookConnection owner,DbDataReader inner):DbDataReader
        {
            private int phase;private bool disposed;
            public override bool Read(){owner.ResultAction?.Invoke(phase);owner.ReadCounts[phase]++;return inner.Read();}
            public override bool NextResult(){owner.NextResults++;if(phase<2)owner.ResultAction?.Invoke(phase+1);var result=inner.NextResult();if(result)phase++;return result;}
            /// <summary>观察当前结果读取边界并将取消令牌传递给真实驱动，不替代数据库返回。</summary>
            public override async Task<bool> ReadAsync(CancellationToken token){owner.LastToken=token;owner.ResultAction?.Invoke(phase);token.ThrowIfCancellationRequested();owner.ReadCounts[phase]++;return await inner.ReadAsync(token);}
            /// <summary>观察结果切换边界并使用真实异步驱动推进；取消保持技术异常。</summary>
            public override async Task<bool> NextResultAsync(CancellationToken token){owner.LastToken=token;owner.NextResults++;if(phase<2)owner.ResultAction?.Invoke(phase+1);token.ThrowIfCancellationRequested();var result=await inner.NextResultAsync(token);if(result)phase++;return result;}
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
