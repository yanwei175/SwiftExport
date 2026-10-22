# Toolbox — 已引入的第三方库与使用规则

来源：各 `.csproj` 的 `PackageReference`（2025 当前分支）。**能用这些库解决的问题，禁止自己实现。**

| 库 | 项目 | 用途 | 规则 |
|---|---|---|---|
| ASP.NET Core 授权（框架自带） | Api | 功能权限 | 用 Policy + `[HasPermission]`；禁止自写权限过滤器、权限字典 |
| Microsoft.AspNetCore.Authentication.JwtBearer | Api | 认证 | 只在 `Program` 配置；业务代码通过 `ICurrentUser` 读取用户 |
| FluentValidation (+DependencyInjectionExtensions) 12.x | Application | 输入格式校验 | 每个请求/命令一个 `XxxValidator : AbstractValidator<T>`；由 Api 的 `ValidationFilter` 自动执行；**不要**用已废弃的 FluentValidation.AspNetCore 自动校验包；禁止在 Service 里手写长度/必填判断 |
| Mapster 7.x | Application | 实体/读模型 → DTO 复制 | 只用于**同名属性的平面复制**和 `TypeAdapterConfig` 中集中配置；**禁止**用 `Adapt` 生成或修改实体（实体只能通过构造函数/方法改变状态）；禁止手写大段 `dto.A = e.A` |
| Dapper 2.x | Infrastructure | SQL 执行与结果映射 | 查询直接映射到 DTO；多结果用 `QueryMultipleAsync`；禁止拼接 SQL 字符串参数 |
| Npgsql 10.x | Infrastructure | PostgreSQL 驱动 | 用其异常 `PostgresException.SqlState` 判断唯一冲突(23505)、死锁(40P01)、序列化失败(40001)；禁止按消息文本判断 |
| Serilog / Serilog.AspNetCore | Api | 日志 | `UseSerilogRequestLogging` 记录每个请求；业务代码注入 `ILogger<T>`，用结构化占位符；禁止自写请求日志中间件 |
| Swashbuckle.AspNetCore | Api |
| System.Text.Json（框架自带） | 全部 | **唯一允许的 JSON 库** | 后端 `AddControllers().AddJsonOptions(...)`，Refit 用 `SystemTextJsonContentSerializer`，两端共用同一份 `JsonSerializerOptions`（camelCase、枚举按名称 `JsonStringEnumConverter`）。DTO 只用 `[JsonPropertyName]` 等 STJ 特性。Contracts 引用 `System.Text.Json` NuGet 包以支持 netstandard2.0 |
| ~~Microsoft.AspNetCore.Mvc.NewtonsoftJson~~、~~Swashbuckle.AspNetCore.Newtonsoft~~、Newtonsoft.Json | — | **待移除** | 禁止新代码使用 `JsonConvert`、`[JsonProperty]`、`JObject`；迁移时一并删除这两个包 |
| Refit 10.x | Contracts | 客户端 API 接口 | 每个 Controller 对应一个 `IXxxApi`；路由与 Controller 一致 |
| BCrypt.Net-Next | Application | 密码哈希 | 禁止自写哈希/加盐 |
| Microsoft.Extensions.Hosting.WindowsServices | Api | Windows 服务宿主 | 只在 `Program` |
| SkiaSharp | Api | 图片处理 | 仅图片相关功能使用 |
| MSTest 4.x | Tests | 单元/集成测试 | 统一 MSTest，不再引入 xUnit/NUnit |

## 框架自带、禁止重造的能力

| 需求 | 用 | 不要 |
|---|---|---|
| 当前时间 | `TimeProvider`（注入，测试可替换） | `DateTime.Now` 散落各处 |
| 有序 GUID | `Guid.CreateVersion7()`（.NET 9+） | 自写 `GuidSequentialGenerator` |
| 统一错误响应 | `IExceptionHandler` + `AddProblemDetails()` | 自写错误中间件、`ServiceResult` 包装 |
| 关联 ID | `HttpContext.TraceIdentifier` / `Activity.Current.TraceId` | 自写 CorrelationId 中间件 |
| 缓存 | `IMemoryCache` | 自写字典缓存 |
| 限流（如需） | `AddRateLimiter` | 自写冷却过滤器 |
| 请求取消 | `CancellationToken` 一路透传 | 自写取消标志 |
| JSON 规范化/哈希 | `JsonSerializer.SerializeToUtf8Bytes` + `SHA256.HashData` | 自写规范化序列化器 |

## 引入新库的条件
在 PR/任务说明中写明：解决什么问题、为什么上表的库与框架无法解决、许可证（必须是 MIT/Apache-2.0/BSD 等）。
