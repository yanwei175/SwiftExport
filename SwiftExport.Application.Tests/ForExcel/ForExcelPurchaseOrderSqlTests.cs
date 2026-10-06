using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.ForExcel;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Core.Interfaces.Procurement;
namespace SwiftExport.Application.Tests.ForExcel;

/// <summary>在获批单PG隔离库中验证原采购导入、落库、重放、重验和回滚，不连接业务库。</summary>
[TestClass,DoNotParallelize]
public sealed class ForExcelPurchaseOrderSqlTests
{
    private IndependentProcurementPostgreSqlFixture db = default!;
    /// <summary>记录原测试名称与结果，供例间事实导出追溯。</summary>
    public TestContext TestContext { get; set; } = default!;
    private Guid customerProductId;

    /// <summary>仅显式开启时建立本测试专属库和客户型号关联。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        db=new();await db.InitializeAsync(TestContext);customerProductId=Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO public.customer_product(uid,customer_id,customer_code,sku_id,customer_sku,link_sku,is_active)
            VALUES(@Uid,@CustomerId,'TEST-C',@ProductId,'AQ1710BGM','AT421100BGM',TRUE);
            """,new{Uid=customerProductId,db.CustomerId,db.ProductId});
    }

    /// <summary>无论成功或失败，先导出本例事实，仅重置获批新库public。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (db is not null) await db.DisposeAsync();
    }

    /// <summary>从实际检查到创建、读取，保留所有商业快照，生成独立草稿且不创建 Link。</summary>
    [TestMethod]
    public async Task CheckCreateAndReadPersistCommercialSnapshots()
    {
        var request=Request();
        var check=await db.InScopeAsync(p=>p.GetRequiredService<IForExcelPurchaseOrderService>().CheckAsync(request));
        Assert.IsTrue(check.Success,check.Message);Assert.IsTrue(check.Data.CanSave);
        Assert.AreEqual(db.ProductId,check.Data.Lines[0].Product.ProductId);
        Assert.AreEqual("TEST-FG",check.Data.Lines[0].Product.Sku);
        var command=db.CreateCommand();
        request.Header.Adapt(command);
        command.CustomerId=db.CustomerId;command.SupplierId=db.SupplierId;
        request.Lines[0].Adapt(command.Lines[0]);
        command.Lines[0].ProductId=check.Data.Lines[0].Product.ProductId!.Value;
        command.Lines[0].SelectedCustomerProductId=customerProductId;
        var created=await db.CreateAsync(command);Assert.IsTrue(created.Success,created.Message);
        var replay=await db.CreateAsync(command);Assert.IsTrue(replay.Success,replay.Message);
        Assert.AreEqual(created.Data.PurchaseOrderId,replay.Data.PurchaseOrderId);
        var detail=await db.InScopeAsync(p=>p.GetRequiredService<IProcurementOrderQueryRepository>().GetDetailAsync(created.Data.PurchaseOrderId,default));
        Assert.IsNotNull(detail);Assert.AreEqual("AT260820DQ",detail.BoNo);
        Assert.AreNotEqual(detail.BoNo,detail.OrderNo);Assert.AreEqual("Order 47",detail.CustomerPoNo);
        Assert.AreEqual("Tony",detail.SalesPerson);Assert.AreEqual("Paggie",detail.PurchasePerson);
        Assert.AreEqual("G1/2",detail.InletHoseType);Assert.AreEqual("Neoperl",detail.AeratorType);
        Assert.AreEqual("不打托",detail.PalletType);Assert.AreEqual("客户定制",detail.BoxType);
        Assert.AreEqual("标贴",detail.LabelType);Assert.AreEqual("说明书",detail.InstructionManualType);
        Assert.AreEqual("外箱",detail.CartonType);Assert.AreEqual("AQUATEK",detail.LogoType);
        Assert.AreEqual(request.Header.ReplyDate,detail.ReplyDate);
        Assert.AreEqual(0.95m,detail.DiscountRate);Assert.AreEqual(100m,detail.DiscountAmount);
        Assert.AreEqual("只记录",detail.DiscountRemark);
        var line=detail.Lines.Single();
        Assert.AreEqual("AQ1710BGM",line.CustomerSku);Assert.AreEqual("AT421100BGM",line.LinkSku);
        Assert.AreEqual("Excel工厂型号",line.SupplierSku);Assert.AreEqual("Excel描述",line.DescriptionZh);
        Assert.AreEqual("Excel颜色",line.FinishZh);Assert.AreEqual("Excel配置",line.FeaturesZh);
        Assert.AreEqual(116m,line.UnitPrice);Assert.AreEqual(4176m,line.UnitAmount);
        Assert.AreEqual(4176m,detail.CnyTotalAmount);Assert.AreEqual(line.Quantity,
            await db.ScalarAsync<decimal>("SELECT unlinked_qty FROM public.purchase_order_line WHERE uid=@Uid",new{line.Uid}));
        Assert.IsFalse(line.IsNonStandard);Assert.AreEqual(0,(int)detail.ConfirmationStatus);
        Assert.IsNull(detail.DemandSourceUid);
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.demand_execution_link"));
        Assert.AreEqual(1,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_head"));
        command.BoNo="改动原单号";
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,(await db.CreateAsync(command)).Error.Code);
    }

    /// <summary>检查成功不授权写入；关联停用、伪造 ProductId 与未确认型号差异均在保存时拒绝。</summary>
    [TestMethod]
    public async Task CreateRevalidatesCurrentRelationAndIdentity()
    {
        var check=await db.InScopeAsync(p=>p.GetRequiredService<IForExcelPurchaseOrderService>().CheckAsync(Request()));
        Assert.IsTrue(check.Data.CanSave);
        var command=db.CreateCommand();command.Lines[0].CustomerSku="AQ1710BGM";
        // 新接口显式提交空客户型号不能退回旧客户端按 ProductId 手工创建的兼容路径。
        command.Lines[0].CustomerSku="";
        Assert.IsFalse((await db.CreateAsync(command)).Success);
        command.Lines[0].CustomerSku="AQ1710BGM";
        command.Lines[0].SelectedCustomerProductId=customerProductId;
        command.Lines[0].ProductId=db.ComponentId;
        Assert.IsFalse((await db.CreateAsync(command)).Success);
        command.Lines[0].ProductId=db.ProductId;command.Lines[0].LinkSku="Excel旧型号";
        Assert.IsFalse((await db.CreateAsync(command)).Success);
        command.Lines[0].AcceptLinkSkuMismatch=true;
        await db.ExecuteAsync("UPDATE public.customer_product SET is_active=FALSE WHERE uid=@Uid",new{Uid=customerProductId});
        Assert.IsFalse((await db.CreateAsync(command)).Success);
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_head"));
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
        await db.ExecuteAsync("UPDATE public.customer_product SET is_active=TRUE WHERE uid=@Uid",new{Uid=customerProductId});
        var created=await db.CreateAsync(command);Assert.IsTrue(created.Success,created.Message);
        Assert.AreEqual("Excel旧型号",await db.ScalarAsync<string>("SELECT link_sku FROM public.purchase_order_line"));
    }

    /// <summary>明确非标无需产品主档但必须有效单位；导入草稿可确认，仍拒绝损坏身份与未知单位。</summary>
    [TestMethod]
    public async Task NonStandardDraftHasStableIdentityWithoutProductOrDemand()
    {
        var command=db.CreateCommand();var input=command.Lines[0];
        input.IsNonStandard=true;input.ProductId=Guid.Empty;input.DescriptionZh="非标组件";input.Unit="PCS";
        var created=await db.CreateAsync(command);Assert.IsTrue(created.Success,created.Message);
        var detail=await db.InScopeAsync(p=>p.GetRequiredService<IProcurementOrderQueryRepository>().GetDetailAsync(created.Data.PurchaseOrderId,default));
        var line=detail!.Lines.Single();Assert.IsTrue(line.IsNonStandard);
        Assert.AreEqual(Guid.Empty,line.ProductId);Assert.AreEqual(NonStandardSkuGenerator.Create(line.Uid),line.Sku);
        Assert.AreEqual("PCS",line.UnitSnapshot);Assert.AreEqual("非标组件",line.DescriptionZh);
        Assert.AreEqual(1,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_line WHERE product_id IS NULL AND source_uid IS NULL"));
        var context=await db.ContextAsync(created.Data.PurchaseOrderId);Assert.IsTrue(context.Success,context.Message);
        var confirmed=await db.ConfirmAsync(created.Data.PurchaseOrderId,new()
        {CommandId=Guid.NewGuid(),ExpectedVersion=context.Data!.Version,ContextFingerprint=context.Data.Fingerprint});
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        var replay=await db.CreateAsync(command);Assert.AreEqual(created.Data.PurchaseOrderId,replay.Data.PurchaseOrderId);
        var invalid=db.CreateCommand();invalid.Lines[0].IsNonStandard=true;
        Assert.IsFalse((await db.CreateAsync(invalid)).Success);
        invalid.Lines[0].ProductId=Guid.Empty;invalid.Lines[0].DescriptionZh="非标";invalid.Lines[0].Unit="未知单位";
        Assert.IsFalse((await db.CreateAsync(invalid)).Success);
        Assert.AreEqual(1,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_head"));
    }

    /// <summary>业务日志失败时头行和回执全部回滚，同一命令可在故障移除后重试。</summary>
    [TestMethod]
    public async Task SnapshotAndReceiptRollBackTogether()
    {
        var command=db.CreateCommand();command.BoNo="Excel原单号";
        await db.RejectLogsAsync();Assert.IsFalse((await db.CreateAsync(command)).Success);
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_head"));
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_line"));
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
        await db.ExecuteAsync("DROP TRIGGER tr_test_reject_log ON public.business_operation_log; DROP FUNCTION public.p09_test_reject_log();");
        var created=await db.CreateAsync(command);Assert.IsTrue(created.Success,created.Message);
    }

    /// <summary>导入允许的最大长度快照在读取后原样回传，只修改交期也必须能完成商务保存。</summary>
    [TestMethod]
    public async Task ImportedLongTextsCanBeSavedAfterReading()
    {
        var command=db.CreateCommand();
        command.Lines[0].CustomerSku="AQ1710BGM";
        command.Lines[0].SupplierSku=new string('型',300);
        command.Lines[0].DescriptionZh=new string('述',2000);
        var created=await db.CreateAsync(command);Assert.IsTrue(created.Success,created.Message);
        var detail=await db.InScopeAsync(p=>p.GetRequiredService<IProcurementOrderQueryRepository>().GetDetailAsync(created.Data.PurchaseOrderId,default));
        Assert.IsNotNull(detail);
        var original=detail.Lines.Single();var delivery=new DateTime(2026,10,4);
        var save=await db.SaveAsync(detail.PurchaseOrderId,new SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto.UpdatePurchaseOrderDraftCommand
        {
            CommandId=Guid.NewGuid(),ExpectedVersion=detail.Version,
            Remark=detail.Remark,PackagingRequirements=detail.PackagingRequirements,DiscountRemark=detail.DiscountRemark,
            Lines=[new()
            {
                PurchaseOrderLineId=original.Uid,ExpectedVersion=original.Version,
                SupplierSku=original.SupplierSku,DescriptionZh=original.DescriptionZh,SubNote=original.SubNote,
                UnitPrice=original.UnitPrice,TaxPrice=original.TaxPrice,PlannedDeliveryDate=delivery
            }]
        });
        Assert.IsTrue(save.Success,save.Message);
        var saved=await db.InScopeAsync(p=>p.GetRequiredService<IProcurementOrderQueryRepository>().GetDetailAsync(detail.PurchaseOrderId,default));
        var line=saved!.Lines.Single();
        Assert.AreEqual(original.SupplierSku,line.SupplierSku);Assert.AreEqual(original.DescriptionZh,line.DescriptionZh);
        Assert.AreEqual((DateTime?)delivery,line.PlannedDeliveryDate);
        Assert.IsTrue(saved.ExpectedDate==delivery,"采购头交期应随明细更新。");
        Assert.AreEqual(original.ProductId,line.ProductId);Assert.AreEqual(original.Quantity,line.Quantity);
        Assert.AreEqual(original.UnitPrice,line.UnitPrice);Assert.AreEqual(original.UnitAmount,line.UnitAmount);
        Assert.AreEqual(original.Version+1,line.Version);Assert.AreEqual(detail.Version+1,saved.Version);
    }

    /// <summary>使用用户提供样例口径，不读取或更改实际 Excel 文件。</summary>
    private CheckPurchaseOrderImportRequest Request()=>new()
    {
        CustomerCode="TEST-C",SupplierId=db.SupplierId,
        Header=new(){OrderDate=new(2026,8,20),BoNo="AT260820DQ",CustomerPoNo="Order 47",ReplyDate=new(2026,10,4),
            SalesPerson="Tony",PurchasePerson="Paggie",InletHoseType="G1/2",AeratorType="Neoperl",PalletType="不打托",
            BoxType="客户定制",LabelType="标贴",InstructionManualType="说明书",CartonType="外箱",LogoType="AQUATEK",
            DiscountRate=0.95m,DiscountAmount=100,DiscountRemark="只记录"},
        Lines=[new(){ClientLineId=Guid.NewGuid(),CustomerSku="AQ1710BGM",LinkSku="AT421100BGM",Quantity=36,UnitPrice=116,
            SupplierSku="Excel工厂型号",DescriptionZh="Excel描述",FinishZh="Excel颜色",FeaturesZh="Excel配置"}]
    };
}
