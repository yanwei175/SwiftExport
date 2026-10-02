using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.SalesOrders.Draft;
using SwiftExport.Application.Services.SalesOrders.Internal;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.SalesOrders;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>核对销售草稿行单位快照的取值、保留和必填边界。</summary>
[TestClass]
public sealed class SalesOrderUnitSnapshotTests
{
    /// <summary>标准行首次选择产品取得当前单位，同产品重存保留快照，换产品重取。</summary>
    [TestMethod]
    public void StandardLine_CapturesAndPreservesUnitUntilProductChanges()
    {
        var first = new Product { Uid = Guid.NewGuid(), Sku = "A", Unit = "PCS" };
        var second = new Product { Uid = Guid.NewGuid(), Sku = "B", Unit = "SET" };
        var line = new SalesOrderLine { SalesOrderId = Guid.NewGuid(), Sku = string.Empty };
        var dto = StandardLine(first);

        Apply(line, dto, first);
        Assert.AreEqual("PCS", line.UnitSnapshot);

        first.Unit = "BOX";
        Apply(line, dto, first);
        Assert.AreEqual("PCS", line.UnitSnapshot);

        dto.ProductId = second.Uid;
        dto.Sku = second.Sku;
        Apply(line, dto, second);
        Assert.AreEqual("SET", line.UnitSnapshot);
    }

    /// <summary>产品单位缺失时，即使订单行已有快照也不能再次保存。</summary>
    [TestMethod]
    public void StandardLine_RejectsProductWithoutUnit()
    {
        var product = new Product { Uid = Guid.NewGuid(), Sku = "A", Unit = " " };
        var line = new SalesOrderLine { SalesOrderId = Guid.NewGuid(), Sku = product.Sku,
            ProductId = product.Uid, UnitSnapshot = "PCS" };

        Assert.ThrowsExactly<SalesOrderValidationException>(() => Apply(line, StandardLine(product), product));
    }

    /// <summary>同一产品的未知历史单位不能由当前产品默认补齐。</summary>
    [TestMethod]
    public void UnknownHistoricalUnitIsNotInferred()
    {
        var product = new Product { Uid = Guid.NewGuid(), Sku = "A", Unit = "PCS" };
        var line = new SalesOrderLine { SalesOrderId = Guid.NewGuid(), ProductId = product.Uid, Sku = product.Sku, UnitSnapshot = string.Empty };
        Assert.ThrowsExactly<SalesOrderValidationException>(() => Apply(line, StandardLine(product), product));
        Assert.AreEqual(string.Empty, line.UnitSnapshot);
    }

    /// <summary>非标行必须由用户明确提供单位，保存时去除首尾空格。</summary>
    [TestMethod]
    public void NonStandardLine_RequiresExplicitUnit()
    {
        var dto = new SalesOrderEditLineDto
        {
            Uid = Guid.NewGuid(), IsNonStandard = true, Qty = 1, DescriptionEn = "Custom"
        };
        var line = new SalesOrderLine { SalesOrderId = Guid.NewGuid(), Sku = string.Empty };

        Assert.ThrowsExactly<SalesOrderValidationException>(() => Apply(line, dto));
        dto.UnitSnapshot = " SET ";
        Apply(line, dto);
        Assert.AreEqual("SET", line.UnitSnapshot);
    }

    /// <summary>通过草稿保存实际使用的单行规则执行测试，并还原业务拒绝异常。</summary>
    private static void Apply(SalesOrderLine line, SalesOrderEditLineDto dto, params Product[] products)
    {
        var method = typeof(SalesOrderDraftService).GetMethod("ApplyLineChanges", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            method.Invoke(null,
            [line, dto, 1, new SalesOrderHeader { OrderNo = "SO-1" }, new Customer { CustomerCode = "C", DocRelateNo = "C" },
                products.ToDictionary(product => product.Uid), new Dictionary<Guid, CustomerProduct>()]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is SalesOrderValidationException validation)
        {
            throw validation;
        }
    }

    /// <summary>构造标准销售行输入；单位字段故意不由客户端决定。</summary>
    private static SalesOrderEditLineDto StandardLine(Product product) => new()
    {
        Uid = Guid.NewGuid(), ProductId = product.Uid, Sku = product.Sku, Qty = 1,
        UnitSnapshot = "CLIENT-IGNORED"
    };
}
