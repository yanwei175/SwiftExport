# Application Service Guide

## 适用场景

适用于新增或重构业务用例、批量保存、审批、导入、跨多个 Repository 协作的后端应用层代码。

## 推荐做法

- Application Service 负责编排业务用例、DTO 输入输出、事务边界和调用 Repository。
- 一个公开方法尽量对应一个清晰业务用例，方法命名体现动作和对象。
- Repository 接口放在应用抽象层，具体实现放在基础设施层。
- WebAPI 默认后端可以使用 scoped `DbSession`，通过 `ServiceBaseContext` 提供当前请求内的 `DbSession` 和 `ILoggerFactory`。
- 默认链路中，Service 层负责开启、提交、回滚事务，Repository 只使用当前 `Session.Transaction`。
- 通用或跨架构场景可以使用 UOW，由 Application Service 创建工作单元，再让多个 Repository 共享同一事务。
- DTO 只表达用例输入输出，不携带 UI 状态。
- 复杂规则优先沉到 Domain 或明确的领域方法，Application Service 只做流程编排。
- `ServiceBaseContext` 只用于 Service 层基础依赖聚合，不替代业务服务。
- 默认 WebAPI 模板位于 `templates/dotnet-webapi`，推荐显式 Service；批量保存可以使用 `BatchRequestDto` / `BatchResultDto` 契约，但不要默认塞进泛型 BaseService。

## 禁止事项

- 禁止在 Application Service 中写 SQL 或直接调用 Dapper。
- 禁止引用 WPF、Controller、窗口、控件等 UI 类型。
- 禁止直接 `new` Repository、连接或事务对象。
- 禁止把 Application Service 写成万能服务、Manager 或 Helper。
- 禁止 Repository 自己决定业务事务边界。
- 禁止把 `ServiceBaseContext` 当成数据访问对象使用。

## Codex 修改前检查清单

- 当前业务入口、调用方和现有命名空间是否已确认。
- 涉及的 DTO、Repository 接口、事务边界是否已列出。
- 项目事务路线是 WebAPI scoped `DbSession`，还是 UOW/事务抽象。
- 如果使用 `ServiceBaseContext`，Service 和 Repository 是否共享同一个 scoped `DbSession`。
- 是否会影响数据库结构、API 契约或 UI 绑定。
- 项目现有 DI、异常处理、日志和返回模型是否已有惯例。
- 是否可以小步修改，避免一次跨多个业务模块。

## 常见错误

- ViewModel 或 Controller 直接调用 Repository，绕过 Application Service。
- Application Service 方法过大，同时处理查询、保存、审批和导出。
- 为了复用创建过度抽象的 BaseService。
- DTO 混入 `IsDirty`、`IsSelected`、`RowIndex` 等界面状态。
- 事务被分散到多个 Repository，导致跨仓储保存无法统一提交或回滚。
- Service 直接写 SQL，绕过 Repository。
- 批量保存中先开启事务再做业务校验，导致校验失败时事务悬挂到 request 结束才释放。
