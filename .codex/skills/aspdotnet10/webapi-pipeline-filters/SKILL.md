---
name: aspdotnet10-webapi-pipeline-filters
description: SwiftExport WebApi 启动管道、Filter 和 Middleware 技能。用于 Program.cs、Serilog、Swagger、NewtonsoftJson、JWT 注册、UseAuthentication/UseAuthorization 顺序、GlobalExceptionMiddleware、PreventDuplicateRequestAttribute、IMemoryCache 防重复提交相关任务。
---

# WebApi Pipeline / Filters / Middleware Skill

## 必读

- `SwiftExport.WebApi/Program.cs`
- `SwiftExport.WebApi/Filters/PreventDuplicateRequestAttribute.cs`
- `SwiftExport.WebApi/Middlewares/GlobalExceptionMiddleware.cs`
- `SwiftExport.WebApi/Middlewares/DevUserMiddleware.cs`
- 涉及认证时读 `../webapi-auth-current-user/SKILL.md`

## 什么时候使用

- 修改 WebApi 启动配置、服务注册或中间件顺序。
- 修改 Serilog、Swagger、Controller JSON 序列化。
- 修改 JWT 注册、`ICurrentUser` 注册、`HttpContextAccessor`。
- 修改全局异常处理。
- 修改 `[PreventDuplicateRequest]` 防重复提交。
- 修改开发用户中间件。

## 核心规则

- `GlobalExceptionMiddleware` 必须尽量靠前，接住后续中间件和 Controller 异常。
- `DevUserMiddleware` 必须在 `UseAuthentication()` 之前，才能覆盖 `HttpContext.User`。
- `UseAuthentication()` 必须在 `UseAuthorization()` 之前。
- `[PreventDuplicateRequest]` 依赖 `IMemoryCache`，`Program.cs` 必须注册 `AddMemoryCache()`。
- JSON 当前使用 `AddNewtonsoftJson`，枚举按字符串处理，null 字段保留。
- Serilog 在 WebApplication 构建前初始化，启动失败要能写日志。

## 详细参考

需要管道顺序、防重复提交和异常处理细节时读：

- `references/WebApi管道与Filter规则.md`
