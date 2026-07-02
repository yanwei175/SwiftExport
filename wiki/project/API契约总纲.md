# API 契约总纲

本文定义 SwiftExport 的 API 契约设计原则，用来指导 WebApi Controller、`SwiftExport.Contracts`、Refit Interface、DTO、Swagger/OpenAPI 和未来 BS 前端之间的边界。

本文补充 [项目地图](project-map.md)、[领域模块总纲](领域模块总纲.md)、[数据模型总纲](数据模型总纲.md) 和 [单据状态机与轻量改单机制](document-state-and-change-policy.md)。涉及 DTO、Refit、Controller 实现细节时，还应读取：

- [DTO Guide](../guides/shared/dto.md)
- [Refit API Guide](../guides/shared/refit-api.md)
- [ServiceResult Guide](../guides/shared/service-result.md)
- [WebApi Controller Guide](../guides/backend/webapi-controller.md)

## 1. 核心定位

SwiftExport 当前客户端主要是 WPF/VSTO，使用 Refit 调用 ASP.NET Core WebApi。未来如果增加 BS 架构前端，Web 前端不会直接消费 C# Refit Interface，而会依赖 HTTP API、DTO 和 OpenAPI/Swagger 文档。

因此 API 契约的定位是：

- `SwiftExport.WebApi` 是 HTTP 边界。
- `SwiftExport.Contracts` 是前后端共享契约。
- `SwiftExport.Contracts/Apis/**` 中的 Refit Interface 是当前 WPF/VSTO 的强类型客户端。
- OpenAPI/Swagger 是未来 BS 前端、脚本、外部系统和自动化测试理解 API 的入口。
- Application Service 是业务编排入口，Controller 不承载复杂业务规则。

Refit 不是业务规则来源。真正需要长期稳定的是 HTTP 路由、请求模型、响应模型、错误格式和业务命令语义。

## 2. 当前事实与目标方向

当前事实：

- 现有 Controller 多使用 `[Route("api/[controller]")]`。
- 一部分 API 继承或对齐 `IGenericApi<TDto>`，包含 `Get`、`Create`、`Update`、`Delete`、`page`、`batch-save` 等通用形态。
- 业务单据已出现显式命令接口，例如销售订单 `confirm`、`cancel-confirm`，净需求 `convert-to-po`，PackingList `post`。
- Refit Interface 与 Controller 路由需要保持一致。

目标方向：

- 新增 API 优先表达业务意图，而不是只套通用 CRUD。
- 查询、保存、业务命令、过账、反过账、红冲等接口分开设计。
- Controller 路由和 Refit Interface 同步设计，并保证 Swagger 能清楚表达请求和响应。
- 未来 BS 前端可以只依赖 HTTP API 文档和 DTO，不需要引用 C# Refit 项目。

兼容原则：

- 已存在的公开路由不因总纲立即重命名。
- 新功能优先按本总纲设计。
- 旧接口重构时需要列出调用方和兼容策略。

## 3. 契约分层

### 3.1 Controller

Controller 负责：

- HTTP 路由、方法、参数绑定和认证授权入口。
- 调用 Application Service。
- 将 Service 结果转换为统一 HTTP 响应。
- 暴露可被 Swagger/OpenAPI 描述的请求和响应模型。

Controller 禁止：

- 直接写 SQL。
- 直接管理数据库事务。
- 把复杂业务规则堆在 Action 方法里。
- 返回 Entity 或内部异常堆栈。

### 3.2 Contracts DTO

DTO 负责：

- API 输入输出。
- 前后端传输。
- 查询结果、详情结果、命令请求和保存请求。
- Refit Interface 与 Controller 之间的公共模型。

DTO 禁止：

- 直接暴露 Entity。
- 放入 WPF 控件、窗口、弹窗、`IsSelected`、`IsDirty`、`RowIndex` 等 UI 状态。
- 表达数据库持久化规则。
- 随意改字段名和字段类型。

### 3.3 Refit Interface

Refit Interface 负责：

- 为 WPF/VSTO 提供强类型 HTTP 客户端。
- 与 Controller 路由、HTTP 方法、参数、请求体和返回体保持一致。
- 在前端 DI 中注册，供 ViewModel 或前端服务调用。

Refit Interface 禁止：

- 设计成 WPF 专属契约。
- 与 Controller 路由不一致。
- 返回 Entity。
- 通过重载或 C# 便利写法隐藏真实 HTTP 语义。

### 3.4 OpenAPI / Swagger

Swagger 负责：

- 作为未来 BS 前端和外部调用方的 API 文档入口。
- 让前端能看到路由、HTTP 方法、请求体、响应体、状态码和字段说明。
- 支持未来生成 TypeScript 客户端、接口 Mock 或自动化测试。

新增 API 时，应让 Swagger 能自然表达，不依赖口头说明或 C# 客户端源码才能理解。

## 4. API 类型

### 4.1 查询 API

用于列表、详情、搜索、下拉选择、弹窗选择。

建议形态：

```text
GET  /api/products/{uid}
POST /api/products/search
POST /api/products/page
GET  /api/products/lookup
```

规则：

- 复杂筛选优先用 `POST search/page` 携带 Request，避免 URL 参数膨胀。
- 列表、详情、选择器返回不同 DTO，不强行共用一个巨大 DTO。
- 查询 API 不改变业务状态。

### 4.2 保存 API

用于草稿保存、详情保存、批量保存、行编辑保存。

建议形态：

```text
POST /api/purchase-orders
PUT  /api/purchase-orders/{uid}
POST /api/purchase-orders/detail/save
POST /api/products/batch-save
```

规则：

- 保存接口只保存草稿或普通资料，不暗中执行确认、拆解、转采购、过账等业务命令。
- 批量保存应使用明确变更集表达新增、修改、删除。
- 保存返回应包含最新版本、单号、行号或前端继续编辑所需信息。

### 4.3 业务命令 API

用于确认、取消确认、拆解、转采购、入库过账、PackingList 过账、改单、关闭、作废等明确业务动作。

建议形态：

```text
POST /api/sales-orders/{uid}/confirm
POST /api/sales-orders/{uid}/cancel-confirm
POST /api/independent-demands/decompose
POST /api/dependent-demands/convert-to-purchase
POST /api/receives/{uid}/post
POST /api/packing-lists/{uid}/post
POST /api/purchase-orders/{uid}/close
POST /api/purchase-orders/{uid}/void
```

规则：

- 业务命令必须显式命名，不能伪装成普通 `Update`。
- 命令 Request 应包含操作者意图、目标单据、必要参数和并发版本。
- 命令执行边界以 Application Service 为准。
- 命令结果应返回执行后的关键状态、影响单据和提示信息。

### 4.4 过账 / 反过账 / 红冲 API

用于影响库存、财务或不可变流水的关键动作。

建议形态：

```text
POST /api/receives/{uid}/post
POST /api/receives/{uid}/reverse-post
POST /api/receives/{uid}/red-correct
POST /api/outbounds/{uid}/post
POST /api/packing-lists/{uid}/post
```

规则：

- 影响库存的 API 必须生成正式业务单据和库存流水。
- 已产生库存流水后，不允许靠普通修改或删除解决错误。
- 反过账和红冲必须有独立命令、独立 Request 和审计信息。

### 4.5 Lookup API

用于搜索框、选择器、下拉框、弹窗选择等轻量查询。

建议形态：

```text
GET  /api/customers/lookup
POST /api/products/lookup
POST /api/customer-products/search
```

规则：

- Lookup DTO 应短小稳定，只返回编码、名称、显示字段和必要辅助字段。
- 不要为选择器返回完整详情 DTO。
- 远程搜索控件优先使用 Search/Lookup API，不把复杂筛选逻辑放到 WPF 控件里。

## 5. 命名约定

### 5.1 路由命名

目标风格：

- 资源名使用业务复数名，例如 `sales-orders`、`purchase-orders`、`dependent-demands`。
- 业务命令作为资源下的动作，例如 `{uid}/confirm`、`{uid}/post`。
- 聚合动作使用集合命令，例如 `dependent-demands/convert-to-purchase`。

当前兼容：

- 已存在 `[Route("api/[controller]")]` 的 Controller 可以继续保留。
- 新接口如需加入旧 Controller，应优先保持当前公开路由不破坏。
- 大规模路由规范化应作为单独重构任务处理。

### 5.2 DTO 命名

建议：

- `XxxListDto`：列表行。
- `XxxDetailDto`：详情头行聚合。
- `XxxLineDto`：单据行。
- `XxxSearchRequest`：查询条件。
- `XxxPageRequest`：分页查询。
- `XxxSaveRequest`：保存草稿或详情。
- `XxxCommandRequest`：业务命令。
- `XxxCommandResult`：业务命令结果。
- `XxxLookupDto`：选择器结果。

禁止：

- `Dto1`、`TempDto`、`ViewDto` 这类无法表达业务意图的名字。
- 一个 DTO 同时承担 Entity、列表、详情、保存、命令和 UI 行状态。

## 6. Request / Response 规则

Request 应表达用户意图：

- 查询请求表达筛选、排序、分页。
- 保存请求表达草稿内容或变更集。
- 命令请求表达业务动作所需参数。

Response 应表达业务结果：

- 查询返回 DTO 或分页 DTO。
- 保存返回最新头行、版本、单号或详情。
- 命令返回执行结果、状态变化、影响单据、失败原因。

不要让前端通过猜字段判断业务执行结果。尤其是确认、拆解、转采购、过账、反过账、红冲等命令，必须返回明确结果。

## 7. 统一返回与错误

原则：

- 优先沿用项目已有 `ServiceResult` 或统一返回模型。
- 区分验证失败、业务失败、权限失败、系统异常。
- 面向用户的错误消息和面向日志的异常信息要分开。
- 不把内部异常堆栈返回给客户端。

建议错误内容：

- `success` 或等价成功标志。
- `code` 或业务错误码。
- `message` 用户可读提示。
- `data` 返回数据。
- `errors` 字段级验证错误。
- `traceId` 或请求追踪号，便于日志排查。

如项目现有模型字段不同，新增接口优先沿用现有模型，不随意引入第二套 Result。

## 8. 分页、筛选、排序

分页请求建议包含：

- `pageIndex`
- `pageSize`
- `keyword`
- `sortBy`
- `sortDirection`
- 模块专属筛选字段

分页返回建议包含：

- `items`
- `totalCount`
- `pageIndex`
- `pageSize`

规则：

- 不把数据库列名直接暴露成任意排序字段。
- 排序字段应使用白名单映射。
- 前端页面分页、导出和搜索应尽量复用同一查询 Request。

## 9. 并发与改单

涉及保存、确认、取消确认、改单、过账、反过账和红冲时，Request 应考虑并发字段：

- `uid`
- `version`
- `reason`
- `operator`
- `remark`

规则：

- 普通保存可用 `Version` 防止覆盖他人修改。
- 改单必须遵循状态机与轻量改单机制。
- 已生成下游单据时，命令 API 必须清楚返回是否允许回退、需要删除哪些未过账下游、或必须反过账/红冲。

## 10. 文件、图片、Excel 与导出

未来涉及文件类 API 时：

- 文件上传使用明确的 Multipart API。
- 文件下载使用 `GET` 并返回文件流，同时定义文件名和内容类型。
- Excel 导入应返回导入预览、错误行和确认导入命令，不直接一次性静默入库。
- 图片字段不要只依赖本机路径；需要考虑未来 Web 可访问的文件标识或 URL。

## 11. 认证授权

当前认证授权以现有 Auth Controller 和系统权限模块为准。

新增 API 时：

- 业务写操作必须考虑认证用户。
- 关键命令应具备权限点。
- Swagger 文档中应能看出接口是否需要认证。
- 不在 DTO 中传入可伪造的用户身份作为业务事实；操作者优先来自认证上下文。

## 12. 未来 BS 架构兼容策略

不需要现在开发 BS 前端，但 API 应满足未来 BS 前端直接消费：

- 路由稳定。
- DTO 稳定。
- Swagger 可读。
- 错误格式统一。
- 分页、搜索、排序通用。
- 文件、图片、导出可通过 HTTP 访问。
- 不依赖 WPF/VSTO 特有状态。
- 不要求前端理解数据库表结构。

如果未来要生成 TypeScript 客户端，应优先从 OpenAPI 生成，而不是从 Refit Interface 反向生成。

## 13. 新增 API 设计流程

1. 先读领域模块总纲，确认接口属于哪个模块。
2. 再读业务总纲、状态机总纲或数据模型总纲，确认业务命令、状态和数据边界。
3. 设计 DTO、Request、Response。
4. 设计 Controller 路由和 HTTP 方法。
5. 设计 Refit Interface，并保证与 Controller 一致。
6. 设计 Application Service 方法，由它承载业务编排。
7. 如果是前端任务，只能先补 Contracts 契约；不要借机实现后端，除非用户明确要求。
8. 修改完成后检查 Swagger、Refit、Controller、前端调用方和兼容风险。

## 14. 常见反例

不要这样做：

- 用 `POST /api/order/updateStatus` 承载确认、取消确认、过账、关闭所有命令。
- 让 `OrderStatus` 成为唯一流程判断依据。
- Refit 接口有方法，但 Controller 路由不存在。
- Controller 路由存在，但 Refit Interface 没同步。
- API 返回 Entity。
- 查询 DTO 混入 `IsSelected`、`IsDirty`、`RowIndex`。
- 保存接口暗中执行拆解或转采购。
- PackingList 过账直接写库存流水，不生成正式入库/出库单。

## 15. 后续待细化

- 统一分页 Request/Response 的正式类型。
- 统一业务命令 Request/Result 的正式类型。
- 统一错误码和验证错误结构。
- Swagger 分组、接口描述和 DTO 字段描述规范。
- 旧 Controller 路由与未来目标路由的兼容迁移策略。
- TypeScript 客户端生成方案。
