using Mapster;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.QualityControl;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>使用唯一隔离库验证单头投诉查询、旧结构升级及清理前置阻断。</summary>
[TestClass, DoNotParallelize]
public sealed class AfterSalesComplaintNumberSqlTests
{
    /// <summary>零补件可查询，重复投诉号允许，多补件搜索不重复单头，修改清空可回读。</summary>
    [TestMethod]
    public async Task ComplaintNumberAndPartsRoundTrip()
    {
        RequireSqlTests();
        await using var f=new IndependentProcurementSqlFixture();await f.InitializeAsync();
        var c=new SaveAfterSalesCommand {Uid=Guid.NewGuid(),CommandId=Guid.NewGuid(),
            Header=new(){CustomerId=f.CustomerId,AcceptedDate=DateTime.Today,IssueDescription="成品松动",CustomerComplaintNo=" CLIENT-001 "}};
        var saved=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SaveAsync(c.Uid,c,true));
        Assert.IsTrue(saved.Success,saved.Message);
        var search=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SearchAsync(new(){Keyword="CLIENT-001"}));
        Assert.AreEqual(1,search.Data.TotalCount);Assert.IsTrue(search.Data.Items[0].HasUndeterminedAmount);
        var detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().GetAsync(c.Uid))).Data;
        Assert.AreEqual("CLIENT-001",detail.Header.CustomerComplaintNo);Assert.AreEqual(0,detail.Lines.Count);
        var edit=new SaveAfterSalesCommand {Uid=c.Uid,Version=detail.Header.Version,CommandId=Guid.NewGuid(),
            Header=detail.Header.Adapt<AfterSalesEditHeaderDto>(),
            AddedLines=[new(){Uid=Guid.NewGuid(),IsNonStandard=true,Quantity=0,CustomerSkuSnapshot="PART-SEARCH"},
                        new(){Uid=Guid.NewGuid(),IsNonStandard=true,CustomerSkuSnapshot="PART-SEARCH"}]};
        edit.Header.CustomerComplaintNo="CLIENT-002";edit.Header.ServiceAmountCny=12;
        saved=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SaveAsync(c.Uid,edit,false));
        Assert.IsTrue(saved.Success,saved.Message);
        search=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SearchAsync(new(){Keyword="PART-SEARCH"}));
        Assert.AreEqual(1,search.Data.TotalCount);Assert.AreEqual(1,search.Data.Items.Count);
        Assert.AreEqual(12m,search.Data.Items[0].KnownAmountCny);Assert.IsFalse(search.Data.Items[0].HasUndeterminedAmount);
        detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().GetAsync(c.Uid))).Data;
        Assert.AreEqual("CLIENT-002",detail.Header.CustomerComplaintNo);Assert.AreEqual(2,detail.Lines.Count);
        c.Uid=Guid.NewGuid();c.CommandId=Guid.NewGuid();c.Header.CustomerComplaintNo="CLIENT-002";
        Assert.IsTrue((await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SaveAsync(c.Uid,c,true))).Success);
        search=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SearchAsync(new(){Keyword="CLIENT-002"}));
        Assert.AreEqual(2,search.Data.TotalCount);
        edit.Uid=detail.Header.Uid;edit.Version=detail.Header.Version;edit.CommandId=Guid.NewGuid();edit.AddedLines.Clear();
        edit.Header.CustomerComplaintNo="   ";edit.Header.ServiceAmountCny=0;
        Assert.IsTrue((await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SaveAsync(edit.Uid,edit,false))).Success);
        detail=(await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().GetAsync(edit.Uid))).Data;
        Assert.IsNull(detail.Header.CustomerComplaintNo);Assert.AreEqual(0m,detail.Header.ServiceAmountCny);
        Assert.AreEqual(1,detail.Lines[0].Version); // 只改头，补件版本不改变。
    }

    /// <summary>旧测试数据在无来源时可清理，迁移移除依赖并允许零数量，重复执行不清理新数据。</summary>
    [TestMethod]
    public async Task LegacyCleanupAndSchemaUpgradeAreGuarded()
    {
        RequireSqlTests();
        await using var f=new IndependentProcurementSqlFixture();await f.InitializeAsync();
        await f.ExecuteAsync(await Read("SwiftExport.Application.Tests/QualityControl/Fixtures/售后重构前结构.sql"));
        await SeedLegacy(f);
        var migration=await Read("SwiftExport.Infrastructure/Migrations/20260922_03_售后单投诉结构调整.sql");
        await Reject(f,migration,"尚未安全清理");
        await f.ExecuteAsync(await Read("SwiftExport.Infrastructure/Migrations/20260922_01_售后重构只读预检.sql"));
        Assert.AreEqual(1,await f.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AfterSalesHeader"));
        await f.ExecuteAsync(await Read("SwiftExport.Infrastructure/Migrations/20260922_02_清理旧售后测试数据.sql"));
        await f.ExecuteAsync(migration);await f.ExecuteAsync(migration);
        Assert.AreEqual(200,await f.ScalarAsync<int>("SELECT max_length FROM sys.columns WHERE object_id=OBJECT_ID('dbo.AfterSalesHeader') AND name='CustomerComplaintNo'"));
        Assert.AreEqual(0,await f.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.AfterSalesLine') AND name IN('IssueDescription','ServiceAmountCny','IssueColorId')"));
        var c=new SaveAfterSalesCommand {Uid=Guid.NewGuid(),CommandId=Guid.NewGuid(),
            Header=new(){CustomerId=f.CustomerId,AcceptedDate=DateTime.Today,IssueDescription="迁移后投诉"},
            AddedLines=[new(){Uid=Guid.NewGuid(),Quantity=0}]};
        var saved=await f.InScopeAsync(p=>p.GetRequiredService<IAfterSalesService>().SaveAsync(c.Uid,c,true));
        Assert.IsTrue(saved.Success,saved.Message);
        await f.ExecuteAsync(migration);
        await Reject(f,await Read("SwiftExport.Infrastructure/Migrations/20260922_02_清理旧售后测试数据.sql"),"禁止执行");
        Assert.AreEqual(1,await f.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AfterSalesHeader"));
    }

    /// <summary>存在CR来源时清理整体阻断，售后及需求均不被删除。</summary>
    [TestMethod]
    public async Task ExistingSourceBlocksCleanup()
    {
        RequireSqlTests();
        await using var f=new IndependentProcurementSqlFixture();await f.InitializeAsync();
        await f.ExecuteAsync(await Read("SwiftExport.Application.Tests/QualityControl/Fixtures/售后重构前结构.sql"));
        await SeedLegacy(f);
        await f.ExecuteAsync("""
            INSERT dbo.GrossDemand(Uid,SourceType,SourceUid,SourceNo,SourceLineId,SkuSnapshot,DemandQty,LifecycleStatus,IsNonStandard)
            SELECT NEWID(),N'CR',h.Uid,h.AfterSalesNo,l.Uid,N'PART',1,0,1
            FROM dbo.AfterSalesHeader h JOIN dbo.AfterSalesLine l ON l.HeaderId=h.Uid;
            """);
        await Reject(f,await Read("SwiftExport.Infrastructure/Migrations/20260922_02_清理旧售后测试数据.sql"),"存在CR来源");
        Assert.AreEqual(1,await f.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AfterSalesHeader"));
        Assert.AreEqual(1,await f.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AfterSalesLine"));
        Assert.AreEqual(1,await f.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.GrossDemand WHERE SourceType='CR'"));
    }

    // 只写夹具独占的旧表，不使用业务连接配置。
    private static Task<int> SeedLegacy(IndependentProcurementSqlFixture f)=>f.ExecuteAsync("""
        INSERT dbo.AfterSalesHeader(Uid,Version,IsDeleted,AfterSalesNo,CustomerId,CustomerCodeSnapshot,AcceptedDate,DocumentStatus,ConfirmationStatus)
        VALUES(@head,1,0,N'CR-LEGACY',@customer,N'C',GETDATE(),0,0);
        INSERT dbo.AfterSalesLine(Uid,Version,IsDeleted,HeaderId,LineNumber,IssueDescription,IsNonStandard,UnitSnapshot)
        VALUES(NEWID(),1,0,@head,1,N'旧投诉',0,N'Pcs');
        """,new{head=Guid.NewGuid(),customer=f.CustomerId});

    private static void RequireSqlTests()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_AFTERSALES_SQL_TESTS")!="1") Assert.Inconclusive("未开启售后LocalDB测试。");
    }
    private static Task<string> Read(string relative)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"SwiftExport.slnx"))) root=root.Parent;
        Assert.IsNotNull(root);return File.ReadAllTextAsync(Path.Combine(root.FullName,relative));
    }
    private static async Task Reject(IndependentProcurementSqlFixture f,string sql,string expected)
    {
        try {await f.ExecuteAsync(sql);Assert.Fail("应阻断该操作。");}
        catch(SqlException ex){StringAssert.Contains(ex.Message,expected);}
    }
}
