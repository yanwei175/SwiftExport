using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证真实业务命令维护计算联动，主图不参与填充，放弃修改恢复手工售价。</summary>
[TestClass]
public sealed class ProductFillDownBaselineTests
{
    /// <summary>填充采购价会正常重算，整行恢复基线会还原手工售价；不产生第二套历史。</summary>
    [TestMethod]
    public void PriceFill_ShouldRecalculateAndRejectRestoreManualPriceWhileMainImageIsDenied()
    {
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, null!, null!, null!);
        vm.Neutrals.LoadFromDtos([CreateRow("N-A", 80m, true), CreateRow("N-B", 40m, false)]);
        var source = vm.Neutrals.Items[0]; var target = vm.Neutrals.Items[1];
        var fill = new FillDownRequest(source, [target], [nameof(ProductNeutralLineModel.DefaultPurchasePriceCny)]);
        Assert.IsTrue(vm.FillDownCommand.CanExecute(fill));
        vm.FillDownCommand.Execute(fill);
        Assert.AreEqual(80m, target.DefaultPurchasePriceCny);
        Assert.AreEqual(20m, target.DefaultSalesPriceUsd);
        Assert.IsTrue(target.IsDirty);
        var dangerous = new FillDownRequest(source, [target], [nameof(ProductNeutralLineModel.IsMainImage)]);
        Assert.IsFalse(vm.FillDownCommand.CanExecute(dangerous));
        vm.FillDownCommand.Execute(dangerous);
        Assert.IsTrue(source.IsMainImage);
        Assert.IsFalse(target.IsMainImage);
        Assert.IsFalse(vm.FillDownCommand.CanExecute(new FillDownRequest(source, [target], [nameof(ProductNeutralLineModel.ImagePath)])));
        vm.Neutrals.RejectChanges();
        Assert.AreEqual(40m, target.DefaultPurchasePriceCny);
        Assert.AreEqual(17m, target.DefaultSalesPriceUsd);
        Assert.IsFalse(vm.Neutrals.HasChanges);
    }

    /// <summary>多列填充与显示顺序无关；显式手动售价最后生效，未选售价则保留正常计算。</summary>
    [TestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, true)]
    public void MultiColumnFill_ShouldRespectExplicitPriceRegardlessOfOrder(bool reverse, bool includePrice, bool nullPrice)
    {
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, null!, null!, null!);
        AssertMultiColumnFill(vm.Neutrals, vm.FillDownCommand, reverse, includePrice, nullPrice);
    }

    /// <summary>用两页真实命令共同验证显式售价、派生计算、通知和基线恢复，不复制生产计算算法。</summary>
    internal static void AssertMultiColumnFill(ProductNeutralListModel rows, System.Windows.Input.ICommand command,
        bool reverse, bool includePrice, bool nullPrice)
    {
        var sourceDto = CreateRow("N-A", 80m, true);
        sourceDto.Data.DefaultSalesPriceUsd = nullPrice ? null : 17m;
        var targetDto = CreateRow("N-B", 40m, false);
        targetDto.Data.DefaultExchangeRate = 10m;
        targetDto.Data.ProfitMargin = 0.5m;
        targetDto.Data.DefaultSalesPriceUsd = 9m;
        rows.LoadFromDtos([sourceDto, targetDto]);
        var source = rows.Items[0];
        var target = rows.Items[1];
        var notifications = 0;
        target.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(target.DefaultSalesPriceUsd)) notifications++; };
        var fields = new List<string>();
        if (includePrice) fields.Add(nameof(ProductNeutralLineModel.DefaultSalesPriceUsd));
        fields.AddRange([nameof(ProductNeutralLineModel.DefaultExchangeRate), nameof(ProductNeutralLineModel.ProfitMargin),
            nameof(ProductNeutralLineModel.DefaultPurchasePriceCny)]);
        if (reverse) fields.Reverse();
        var request = new FillDownRequest(source, [target], fields);
        Assert.IsTrue(command.CanExecute(request));
        command.Execute(request);
        Assert.AreEqual(includePrice ? source.DefaultSalesPriceUsd : 20m, target.DefaultSalesPriceUsd);
        Assert.AreEqual(80m, target.DefaultPurchasePriceCny);
        Assert.AreEqual(5m, target.DefaultExchangeRate);
        Assert.AreEqual(0.2m, target.ProfitMargin);
        Assert.IsTrue(target.IsDirty);
        Assert.IsGreaterThan(0, notifications);
        Assert.IsFalse(source.IsDirty);
        Assert.IsFalse(target.IsMainImage);
        // 后续普通编辑仍触发计算；填充不能通过抑制通知来绕过业务联动。
        target.DefaultPurchasePriceCny = 100m;
        Assert.AreEqual(25m, target.DefaultSalesPriceUsd);
        rows.RejectChanges();
        Assert.AreEqual(9m, target.DefaultSalesPriceUsd);
        Assert.AreEqual(40m, target.DefaultPurchasePriceCny);
        Assert.IsFalse(rows.HasChanges);
    }

    private static ProductNeutralDto CreateRow(string code, decimal price, bool main) => new()
    {
        Uid = Guid.NewGuid(),
        Data = new ProductNeutralWriteDto { ProductCode = code, ColorId = Guid.NewGuid(), IsMainImage = main,
            DefaultPurchasePriceCny = price, DefaultExchangeRate = 5m, ProfitMargin = 0.2m, DefaultSalesPriceUsd = 17m }
    };
}
