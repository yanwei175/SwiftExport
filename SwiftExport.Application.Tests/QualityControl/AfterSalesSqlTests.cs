using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.QualityControl;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Confirmation;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Application.Services.QualityControl.AfterSales.Maintenance;
using SwiftExport.Application.Services.QualityControl.AfterSales.Confirmation;
using SwiftExport.Application.Services.QualityControl.AfterSales.Lifecycle;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.QualityControl;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>在获批单PG隔离库验证售后真实Dapper、来源接收、状态映射和并发，不连接既有业务库。</summary>
[TestClass,DoNotParallelize]
public sealed class AfterSalesSqlTests
{
    private IndependentProcurementPostgreSqlFixture fixture=null!;
    /// <summary>将原测试名称和结果附到例间完整事实导出。</summary>
    public TestContext TestContext { get; set; } = default!;
    /// <summary>显式开启已批准单库门禁，复用串行事实导出与重置夹具。</summary>
    [TestInitialize]
    public async Task Initialize()
    {
        fixture=new();await fixture.InitializeAsync(TestContext);
    }
    /// <summary>关闭业务连接并导出本例事实，只重置获批新库public。</summary>
    [TestCleanup]
    public async Task Cleanup(){if(fixture is not null)await fixture.DisposeAsync();}

    /// <summary>用单位完整的补发要求验证确认、取消及重确认；交付核验未接入时仍拒绝结案。</summary>
    [TestMethod]
    public async Task SaveConfirmCancelAndCloseUseRealDatabase()
    {
        var command=Command();command.Header.CustomerComplaintNo="CLIENT-PACKING";
        var saved=await Call(s=>s.SaveAsync(command.Uid,command,true));Assert.IsTrue(saved.Success,saved.Message);
        var confirm=new ConfirmAfterSalesDemandCommand{CommandId=Guid.NewGuid(),Version=saved.Data.Version};
        var confirmed=await ConfirmCall(s=>s.ConfirmDemandAsync(command.Uid,confirm));Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(1,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM public.gross_demand WHERE source_uid=@uid",new{uid=command.Uid}));
        var demandId=await fixture.ScalarAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_uid=@uid",new{uid=command.Uid});
        var enrichment=await fixture.InScopeAsync(p=>p.GetRequiredService<SwiftExport.Core.Interfaces.Shipping.PackingLists.IPackingListQueryRepository>()
            .GetSourceEnrichmentsAsync([demandId]));
        Assert.AreEqual("CLIENT-PACKING",enrichment.Single().CustomerPoNo);
        var replay=await ConfirmCall(s=>s.ConfirmDemandAsync(command.Uid,confirm));Assert.IsTrue(replay.Data.AlreadyApplied);
        var blocked=await Call(s=>s.CloseAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="未完成"}));
        Assert.AreEqual("AFTERSALES_DELIVERY_QUERY_UNAVAILABLE",blocked.Error.Code);
        var cancelled=await Call(s=>s.CancelDemandConfirmationAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="修改"}));
        Assert.IsTrue(cancelled.Success,cancelled.Message);
        Assert.AreEqual(0,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM public.gross_demand WHERE source_uid=@uid AND is_deleted=FALSE AND lifecycle_status=0",new{uid=command.Uid}));
        var again=await ConfirmCall(s=>s.ConfirmDemandAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=cancelled.Data.Version}));
        Assert.IsTrue(again.Success,again.Message);
        // 模拟来源领域已经合法关闭；这是隔离库测试数据，不是售后服务的终止入口。
        await fixture.ExecuteAsync("UPDATE public.gross_demand SET lifecycle_status=8,version=version+1 WHERE source_uid=@uid",new{uid=command.Uid});
        var closed=await Call(s=>s.CloseAsync(command.Uid,new(){CommandId=Guid.NewGuid(),Version=again.Data.Version,Reason="来源已处置"}));
        Assert.AreEqual("AFTERSALES_DELIVERY_QUERY_UNAVAILABLE",closed.Error.Code);
        var detail=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().GetAsync(command.Uid));
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
        Assert.AreEqual(1,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM public.after_sales_header"));
        var search=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().SearchAsync(new(){CustomerId=fixture.CustomerId}));
        Assert.IsTrue(search.Success,search.Message);Assert.AreEqual(1,search.Data.TotalCount);
        Assert.IsTrue(search.Data.Items[0].HasUndeterminedAmount);
        foreach(var kind in new[]{"customers","owners","suppliers","products","sales-order-lines","colors","quality-categories"})
        {
            var lookup=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().LookupAsync(kind,new(){CustomerId=fixture.CustomerId}));
            Assert.IsTrue(lookup.Success,kind+":"+lookup.Message);
        }
    }

    /// <summary>确认部分来源写入失败时，真实事务撤销已插入需求与确认状态。</summary>
    [TestMethod]
    public async Task ReceptionFailureRollsBackInsertedRows()
    {
        var c=Command();
        // 两行都满足确认必填项，失败必须由下面注入的需求约束触发，不能提前停在单位校验。
        c.AddedLines.Add(new(){Uid=Guid.NewGuid(),IsNonStandard=true,DescriptionSnapshot="配件",UnitSnapshot="PCS",Quantity=2});
        var saved=await Call(s=>s.SaveAsync(c.Uid,c,true));Assert.IsTrue(saved.Success,saved.Message);
        var rejectedLine=c.AddedLines[1].Uid;
        // 仅在本测试库加入失败注入约束，验证事务回滚；不改任何业务库结构。
        await fixture.ExecuteAsync($"ALTER TABLE public.gross_demand ADD CONSTRAINT ck_test_reject_second CHECK(source_line_id<>'{rejectedLine:D}')");
        var confirmed=await ConfirmCall(s=>s.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version}));
        Assert.IsFalse(confirmed.Success);
        Assert.AreEqual(0,await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM public.gross_demand WHERE source_uid=@uid",new{uid=c.Uid}));
        Assert.AreEqual(0,await fixture.ScalarAsync<int>("SELECT confirmation_status FROM public.after_sales_header WHERE uid=@uid",new{uid=c.Uid}));
    }

    /// <summary>问题成品与标准补件分开，换客户刷新两组客户型号，跨客户原订单拒绝。</summary>
    [TestMethod]
    public async Task StandardPartsAndCustomerReferencesStayConsistent()
    {
        var other=Guid.NewGuid();var order=Guid.NewGuid();var orderLine=Guid.NewGuid();var color=Guid.NewGuid();
        await fixture.ExecuteAsync("""
            INSERT INTO public.product_color(uid,color_code_unique,color_code,color_name_zh) VALUES(@color,'TEST-COLOR','TEST-COLOR','测试颜色');
            UPDATE public.product SET color_id=@color WHERE uid=@part;
            INSERT INTO public.customer(uid,customer_code,customer_name,is_active,doc_relate_no,deposit_rate,lead_time_days) VALUES(@other,'OTHER','另一客户',TRUE,'OTHER',0,0);
            INSERT INTO public.customer_product(uid,customer_id,sku_id,customer_sku,customer_code,is_active) VALUES
              (gen_random_uuid(),@customer,@part,'OLD-PART','C',TRUE),(gen_random_uuid(),@other,@part,'NEW-PART','OTHER',TRUE);
            INSERT INTO public.sales_order_header(uid,order_no,customer_id,document_status,confirmation_status,lead_time_days) VALUES(@order,'SO-OTHER',@other,0,0,0);
            INSERT INTO public.sales_order_line(uid,sales_order_id,line_number,sku,qty,unit_snapshot,is_non_standard) VALUES(@orderLine,@order,1,'TEST-FG',1,'PCS',FALSE);
            """,new {other,order,orderLine,color,customer=fixture.CustomerId,part=fixture.ComponentId});
        var c=Command();var line=c.AddedLines[0];
        line.IsNonStandard=false;line.ProductId=fixture.ComponentId;c.Header.IssueProductId=fixture.ProductId;
        c.Header.OriginalSalesOrderLineId=orderLine;
        var rejected=await Call(s=>s.SaveAsync(c.Uid,c,true));
        Assert.AreEqual("VALIDATION",rejected.Error.Code);
        c.Header.OriginalSalesOrderLineId=null;
        var saved=await Call(s=>s.SaveAsync(c.Uid,c,true));Assert.IsTrue(saved.Success,saved.Message);
        Assert.AreEqual("TEST-FG",await fixture.ScalarAsync<string>("SELECT issue_sku_snapshot FROM public.after_sales_header WHERE uid=@uid",new{uid=c.Uid}));
        Assert.AreEqual("TEST-B",await fixture.ScalarAsync<string>("SELECT sku_snapshot FROM public.after_sales_line WHERE uid=@uid",new{uid=line.Uid}));
        Assert.AreEqual("OLD-PART",await fixture.ScalarAsync<string>("SELECT customer_sku_snapshot FROM public.after_sales_line WHERE uid=@uid",new{uid=line.Uid}));
        // 只改头也必须刷新未进入UpdatedLines的客户型号，且增加头行版本。
        var edit=new SaveAfterSalesCommand {CommandId=Guid.NewGuid(),Uid=c.Uid,Version=saved.Data.Version,
            Header=new(){CustomerId=other,AcceptedDate=c.Header.AcceptedDate,IssueDescription=c.Header.IssueDescription,IssueProductId=c.Header.IssueProductId,ResponsibleSupplierId=fixture.SupplierId,ServiceAmountCny=0}};
        var changed=await Call(s=>s.SaveAsync(c.Uid,edit,false));Assert.IsTrue(changed.Success,changed.Message);
        Assert.AreEqual("NEW-PART",await fixture.ScalarAsync<string>("SELECT customer_sku_snapshot FROM public.after_sales_line WHERE uid=@uid",new{uid=line.Uid}));
        Assert.AreEqual(2,await fixture.ScalarAsync<int>("SELECT version FROM public.after_sales_line WHERE uid=@uid",new{uid=line.Uid}));
        var confirmed=await ConfirmCall(s=>s.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=changed.Data.Version}));
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(fixture.ComponentId,await fixture.ScalarAsync<Guid>("SELECT product_id FROM public.gross_demand WHERE source_uid=@uid",new{uid=c.Uid}));
    }

    /// <summary>生成满足原确认规则的非标补发输入，显式保存单位快照，不依赖数据库默认值。</summary>
    private SaveAfterSalesCommand Command()=>new(){CommandId=Guid.NewGuid(),Uid=Guid.NewGuid(),
        Header=new(){CustomerId=fixture.CustomerId,AcceptedDate=new(2026,9,8),IssueDescription="问题成品补螺丝",ServiceAmountCny=0,ResponsibleSupplierId=fixture.SupplierId},
        AddedLines=[new(){Uid=Guid.NewGuid(),IsNonStandard=true,DescriptionSnapshot="螺丝",UnitSnapshot="PCS",Quantity=2}]};
    private Task<ServiceResult<AfterSalesCommandResult>> Call(Func<SqlCommandPorts,Task<ServiceResult<AfterSalesCommandResult>>> action)
        =>fixture.InScopeAsync(async p=>
        {
            using var logger=LoggerFactory.Create(builder=>builder.AddConsole());
            var context=new ServiceBaseContext(p.GetRequiredService<DbSession>(),logger);
            var service=new SqlCommandPorts(
                ActivatorUtilities.CreateInstance<AfterSalesMaintenanceService>(p,context),
                ActivatorUtilities.CreateInstance<AfterSalesCancellationService>(p,context),
                ActivatorUtilities.CreateInstance<AfterSalesLifecycleService>(p,context));
            return await action(service);
        });
    /// <summary>隔离数据库夹具中组合真实窄命令服务；该类不参与本轮无数据库测试。</summary>
    private sealed class SqlCommandPorts(AfterSalesMaintenanceService maintenance, AfterSalesCancellationService cancellation, AfterSalesLifecycleService lifecycle)
    {
        public Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, SaveAfterSalesCommand command, bool create) => maintenance.SaveAsync(uid,command,create);
        public Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid, CancelAfterSalesDemandConfirmationCommand command) => cancellation.CancelDemandConfirmationAsync(uid,command);
        public Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid, CloseAfterSalesCommand command) => lifecycle.CloseAsync(uid,command);
    }
    /// <summary>SQL 夹具只更新确认服务接线；本轮没有业务库，不执行此测试类。</summary>
    private Task<ServiceResult<AfterSalesCommandResult>> ConfirmCall(
        Func<IAfterSalesConfirmationService,Task<ServiceResult<AfterSalesCommandResult>>> action)
        => fixture.InScopeAsync(p => action(p.GetRequiredService<IAfterSalesConfirmationService>()));
}
