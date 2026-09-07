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

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证中性产品管理专用契约、编辑模型、主图互斥和客户端注册。</summary>
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

    /// <summary>当前页只取消同一主型号的其他主图，不影响不同主型号。</summary>
    [TestMethod]
    public void SetMainImage_ShouldBeExclusiveWithinSameMaster()
    {
        var firstMaster = Guid.NewGuid();
        var secondMaster = Guid.NewGuid();
        var list = new ProductNeutralManagerListModel();
        var first = list.AddNewNeutral();
        var replacement = list.AddNewNeutral();
        var unrelated = list.AddNewNeutral();
        first.ProductMasterId = firstMaster;
        replacement.ProductMasterId = firstMaster;
        unrelated.ProductMasterId = secondMaster;
        first.IsMainImage = true;
        replacement.IsMainImage = true;
        unrelated.IsMainImage = true;

        list.SetMainImage(replacement);

        Assert.IsFalse(first.IsMainImage);
        Assert.IsTrue(replacement.IsMainImage);
        Assert.IsTrue(unrelated.IsMainImage);
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
}
