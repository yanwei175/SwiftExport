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
        fixture=new();await fixture.InitializeAsync();
    }
    /// <summary>只释放本夹具唯一创建的LocalDB库。</summary>
    [TestCleanup]
    public async Task Cleanup(){if(fixture is not null)await fixture.DisposeAsync();}

    /// <summary>真实库确认、取消、修改、重确认；交付核验未接入时保持结案阻断，tinyint正确映射。</summary>
    [TestMethod]
    public async Task SaveConfirmCancelAndCloseUseRealDatabase()
    {
        var command=Command();command.Header.CustomerComplaintNo="CLIENT-PACKING";
        var saved=await Call(s=>s.SaveAsync(command.Uid,command,true));Assert.IsTrue(saved.Success,saved.Message);
        var confirm=new ConfirmAfterSalesDemandCommand{CommandId=Guid.NewGuid(),Version=saved.Data.Version};
        var confirmed=await Call(s=>s.ConfirmDemandAsync(command.Uid,confirm));Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(1,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GrossDemand WHERE SourceUid=@uid",new{uid=command.Uid}));
        var demandId=await fixture.ScalarAsync<Guid>("SELECT Uid FROM dbo.GrossDemand WHERE SourceUid=@uid",new{uid=command.Uid});
        var enrichment=await fixture.InScopeAsync(p=>p.GetRequiredService<SwiftExport.Core.Interfaces.Shipping.IPackingListRepository>()
            .GetSourceEnrichmentsAsync([demandId]));
        Assert.AreEqual("CLIENT-PACKING",enrichment.Single().CustomerPoNo);
        var replay=await Call(s=>s.ConfirmDemandAsync(command.Uid,confirm));Assert.IsTrue(replay.Data.AlreadyApplied);
        var blocked=await Call(s=>s.CloseAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="未完成"}));
        Assert.AreEqual("AFTERSALES_DELIVERY_QUERY_UNAVAILABLE",blocked.Error.Code);
        var cancelled=await Call(s=>s.CancelDemandConfirmationAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="修改"}));
        Assert.IsTrue(cancelled.Success,cancelled.Message);
        Assert.AreEqual(0,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GrossDemand WHERE SourceUid=@uid AND IsDeleted=0 AND LifecycleStatus=0",new{uid=command.Uid}));
        var again=await Call(s=>s.ConfirmDemandAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=cancelled.Data.Version}));
        Assert.IsTrue(again.Success,again.Message);
        // 模拟来源领域已经合法关闭；这是隔离库测试数据，不是售后服务的终止入口。
        await fixture.ExecuteAsync("UPDATE dbo.GrossDemand SET LifecycleStatus=8,Version=Version+1 WHERE SourceUid=@uid",new{uid=command.Uid});
        var closed=await Call(s=>s.CloseAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=again.Data.Version,Reason="来源已处置"}));
        Assert.AreEqual("AFTERSALES_DELIVERY_QUERY_UNAVAILABLE",closed.Error.Code);
        var detail=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().GetAsync(command.Uid));
        Assert.AreEqual(DocumentStatusEnum.Open,detail.Data.Header.DocumentStatus);
        Assert.AreEqual(ConfirmationStatusEnum.已确认,detail.Data.Header.ConfirmationStatus);
    }

    /// <summary>并发重复创建只生成一份单据，查询与所有候选SQL可执行。</summary>
    [TestMethod]
    public async Task ConcurrentReplayAndQueries()
    {
        var c=Command();c.Header.ServiceAmountCny=null;c.AddedLines.Add(new(){Uid=Guid.NewGuid(),Quantity=0});
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
        c.AddedLines.Add(new(){Uid=Guid.NewGuid(),IsNonStandard=true,DescriptionSnapshot="配件",Quantity=2});
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
            DECLARE @color uniqueidentifier=NEWID();
            INSERT dbo.ProductColor(Uid,ColorCodeUnique,ColorCode,ColorNameZh) VALUES(@color,N'TEST-COLOR',N'TEST-COLOR',N'测试颜色');
            UPDATE dbo.Product SET ColorId=@color WHERE Uid=@part;
            INSERT dbo.Customer(Uid,CustomerCode,CustomerName,IsActive,DocRelateNo,DepositRate,LeadTimeDays) VALUES(@other,N'OTHER',N'另一客户',1,N'OTHER',0,0);
            INSERT dbo.CustomerProduct(Uid,CustomerId,SkuId,CustomerSku,CustomerCode,IsActive) VALUES
              (NEWID(),@customer,@part,N'OLD-PART',N'C',1),(NEWID(),@other,@part,N'NEW-PART',N'OTHER',1);
            INSERT dbo.SalesOrderHeader(Uid,OrderNo,CustomerId,DocumentStatus,ConfirmationStatus,LeadTimeDays) VALUES(@order,N'SO-OTHER',@other,0,0,0);
            INSERT dbo.SalesOrderLine(Uid,SalesOrderId,LineNumber,Sku,Qty,IsNonStandard) VALUES(@orderLine,@order,1,N'TEST-FG',1,0);
            """,new {other,order,orderLine,customer=fixture.CustomerId,part=fixture.ComponentId});
        var c=Command();var line=c.AddedLines[0];
        line.IsNonStandard=false;line.ProductId=fixture.ComponentId;c.Header.IssueProductId=fixture.ProductId;
        c.Header.OriginalSalesOrderLineId=orderLine;
        var rejected=await Call(s=>s.SaveAsync(c.Uid,c,true));
        Assert.AreEqual("VALIDATION",rejected.Error.Code);
        c.Header.OriginalSalesOrderLineId=null;
        var saved=await Call(s=>s.SaveAsync(c.Uid,c,true));Assert.IsTrue(saved.Success,saved.Message);
        Assert.AreEqual("TEST-FG",await fixture.ScalarAsync<string>("SELECT IssueSkuSnapshot FROM dbo.AfterSalesHeader WHERE Uid=@uid",new{uid=c.Uid}));
        Assert.AreEqual("TEST-B",await fixture.ScalarAsync<string>("SELECT SkuSnapshot FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        Assert.AreEqual("OLD-PART",await fixture.ScalarAsync<string>("SELECT CustomerSkuSnapshot FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        // 只改头也必须刷新未进入UpdatedLines的客户型号，且增加头行版本。
        var edit=new SaveAfterSalesCommand {CommandId=Guid.NewGuid(),Uid=c.Uid,Version=saved.Data.Version,
            Header=new(){CustomerId=other,AcceptedDate=c.Header.AcceptedDate,IssueDescription=c.Header.IssueDescription,IssueProductId=c.Header.IssueProductId,ResponsibleSupplierId=fixture.SupplierId,ServiceAmountCny=0}};
        var changed=await Call(s=>s.SaveAsync(c.Uid,edit,false));Assert.IsTrue(changed.Success,changed.Message);
        Assert.AreEqual("NEW-PART",await fixture.ScalarAsync<string>("SELECT CustomerSkuSnapshot FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        Assert.AreEqual(2,await fixture.ScalarAsync<int>("SELECT Version FROM dbo.AfterSalesLine WHERE Uid=@uid",new{uid=line.Uid}));
        var confirmed=await Call(s=>s.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=changed.Data.Version}));
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(fixture.ComponentId,await fixture.ScalarAsync<Guid>("SELECT ProductId FROM dbo.GrossDemand WHERE SourceUid=@uid",new{uid=c.Uid}));
    }

    private SaveAfterSalesCommand Command()=>new(){CommandId=Guid.NewGuid(),Uid=Guid.NewGuid(),
        Header=new(){CustomerId=fixture.CustomerId,AcceptedDate=new(2026,9,8),IssueDescription="问题成品补螺丝",ServiceAmountCny=0,ResponsibleSupplierId=fixture.SupplierId},
        AddedLines=[new(){Uid=Guid.NewGuid(),IsNonStandard=true,DescriptionSnapshot="螺丝",Quantity=2}]};
    private Task<ServiceResult<AfterSalesCommandResult>> Call(Func<IAfterSalesService,Task<ServiceResult<AfterSalesCommandResult>>> action)
        =>fixture.InScopeAsync(async p=>
        {
            using var logger=LoggerFactory.Create(builder=>builder.AddConsole());
            var service=ActivatorUtilities.CreateInstance<AfterSalesService>(p,new ServiceBaseContext(p.GetRequiredService<DbSession>(),logger));
            return await action(service);
        });
}
