# Contracts 约束（netstandard2.0，WPF / VSTO / 后端共用）

- 只放：请求/响应 DTO、枚举、权限码、错误码、Refit 接口、分页模型。
- DTO 一律 `public class X { public T P { get; set; } }`；不用 `record`/`init`/`required`/`DateOnly`。
- 新增字段可空或有默认值；删除/改名为破坏性变更，需三端同时发布。
- 文件夹与 Api/Application 相同：`<模块>/<功能>/`。
- 命名：`CreateXxxRequest`、`UpdateXxxRequest`、`XxxCommandRequest`（如 `ConfirmPurchaseOrderRequest`）、`XxxDto`（详情）、`XxxListItemDto`（列表行）、`XxxSearchRequest`（查询条件）、`IXxxApi`（Refit）。

公共文件：
- `Common/PagedRequest.md`、`Common/PagedResult.md`
- `Common/Permissions.md`、`Common/ErrorCodes.md`
