using System.Reflection;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.Features.Products;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features.Suppliers;

/// <summary>供应商页面测试的窄离线依赖，始终显式提供权限，不让生产代码引入测试兜底。</summary>
internal static class SupplierTestSupport
{
    /// <summary>为已有合法用例授予本页权限；拒绝场景由测试明确替换。</summary>
    public static CurrentUserService FullUser()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.SuppliersSupplierView, PermissionKeys.SuppliersSupplierCreate,
            PermissionKeys.SuppliersSupplierEdit, PermissionKeys.SuppliersSupplierDelete, PermissionKeys.SuppliersProductsView,
            PermissionKeys.SuppliersProductsCreate, PermissionKeys.SuppliersProductsEdit, PermissionKeys.SuppliersProductsDelete, PermissionKeys.ProductsProductEdit] });
        return user;
    }
    /// <summary>构造已保存供应商，避免绕过真正选择门禁设置页面内部状态。</summary>
    public static SupplierLineModel Supplier()
    {
        var line = new SupplierLineModel();
        line.Load(new SupplierDto { Uid = Guid.NewGuid(), Version = 1, SupplierCode = "S1", SupplierName = "供应商", Currency = "CNY" });
        return line;
    }
    /// <summary>原样执行离线请求委托，保持业务代码和列表接纳真实运行。</summary>
    public static IApiCallRunner Runner() => Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
    /// <summary>为仅验证编辑回填的用例提供空产品页。</summary>
    public static ISupplierProductApi EmptyProducts() => Proxy<ISupplierProductApi>((method, _) =>
        method.Name == nameof(ISupplierProductApi.GetPagedAsync) ? Task.FromResult(ServiceResult<PagedResponseBase<SupplierProductDto>>.Ok(new())) : throw new NotSupportedException(method.Name));
    /// <summary>复用已有代理类型，仅替换外部服务，不模拟页面状态。</summary>
    public static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call; return proxy;
    }
}
