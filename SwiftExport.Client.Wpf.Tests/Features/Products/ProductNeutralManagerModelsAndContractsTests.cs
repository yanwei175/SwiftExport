using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Mvvm;
using System.IO;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证中性产品管理专用契约、编辑模型、工具栏颜色多选和客户端注册。</summary>
[TestClass]
public sealed class ProductNeutralManagerModelsAndContractsTests
{
    /// <summary>专用 Refit API 必须与 Controller 路由一致并公开取消参数。</summary>
    [TestMethod]
    public void Api_ShouldExposeDedicatedRoutesAndCancellation()
    {
        AssertRoute(nameof(IProductNeutralManagerApi.GetPagedAsync), "/page");
        AssertRoute(nameof(IProductNeutralManagerApi.BatchSaveAsync), "/batch-save");
        foreach (var method in typeof(IProductNeutralManagerApi).GetMethods())
            Assert.AreEqual(typeof(CancellationToken), method.GetParameters().Last().ParameterType, method.Name);

        var services = new ServiceCollection();
        services.AddSwiftExportApis();
        Assert.IsTrue(services.Any(item => item.ServiceType == typeof(IProductNeutralManagerApi)));
    }

    /// <summary>列表投影应完整进入行模型，并把重新归属及可空数值写入专用变更集。</summary>
    [TestMethod]
    public void List_ShouldMapReassignmentAndBuildDedicatedRequest()
    {
        var oldMaster = Guid.NewGuid();
        var newMaster = Guid.NewGuid();
        var color = Guid.NewGuid();
        var list = new ProductNeutralManagerListModel();
        list.LoadFromDtos([new ProductNeutralManagerListItemDto
        {
            Uid = Guid.NewGuid(),
            Version = 3,
            MasterSku = "MASTER-OLD",
            ProductMasterDescriptionZh = "旧描述",
            CategoryName = "旧分类",
            ColorName = "红色",
            Data = new ProductNeutralManagerWriteDto
            {
                ProductMasterId = oldMaster,
                ProductCode = "N-001",
                ColorId = color
            }
        }]);

        var line = list.Items.Single();
        Assert.AreEqual(LineState.Unchanged, line.State);
        line.ProductMasterId = newMaster;
        line.MasterSku = "MASTER-NEW";
        line.DefaultExchangeRate = null;
        line.DefaultSalesPriceUsd = null;
        var request = list.CreateSaveRequest();

        Assert.AreEqual(LineState.Modified, line.State);
        Assert.AreEqual(newMaster, request.UpdateItems.Single().Data.ProductMasterId);
        Assert.IsNull(request.UpdateItems.Single().Data.DefaultExchangeRate);
        Assert.IsNull(request.UpdateItems.Single().Data.DefaultSalesPriceUsd);
    }

    /// <summary>建议售价按既有公式计算且允许手工覆盖，空数值不会触发业务验证。</summary>
    [TestMethod]
    public void Line_ShouldCalculateSuggestedPriceAndAllowNullableNumbers()
    {
        var line = new ProductNeutralManagerLineModel
        {
            ProductMasterId = Guid.NewGuid(),
            ProductCode = "N-PRICE",
            ColorId = Guid.NewGuid(),
            DefaultPurchasePriceCny = 70m,
            ProfitMargin = 0.2m,
            DefaultExchangeRate = 7m
        };
        Assert.AreEqual(12.50m, line.DefaultSalesPriceUsd);
        line.DefaultSalesPriceUsd = 13.25m;
        Assert.IsNull(line.ValidateRow());

        line.DefaultExchangeRate = null;
        line.DefaultPurchasePriceCny = null;
        line.ProfitMargin = null;
        Assert.AreEqual(13.25m, line.DefaultSalesPriceUsd);
        Assert.IsNull(line.ValidateRow());
    }

    /// <summary>查询契约应支持多个颜色，Manager 写入契约不再暴露默认主图设置。</summary>
    [TestMethod]
    public void Contracts_ShouldSupportMultipleColorsWithoutMainImageEditing()
    {
        var colors = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var request = new ProductNeutralManagerSearchRequest { ColorIds = colors.ToList() };

        CollectionAssert.AreEqual(colors, request.ColorIds);
        Assert.IsNull(typeof(ProductNeutralManagerWriteDto).GetProperty("IsMainImage"));
    }

    /// <summary>页面应只保留工具栏颜色多选，并且不再公开默认主图编辑入口。</summary>
    [TestMethod]
    public void View_ShouldUseToolbarMultiColorFilterWithoutMainImageColumn()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Products", "ProductList", "Views", "ProductNeutralManagerView.xaml");

        StringAssert.Contains(view, "<filter:MultiSelectLookupButton");
        StringAssert.Contains(view, "SelectedItems=\"{Binding FilterColorList, Mode=OneWay}\"");
        Assert.IsFalse(view.Contains("<productComponents:ProductColorFilterList", StringComparison.Ordinal));
        Assert.IsFalse(view.Contains("ColorFilter", StringComparison.Ordinal));
        Assert.IsFalse(view.Contains("<controls:DropDownCheckBox", StringComparison.Ordinal));
        Assert.IsFalse(view.Contains("默认主图", StringComparison.Ordinal));
        Assert.IsFalse(view.Contains("SetMainImageCommand", StringComparison.Ordinal));
    }

    /// <summary>保存结果应回填新增行身份与版本，并接纳全部变更。</summary>
    [TestMethod]
    public void SaveResponse_ShouldAcceptServerIdentityAndVersion()
    {
        var list = new ProductNeutralManagerListModel();
        var line = list.AddNewNeutral();
        line.ProductMasterId = Guid.NewGuid();
        line.ProductCode = "N-NEW";
        line.ColorId = Guid.NewGuid();
        var request = list.CreateSaveRequest();
        var uid = Guid.NewGuid();

        list.ApplySaveResponse(new ProductNeutralManagerBatchSaveResponse
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

    /// <summary>固定路由与产品菜单必须注册中性产品管理页。</summary>
    [TestMethod]
    public void Navigation_ShouldRegisterManagerRouteAndMenu()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(ProductNeutralManagerViewModel.RouteKey, out var route));
        Assert.AreEqual(typeof(ProductNeutralManagerViewModel), route.ViewModelType);
        var products = registry.MenuItems.Single(item => item.RouteKey == "products");
        Assert.IsTrue(products.Children!.Any(item => item.RouteKey == ProductNeutralManagerViewModel.RouteKey));
    }

    private static void AssertRoute(string methodName, string path)
    {
        var method = typeof(IProductNeutralManagerApi).GetMethod(methodName)!;
        var post = method.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single();
        Assert.AreEqual(path, post.Path);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

    private static string RepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
