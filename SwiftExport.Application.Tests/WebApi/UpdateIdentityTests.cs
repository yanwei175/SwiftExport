using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Customers.Management;
using SwiftExport.Application.Interfaces.Products.Catalog.Maintenance;
using SwiftExport.Application.Interfaces.Suppliers.SupplierProducts.Maintenance;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Controllers.Customers.Management;
using SwiftExport.WebApi.Controllers.Products.Catalog;
using SwiftExport.WebApi.Controllers.Suppliers.SupplierProducts;

namespace SwiftExport.Application.Tests.WebApi;

/// <summary>通过真实 MVC 更新方法验证路径身份边界，不执行持久化或连接数据库。</summary>
[TestClass]
public sealed class UpdateIdentityTests
{
    /// <summary>身份冲突必须在服务前拒绝；合法请求原样转交并保持保存响应。</summary>
    [TestMethod]
    [DataRow("Customer", false)]
    [DataRow("Customer", true)]
    [DataRow("Product", false)]
    [DataRow("Product", true)]
    [DataRow("SupplierProduct", false)]
    [DataRow("SupplierProduct", true)]
    public async Task Update_RequiresMatchingRouteAndRequestIdentity(string controllerName, bool matches)
    {
        var routeId = Guid.NewGuid();
        var bodyId = matches ? routeId : Guid.NewGuid();
        var expected = ServiceResult<SaveItemResult>.Ok(new SaveItemResult { Uid = bodyId, Success = true });
        var calls = 0;
        object? forwarded = null;
        object Handle(MethodInfo method, object?[]? arguments)
        {
            Assert.AreEqual("UpdateAsync", method.Name);
            calls++;
            forwarded = arguments![0];
            return Task.FromResult(expected);
        }
        object controller;
        object request;
        switch (controllerName)
        {
            case "Customer":
                controller = new CustomerController(Service<ICustomerService>(Handle));
                request = new UpdateItemRequest<CustomerDto> { Uid = bodyId, Version = 3, Data = new() };
                break;
            case "Product":
                controller = new ProductController(null!, Service<IProductMaintenanceService>(Handle), null!);
                request = new UpdateItemRequest<ProductDto> { Uid = bodyId, Version = 3, Data = new() };
                break;
            default:
                controller = new SupplierProductController(null!, Service<ISupplierProductMaintenanceService>(Handle), null!);
                request = new UpdateItemRequest<SupplierProductDto> { Uid = bodyId, Version = 3, Data = new() };
                break;
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(CustomerController).Assembly);
        using var provider = services.BuildServiceProvider();
        var action = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Single(item => item.ControllerTypeInfo.AsType() == controller.GetType()
                && item.MethodInfo.Name == "Update");
        Assert.AreEqual($"api/{controllerName}/{{id}}", action.AttributeRouteInfo!.Template);
        var result = await (Task<ServiceResult<SaveItemResult>>)action.MethodInfo.Invoke(controller, [routeId, request])!;
        Assert.AreEqual(bodyId, request.GetType().GetProperty("Uid")!.GetValue(request), "不能覆盖请求体身份。");
        if (matches)
        {
            Assert.AreEqual(1, calls);
            Assert.AreSame(request, forwarded);
            Assert.AreSame(expected, result);
        }
        else
        {
            Assert.AreEqual(0, calls, "矛盾输入不得调用写入服务。");
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
            Assert.AreEqual("Uid", result.Error.Field);
        }
    }

    /// <summary>只模拟写入服务入口，意外调用立即失败。</summary>
    private static T Service<T>(Func<MethodInfo, object?[]?, object> handler) where T : class
    {
        var service = DispatchProxy.Create<T, ServiceProbe>();
        ((ServiceProbe)(object)service).Handler = handler;
        return service;
    }

    /// <summary>记录 Controller 到服务的调用，供生成代理继承。</summary>
    public class ServiceProbe : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object> Handler { get; set; } = null!;
        /// <summary>转交测试预期调用，不提供任何实际服务实现。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
