using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using System.IO;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证主产品契约、编辑模型变更集与导航入口。</summary>
[TestClass]
public sealed class ProductMasterModelsAndContractsTests
{
    /// <summary>验证 Refit 路由及每个异步接口都保留取消参数。</summary>
    [TestMethod]
    public void ProductMasterApi_ShouldMatchControllerRoutesAndExposeCancellation()
    {
        AssertRoute(nameof(IProductMasterApi.GetPagedAsync), "/page", typeof(PostAttribute));
        AssertRoute(nameof(IProductMasterApi.GetDetailAsync), "/{productMasterId}", typeof(GetAttribute));
        AssertRoute(nameof(IProductMasterApi.BatchSaveAsync), "/batch-save", typeof(PostAttribute));
        AssertRoute(nameof(IProductMasterApi.SaveDetailAsync), "/save-detail", typeof(PostAttribute));
        AssertRoute(nameof(IProductMasterApi.AssignCategoryAsync), "/assign-category", typeof(PostAttribute));
        AssertRoute(nameof(IProductMasterApi.SyncDescriptionsAsync), "/sync-descriptions", typeof(PostAttribute));
        AssertRoute(nameof(IProductMasterApi.SyncFeaturesAsync), "/sync-features", typeof(PostAttribute));
        AssertRoute(nameof(IProductMasterApi.SyncSupplierAsync), "/sync-supplier", typeof(PostAttribute));
        AssertRoute(nameof(IProductMasterApi.SyncWeightVolumeAsync), "/sync-weight-volume", typeof(PostAttribute));
        AssertRoute(nameof(IProductMasterApi.SyncAllAsync), "/sync-all", typeof(PostAttribute));
        foreach (var method in typeof(IProductMasterApi).GetMethods())
            Assert.AreEqual(typeof(CancellationToken), method.GetParameters().Last().ParameterType, method.Name);
    }

    /// <summary>验证主型号行加载、修改、回滚和批量请求映射。</summary>
    [TestMethod]
    public void ProductMasterLine_ShouldTrackAndBuildWriteSet()
    {
        var categoryId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var list = new ProductMasterListModel();
        list.LoadFromDtos([new ProductMasterListItemDto
        {
            Uid = Guid.NewGuid(), Version = 3, CategoryName = "五金", DefaultSupplierName = "供应商一",
            Data = new ProductMasterWriteDto
            {
                MasterSku = "MASTER-1", DefaultPcsPerPackage = 10,
                CategoryId = categoryId, DefaultSupplierId = supplierId
            }
        }]);
        var line = list.Items.Single();
        Assert.AreEqual(LineState.Unchanged, line.State);
        line.ProductTag = "新品";
        Assert.AreEqual(LineState.Modified, line.State);
        var request = list.CreateSaveRequest();
        Assert.AreEqual("MASTER-1", request.UpdateItems.Single().Data.MasterSku);
        Assert.AreEqual(categoryId, request.UpdateItems.Single().Data.CategoryId);
        line.RejectChanges();
        Assert.AreEqual(LineState.Unchanged, line.State);
        Assert.AreEqual(string.Empty, line.ProductTag);
    }

    /// <summary>验证保存成功但刷新失败时仍保留服务端主键和版本。</summary>
    [TestMethod]
    public void ProductMasterList_ShouldApplyServerIdentityBeforeRefresh()
    {
        var list = new ProductMasterListModel();
        var line = list.AddNewMaster();
        line.MasterSku = "MASTER-NEW";
        line.CategoryId = Guid.NewGuid();
        line.DefaultSupplierId = Guid.NewGuid();
        var request = list.CreateSaveRequest();
        var uid = Guid.NewGuid();
        list.ApplySaveResponse(new ProductMasterBatchSaveResponse
        {
            TotalCount = 1,
            Items =
            {
                new SaveItemResult
                {
                    ClientKey = request.CreateItems.Single().ClientKey,
                    Operation = SaveOperation.Create,
                    Success = true,
                    Uid = uid,
                    NewVersion = 1
                }
            }
        });
        Assert.AreEqual(uid, line.Uid);
        Assert.AreEqual(1, line.Version);
        Assert.AreEqual(LineState.Unchanged, line.State);
    }

    /// <summary>验证中性产品新增、数值校验及详情聚合变更集。</summary>
    [TestMethod]
    public void ProductNeutralList_ShouldBuildAggregateChangesWithManualPriceOverride()
    {
        var list = new ProductNeutralListModel();
        var line = list.AddNewNeutral(Guid.NewGuid());
        line.ProductCode = "N-001";
        line.ColorId = Guid.NewGuid();
        line.DefaultSalesPriceUsd = 12.34m;
        line.DefaultExchangeRate = 7.2m;
        line.ProfitMargin = -0.1m;
        line.DefaultPurchasePriceCny = 50m;
        line.IsMainImage = true;
        Assert.AreEqual(6.31m, line.DefaultSalesPriceUsd);
        Assert.IsNull(line.ValidateRow(), "负利润率仍允许参与售价计算。");
        line.DefaultSalesPriceUsd = 12.34m;

        var request = new SaveProductMasterDetailRequest();
        list.ApplyChanges(request);
        var data = request.CreateNeutrals.Single().Data;
        Assert.AreEqual(12.34m, data.DefaultSalesPriceUsd);
        Assert.AreEqual(7.2m, data.DefaultExchangeRate);
        Assert.AreEqual(-0.1m, data.ProfitMargin);
        Assert.AreEqual(50m, data.DefaultPurchasePriceCny);
        Assert.IsTrue(data.IsMainImage);
    }

    /// <summary>验证勾选新的默认主图时会取消同一详情内原有默认行。</summary>
    [TestMethod]
    public void ProductNeutralList_SetMainImage_ShouldKeepSingleDefault()
    {
        var list = new ProductNeutralListModel();
        var first = list.AddNewNeutral(Guid.NewGuid());
        var second = list.AddNewNeutral(Guid.NewGuid());
        first.IsMainImage = true;
        second.IsMainImage = true;

        list.SetMainImage(second);

        Assert.IsFalse(first.IsMainImage);
        Assert.IsTrue(second.IsMainImage);
    }

    /// <summary>验证主型号编码必填，分类、默认供应商和默认装率允许暂不设置。</summary>
    [TestMethod]
    public void ProductMasterValidation_ShouldAllowUnsetOptionalValues()
    {
        var line = new ProductMasterLineModel
        {
            MasterSku = "MASTER-OPTIONAL",
            CategoryId = Guid.Empty,
            DefaultSupplierId = Guid.Empty,
            DefaultPcsPerPackage = null,
            DefaultNetWeight = -1
        };
        var detail = new ProductMasterEditModel
        {
            MasterSku = "MASTER-OPTIONAL",
            CategoryId = Guid.Empty,
            DefaultSupplierId = Guid.Empty,
            DefaultPcsPerPackage = null,
            DefaultGrossWeight = -1
        };

        Assert.IsNull(line.ValidateRow());
        Assert.IsNull(detail.ValidateRow());
        line.MasterSku = " ";
        detail.MasterSku = string.Empty;
        Assert.AreEqual("主型号编码不能为空。", line.ValidateRow());
        Assert.AreEqual("主型号编码不能为空。", detail.ValidateRow());
    }

    /// <summary>验证列表主图投影能够进入只读行模型。</summary>
    [TestMethod]
    public void ProductMasterList_ShouldMapMainImagePath()
    {
        var list = new ProductMasterListModel();
        list.LoadFromDtos([new ProductMasterListItemDto
        {
            Uid = Guid.NewGuid(),
            MainImagePath = @"images\main.jpg",
            DefaultSalesPriceUsd = 18.5m,
            DefaultPurchasePriceCny = 72m,
            Data = new ProductMasterWriteDto { MasterSku = "MASTER-IMAGE" }
        }]);

        var line = list.Items.Single();
        Assert.AreEqual(@"images\main.jpg", line.MainImagePath);
        Assert.AreEqual(18.5m, line.DefaultSalesPriceUsd);
        Assert.AreEqual(72m, line.DefaultPurchasePriceCny);
    }

    /// <summary>验证管理页和详情页使用单一 CurrentRow 桥接及可切换选择模式。</summary>
    [TestMethod]
    public void ProductMasterViews_ShouldExposeSearchImageAndSelectionSwitches()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var manager = XDocument.Load(ViewPath("ProductMasterManagerView.xaml"));
        foreach (var command in new[] { "SyncDescriptionsCommand", "SyncFeaturesCommand", "SyncSupplierCommand", "SyncWeightVolumeCommand", "SyncAllCommand" })
        {
            var menu = manager.Descendants(presentation + "MenuItem")
                .Single(item => ((string?)item.Attribute("Command"))?.Contains(command, StringComparison.Ordinal) == true);
            StringAssert.Contains((string?)menu.Attribute("Command"), "PlacementTarget.DataContext.");
        }
        var managerGrid = manager.Descendants(presentation + "DataGrid")
            .Single(grid => (string?)grid.Attribute("ItemsSource") == "{Binding Masters.Items}");
        var searchBox = manager.Descendants().Single(element =>
            element.Name.LocalName == "TextBox" && ((string?)element.Attribute("Text"))?.Contains("Keyword", StringComparison.Ordinal) == true);
        Assert.IsTrue(searchBox.Descendants(presentation + "KeyBinding")
            .Any(binding => (string?)binding.Attribute("Key") == "Enter"
                && ((string?)binding.Attribute("Command"))?.Contains("SearchCommand", StringComparison.Ordinal) == true));
        Assert.IsNull(managerGrid.Attribute("SelectedItem"));
        StringAssert.Contains((string?)managerGrid.Attribute("SelectionUnit"), "GridSelectionUnitConverter");
        var mainImageColumn = managerGrid.Descendants(presentation + "DataGridTemplateColumn")
            .Single(column => (string?)column.Attribute("Header") == "图片");
        Assert.AreEqual("True", (string?)mainImageColumn.Attribute("IsReadOnly"));
        StringAssert.Contains(mainImageColumn.ToString(), "MainImagePath");
        foreach (var header in new[] { "英文描述", "英文配置" })
        {
            var column = managerGrid.Descendants(presentation + "DataGridTextColumn")
                .Single(item => (string?)item.Attribute("Header") == header);
            StringAssert.Contains((string?)column.Attribute("Visibility"), "IsEnglishFieldsVisible");
        }
        var cardList = manager.Descendants(presentation + "ListBox").Single();
        StringAssert.Contains((string?)cardList.Attribute("ItemContainerStyle"), "ProductMasterCardItemStyle");
        Assert.IsNotNull(cardList.Descendants(presentation + "WrapPanel").SingleOrDefault());
        var cardTemplate = cardList.Element(presentation + "ListBox.ItemTemplate")!.ToString();
        foreach (var field in new[]
        {
            "MainImagePath", "MasterSku", "DefaultDescriptionEn",
            "DefaultSalesPriceUsd", "DefaultPurchasePriceCny", "DefaultSupplierName"
        })
            StringAssert.Contains(cardTemplate, field);

        var detail = XDocument.Load(ViewPath("ProductMasterDetailView.xaml"));
        var detailGrid = detail.Descendants(presentation + "DataGrid").Single();
        Assert.IsNull(detailGrid.Attribute("SelectedItem"));
        StringAssert.Contains((string?)detailGrid.Attribute("SelectionUnit"), "GridSelectionUnitConverter");
        var mainImageFlag = detailGrid.Descendants(presentation + "DataGridTemplateColumn")
            .Single(column => (string?)column.Attribute("Header") == "默认主图");
        StringAssert.Contains(mainImageFlag.ToString(), "IsMainImage");
        StringAssert.Contains(mainImageFlag.ToString(), "SetMainImageCommand");
    }

    /// <summary>验证主产品管理页为菜单入口而详情仅作为文档路由。</summary>
    [TestMethod]
    public void Navigation_ShouldRegisterManagerAndDetailRoutes()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(ProductMasterManagerViewModel.RouteKey, out var manager));
        Assert.AreEqual(typeof(ProductMasterManagerViewModel), manager.ViewModelType);
        Assert.IsTrue(registry.TryGetRoute(ProductMasterDetailViewModel.RouteKey, out var detail));
        Assert.AreEqual(typeof(ProductMasterDetailViewModel), detail.ViewModelType);
        var products = registry.MenuItems.Single(item => item.RouteKey == "products");
        Assert.IsTrue(products.Children.Any(item => item.RouteKey == ProductMasterManagerViewModel.RouteKey));
        Assert.IsFalse(products.Children.Any(item => item.RouteKey == ProductMasterDetailViewModel.RouteKey));
    }

    private static void AssertRoute(string methodName, string expectedPath, Type attributeType)
    {
        var method = typeof(IProductMasterApi).GetMethod(methodName)!;
        var attribute = method.GetCustomAttributes(attributeType, false).Single();
        var path = (string)attributeType.GetProperty("Path")!.GetValue(attribute)!;
        Assert.AreEqual(expectedPath, path);
    }

    private static string ViewPath(string fileName) => Path.Combine(
        FindRepositoryRoot(),
        "SwiftExport.Client.Wpf",
        "Features",
        "Products",
        "ProductList",
        "Views",
        fileName);

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            }
        }

        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }

}
