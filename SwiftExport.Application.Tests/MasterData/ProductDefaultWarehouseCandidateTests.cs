using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Products.Catalog.Query;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.WebApi.Controllers.Products.Catalog;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证产品默认仓库候选独立于库存模块权限，且只暴露精简资料。</summary>
[TestClass]
public sealed class ProductDefaultWarehouseCandidateTests
{
    /// <summary>未认证和无产品权限均不查仓库；仅有产品权限可读取活动候选。</summary>
    [TestMethod]
    public async Task CandidateQuery_RequiresProductAccessWithoutInventoryAccess()
    {
        var warehouses = DispatchProxy.Create<IWarehouseQueryRepository, WarehouseProxy>();
        var proxy = (WarehouseProxy)warehouses;
        var currentUser = new CurrentUserStub();
        var permissions = new PermissionStub();
        var service = new ProductQueryService(
            DispatchProxy.Create<IProductRepository, RejectProxy>(), warehouses,
            currentUser, permissions, new ServiceBaseContext(null!, NullLoggerFactory.Instance), new EmptyProductAttributeRepository());
        var request = new PagedRequestBase { PageIndex = 1, PageSize = 20 };

        currentUser.Authenticated = false;
        Assert.IsFalse((await service.GetDefaultWarehouseCandidatesAsync(request)).Success);
        currentUser.Authenticated = true;
        Assert.IsFalse((await service.GetDefaultWarehouseCandidatesAsync(request)).Success);
        Assert.AreEqual(0, proxy.Calls);

        permissions.ProductAccess = true;
        var allowed = await service.GetDefaultWarehouseCandidatesAsync(request);
        Assert.IsTrue(allowed.Success, allowed.Message);
        Assert.AreEqual(1, proxy.Calls);
        Assert.AreEqual("W-01", allowed.Data!.Items.Single().Code);
        CollectionAssert.AreEquivalent(new[] { "Uid", "Code", "Name" },
            typeof(ProductDefaultWarehouseCandidateDto).GetProperties().Select(property => property.Name).ToArray());
    }

    /// <summary>产品候选端点独立要求认证，并与既有 Product Refit 基址下相对路由对齐。</summary>
    [TestMethod]
    public void CandidateRoute_IsProductScoped()
    {
        var controller = typeof(ProductController).GetMethod(nameof(ProductController.GetDefaultWarehouseCandidates))!;
        var api = typeof(IProductApi).GetMethod(nameof(IProductApi.GetDefaultWarehouseCandidatesAsync))!;
        Assert.IsNotNull(controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.AreEqual("default-warehouse-candidates", controller.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.AreEqual("/default-warehouse-candidates", api.GetCustomAttribute<GetAttribute>()!.Path);
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public bool Authenticated { get; set; }
        public string UserName => "产品测试用户";
        public Guid? UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public bool IsAuthenticated => Authenticated;
    }

    private sealed class PermissionStub : IPermissionEvaluationService
    {
        public bool ProductAccess { get; set; }
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
            => Task.FromResult(permissionKey == "Products.Product.View" && ProductAccess);
        public Task<SwiftExport.Application.Models.Auth.Permissions.Evaluation.UserPermissionProjection?> GetProjectionAsync(
            Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>确保候选用例不读取产品仓储的替身。</summary>
    public class RejectProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new AssertFailedException("产品候选查询不应读取产品仓储。");
    }

    /// <summary>记录仓库候选仓储调用并阻断其他仓库查询的替身。</summary>
    public class WarehouseProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IWarehouseQueryRepository.GetProductDefaultCandidatesAsync))
                throw new AssertFailedException("产品候选查询不应调用库存仓库宽查询。");
            Calls++;
            return Task.FromResult(new PagedResponseBase<ProductDefaultWarehouseCandidateDto>
            {
                PageIndex = 1, PageSize = 20, TotalCount = 1,
                Items = [new ProductDefaultWarehouseCandidateDto { Uid = Guid.NewGuid(), Code = "W-01", Name = "主仓" }]
            });
        }
    }
}
