# Refit 接口规则

## 当前源码事实

`IGenericApi<TDto>` 位于 `SwiftExport.Contracts/Apis/BaseApi/IGenericApi.cs`：

- `[Get("/{id}")]`
- `[Post("/")]`
- `[Put("/{id}")]`
- `[Delete("/{id}")]`

`IAuthApi` 位于 `SwiftExport.Contracts/Apis/AuthApis/IAuthApi.cs`：

- `POST /api/auth/login`
- `GET /api/auth/me`
- `POST /api/auth/register`
- `POST /api/auth/reset-password`

## 设计规则

- Refit 接口名称以 `I<Xxx>Api` 命名。
- 模块目录按业务域归类，如 `ProductApis`、`Orders`、`SystemApis`。
- 返回类型优先使用 `Task<ServiceResult<T>>` 或明确 DTO。
- 登录等历史接口如返回 `LoginTokenDto`，修改时必须同步 WebApi 和前端。
- 路由大小写和 `Program.cs` 的 lowercase URL 行为一起考虑。

## Controller 对齐检查

新增或修改 API 时检查：

- Controller route。
- Action route。
- HTTP method。
- Request body。
- Query/path 参数。
- Response DTO。
- Refit 注册和前端调用方。

## 禁止事项

- 禁止 Refit 接口返回 Entity。
- 禁止使用匿名 response。
- 禁止 Refit 路由与 Controller 只靠猜。
- 禁止为了前端临时字段污染后端业务 DTO。
