using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.QualityControl;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.QualityControl;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>在唯一命名LocalDB隔离库验证售后真实Dapper、来源接收、状态映射和并发，不读取业务连接配置。</summary>
[TestClass,DoNotParallelize]
public sealed class AfterSalesSqlTests
{
    private IndependentProcurementSqlFixture fixture=null!;
    /// <summary>显式开启才创建隔离测试库，复用既有安全销毁夹具。</summary>
    [TestInitialize]
    public async Task Initialize()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_AFTERSALES_SQL_TESTS")!="1")
            Assert.Inconclusive("未开启售后LocalDB测试。");
        fixture=new();await fixture.InitializeAsync();await fixture.ExecuteAsync(Schema);
    }
    /// <summary>只释放本夹具唯一创建的LocalDB库。</summary>
    [TestCleanup]
    public async Task Cleanup(){if(fixture is not null)await fixture.DisposeAsync();}

    /// <summary>真实库确认、取消、修改、重确认；已关闭来源可结案，tinyint正确映射。</summary>
    [TestMethod]
    public async Task SaveConfirmCancelAndCloseUseRealDatabase()
    {
        var command=Command();
        var saved=await Call(s=>s.SaveAsync(command.Uid,command,true));Assert.IsTrue(saved.Success,saved.Message);
        var confirm=new ConfirmAfterSalesDemandCommand{CommandId=Guid.NewGuid(),Version=saved.Data.Version};
        var confirmed=await Call(s=>s.ConfirmDemandAsync(command.Uid,confirm));Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(1,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GrossDemand WHERE SourceUid=@uid",new{uid=command.Uid}));
        var replay=await Call(s=>s.ConfirmDemandAsync(command.Uid,confirm));Assert.IsTrue(replay.Data.AlreadyApplied);
        var blocked=await Call(s=>s.CloseAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="未完成"}));
        Assert.AreEqual("SOURCE_NOT_COMPLETED",blocked.Error.Code);
        var cancelled=await Call(s=>s.CancelDemandConfirmationAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="修改"}));
        Assert.IsTrue(cancelled.Success,cancelled.Message);
        Assert.AreEqual(0,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GrossDemand WHERE SourceUid=@uid AND IsDeleted=0 AND LifecycleStatus=0",new{uid=command.Uid}));
        var again=await Call(s=>s.ConfirmDemandAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=cancelled.Data.Version}));
        Assert.IsTrue(again.Success,again.Message);
        // 模拟来源领域已经合法关闭；这是隔离库测试数据，不是售后服务的终止入口。
        await fixture.ExecuteAsync("UPDATE dbo.GrossDemand SET LifecycleStatus=8,Version=Version+1 WHERE SourceUid=@uid",new{uid=command.Uid});
        var closed=await Call(s=>s.CloseAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=again.Data.Version,Reason="来源已处置"}));
        Assert.IsTrue(closed.Success,closed.Message);
        var detail=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().GetAsync(command.Uid));
        Assert.AreEqual(DocumentStatusEnum.Closed,detail.Data.Header.DocumentStatus);
        Assert.AreEqual(ConfirmationStatusEnum.已确认,detail.Data.Header.ConfirmationStatus);
    }

    /// <summary>并发重复创建只生成一份单据，查询与所有候选SQL可执行。</summary>
    [TestMethod]
    public async Task ConcurrentReplayAndQueries()
    {
        var c=Command();c.AddedLines.Add(new(){Uid=Guid.NewGuid(),IssueDescription="零数量投诉",Quantity=0});
        var results=await Task.WhenAll(Call(s=>s.SaveAsync(c.Uid,c,true)),Call(s=>s.SaveAsync(c.Uid,c,true)));
        Assert.IsTrue(results.All(x=>x.Success),string.Join(";",results.Select(x=>x.Message)));
        Assert.AreEqual(1,results.Count(x=>x.Data.AlreadyApplied));
        Assert.AreEqual(1,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AfterSalesHeader"));
        var search=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SearchAsync(new(){CustomerId=fixture.CustomerId}));
        Assert.IsTrue(search.Success,search.Message);Assert.AreEqual(1,search.Data.TotalCount);
        Assert.IsTrue(search.Data.Items[0].HasUndeterminedAmount);
        foreach(var kind in new[]{"customers","owners","suppliers","products","sales-order-lines","colors","quality-categories"})
        {
            var lookup=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().LookupAsync(kind,new(){CustomerId=fixture.CustomerId}));
            Assert.IsTrue(lookup.Success,kind+":"+lookup.Message);
        }
    }

    /// <summary>确认部分来源写入失败时，真实事务撤销已插入需求与确认状态。</summary>
    [TestMethod]
    public async Task ReceptionFailureRollsBackInsertedRows()
    {
        var c=Command();
        c.AddedLines.Add(new(){Uid=Guid.NewGuid(),IssueDescription="第二项",IsNonStandard=true,DescriptionSnapshot="配件",Quantity=2,ResponsibleSupplierId=fixture.SupplierId,ServiceAmountCny=0});
        var saved=await Call(s=>s.SaveAsync(c.Uid,c,true));Assert.IsTrue(saved.Success,saved.Message);
        var rejectedLine=c.AddedLines[1].Uid;
        // 仅在本测试库加入失败注入约束，验证事务回滚；不改任何业务库结构。
        await fixture.ExecuteAsync($"ALTER TABLE dbo.GrossDemand ADD CONSTRAINT CK_Test_RejectSecond CHECK(SourceLineId<>'{rejectedLine:D}')");
        var confirmed=await Call(s=>s.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version}));
        Assert.IsFalse(confirmed.Success);
        Assert.AreEqual(0,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GrossDemand WHERE SourceUid=@uid",new{uid=c.Uid}));
        Assert.AreEqual(0,await fixture.ScalarAsync<int>("SELECT ConfirmationStatus FROM dbo.AfterSalesHeader WHERE Uid=@uid",new{uid=c.Uid}));
    }

    /// <summary>问题成品与标准补件分开，换客户刷新两组客户型号，跨客户原订单拒绝。</summary>
    [TestMethod]
    public async Task StandardPartsAndCustomerReferencesStayConsistent()
    {
        var other=Guid.NewGuid();var order=Guid.NewGuid();var orderLine=Guid.NewGuid();
        await fixture.ExecuteAsync("""
            INSERT dbo.Customer(Uid,CustomerCode,CustomerName) VALUES(@other,N'OTHER',N'另一客户');
            INSERT dbo.CustomerProduct(Uid,CustomerId,SkuId,CustomerSku) VALUES
              (NEWID(),@customer,@part,N'OLD-PART'),(NEWID(),@other,@part,N'NEW-PART');
            INSERT dbo.SalesOrderHeader(Uid,OrderNo,CustomerId) VALUES(@order,N'SO-OTHER',@other);
            INSERT dbo.SalesOrderLine(Uid,SalesOrderId,LineNumber,Sku,Qty) VALUES(@orderLine,@order,1,N'TEST-FG',1);
            """,new {other,order,orderLine,customer=fixture.CustomerId,part=fixture.ComponentId});
        var c=Command();var line=c.AddedLines[0];
        line.IsNonStandard=false;line.ProductId=fixture.ComponentId;line.IssueProductId=fixture.ProductId;
        line.OriginalSalesOrderLineId=orderLine;
        var rejected=await Call(s=>s.SaveAsync(c.Uid,c,true));
        Assert.AreEqual("VALIDATION",rejected.Error.Code);
        line.OriginalSalesOrderLineId=null;
        var saved=await Call(s=>s.SaveAsync(c.Uid,c,true));Assert.IsTrue(saved.Success,saved.Message);
        Assert.AreEqual("TEST-FG",await fixture.ScalarAsync<string>("SELECT IssueSkuSnapshot FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        Assert.AreEqual("TEST-B",await fixture.ScalarAsync<string>("SELECT SkuSnapshot FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        Assert.AreEqual("OLD-PART",await fixture.ScalarAsync<string>("SELECT CustomerSkuSnapshot FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        // 只改头也必须刷新未进入UpdatedLines的客户型号，且增加头行版本。
        var edit=new SaveAfterSalesCommand {CommandId=Guid.NewGuid(),Uid=c.Uid,Version=saved.Data.Version,
            Header=new(){CustomerId=other,AcceptedDate=c.Header.AcceptedDate}};
        var changed=await Call(s=>s.SaveAsync(c.Uid,edit,false));Assert.IsTrue(changed.Success,changed.Message);
        Assert.AreEqual("NEW-PART",await fixture.ScalarAsync<string>("SELECT CustomerSkuSnapshot FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        Assert.AreEqual(2,await fixture.ScalarAsync<int>("SELECT Version FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        var confirmed=await Call(s=>s.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=changed.Data.Version}));
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(fixture.ComponentId,await fixture.ScalarAsync<Guid>("SELECT ProductId FROM dbo.GrossDemand WHERE SourceUid=@uid",new{uid=c.Uid}));
    }

    private SaveAfterSalesCommand Command()=>new(){CommandId=Guid.NewGuid(),Uid=Guid.NewGuid(),
        Header=new(){CustomerId=fixture.CustomerId,AcceptedDate=new(2026,9,8)},
        AddedLines=[new(){Uid=Guid.NewGuid(),IssueDescription="问题成品补螺丝",IsNonStandard=true,DescriptionSnapshot="螺丝",Quantity=2,ServiceAmountCny=0,ResponsibleSupplierId=fixture.SupplierId}]};
    private Task<ServiceResult<AfterSalesCommandResult>> Call(Func<IAfterSalesService,Task<ServiceResult<AfterSalesCommandResult>>> action)
        =>fixture.InScopeAsync(async p=>
        {
            using var logger=LoggerFactory.Create(builder=>builder.AddConsole());
            var service=ActivatorUtilities.CreateInstance<AfterSalesService>(p,new ServiceBaseContext(p.GetRequiredService<DbSession>(),logger));
            return await action(service);
        });
    private const string Schema="""
CREATE TABLE dbo.AfterSalesHeader (
    [Uid] uniqueidentifier NOT NULL PRIMARY KEY,
    [Version] int NOT NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedDate] datetime2(7) NULL,
    [CreatedBy] nvarchar(100) NULL,
    [UpdatedDate] datetime2(7) NULL,
    [UpdatedBy] nvarchar(100) NULL,
    [AfterSalesNo] nvarchar(100) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [CustomerCodeSnapshot] nvarchar(100) NOT NULL,
    [CustomerNameSnapshot] nvarchar(200) NULL,
    [AcceptedDate] date NOT NULL,
    [OwnerId] uniqueidentifier NULL,
    [DocumentStatus] tinyint NOT NULL,
    [ConfirmationStatus] tinyint NOT NULL,
    [DemandConfirmedAt] datetime2(7) NULL,
    [DemandConfirmedBy] nvarchar(100) NULL,
    [ExpectedCompletionDate] date NULL,
    [Conclusion] nvarchar(2000) NULL,
    [Remark] nvarchar(2000) NULL,
 UNIQUE(AfterSalesNo)
);
CREATE TABLE dbo.AfterSalesLine (
    [Uid] uniqueidentifier NOT NULL PRIMARY KEY,
    [Version] int NOT NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedDate] datetime2(7) NULL,
    [CreatedBy] nvarchar(100) NULL,
    [UpdatedDate] datetime2(7) NULL,
    [UpdatedBy] nvarchar(100) NULL,
    [HeaderId] uniqueidentifier NOT NULL,
    [LineNumber] int NOT NULL,
    [OriginalSalesOrderLineId] uniqueidentifier NULL,
    [CustomerComplaintNo] nvarchar(100) NULL,
    [ResponsibleSupplierId] uniqueidentifier NULL,
    [IssueProductId] uniqueidentifier NULL,
    [IssueSkuSnapshot] nvarchar(100) NULL,
    [IssueCustomerSkuSnapshot] nvarchar(100) NULL,
    [IssueLinkSkuSnapshot] nvarchar(100) NULL,
    [IssueColorId] uniqueidentifier NULL,
    [IssueColorSnapshot] nvarchar(100) NULL,
    [IssueProductDescription] nvarchar(500) NULL,
    [IssueDescription] nvarchar(2000) NOT NULL,
    [QualityCategoryId] uniqueidentifier NULL,
    [CauseAnalysis] nvarchar(2000) NULL,
    [ProductId] uniqueidentifier NULL,
    [IsNonStandard] bit NOT NULL,
    [SkuSnapshot] nvarchar(100) NULL,
    [CustomerSkuSnapshot] nvarchar(100) NULL,
    [LinkSkuSnapshot] nvarchar(100) NULL,
    [DescriptionSnapshot] nvarchar(500) NULL,
    [ColorId] uniqueidentifier NULL,
    [ColorSnapshot] nvarchar(100) NULL,
    [SpecificationSnapshot] nvarchar(500) NULL,
    [Quantity] decimal(18,4) NULL,
    [UnitSnapshot] nvarchar(100) NOT NULL,
    [RequiredDate] date NULL,
    [ServiceAmountCny] decimal(18,2) NULL,
    [ReturnedReceivedDate] date NULL,
    [TreatmentDescription] nvarchar(2000) NULL,
    [Remark] nvarchar(2000) NULL,
 UNIQUE(HeaderId,LineNumber)
);
CREATE INDEX IX_AfterSalesHeader_Customer_Status_Date ON dbo.AfterSalesHeader(CustomerId,DocumentStatus,AcceptedDate DESC,Uid) WHERE IsDeleted=0;
ALTER TABLE dbo.SysUser ADD NickName nvarchar(100) NULL;
ALTER TABLE dbo.SysDictData ADD DataNameZh nvarchar(100) NULL;
ALTER TABLE dbo.Product ADD FeaturesEn nvarchar(300) NULL;
ALTER TABLE dbo.ProductColor ADD ColorNameEn nvarchar(100) NULL,ColorCode nvarchar(100) NULL;
""";
}
