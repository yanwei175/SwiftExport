using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.WebApi.Controllers.Products.ProductMasters;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证主型号被动下发退出契约；物料主动获取由独立 Pull 契约测试覆盖。</summary>
[TestClass]
public sealed class ProductMasterSyncApiContractTests
{
    /// <summary>主型号 HTTP、服务及仓储不再暴露按来源批量覆盖物料的入口。</summary>
    [TestMethod]
    public void MasterPushEndpointsAndRepositoryMethods_AreRemoved()
    {
        Assert.IsFalse(typeof(IProductMasterApi).GetMethods().Any(method =>
            method.GetCustomAttribute<PostAttribute>()?.Path.StartsWith("/sync-", StringComparison.Ordinal) == true));
        Assert.IsFalse(typeof(ProductMasterController).GetMethods().Any(method =>
            method.GetCustomAttribute<HttpPostAttribute>()?.Template?.StartsWith("sync-", StringComparison.Ordinal) == true));
        Assert.IsNull(typeof(IProductMasterMaintenanceService).GetMethod("SyncProductsAsync"));
        Assert.IsNull(typeof(IProductRepository).GetMethod("SyncFromProductMasterAsync"));
        Assert.IsNull(typeof(IProductRepository).GetMethod("SyncSelectedFieldsFromMastersAsync"));
    }

    /// <summary>列表和详情保存请求不再携带覆盖物料的开关，旧选项不能成为保存隐式副作用。</summary>
    [TestMethod]
    public void MasterSaveRequests_DoNotExposeProductOverwriteOptions()
    {
        foreach (var type in new[] { typeof(ProductMasterBatchSaveRequest), typeof(SaveProductMasterDetailRequest) })
        {
            Assert.IsNull(type.GetProperty("UpdateProductDescriptions"), type.Name);
            Assert.IsNull(type.GetProperty("UpdateProductDefaultSupplier"), type.Name);
        }
    }
}
