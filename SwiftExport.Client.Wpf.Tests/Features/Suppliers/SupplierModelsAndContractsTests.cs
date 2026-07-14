using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;

namespace SwiftExport.Client.Wpf.Tests.Features.Suppliers;

[TestClass]
public sealed class SupplierModelsAndContractsTests
{
    [TestMethod]
    public void SupplierLine_LoadEditRevertAndMappedFields_ShouldRoundTrip()
    {
        var line = new SupplierLineModel();
        line.Load(new SupplierDto
        {
            Uid = Guid.NewGuid(), Version = 3,
            SupplierCode = "S001", SupplierName = "供应商一", DocRelateNo = "DOC-S001",
            ZheKou = 0.8m, ZheKouBeiZhu = "年度折扣", DefaultLeadTime = 15,
        });
        Assert.AreEqual(LineState.Unchanged, line.State);
        line.SupplierName = "修改";
        Assert.AreEqual(LineState.Modified, line.State);
        line.SupplierName = "供应商一";
        Assert.AreEqual(LineState.Unchanged, line.State);
        var dto = line.ToDto();
        Assert.AreEqual(0.8m, dto.ZheKou);
        Assert.AreEqual("年度折扣", dto.ZheKouBeiZhu);
        Assert.AreEqual(15, dto.DefaultLeadTime);
    }

    [TestMethod]
    public void SupplierProduct_PersistedAndDisplayFields_ShouldRoundTrip()
    {
        var line = new SupplierProductLineModel();
        line.Load(new SupplierProductDto
        {
            Uid = Guid.NewGuid(), Version = 2,
            SupplierId = Guid.NewGuid(), SupplierCode = "S001", SupplierName = "供应商一",
            SkuId = Guid.NewGuid(), ProductSku = "P-1", SupplierSku = "SP-1",
            SupplierSkuDescriptionZh = "供应商描述", SupplierSkuFeaturesZh = "供应商规格",
            UnitPrice = 12.5m, TaxPrice = 14.13m, MainImage = @"C:\p.jpg",
        });
        Assert.AreEqual(LineState.Unchanged, line.State);
        var dto = line.ToDto();
        Assert.AreEqual("SP-1", dto.SupplierSku);
        Assert.AreEqual("供应商描述", dto.SupplierSkuDescriptionZh);
        Assert.AreEqual("供应商规格", dto.SupplierSkuFeaturesZh);
        Assert.AreEqual(12.5m, dto.UnitPrice);
        Assert.AreEqual(14.13m, dto.TaxPrice);
        Assert.AreEqual(@"C:\p.jpg", dto.MainImage);
    }

    [TestMethod]
    public void SaveResponse_ShouldUseClientKeyAndUpdateIdentityWithoutReload()
    {
        var list = new SupplierListModel();
        var line = list.AddNewSupplier();
        line.SupplierCode = "S001";
        line.SupplierName = "供应商一";
        line.DocRelateNo = "DOC-S001";
        var request = list.CreateSaveRequest();
        var uid = Guid.NewGuid();
        list.ApplySaveResponse(new SupplierBatchSaveResponse
        {
            TotalCount = 1,
            Items =
            {
                new SaveItemResult
                {
                    ClientKey = request.CreateItems[0].ClientKey,
                    RowIndex = request.CreateItems[0].RowIndex,
                    Operation = SaveOperation.Create,
                    Success = true,
                    Uid = uid,
                    NewVersion = 1,
                },
            },
        });
        Assert.AreSame(line, list.Items.Single());
        Assert.AreEqual(uid, line.Uid);
        Assert.AreEqual(1, line.Version);
        Assert.AreEqual(LineState.Unchanged, line.State);
    }

    [TestMethod]
    public void SupplierProductApiAndNavigation_ShouldExposeNewRouteAndRemovePlaceholder()
    {
        var page = typeof(ISupplierProductApi).GetMethod(nameof(ISupplierProductApi.GetPagedAsync));
        var batchSave = typeof(ISupplierProductApi).GetMethod(nameof(ISupplierProductApi.BatchSaveAsync));
        Assert.AreEqual("/page", page!.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.AreEqual("/batch-save", batchSave!.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.AreEqual(typeof(Guid), typeof(SupplierProductDto).GetProperty(nameof(SupplierProductDto.SupplierId))!.PropertyType);
        Assert.AreEqual(typeof(Guid), typeof(SupplierProductDto).GetProperty(nameof(SupplierProductDto.SkuId))!.PropertyType);

        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute("suppliers", out var route));
        Assert.AreEqual("供应商管理", route.Title);
        Assert.IsFalse(registry.TryGetRoute("supplier-products", out _));
    }

    [TestMethod]
    public void ProductLookup_ShouldFillStandardFieldsWithoutOverwritingSupplierValuesOrPrices_AndClearExplicitly()
    {
        var viewModel = new SupplierProductManagerViewModel(null!, null!, null!, null!, null!);
        var line = new SupplierProductLineModel
        {
            SupplierSkuDescriptionZh = "供应商自定义描述",
            SupplierSkuFeaturesZh = string.Empty,
            UnitPrice = 99m,
            TaxPrice = 110m,
        };
        viewModel.SelectedCandidate = new ProductLookupDto
        {
            Uid = Guid.NewGuid(), Sku = "P-1", LinkSku = "L-1",
            DescriptionEn = "Standard", DescriptionZh = "标准中文", FeaturesEn = "Feature", FeaturesZh = "中文规格",
            CategoryName = "Category", MaterialType = "Material", MainImage = @"C:\p.jpg",
            USDPrice = 10m, CNYPrice = 70m,
        };

        viewModel.CommitProductCommand.Execute(line);

        Assert.AreEqual(viewModel.SelectedCandidate.Uid, line.SkuId);
        Assert.AreEqual("P-1", line.ProductSku);
        Assert.AreEqual("供应商自定义描述", line.SupplierSkuDescriptionZh);
        Assert.AreEqual("中文规格", line.SupplierSkuFeaturesZh);
        Assert.AreEqual(99m, line.UnitPrice, "供应商未税价不得从销售价自动带入。");
        Assert.AreEqual(110m, line.TaxPrice, "供应商含税价不得从销售价自动带入。");

        viewModel.ClearProductCommand.Execute(line);
        Assert.AreEqual(Guid.Empty, line.SkuId);
        Assert.AreEqual(string.Empty, line.ProductSku);
        Assert.AreEqual("供应商自定义描述", line.SupplierSkuDescriptionZh);
        Assert.AreEqual(99m, line.UnitPrice);
    }
}
