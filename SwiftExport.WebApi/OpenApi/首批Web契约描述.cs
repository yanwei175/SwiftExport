using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Authentication;

namespace SwiftExport.WebApi.OpenApi;

/// <summary>补充首批 Web 端点的成功类型、JWT 和既有错误出口描述；不改变运行响应或业务规则。</summary>
public sealed class FirstWebContractOperationFilter : IOperationFilter, IDocumentFilter
{
    /// <summary>只描述本轮实际验收的产品页面依赖端点，其他端点保留现有生成结果。</summary>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var action = context.MethodInfo.DeclaringType?.Name + "." + context.MethodInfo.Name;
        var contract = action switch
        {
            "AuthController.Login" => ("login", "验证凭据并返回裸 Token DTO。Web 标记为 1 时必须 HTTPS、无 Authorization，并验证匿名 X-CSRF-TOKEN；保存同一原 JWT Cookie，不改变 WPF 调用。", new[] { 400, 401, 500 }),
            "AuthController.BrowserSessionCsrf" => ("getBrowserSessionCsrf", "匿名取得 Web 登录/退出防伪令牌；必须 HTTPS、无 Authorization，防伪 Cookie 覆盖 /api/auth。", new[] { 400, 500 }),
            "AuthController.RestoreBrowserSession" => ("restoreBrowserSession", "仅从恢复 Cookie 返回同一原 JWT；复用密码学及凭据版本验证，严格检查原 exp，不续签、不设置 Cookie，拒绝 Authorization。", new[] { 400, 401, 500 }),
            "AuthController.LogoutBrowserSession" => ("logoutBrowserSession", "匿名防伪验证后删除恢复 Cookie；必须 HTTPS、无 Authorization。204 仅证明协议成功，不撤销 JWT；随后以不接纳身份的 restore=401 核对恢复不可用。", new[] { 400, 500 }),
            "AuthController.Me" => ("getMe", "返回裸身份 DTO；权限快照不替代服务器实时授权。", new[] { 401, 403, 500 }),
            "ProductController.GetPaged" => ("getProductPage", "产品分页；要求 Products.Product.View。", new[] { 400, 401, 403, 422, 500 }),
            "ProductController.BatchSave" => ("saveProductBatch", "原子保存；按集合分别要求 Products.Product.Create/Edit/Delete。当前过期版本按 BUSINESS_BLOCKED 返回 422；未知提交结果返回 503，禁止盲目重试。", new[] { 400, 401, 403, 422, 429, 500, 503 }),
            "ProductCategoryController.GetAll" => ("getProductCategories", "读取平铺分类及 ParentId；要求 Products.Category.View。", new[] { 401, 403, 422, 500 }),
            "SysDictDataController.GetByCode" => ("getDictionaryByCode", "按字典 Code 读取字典值；产品单位使用 Unit，要求 System.Dictionary.View。", new[] { 401, 403, 422, 500 }),
            "ProductColorController.GetAll" => ("getProductColors", "读取产品颜色；要求 Products.Color.View。", new[] { 401, 403, 422, 500 }),
            "SupplierController.GetPagedList" => ("getSupplierPage", "产品默认供应商/制造商选择查询；要求 Suppliers.Supplier.View。", new[] { 400, 401, 403, 422, 500 }),
            "ProductController.GetDefaultWarehouseCandidates" => ("getProductWarehouseCandidates", "产品参考仓库查询；要求 Products.Product.View，不授予库存管理查询。", new[] { 400, 401, 403, 422, 500 }),
            _ => default
        };
        if (contract.Item1 is null) return;

        operation.OperationId = contract.Item1;
        operation.Summary = contract.Item2;
        operation.Security = [];
        if (action == "AuthController.Login")
        {
            AddHeader(operation, BrowserSessionProtocol.WebModeHeader, false, "仅 Web 传 1；不携带该标记时保持 WPF 登录协议。");
            AddHeader(operation, BrowserSessionProtocol.CsrfHeader, false, "Web 模式必填；来自匿名 csrf 端点，普通 WPF 登录不需要。");
        }
        else if (action == "AuthController.LogoutBrowserSession")
            AddHeader(operation, BrowserSessionProtocol.CsrfHeader, true, "与匿名防伪 Cookie 配对的请求令牌。");
        if (operation.RequestBody is OpenApiRequestBody body)
        {
            body.Required = true;
            // 本轮验证 JSON 客户端；只发布已验收的 application/json，避免生成器默认选择 json-patch。
            body.Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = body.Content!["application/json"] };
        }
        var successType = action switch
        {
            "AuthController.Login" => typeof(LoginTokenDto),
            "AuthController.BrowserSessionCsrf" => typeof(BrowserSessionCsrfDto),
            "AuthController.RestoreBrowserSession" => typeof(LoginTokenDto),
            "AuthController.LogoutBrowserSession" => typeof(void),
            "AuthController.Me" => typeof(MeDto),
            _ => context.MethodInfo.ReturnType.GetGenericArguments()[0]
        };
        if (successType == typeof(void))
        {
            operation.Responses!.Remove("200");
            operation.Responses["204"] = new OpenApiResponse { Description = "删除恢复 Cookie 的协议已执行；无正文，不表示服务器 JWT 撤销。" };
        }
        else
            operation.Responses!["200"] = new OpenApiResponse
            {
                Description = action.StartsWith("AuthController.", StringComparison.Ordinal) ? "成功，直接返回 DTO。" : "成功，返回既有 ServiceResult 包装。",
                Content = JsonContent(context.SchemaGenerator.GenerateSchema(successType, context.SchemaRepository))
            };

        var errorSchema = context.SchemaGenerator.GenerateSchema(typeof(ServiceResult<object>), context.SchemaRepository);
        foreach (var status in contract.Item3)
        {
            var response = new OpenApiResponse
            {
                Description = status switch
                {
                    400 => "VALIDATION：请求绑定、参数、防伪或浏览器会话协议验证失败。",
                    401 => "UNAUTHENTICATED：令牌无效、凭据版本变化或登录凭据失败。",
                    403 => "FORBIDDEN：缺少当前操作权限。",
                    422 => "BUSINESS_BLOCKED：业务校验或整批写入阻断。",
                    429 => "RATE_LIMITED：单实例短时防重复提交。",
                    503 => "SERVICE_UNAVAILABLE 或 OUTCOME_UNKNOWN：事务终止或提交结果无法确认。",
                    _ => "UNEXPECTED：已隐藏技术异常详情。"
                },
                Content = JsonContent(errorSchema),
                Headers = new Dictionary<string, IOpenApiHeader>
                {
                    ["X-Correlation-ID"] = new OpenApiHeader { Description = "服务器调用追踪标识。", Schema = new OpenApiSchema { Type = JsonSchemaType.String } }
                }
            };
            if (status is 401 or 429)
            {
                response.Headers[status == 401 ? "WWW-Authenticate" : "Retry-After"] = new OpenApiHeader
                {
                    Description = status == 401 ? "Bearer 挑战。" : "建议等待的秒数；不表示业务幂等。",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                };
            }
            operation.Responses![status.ToString(System.Globalization.CultureInfo.InvariantCulture)] = response;
        }
    }

    /// <summary>文档生成后绑定 Bearer 引用宿主，避免 OpenAPI v2 序列化未解析引用时输出空安全要求。</summary>
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        // 登录服务明确拒绝空用户名和密码；只补描述，不给共享 DTO 添加运行时验证副作用。
        RequireStrings(document, typeof(LoginRequestDto), "userName", "password");
        RequireStrings(document, typeof(LoginTokenDto), "access_token");
        RequireStrings(document, typeof(BrowserSessionCsrfDto), "requestToken");
        foreach (var path in document.Paths.Values)
        foreach (var operation in path.Operations!.Values)
        {
            if (operation.OperationId == "restoreBrowserSession")
            {
                operation.Security = [new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("BrowserSessionCookie", document)] = []
                }];
                continue;
            }
            if (operation.OperationId is not ("getMe" or "getProductPage" or "saveProductBatch" or "getProductCategories" or "getDictionaryByCode" or "getProductColors" or "getSupplierPage" or "getProductWarehouseCandidates")) continue;
            operation.Security = [new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            }];
        }
    }

    /// <summary>描述浏览器协议真实使用的请求头，保持未标记 Web 的 WPF 登录可选性。</summary>
    private static void AddHeader(OpenApiOperation operation, string name, bool required, string description)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name, In = ParameterLocation.Header, Required = required, Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
    }

    /// <summary>描述已经存在的非空认证字段约束；其他共享查询/写入 DTO 保留其实际绑定可选性。</summary>
    private static void RequireStrings(OpenApiDocument document, Type type, params string[] fields)
    {
        if (document.Components?.Schemas?[type.FullName!] is not OpenApiSchema schema) return;
        schema.Required ??= new HashSet<string>();
        foreach (var field in fields)
        {
            schema.Required.Add(field);
            if (schema.Properties![field] is not OpenApiSchema property) continue;
            property.Type = JsonSchemaType.String;
            property.MinLength = 1;
        }
    }

    /// <summary>错误出口实际输出 application/json，沿用现有 ServiceResult 泛型组件。</summary>
    private static Dictionary<string, OpenApiMediaType> JsonContent(IOpenApiSchema schema) => new()
    {
        ["application/json"] = new OpenApiMediaType { Schema = schema }
    };
}

/// <summary>保留完整类型身份消除同名 DTO 冲突，泛型 ID 不带程序集限定信息和 OpenAPI 禁用字符。</summary>
internal static class WebContractSchemaIds
{
    /// <summary>递归表达封闭泛型实参；普通 DTO 名称保持现有完整名称。</summary>
    internal static string Create(Type type)
    {
        if (type.IsArray) return Create(type.GetElementType()!) + "Array";
        var name = (type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName) ?? type.Name;
        name = name.Replace('+', '.');
        if (!type.IsGenericType) return name;
        name = name[..name.IndexOf('`')];
        return name + "Of" + string.Join("And", type.GetGenericArguments().Select(Create));
    }
}
