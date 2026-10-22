using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>独立采购输入精度与商务补丁安全边界，验证不能悄悄改变业务身份。</summary>
[TestClass]
public sealed class ProcurementOrderPolicyTests
{
    /// <summary>包材采用普通采购资格，供货方式、停用及空型号限制继续有效，虚拟件仍不能采购。</summary>
    [TestMethod]
    public void PackagingMaterial_UsesExistingPurchaseEligibility()
    {
        var product = new Product { Sku = "PACK", IsActive = true,
            SupplyType = SupplyTypeEnums.采购, MaterialType = MaterialTypesEnum.包材 };
        ProcurementOrderPolicy.RequireProduct(product);
        product.IsActive = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => ProcurementOrderPolicy.RequireProduct(product));
        product.IsActive = true;
        product.SupplyType = SupplyTypeEnums.生产;
        Assert.ThrowsExactly<InvalidOperationException>(() => ProcurementOrderPolicy.RequireProduct(product));
        product.SupplyType = SupplyTypeEnums.采购;
        product.Sku = " ";
        Assert.ThrowsExactly<InvalidOperationException>(() => ProcurementOrderPolicy.RequireProduct(product));
        product.Sku = "PACK";
        product.MaterialType = MaterialTypesEnum.虚拟件;
        Assert.ThrowsExactly<InvalidOperationException>(() => ProcurementOrderPolicy.RequireProduct(product));
    }

    /// <summary>产品主档的 SKU 恰好以 NS 开头也仍按真实主档资格判断，非标类型由单据身份表达。</summary>
    [TestMethod]
    public void ProductWithNsPrefix_IsNotAutomaticallyNonStandard()
    {
        ProcurementOrderPolicy.RequireProduct(new Product
        {
            Sku = "NS-ORDINARY-PRODUCT", IsActive = true,
            SupplyType = SupplyTypeEnums.采购, MaterialType = MaterialTypesEnum.成品
        });
    }

    /// <summary>历史读取可表达空价，明确零价保留，金额仅在最终四位舍入。</summary>
    [TestMethod]
    public void AmountDistinguishesMissingAndZeroAndRoundsFinalResult()
    {
        Assert.IsNull(ProcurementOrderPolicy.Amount(2m, null));
        Assert.AreEqual(0m, ProcurementOrderPolicy.Amount(2m, 0m));
        Assert.AreEqual(0.0001m, ProcurementOrderPolicy.Amount(0.5m, 0.0001m));
    }

    /// <summary>数量和价格不得依靠 SQL 静默舍入，金额溢出也在业务入口拒绝。</summary>
    [TestMethod]
    public void AmountRejectsInvalidPrecisionAndOverflow()
    {
        Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.Amount(0m, 1m));
        Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.Amount(1.00001m, 1m));
        Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.Amount(1m, 0.0000001m));
        Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.Amount(100m, 1.000049m));
        Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.Amount(99999999999999.9999m, 999999999999.999999m));
        Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.Amount(1m, -1m));
    }

    /// <summary>创建和保存均要求明确两价，拒绝负价、超四位精度、超范围及金额溢出，允许明确零价。</summary>
    [TestMethod]
    public void CreateAndSaveShareRequiredPriceAndStoragePrecisionRules()
    {
        foreach (var (unitPrice, taxPrice) in new (decimal?, decimal?)[]
        {
            (null, 0), (0, null), (-1, 0), (0, -1), (0.00001m, 0), (0, 0.00001m),
            (100000000000000m, 0), (0, 100000000000000m)
        })
        {
            var input = new CreateIndependentPurchaseOrderLineCommand { Quantity = 1, UnitPrice = unitPrice, TaxPrice = taxPrice };
            var line = new PurchaseOrderLine { Uid = Guid.NewGuid(), Quantity = 1 };
            var command = new UpdatePurchaseOrderDraftCommand
            {
                ExpectedVersion = 1, Lines = [new() { PurchaseOrderLineId = line.Uid, ExpectedVersion = 1, UnitPrice = unitPrice, TaxPrice = taxPrice }]
            };
            Assert.ThrowsExactly<ProcurementOperationException>(() => IndependentPurchaseOrderInputPolicy.ValidateLine(input));
            Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.ApplySave(new(), [line], command));
        }
        IndependentPurchaseOrderInputPolicy.ValidateLine(new() { Quantity = 1, UnitPrice = 0, TaxPrice = 0 });
        Assert.AreEqual((0m, 0m), ProcurementOrderPolicy.CalculateAmounts(1, 0, 0));
        Assert.AreEqual((0.0001m, 0.0002m), ProcurementOrderPolicy.CalculateAmounts(0.5m, 0.0001m, 0.0003m));
        Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.CalculateAmounts(2, 99999999999999.9999m, 0));
    }

    /// <summary>保存忽略旧金额并按两价重算；保存结果能通过与确认预检共用的金额核验。</summary>
    [TestMethod]
    public void SaveRecalculatesAmountsWithFourDigitRounding()
    {
        var line = new PurchaseOrderLine { Uid = Guid.NewGuid(), Quantity = 0.5m, UnitAmount = 99, TaxAmount = null };
        ProcurementOrderPolicy.ApplySave(new(), [line], new()
        {
            ExpectedVersion = 1, Lines = [new() { PurchaseOrderLineId = line.Uid, ExpectedVersion = 1, UnitPrice = 0.0001m, TaxPrice = 0.0003m }]
        });
        Assert.AreEqual(0.0001m, line.UnitAmount);
        Assert.AreEqual(0.0002m, line.TaxAmount);
        ProcurementOrderPolicy.RequireConfirmationAmounts(line);
    }

    /// <summary>新保存协议物理上没有客户、供应商、数量、产品或收货目标字段。</summary>
    [TestMethod]
    public void SaveContractCannotExpressIdentityOrQuantityChanges()
    {
        var header = typeof(UpdatePurchaseOrderDraftCommand).GetProperties().Select(x => x.Name).ToArray();
        var line = typeof(UpdatePurchaseOrderDraftLineCommand).GetProperties().Select(x => x.Name).ToArray();
        foreach (var name in new[] { "CustomerId", "SupplierId", "OriginKind" })
            Assert.DoesNotContain(name, header);
        foreach (var name in new[] { "Quantity", "ProductId", "UnitSnapshot", "WarehouseSiteId", "UnlinkedQty", "AllocationId" })
            Assert.DoesNotContain(name, line);
        Assert.IsFalse(typeof(PurchaseOrderDetailDto).GetProperties().Any(x =>
            x.Name is "Head" or "Operations" or "ProgressHistory" or "Contributions"));
    }

    /// <summary>商务补丁保持原身份、单位、数量和收货仓，只改允许字段与金额。</summary>
    [TestMethod]
    public void CommercialPatchPreservesFixedIdentity()
    {
        var head = new PurchaseOrderHead { Uid = Guid.NewGuid(), OriginKind = 2, CustomerId = Guid.NewGuid(), SupplierId = Guid.NewGuid() };
        var line = new PurchaseOrderLine { Uid = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 12m,
            UnlinkedQty = 12m, UnitSnapshot = "PCS" };
        var identity = (head.CustomerId, head.SupplierId, line.ProductId, line.Quantity, line.UnitSnapshot);
        ProcurementOrderPolicy.ApplySave(head, [line], new UpdatePurchaseOrderDraftCommand
        {
            ExpectedVersion = 1, Lines = [new() { PurchaseOrderLineId = line.Uid, ExpectedVersion = 1, UnitPrice = 2m, TaxPrice = 3m, SupplierSku = "  S-1  " }]
        });
        Assert.AreEqual(identity, (head.CustomerId, head.SupplierId, line.ProductId, line.Quantity, line.UnitSnapshot));
        Assert.AreEqual(24m, line.UnitAmount);
        Assert.AreEqual(36m, line.TaxAmount);
        Assert.AreEqual("S-1", line.SupplierSku);
    }

    /// <summary>创建和商务保存都接受物理列最大长度，并同时拒绝越界，不能悄悄截断。</summary>
    [TestMethod]
    public void CreationAndSaveShareCommercialTextLimits()
    {
        foreach(var (skuLength,descriptionLength) in new[]{(300,2000),(301,1),(1,2001)})
        {
            var input=new CreateIndependentPurchaseOrderLineCommand
            {
                Quantity=1,UnitPrice=1,TaxPrice=1,SupplierSku=new string('型',skuLength),DescriptionZh=new string('述',descriptionLength)
            };
            var head=new PurchaseOrderHead();var line=new PurchaseOrderLine{Uid=Guid.NewGuid(),Quantity=1};
            var command=new UpdatePurchaseOrderDraftCommand
            {
                ExpectedVersion=1,Lines=[new(){PurchaseOrderLineId=line.Uid,ExpectedVersion=1,UnitPrice=1,TaxPrice=1,
                    SupplierSku=input.SupplierSku,DescriptionZh=input.DescriptionZh}]
            };
            if(skuLength<=300 && descriptionLength<=2000)
            {
                IndependentPurchaseOrderInputPolicy.ValidateLine(input);
                ProcurementOrderPolicy.ApplySave(head,[line],command);
                Assert.AreEqual(input.SupplierSku,line.SupplierSku);
                Assert.AreEqual(input.DescriptionZh,line.DescriptionZh);
            }
            else
            {
                Assert.ThrowsExactly<ProcurementOperationException>(()=>IndependentPurchaseOrderInputPolicy.ValidateLine(input));
                Assert.ThrowsExactly<ProcurementOperationException>(()=>ProcurementOrderPolicy.ApplySave(head,[line],command));
            }
        }
    }

    /// <summary>文本超长整次拒绝，不能将用户商务内容悄悄截断。</summary>
    [TestMethod]
    public void TextOverflowIsRejectedInsteadOfTruncated()
    {
        Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementOrderPolicy.Text("1234", 3, "内容"));
        Assert.IsNull(ProcurementOrderPolicy.Text("  ", 3, "内容"));
    }
}
