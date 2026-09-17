using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.QualityControl;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>使用统一空库基线和唯一隔离库，验证客户投诉单号迁移及真实保存查询。</summary>
[TestClass, DoNotParallelize]
public sealed class AfterSalesComplaintNumberSqlTests
{
    /// <summary>迁移可重复执行，历史行为空，重复客户单号不重复单头，更新和清空可回读。</summary>
    [TestMethod]
    public async Task MigrationAndComplaintNumberRoundTrip()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_AFTERSALES_SQL_TESTS")!="1")
            Assert.Inconclusive("未开启售后LocalDB测试。");
        await using var fixture=new IndependentProcurementSqlFixture();
        await fixture.InitializeAsync();
        // 本夹具独占的空测试库模拟升级前结构，不连接业务库。
        await fixture.ExecuteAsync("ALTER TABLE dbo.AfterSalesLine DROP COLUMN CustomerComplaintNo;");
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"SwiftExport.slnx"))) root=root.Parent;
        Assert.IsNotNull(root);
        var sql=await File.ReadAllTextAsync(Path.Combine(root.FullName,"SwiftExport.Infrastructure/Migrations/20260917_售后行增加客户投诉单号.sql"));
        await fixture.ExecuteAsync(sql);await fixture.ExecuteAsync(sql);
        Assert.AreEqual(200,await fixture.ScalarAsync<int>("SELECT max_length FROM sys.columns WHERE object_id=OBJECT_ID('dbo.AfterSalesLine') AND name='CustomerComplaintNo'"));
        var command=new SaveAfterSalesCommand {Uid=Guid.NewGuid(),CommandId=Guid.NewGuid(),
            Header=new(){CustomerId=fixture.CustomerId,AcceptedDate=DateTime.Today},
            AddedLines=[new(){Uid=Guid.NewGuid(),IssueDescription="投诉一",Quantity=1,CustomerComplaintNo="  CLIENT-001  "},
                        new(){Uid=Guid.NewGuid(),IssueDescription="投诉二",Quantity=1,CustomerComplaintNo="CLIENT-001"},
                        new(){Uid=Guid.NewGuid(),IssueDescription="未填单号",Quantity=1}]};
        var created=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SaveAsync(command.Uid,command,true));
        Assert.IsTrue(created.Success,created.Message);
        var detail=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().GetAsync(command.Uid));
        Assert.AreEqual("CLIENT-001",detail.Data.Lines[0].CustomerComplaintNo);
        Assert.IsNull(detail.Data.Lines[2].CustomerComplaintNo);
        var search=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SearchAsync(new(){Keyword="CLIENT-001"}));
        Assert.IsTrue(search.Success,search.Message);Assert.AreEqual(1,search.Data.TotalCount);Assert.AreEqual(1,search.Data.Items.Count);
        var edit=new SaveAfterSalesCommand {Uid=command.Uid,CommandId=Guid.NewGuid(),Version=detail.Data.Header.Version,
            Header=detail.Data.Header.Adapt<AfterSalesEditHeaderDto>(),UpdatedLines=detail.Data.Lines.Adapt<List<AfterSalesEditLineDto>>()};
        edit.UpdatedLines[0].CustomerComplaintNo="CLIENT-002";edit.UpdatedLines[1].CustomerComplaintNo="  ";
        var changed=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SaveAsync(command.Uid,edit,false));
        Assert.IsTrue(changed.Success,changed.Message);
        detail=await fixture.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().GetAsync(command.Uid));
        Assert.AreEqual("CLIENT-002",detail.Data.Lines[0].CustomerComplaintNo);Assert.IsNull(detail.Data.Lines[1].CustomerComplaintNo);
        Assert.AreEqual(2,detail.Data.Lines[0].Version);
    }
}
