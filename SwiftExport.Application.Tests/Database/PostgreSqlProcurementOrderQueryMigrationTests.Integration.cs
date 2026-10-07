using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Data.SqlTypes;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行执行采购基础查询真实PG验收；合法随机夹具仅在单事务内自写并finally回滚。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlProcurementOrderQueryMigrationIntegrationTests
{
    /// <summary>合法确认和生命周期组合保持按钮及阻断理由优先级，不插入违反CHECK的未知状态。</summary>
    [TestMethod]
    [DataRow(0,0)] [DataRow(1,0)] [DataRow(0,8)] [DataRow(1,8)] [DataRow(0,9)] [DataRow(1,9)]
    public async Task Detail_StateCases(int confirmation,int lifecycle)
    {
        using var f=new Fixture();var h=f.Head();h.ConfirmationStatus=(ConfirmationStatusEnum)confirmation;h.LifecycleStatus=(PurchaseOrderLifecycleStatusEnum)lifecycle;await f.InsertAsync(h);
        var d=await f.Repository.GetDetailAsync(h.Uid,default);
        Assert.IsNotNull(d);Assert.AreEqual(confirmation==0&&lifecycle==0,d.CanEditDraft);Assert.AreEqual(d.CanEditDraft,d.CanConfirm);
        Assert.AreEqual(confirmation==1&&lifecycle==0,d.CanCancelConfirmation);Assert.AreEqual(lifecycle==0?"":"采购单不是活动状态。",d.CommandBlockedReason);
        Assert.AreEqual(0,d.LineCount);Assert.AreEqual(0m,d.TotalQuantity);Assert.AreEqual(0m,d.CnyTotalAmount);Assert.HasCount(0,d.Lines);
    }

    /// <summary>空头仍读第二阶段，软删头与行分别按源排除；空集合汇总保持零。</summary>
    [TestMethod]
    [DataRow("missing")] [DataRow("deleted-head")] [DataRow("deleted-line")] [DataRow("empty")]
    public async Task Detail_EmptyAndDeleted(string mode)
    {
        using var f=new Fixture();var h=f.Head();if(mode=="deleted-head")h.IsDeleted=true;
        if(mode!="missing")await f.InsertAsync(h);
        if(mode is "deleted-head" or "deleted-line"){var l=f.Line(h);l.IsDeleted=mode=="deleted-line";await f.InsertAsync(l);}
        f.Hook.Commands.Clear();var d=await f.Repository.GetDetailAsync(h.Uid,default);Assert.HasCount(2,f.Hook.Commands);
        if(mode is "missing" or "deleted-head")Assert.IsNull(d);else{Assert.IsNotNull(d);Assert.AreEqual(0,d.LineCount);Assert.AreEqual(0m,d.TotalQuantity);Assert.HasCount(0,d.Lines);}
        await f.Usable();
    }

    /// <summary>商业NULL、墙上时间和旧字段别名保持；金额沿未税快照，不按税额、币种或折扣重算。</summary>
    [TestMethod]
    public async Task Detail_CommercialSnapshotsAndTotals()
    {
        using var f=new Fixture();var h=f.Head();h.Currency="USD";h.DiscountRate=null;h.DiscountAmount=null;h.InletrHoseType="进水管";h.IMType="说明书";h.SoPerson="销售";h.PoPerson="采购";
        h.ExpectedDate=new DateTime(2026,10,6,13,14,15,DateTimeKind.Unspecified);h.CustomerNameSnapshot=null;h.SupplierName=null;await f.InsertAsync(h);
        var a=f.Line(h);a.Quantity=3m;a.UnitAmount=7m;a.TaxAmount=999m;a.UnitPrice=null;await f.InsertAsync(a);
        var b=f.Line(h,2);b.Quantity=2m;b.UnitAmount=null;b.TaxAmount=777m;await f.InsertAsync(b);
        var removed=f.Line(h,3);removed.IsDeleted=true;removed.Quantity=100m;removed.UnitAmount=1000m;await f.InsertAsync(removed);
        var d=await f.Repository.GetDetailAsync(h.Uid,default);Assert.IsNotNull(d);Assert.AreEqual(2,d.LineCount);Assert.AreEqual(5m,d.TotalQuantity);Assert.AreEqual(7m,d.CnyTotalAmount);
        Assert.IsNull(d.DiscountRate);Assert.IsNull(d.DiscountAmount);Assert.IsNull(d.CustomerName);Assert.IsNull(d.SupplierName);
        Assert.AreEqual("进水管",d.InletHoseType);Assert.AreEqual("说明书",d.InstructionManualType);Assert.AreEqual("销售",d.SalesPerson);Assert.AreEqual("采购",d.PurchasePerson);
        Assert.AreEqual(h.ExpectedDate,d.ExpectedDate);Assert.AreEqual(DateTimeKind.Unspecified,d.ExpectedDate!.Value.Kind);Assert.IsNull(d.Lines[0].UnitPrice);Assert.IsNull(d.Lines[1].UnitAmount);
    }

    /// <summary>标准性仅按ProductId，产品软删或缺失只影响当前图像；不以快照标志替代。</summary>
    [TestMethod]
    [DataRow("standard")] [DataRow("nonstandard")] [DataRow("deleted-product")] [DataRow("missing-product")]
    public async Task Detail_CurrentProductAndStandardness(string mode)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);
        if(mode!="nonstandard"){var product=f.Product();l.ProductId=product.Uid;if(mode=="deleted-product")product.IsDeleted=true;if(mode!="missing-product")await f.InsertAsync(product);}
        await f.InsertAsync(l);var d=await f.Repository.GetDetailAsync(h.Uid,default);var line=d!.Lines.Single();
        Assert.AreEqual(mode=="nonstandard",line.IsNonStandard);Assert.AreEqual(mode=="standard"?"当前图像":null,line.MainImage);
        Assert.AreEqual(l.ProductId??Guid.Empty,line.ProductId);
    }

    /// <summary>客供件保留NULL配送快照；其他类型返回空串和空Guid，尾空格沿源比较。</summary>
    [TestMethod]
    [DataRow("客供件",false)] [DataRow("客供件 ",true)] [DataRow("零件",false)] [DataRow(null,false)]
    public async Task Detail_DeliverySupplier(string? material,bool snapshots)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);var l=f.Line(h);l.MaterialType=material;l.DeliveryTargetSupplierId=Guid.NewGuid();
        l.DeliverySupplierCodeSnapshot=snapshots?"送达编码":null;l.DeliverySupplierNameSnapshot=snapshots?"送达快照":null;await f.InsertAsync(l);
        var line=(await f.Repository.GetDetailAsync(h.Uid,default))!.Lines.Single();bool customer=material?.TrimEnd(' ')=="客供件";
        Assert.AreEqual(customer?l.DeliveryTargetSupplierId.Value:Guid.Empty,line.DeliverySupplierId);
        Assert.AreEqual(customer?l.DeliverySupplierCodeSnapshot:"",line.DeliverySupplierCode);Assert.AreEqual(customer?l.DeliverySupplierNameSnapshot:"",line.DeliverySupplierName);
    }

    /// <summary>字面特殊字符、内部空格与问号不成为通配；外部空白清理且空白不筛选。</summary>
    [TestMethod]
    [DataRow("%")] [DataRow("_")] [DataRow("~")] [DataRow("[ab]")] [DataRow("?")] [DataRow("A B")] [DataRow("   ")]
    public async Task Search_LiteralKeyword(string literal)
    {
        // 独立身份前缀不能含本例搜索的字面下划线，否则源LIKE也会命中两个头，无法验明~_转义。
        using var f=new Fixture();var cleanPrefix=f.Prefix.Replace("_","");var h=f.Head();h.OrderNo=cleanPrefix+"LEFT"+literal+"RIGHT";await f.InsertAsync(h);
        var other=f.Head();other.OrderNo=cleanPrefix+"LEFTaxxbRIGHT";await f.InsertAsync(other);
        var request=new ProcurementOrderSearchRequest{CustomerId=f.CustomerId,Keyword=" "+literal+" ",PageIndex=1,PageSize=20};
        var d=await f.Repository.SearchAsync(request,default);Assert.AreEqual(string.IsNullOrWhiteSpace(literal)?2:1,d.TotalCount);
        if(!string.IsNullOrWhiteSpace(literal))Assert.AreEqual(h.Uid,d.Items.Single().PurchaseOrderId);
    }

    /// <summary>可空筛选、Guid.Empty和超末页保持源含义；列表采用头快照且不默认过滤关闭状态。</summary>
    [TestMethod]
    public async Task Search_FiltersSnapshotsAndBeyondPage()
    {
        using var f=new Fixture();var h=f.Head();h.LifecycleStatus=(PurchaseOrderLifecycleStatusEnum)8;await f.InsertAsync(h);
        var r=new ProcurementOrderSearchRequest{Keyword=f.Prefix,CustomerId=h.CustomerId,SupplierId=h.SupplierId,ConfirmationStatus=h.ConfirmationStatus,PageIndex=1,PageSize=1};
        var d=await f.Repository.SearchAsync(r,default);Assert.AreEqual(1,d.TotalCount);Assert.AreEqual("客户旧快照",d.Items.Single().CustomerName);Assert.AreEqual("供应商旧快照",d.Items.Single().SupplierName);
        r.PageIndex=99;d=await f.Repository.SearchAsync(r,default);Assert.AreEqual(1,d.TotalCount);Assert.HasCount(0,d.Items);
        r.PageIndex=1;r.CustomerId=Guid.Empty;Assert.AreEqual(0,(await f.Repository.SearchAsync(r,default)).TotalCount);
        r.CustomerId=null;r.SupplierId=Guid.Empty;Assert.AreEqual(0,(await f.Repository.SearchAsync(r,default)).TotalCount);
        r.SupplierId=null;r.ConfirmationStatus=null;Assert.AreEqual(1,(await f.Repository.SearchAsync(r,default)).TotalCount);
        h=f.Head();h.IsDeleted=true;await f.InsertAsync(h);Assert.AreEqual(1,(await f.Repository.SearchAsync(r,default)).TotalCount);
    }

    /// <summary>日期、字典单号依次排序；抽取实际入口生成的UUID尾键用独立SqlGuid参照验证驱动执行。</summary>
    [TestMethod]
    public async Task Search_OrderingAndActualUuidTail()
    {
        using var f=new Fixture();var a=f.Head();var b=f.Head();var c=f.Head();a.OrderNo=f.Prefix+"A";b.OrderNo=f.Prefix+"B";c.OrderDate=a.OrderDate.AddDays(1);
        await f.InsertAsync(a);await f.InsertAsync(b);await f.InsertAsync(c);f.Hook.Commands.Clear();
        var d=await f.Repository.SearchAsync(new(){Keyword=f.Prefix,PageIndex=1,PageSize=20},default);
        CollectionAssert.AreEqual(new[]{c.Uid,b.Uid,a.Uid},d.Items.Select(x=>x.PurchaseOrderId).ToArray());
        var key=Regex.Match(f.Hook.Commands[1],@"ARRAY\[get_byte\(uuid_send\(h\.""uid""\).*?\]").Value;
        Assert.IsFalse(string.IsNullOrWhiteSpace(key));key=key.Replace("h.\"uid\"","v.uid");
        var ids=new[]{Guid.Parse("ffffffff-ffff-ffff-ffff-000000000001"),Guid.Parse("00000000-0000-0000-0000-000000000002")};
        var actual=(await f.Session.Connection.QueryAsync<Guid>(new CommandDefinition($"SELECT v.uid FROM (VALUES (@A::uuid),(@B::uuid)) v(uid) ORDER BY {key} DESC",new{A=ids[0],B=ids[1]},f.Session.Transaction))).ToArray();
        CollectionAssert.AreEqual(ids.OrderByDescending(x=>new SqlGuid(x)).ToArray(),actual);
        // 单号唯一索引阻止有效头的单号并列；不绕索引构造不合法尾键夹具。
    }

    /// <summary>count后删除或改出筛选保留旧count和当前空页，两命令不合并观察时刻。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Search_StageChange(bool deleted)
    {
        using var f=new Fixture();var h=f.Head();await f.InsertAsync(h);bool changed=false;
        f.Hook.Before=async(sql,token)=>{if(!sql.Contains("OFFSET @Skip"))return;f.Hook.Before=null;changed=true;
            await f.Session.Connection.ExecuteAsync(new CommandDefinition(deleted?"UPDATE public.purchase_order_head SET is_deleted=true WHERE uid=@Uid":"UPDATE public.purchase_order_head SET order_no=@Outside WHERE uid=@Uid",new{Uid=h.Uid,Outside="OUTSIDE"+Guid.NewGuid().ToString("N")},f.Session.Transaction,cancellationToken:token));};
        var d=await f.Repository.SearchAsync(new(){Keyword=f.Prefix,PageIndex=1,PageSize=20},default);
        Assert.IsTrue(changed);Assert.AreEqual(1,d.TotalCount);Assert.HasCount(0,d.Items);await f.Usable();
    }

    /// <summary>头汇总冻结而行和图像读当前值；阶段删除头则旧头保留、行空，不刷新先读出的数量。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Detail_StageChange(bool deleted)
    {
        using var f=new Fixture();var h=f.Head();var p=f.Product();await f.InsertAsync(h);await f.InsertAsync(p);var l=f.Line(h);l.ProductId=p.Uid;l.UnitAmount=3m;await f.InsertAsync(l);bool changed=false;
        f.Hook.Before=async(sql,token)=>{if(!sql.Contains(" AS \"IsNonStandard\""))return;f.Hook.Before=null;changed=true;
            if(deleted)await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.purchase_order_head SET is_deleted=true WHERE uid=@Uid",new{Uid=h.Uid},f.Session.Transaction,cancellationToken:token));
            else {await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.purchase_order_line SET quantity=17 WHERE uid=@Uid",new{Uid=l.Uid},f.Session.Transaction,cancellationToken:token));
                await f.Session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.product SET main_image='更新图像' WHERE uid=@Uid",new{Uid=p.Uid},f.Session.Transaction,cancellationToken:token));}};
        var d=await f.Repository.GetDetailAsync(h.Uid,default);Assert.IsTrue(changed);Assert.IsNotNull(d);Assert.AreEqual(10m,d.TotalQuantity);Assert.AreEqual(3m,d.CnyTotalAmount);Assert.AreEqual("客户旧快照",d.CustomerName);
        if(deleted)Assert.HasCount(0,d.Lines);else{Assert.AreEqual(17m,d.Lines.Single().Quantity);Assert.AreEqual("更新图像",d.Lines.Single().MainImage);}await f.Usable();
    }

    /// <summary>第一阶段缺头后第二阶段可见新增合法头行，仍返回原null并完整执行行命令。</summary>
    [TestMethod]
    public async Task Detail_MissingThenCreated()
    {
        using var f=new Fixture();var h=f.Head();bool added=false;
        f.Hook.Before=async(sql,token)=>{if(!sql.Contains(" AS \"IsNonStandard\""))return;f.Hook.Before=null;added=true;await f.InsertAsync(h);await f.InsertAsync(f.Line(h));};
        var d=await f.Repository.GetDetailAsync(h.Uid,default);Assert.IsTrue(added);Assert.IsNull(d);await f.Usable();
    }

    /// <summary>预取消和两个命令边界取消透传，不接管事务；真实服务器中途取消另由主控安排。</summary>
    [TestMethod]
    [DataRow(false,-1)] [DataRow(false,0)] [DataRow(false,1)] [DataRow(true,-1)] [DataRow(true,0)] [DataRow(true,1)]
    public async Task Cancellation_AtBoundary(bool detail,int stage)
    {
        using var f=new Fixture();f.Hook.Commands.Clear();using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else f.Hook.Before=(sql,token)=>{if(f.Hook.Commands.Count==stage)c.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>{if(detail)await f.Repository.GetDetailAsync(Guid.NewGuid(),c.Token);else await f.Repository.SearchAsync(new(){Keyword=f.Prefix,PageIndex=1,PageSize=1},c.Token);});
        f.Hook.Before=null;Assert.IsNotNull(f.Session.Transaction);await f.Usable();
    }

    /// <summary>无外层事务的连续只读入口借用连接，不隐式开事务，也不写入夹具。</summary>
    [TestMethod]
    public async Task NoOuterTransaction_ContinuousRead()
    {
        using var f=new Fixture(false);for(int i=0;i<2;i++){Assert.IsNull(await f.Repository.GetDetailAsync(Guid.NewGuid(),default));Assert.AreEqual(0,(await f.Repository.SearchAsync(new(){Keyword=f.Prefix,PageIndex=1,PageSize=1},default)).TotalCount);}
        Assert.IsNull(f.Session.Transaction);await f.Usable();
    }

    /// <summary>独立随机身份与事务；原始元数据插入不得通过审计Add重置指定UID。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly string Prefix="M18Q_"+Guid.NewGuid().ToString("N")+"_";
        public readonly Guid CustomerId=Guid.NewGuid();
        public readonly DbSession Session;
        public readonly PostgreSqlProcurementOrderQueryRepository Repository;
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
        /// <summary>产品仅用于当前图像读取，明确必填供货方式和独立SKU。</summary>
        public Product Product()=>new(){Uid=Guid.NewGuid(),Sku=Prefix+Guid.NewGuid().ToString("N"),SupplyType="采购",MaterialType="零件",Unit="PCS",MainImage="当前图像"};
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
        public Guid? UserId=>null;public string UserName=>"采购基础查询回滚夹具";public bool IsAuthenticated=>false;
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

