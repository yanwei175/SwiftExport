using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SwiftExport.Application.Interfaces.Auth.Session;
using SwiftExport.Application.Interfaces.Auth.Users;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Products.Catalog.Query;
using SwiftExport.Application.Interfaces.Products.Catalog.Maintenance;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;
using SwiftExport.Application.Interfaces.Products.Categories;
using SwiftExport.Application.Interfaces.Products.Colors;
using SwiftExport.Application.Interfaces.Suppliers.Management;
using SwiftExport.Application.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Application.Models.Auth.Session;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Configuration;

namespace SwiftExport.Application.Tests.WebApi;

/// <summary>沿用现有 HTTP 测试的临时 Kestrel 宿主，保留生产 JWT、Newtonsoft、权限和错误管道；仅替换首批业务端口。</summary>
internal sealed class FirstWebContractHost : IAsyncDisposable
{
    internal readonly Guid UserId = Guid.Parse("49cdbca0-ef30-488d-9d05-2c25d2308ef7");
    internal readonly HashSet<string> Permissions = ["Products.Product.View", "Products.Product.Create", "Products.Product.Edit", "Products.Product.Delete", "Products.Category.View", "System.Dictionary.View", "Products.Color.View", "Suppliers.Supplier.View"];
    internal readonly List<string> CheckedPermissions = [];
    internal bool CredentialValid = true;
    internal ProductSearchRequest? Search;
    internal ProductBatchSaveRequest? Batch;
    internal LoginRequestDto? Login;
    internal PagedRequestBase? WarehouseSearch;
    internal SupplierSearchRequest? SupplierSearch;
    internal string? FailureCode;
    internal int BusinessCalls;
    internal WebApplication App = null!;
    internal HttpClient Client = null!;

    /// <summary>使用显式测试 JWT 配置和禁用的文件/开发身份，不读取业务连接或解析真实仓储。</summary>
    internal async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        // 不继承构建输出目录中的 appsettings 或环境连接配置，意外仓储解析也禁止创建连接。
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Configuration["Auth:JwtKey"] = "first-web-contract-only-signing-key-at-least-32-bytes";
        builder.Configuration["Auth:JwtIssuer"] = "web-contract";
        builder.Configuration["Auth:JwtAudience"] = "web-contract";
        builder.Configuration["Auth:UseDevUser"] = "false";
        builder.Configuration["FileStorage:RootPath"] = "";
        builder.Configuration["FileStorage:Enabled"] = "false";
        builder.Configuration["FileStorage:ProductImages:UploadEnabled"] = "false";
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddWebApiServices();
        builder.Services.AddControllers().AddApplicationPart(typeof(SwiftExport.WebApi.Controllers.Auth.AuthController).Assembly);
        builder.Services.AddSingleton(CreateProxy<SwiftExport.Infrastructure.Data.IDbConnectionFactory>(Reject));
        builder.Services.AddSingleton(CreateProxy<IAuthSessionService>(Auth));
        builder.Services.AddSingleton(CreateProxy<IUserAccountService>(Reject));
        builder.Services.AddSingleton(CreateProxy<IPermissionEvaluationService>((name, args) =>
        {
            if (name != "HasPermissionAsync") return Reject(name, args);
            var key = (string)args[1]!;
            CheckedPermissions.Add(key);
            return Task.FromResult(Permissions.Contains(key));
        }));
        builder.Services.AddSingleton(CreateProxy<IProductQueryService>((name, args) =>
        {
            BusinessCalls++;
            if (name == "GetDefaultWarehouseCandidatesAsync")
            {
                WarehouseSearch = (PagedRequestBase)args[0]!;
                return Task.FromResult(ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>.Ok(new()
                {
                    PageIndex = WarehouseSearch.PageIndex, PageSize = WarehouseSearch.PageSize, TotalCount = 1,
                    Items = [new() { Uid = UserId, Code = "WH", Name = "参考仓库" }]
                }));
            }
            if (name != "GetPagedAsync") return Reject(name, args);
            Search = (ProductSearchRequest)args[0]!;
            return Task.FromResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(new()
            {
                PageIndex = Search.PageIndex, PageSize = Search.PageSize, TotalCount = 1,
                Items = [new() { Uid = UserId, Sku = "HTTP-SKU", Version = 7, Unit = null,
                    CreatedDate = new DateTime(2026, 10, 7, 1, 2, 3, DateTimeKind.Utc) }]
            }));
        }));
        builder.Services.AddSingleton(CreateProxy<IProductMaintenanceService>((name, args) =>
        {
            BusinessCalls++;
            if (name != "BatchSaveAsync") return Reject(name, args);
            Batch = (ProductBatchSaveRequest)args[0]!;
            if (FailureCode == "THROW") throw new InvalidOperationException("secret-contract-probe");
            if (FailureCode is not null)
                return Task.FromResult(ServiceResult<ProductBatchSaveResponse>.Fail(FailureCode, "受控服务失败。", "UpdateItems[0].Version"));
            // 返回与输入逐项对应的 HTTP 示例回执；具体写入与回滚由真实服务夹具验证。
            var items = Batch.CreateItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, RowIndex = item.RowIndex,
                Success = true, Operation = SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation.Create, Uid = UserId, NewVersion = 1 })
                .Concat(Batch.UpdateItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, RowIndex = item.RowIndex,
                    Success = true, Operation = SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation.Update,
                    Uid = item.Uid, OldVersion = item.Version, NewVersion = item.Version + 1 }))
                .Concat(Batch.DeleteItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, RowIndex = item.RowIndex,
                    Success = true, Operation = SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation.Delete,
                    Uid = item.Uid, OldVersion = item.Version, NewVersion = item.IsSoftDelete ? item.Version : null })).ToList();
            return Task.FromResult(ServiceResult<ProductBatchSaveResponse>.Ok(new() { TotalCount = items.Count, Items = items }));
        }));
        builder.Services.AddSingleton(CreateProxy<IProductMasterMaintenanceService>(Reject));
        builder.Services.AddSingleton(CreateProxy<IProductColorService>((name, args) =>
        {
            BusinessCalls++;
            return name == "GetAllAsync" ? Task.FromResult(ServiceResult<IReadOnlyList<ProductColorDto>>.Ok(
                [new() { Uid = UserId }])) : Reject(name, args);
        }));
        builder.Services.AddSingleton(CreateProxy<ISupplierService>((name, args) =>
        {
            BusinessCalls++;
            if (name != "GetPagedAsync") return Reject(name, args);
            SupplierSearch = (SupplierSearchRequest)args[0]!;
            return Task.FromResult(ServiceResult<PagedResponseBase<SupplierDto>>.Ok(new()
            {
                PageIndex = SupplierSearch.PageIndex, PageSize = SupplierSearch.PageSize, TotalCount = 1,
                Items = [new() { Uid = UserId, SupplierCode = "SUP" }]
            }));
        }));
        builder.Services.AddSingleton(CreateProxy<IProductCategoryService>((name, args) =>
        {
            BusinessCalls++;
            return name == "GetAllAsync" ? Task.FromResult(ServiceResult<IReadOnlyList<ProductCategoryDto>>.Ok(
                [new() { Uid = UserId, CategoryName = "首批分类", ParentId = null }])) : Reject(name, args);
        }));
        builder.Services.AddSingleton(CreateProxy<ISysDictDataService>((name, args) =>
        {
            BusinessCalls++;
            return name == "GetByCodeAsync" ? Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(
                [new() { DictCode = (string)args[0]!, DataNameEn = "PCS", IsEnabled = true }])) : Reject(name, args);
        }));
        App = builder.Build();
        App.UseWebApiPipeline();
        await App.StartAsync();
        var address = App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Client = new HttpClient { BaseAddress = new Uri(address) };
    }

    /// <summary>仅返回签发/验证 JWT 所需的测试身份；真实账号密码与数据库投影由已有 Auth 测试覆盖。</summary>
    private object Auth(string name, object?[] args)
    {
        switch (name)
        {
            case "AuthenticateAsync":
                Login = (LoginRequestDto)args[0]!;
                if (string.IsNullOrWhiteSpace(Login.UserName) || string.IsNullOrWhiteSpace(Login.Password))
                    return new SwiftExport.Application.Services.Auth.Session.AuthSessionService(null!, null!, null!, null!).AuthenticateAsync(Login);
                return Task.FromResult(Login.Password == "contract-password"
                    ? ServiceResult<AuthenticatedUser>.Ok(new() { UserId = UserId, UserName = "contract-user", CredentialVersion = 3 })
                    : ServiceResult<AuthenticatedUser>.Fail("UNAUTHENTICATED", "凭据无效。"));
            case "IsCredentialVersionValidAsync":
                return Task.FromResult(CredentialValid && (Guid)args[0]! == UserId && (int)args[1]! == 3);
            case "GetCurrentUserAsync":
                return Task.FromResult(ServiceResult<MeDto>.Ok(new() { UserId = UserId.ToString(), UserName = "contract-user", NickName = null, Permissions = Permissions.ToList() }));
            default: return Reject(name, args);
        }
    }

    /// <summary>未纳入当前契约场景的调用立即失败，避免自动成功掩盖服务越界。</summary>
    private static object Reject(string name, object?[] args) => throw new NotSupportedException("未配置 HTTP 测试端口：" + name);

    /// <summary>复用既有 DispatchProxy 测试模式，只转发本任务配置的方法，不建立通用模拟框架。</summary>
    private static T CreateProxy<T>(Func<string, object?[], object> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ContractProxy>();
        ((ContractProxy)(object)proxy).Call = call;
        return proxy;
    }

    /// <summary>释放临时 HTTP 监听；本夹具没有数据库资源。</summary>
    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null) await App.DisposeAsync();
    }

    /// <summary>局部服务代理，保持原接口方法与参数。</summary>
    public class ContractProxy : DispatchProxy
    {
        internal Func<string, object?[], object> Call = null!;
        /// <summary>将调用传给当前场景，异常由真实生产管道处理。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args ?? []);
    }
}
