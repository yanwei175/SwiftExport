using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Maintenance;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.QualityControl;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>使用获批单PG隔离库验证原单头路径与投诉查询；旧SQL Server结构升级已退役。</summary>
[TestClass, DoNotParallelize]
public sealed class AfterSalesComplaintNumberSqlTests
{
    /// <summary>将测试身份附到逐例事实导出，最终结果以本批运行记录为准。</summary>
    public TestContext TestContext { get; set; } = default!;
    /// <summary>真实 SQL 查询、新增、更新和清空均保留单头路径，不改变补件及来源。</summary>
    [TestMethod]
    public async Task LocalFolderRoundTripsInSql()
    {
        RequirePgTests();
        await using var f=new IndependentProcurementPostgreSqlFixture();await f.InitializeAsync(TestContext);
        var c=new SaveAfterSalesCommand {Uid=Guid.NewGuid(),CommandId=Guid.NewGuid(),
            Header=new(){CustomerId=f.CustomerId,AcceptedDate=DateTime.Today,IssueDescription="资料路径",
                LocalFileFolder=@"  \\server\客户投诉\测试  "}};
        Assert.IsTrue((await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(c.Uid,c,true))).Success);
        var detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().GetAsync(c.Uid))).Data;
        Assert.AreEqual(@"\\server\客户投诉\测试",detail.Header.LocalFileFolder);
        var edit=new SaveAfterSalesCommand {Uid=c.Uid,CommandId=Guid.NewGuid(),Version=detail.Header.Version,
            Header=detail.Header.Adapt<AfterSalesEditHeaderDto>()};
        edit.Header.LocalFileFolder=new string('中',1000);
        Assert.IsTrue((await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(c.Uid,edit,false))).Success);
        detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().GetAsync(c.Uid))).Data;
        Assert.AreEqual(1000,detail.Header.LocalFileFolder.Length);
        edit.Version=detail.Header.Version;edit.CommandId=Guid.NewGuid();edit.Header.LocalFileFolder="  ";
        Assert.IsTrue((await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(c.Uid,edit,false))).Success);
        detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().GetAsync(c.Uid))).Data;
        Assert.IsNull(detail.Header.LocalFileFolder);
        Assert.AreEqual(0,await f.ScalarAsync<int>("SELECT COUNT(*) FROM public.gross_demand WHERE source_uid=@id",new {id=c.Uid}));
    }

    /// <summary>零补件可查询，重复投诉号允许，多补件搜索不重复单头，修改清空可回读。</summary>
    [TestMethod]
    public async Task ComplaintNumberAndPartsRoundTrip()
    {
        RequirePgTests();
        await using var f=new IndependentProcurementPostgreSqlFixture();await f.InitializeAsync(TestContext);
        var c=new SaveAfterSalesCommand {Uid=Guid.NewGuid(),CommandId=Guid.NewGuid(),
            Header=new(){CustomerId=f.CustomerId,AcceptedDate=DateTime.Today,IssueDescription="成品松动",CustomerComplaintNo=" CLIENT-001 "}};
        var saved=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(c.Uid,c,true));
        Assert.IsTrue(saved.Success,saved.Message);
        var search=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().SearchAsync(new(){Keyword="CLIENT-001"}));
        Assert.AreEqual(1,search.Data.TotalCount);Assert.IsTrue(search.Data.Items[0].HasUndeterminedAmount);
        var detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().GetAsync(c.Uid))).Data;
        Assert.AreEqual("CLIENT-001",detail.Header.CustomerComplaintNo);Assert.AreEqual(0,detail.Lines.Count);
        var edit=new SaveAfterSalesCommand {Uid=c.Uid,Version=detail.Header.Version,CommandId=Guid.NewGuid(),
            Header=detail.Header.Adapt<AfterSalesEditHeaderDto>(),
            AddedLines=[new(){Uid=Guid.NewGuid(),IsNonStandard=true,Quantity=0,CustomerSkuSnapshot="PART-SEARCH"},
                        new(){Uid=Guid.NewGuid(),IsNonStandard=true,CustomerSkuSnapshot="PART-SEARCH"}]};
        edit.Header.CustomerComplaintNo="CLIENT-002";edit.Header.ServiceAmountCny=12;
        saved=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(c.Uid,edit,false));
        Assert.IsTrue(saved.Success,saved.Message);
        search=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().SearchAsync(new(){Keyword="PART-SEARCH"}));
        Assert.AreEqual(1,search.Data.TotalCount);Assert.AreEqual(1,search.Data.Items.Count);
        Assert.AreEqual(12m,search.Data.Items[0].KnownAmountCny);Assert.IsFalse(search.Data.Items[0].HasUndeterminedAmount);
        detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().GetAsync(c.Uid))).Data;
        Assert.AreEqual("CLIENT-002",detail.Header.CustomerComplaintNo);Assert.AreEqual(2,detail.Lines.Count);
        c.Uid=Guid.NewGuid();c.CommandId=Guid.NewGuid();c.Header.CustomerComplaintNo="CLIENT-002";
        Assert.IsTrue((await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(c.Uid,c,true))).Success);
        search=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().SearchAsync(new(){Keyword="CLIENT-002"}));
        Assert.AreEqual(2,search.Data.TotalCount);
        edit.Uid=detail.Header.Uid;edit.Version=detail.Header.Version;edit.CommandId=Guid.NewGuid();edit.AddedLines.Clear();
        edit.Header.CustomerComplaintNo="   ";edit.Header.ServiceAmountCny=0;
        Assert.IsTrue((await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(edit.Uid,edit,false))).Success);
        detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesQueryService>().GetAsync(edit.Uid))).Data;
        Assert.IsNull(detail.Header.CustomerComplaintNo);Assert.AreEqual(0m,detail.Header.ServiceAmountCny);
        Assert.AreEqual(1,detail.Lines[0].Version); // 只改头，补件版本不改变。
    }

    /// <summary>旧LocalDB门禁不再开启本组，只接受已批准单库PG门禁。</summary>
    private static void RequirePgTests()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_LEGACY_TESTS")!="1") Assert.Inconclusive("未开启已批准单库PG测试。");
    }
}
