using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

[TestClass]
public sealed class CustomerModelsAndContractsTests
{
    [TestMethod]
    public void CustomerLine_LoadEditRevertAndHiddenFields_ShouldRoundTrip()
    {
        var line = new CustomerLineModel();
        line.Load(new CustomerDto
        {
            Uid = Guid.NewGuid(), Version = 3,
            CustomerCode = "C001", CustomerName = "客户一",
            LabelFolderPath = @"C:\labels", SpecSheetFolderPath = @"C:\specs",
            Incoterm = "FOB", DepositRate = 0.3m,
        });
        Assert.AreEqual(LineState.Unchanged, line.State);
        line.CustomerName = "修改";
        Assert.AreEqual(LineState.Modified, line.State);
        line.CustomerName = "客户一";
        Assert.AreEqual(LineState.Unchanged, line.State);
        var dto = line.ToDto();
        Assert.AreEqual(@"C:\labels", dto.LabelFolderPath);
        Assert.AreEqual(@"C:\specs", dto.SpecSheetFolderPath);
        Assert.AreEqual("FOB", dto.Incoterm);
    }

    [TestMethod]
    public void CustomerProduct_NonStandardAndPersistedFields_ShouldRoundTrip()
    {
        var line = new CustomerProductLineModel();
        line.Load(new CustomerProductDto
        {
            Uid = Guid.NewGuid(), Version = 2,
            CustomerId = Guid.NewGuid(), CustomerCode = "C001", CustomerSku = "CUSTOM-1",
            SkuId = null, Note = "保留备注", DescriptionEn = "Stored description",
            FeaturesEn = "Stored features", USDPrice = 12.5m, CNYPrice = 80m,
        });
        Assert.AreEqual(LineState.Unchanged, line.State);
        var dto = line.ToDto();
        Assert.IsNull(dto.SkuId);
        Assert.AreEqual("保留备注", dto.Note);
        Assert.AreEqual("Stored description", dto.DescriptionEn);
        Assert.AreEqual("Stored features", dto.FeaturesEn);
        Assert.AreEqual(12.5m, dto.USDPrice);
        Assert.AreEqual(80m, dto.CNYPrice);
    }

    [TestMethod]
    public void CustomerProduct_ShouldNotExposeUnitOrCurrency()
    {
        var list = new CustomerProductListModel();

        var line = list.AddNewItem(Guid.NewGuid(), "C001");

        Assert.IsNull(typeof(CustomerProductDto).GetProperty("CustomerSkuUnit"));
        Assert.IsNull(typeof(CustomerProductDto).GetProperty("Currency"));
        Assert.IsNull(typeof(CustomerProductLineModel).GetProperty("CustomerSkuUnit"));
        Assert.IsNull(typeof(CustomerProductLineModel).GetProperty("Currency"));
        Assert.AreEqual(LineState.Added, line.State);
    }

    [TestMethod]
    public void CustomerProductSelectionMode_ShouldDefaultToFullRow()
    {
        var viewModel = new CustomerProductManagerViewModel(
            null!, null!, null!, null!, null!);

        Assert.IsFalse(viewModel.IsCellSelectionMode);
        viewModel.IsCellSelectionMode = true;
        Assert.IsTrue(viewModel.IsCellSelectionMode);
    }

    [TestMethod]
    public void SaveResponse_ShouldUseClientKeyAndUpdateIdentityWithoutReload()
    {
        var list = new CustomerListModel();
        var line = list.AddNewCustomer();
        line.CustomerCode = "C001";
        line.CustomerName = "客户一";
        var request = list.CreateSaveRequest();
        var uid = Guid.NewGuid();
        list.ApplySaveResponse(new CustomerBatchSaveResponse
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
    public void CustomerProductApiAndNavigation_ShouldExposeGuidContractAndCustomersRoute()
    {
        var method = typeof(ICustomerProductApi).GetMethod(nameof(ICustomerProductApi.GetByCustomerSku));
        Assert.IsNotNull(method);
        Assert.AreEqual(typeof(Guid), method.GetParameters()[0].ParameterType);
        var route = method.GetCustomAttributes(typeof(GetAttribute), false).Cast<GetAttribute>().Single();
        Assert.AreEqual("/customersku/{sku}", route.Path);
        Assert.AreEqual(typeof(Guid?), typeof(CustomerProductDto).GetProperty(nameof(CustomerProductDto.SkuId))!.PropertyType);

        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute("customers", out var customerRoute));
        Assert.AreEqual("客户管理", customerRoute.Title);
        Assert.IsTrue(registry.TryGetRoute(CustomerProductWorkspaceViewModel.RouteKey, out var customerProductRoute));
        Assert.AreEqual(typeof(CustomerProductWorkspaceViewModel), customerProductRoute.ViewModelType);

        var customerGroup = registry.MenuItems.Single(item => item.RouteKey == "customers");
        Assert.AreEqual("客户", customerGroup.Title);
        CollectionAssert.AreEqual(
            new[] { "customers", CustomerProductWorkspaceViewModel.RouteKey },
            customerGroup.Children.Select(item => item.RouteKey).ToArray());
    }

    [TestMethod]
    public void ProductLookup_ShouldFillStandardFieldsWithoutOverwritingCustomerValues_AndClearExplicitly()
    {
        var viewModel = new CustomerProductManagerViewModel(
            null!, null!, null!, null!, null!);
        var line = new CustomerProductLineModel
        {
            CustomerPdDescription = "客户自定义描述",
            CustomerPdFeatures = string.Empty,
            UsdPrice = 99m,
            CnyPrice = null,
        };
        viewModel.SelectedCandidate = new SwiftExport.Contracts.DTOs.Products.ProductLookupDto
        {
            Uid = Guid.NewGuid(), Sku = "P-1", LinkSku = "L-1",
            DescriptionEn = "Standard", DescriptionZh = "标准中文", FeaturesEn = "Feature",
            CategoryName = "Category", MaterialType = "Material", MainImage = @"C:\p.jpg",
            USDPrice = 10m, CNYPrice = 70m,
        };

        viewModel.CommitProductCommand.Execute(line);

        Assert.AreEqual(viewModel.SelectedCandidate.Uid, line.SkuId);
        Assert.AreEqual("P-1", line.Sku);
        Assert.AreEqual("客户自定义描述", line.CustomerPdDescription, "客户自定义描述不得被标准值覆盖。");
        Assert.AreEqual("Feature", line.CustomerPdFeatures);
        Assert.AreEqual(99m, line.UsdPrice, "已有客户价不得覆盖。");
        Assert.AreEqual(70m, line.CnyPrice, "空价格可带入标准默认价。");

        viewModel.ClearProductCommand.Execute(line);
        Assert.IsNull(line.SkuId);
        Assert.AreEqual(string.Empty, line.Sku);
        Assert.AreEqual("客户自定义描述", line.CustomerPdDescription);
        Assert.AreEqual(99m, line.UsdPrice);
    }
}
