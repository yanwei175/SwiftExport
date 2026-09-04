using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证中性产品售价联动、手动覆盖以及基线恢复边界。</summary>
[TestClass]
public sealed class ProductMasterPriceCalculationTests
{
    /// <summary>三个依赖各自触发计算；手动售价不反算，直到依赖再次变化。</summary>
    [TestMethod]
    public void Dependencies_ShouldRecalculateAndAllowManualOverride()
    {
        var line = CreateLoadedLine();
        line.DefaultPurchasePriceCny = 80m;
        Assert.AreEqual(20m, line.DefaultSalesPriceUsd);
        line.ProfitMargin = 0.5m;
        Assert.AreEqual(32m, line.DefaultSalesPriceUsd);
        line.DefaultExchangeRate = 8m;
        Assert.AreEqual(20m, line.DefaultSalesPriceUsd);
        line.DefaultSalesPriceUsd = 99m;
        Assert.AreEqual(80m, line.DefaultPurchasePriceCny);
        Assert.AreEqual(0.5m, line.ProfitMargin);
        Assert.AreEqual(8m, line.DefaultExchangeRate);
        Assert.AreEqual(99m, line.ToWriteDto().DefaultSalesPriceUsd);
        line.DefaultPurchasePriceCny = 40m;
        Assert.AreEqual(10m, line.DefaultSalesPriceUsd);
        Assert.AreEqual(LineState.Modified, line.State);
    }

    /// <summary>使用两位商业四舍五入，零采购价仍能正常计算。</summary>
    [TestMethod]
    public void Calculation_ShouldRoundHalfAwayFromZero()
    {
        var line = new ProductNeutralLineModel
        {
            ProfitMargin = 0m, DefaultExchangeRate = 1m, DefaultPurchasePriceCny = 1.005m
        };
        Assert.AreEqual(1.01m, line.DefaultSalesPriceUsd);
        line.DefaultPurchasePriceCny = 0m;
        Assert.AreEqual(0m, line.DefaultSalesPriceUsd);
    }

    /// <summary>载入、撤销和服务端重新载入保留持久化的手动售价及干净基线。</summary>
    [TestMethod]
    public void Baseline_ShouldPreserveManualPriceDuringLoadAndReject()
    {
        var line = CreateLoadedLine();
        Assert.AreEqual(17m, line.DefaultSalesPriceUsd);
        Assert.AreEqual(LineState.Unchanged, line.State);
        line.DefaultPurchasePriceCny = 80m;
        line.DefaultExchangeRate = 8m;
        line.ProfitMargin = 0.5m;
        line.RejectChanges();
        Assert.AreEqual(40m, line.DefaultPurchasePriceCny);
        Assert.AreEqual(5m, line.DefaultExchangeRate);
        Assert.AreEqual(0.2m, line.ProfitMargin);
        Assert.AreEqual(17m, line.DefaultSalesPriceUsd);
        Assert.AreEqual(LineState.Unchanged, line.State);

        var saved = line.ToDto();
        saved.Data.DefaultSalesPriceUsd = 23m;
        saved.Data.DefaultExchangeRate = 7m;
        saved.Data.ProfitMargin = 0.3m;
        saved.Data.DefaultPurchasePriceCny = 90m;
        line.Load(saved);
        Assert.AreEqual(23m, line.DefaultSalesPriceUsd);
        Assert.AreEqual(LineState.Unchanged, line.State);
    }

    /// <summary>缺项、除零和超出数值范围不抛异常，也不清空已有售价。</summary>
    [TestMethod]
    public void IncompleteOrUncomputableInputs_ShouldKeepCurrentPrice()
    {
        Action<ProductNeutralLineModel>[] edits =
        [
            line => line.DefaultPurchasePriceCny = null,
            line => line.DefaultExchangeRate = null,
            line => line.ProfitMargin = null,
            line => line.DefaultExchangeRate = 0m,
            line => line.DefaultExchangeRate = -1m,
            line => line.DefaultPurchasePriceCny = -1m,
            line => line.ProfitMargin = 1m,
            line => line.DefaultPurchasePriceCny = decimal.MaxValue,
            line => line.ProfitMargin = decimal.MinValue
        ];
        foreach (var edit in edits)
        {
            var line = CreateLoadedLine();
            edit(line);
            Assert.AreEqual(17m, line.DefaultSalesPriceUsd);
        }
    }

    /// <summary>构造公式结果与持久化售价不同的基线，以检测程序性赋值误触发计算。</summary>
    private static ProductNeutralLineModel CreateLoadedLine()
    {
        var line = new ProductNeutralLineModel();
        line.Load(new ProductNeutralDto
        {
            Uid = Guid.NewGuid(),
            Data = new ProductNeutralWriteDto
            {
                ProductCode = "N-PRICE", ColorId = Guid.NewGuid(),
                DefaultPurchasePriceCny = 40m, DefaultExchangeRate = 5m,
                ProfitMargin = 0.2m, DefaultSalesPriceUsd = 17m
            }
        });
        return line;
    }
}
