# WebApi 管道与 Filter 规则

## Program.cs 当前职责

`Program.cs` 当前负责：

- 读取 `appsettings.json` 和环境配置。
- 初始化 Serilog。
- 注册 Infrastructure 服务。
- 注册 Application 服务。
- 注册 JWT Bearer。
- 注册 `IHttpContextAccessor`。
- 注册 `ICurrentUser`。
- 注册 `IMemoryCache`。
- 注册 Controllers + Newtonsoft.Json。
- 注册 Swagger。
- 配置中间件管道。

## 中间件顺序

当前关键顺序：

```text
GlobalExceptionMiddleware
DevUserMiddleware
Swagger(Development only)
HttpsRedirection
UseAuthentication
UseAuthorization
MapControllers
```

规则：

- 全局异常应尽量在管道前面。
- DevUser 必须在认证前。
- 认证必须在授权前。
- Controller 映射最后。

## PreventDuplicateRequestAttribute

路径：`SwiftExport.WebApi/Filters/PreventDuplicateRequestAttribute.cs`

用途：

- 防抖式防重复提交。
- 默认冷却窗口 3 秒。
- 同一用户 + 同一 Controller + 同一 Action 在窗口内重复请求返回 HTTP 429。
- 请求完成后不主动删除缓存 key，让冷却窗口完整生效。

规则：

- 只贴在写操作或危险命令上。
- 不用于普通查询。
- 当前使用 `IMemoryCache`，只适合单实例；多实例部署需替换为分布式缓存。
- 缓存 key 当前不包含请求体，适合用户加接口粒度防抖，不适合幂等业务键去重。

## GlobalExceptionMiddleware

路径：`SwiftExport.WebApi/Middlewares/GlobalExceptionMiddleware.cs`

用途：

- 捕获未处理异常。
- 写错误日志。
- 返回 `ServiceResult<string>.Fail(...)`。

当前行为：

- HTTP 状态码返回 `200 OK`。
- 响应 body 表达失败。

修改该行为会影响前端 Refit 和统一错误处理，必须同步评估。

## JSON 规则

- 当前 Controller 使用 Newtonsoft.Json。
- 枚举序列化为字符串名称。
- null 字段仍输出。
- 不要随意切换到 System.Text.Json，除非同步评估前端 Refit、枚举、日期和 null 行为。

## 禁止事项

- 禁止在 Program.cs 里写业务逻辑。
- 禁止生产开启 `Auth:UseDevUser=true`。
- 禁止把防重复提交 Filter 当作业务幂等保障。
- 禁止全局异常中间件泄露堆栈、SQL、连接字符串、JWT、密码等敏感信息。
