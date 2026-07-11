# Controller 边界规则

## 当前源码事实

`BaseController<TEntity,TDto>` 位于 `SwiftExport.WebApi/Controllers/Base/BaseController.cs`：

- `[ApiController]`
- `[Route("api/[controller]")]`
- `abstract`
- 注入 `IBaseService<TEntity,TDto>`
- 暴露：
  - `GET api/[controller]/{id}` -> `GetById`
  - `POST api/[controller]` -> `Create`
  - `PUT api/[controller]/{id}` -> `Update`
  - `DELETE api/[controller]/{id}` -> `Delete`

## 使用规则

- 标准 CRUD Controller 优先复用 `IBaseService` / `BaseService` 的契约。
- 写操作默认加 `[PreventDuplicateRequest]`。
- Controller action 返回项目统一响应，如 `ServiceResult<T>`。
- Controller 路由、HTTP 方法、Request/Response 必须与 `SwiftExport.Contracts` 中的 Refit API 保持一致。
- 复杂业务命令应显式建 action，例如 `confirm`、`cancel-confirm`、`explode`、`convert-to-purchase`、`post`、`reverse-post`。

## 禁止事项

- 禁止 Controller 写 Dapper/SQL。
- 禁止 Controller 直接注入 Infrastructure 具体实现。
- 禁止 Controller 处理库存、过账、拆解、改单等复杂业务流程。
- 禁止把业务命令伪装成普通 `Update`。
- 禁止返回 Entity 给前端。

## 注意点

- `BaseController.Update(Guid id, UpdateItemRequest<TDto> dto)` 当前没有统一校验 route id 与 dto.Uid，一旦改动需要与现有调用方一起评估。
- `BaseController.Delete(DeleteItemRequest id)` 当前 action 路由有 `{id}`，但参数来自复杂对象；调整时必须同步 Refit API。
