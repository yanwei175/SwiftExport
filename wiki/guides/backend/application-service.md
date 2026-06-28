# Application Service Guide

## 适用场景

用于新增或重构后端业务用例、批量保存、审批、导入、跨多个 Repository 协作的 Application 层代码。

## SwiftExport 规则

- Application Service 负责编排业务用例、业务校验、DTO 输入输出和事务边界。
- Service 调用 `SwiftExport.Core` 中的 Repository 接口，不直接依赖 Infrastructure 实现。
- 不在 Application Service 中写 SQL、创建 `SqlConnection` 或处理 WPF UI 状态。
- 本项目后端事务优先沿用当前 `DbSession.Transaction` 模式；具体事务细节见 [DbSession Skill](../../skills/backend/db-session-context.md)。
- DTO 只表达用例输入输出，不携带 `IsDirty`、`IsSelected`、控件状态或窗口状态。
- 复杂业务规则优先沉到明确领域方法或独立业务方法，Service 不写成万能 Helper。

## 修改前检查

- 当前业务入口、Controller、Service、Repository、DTO 和调用方是否已确认。
- 事务边界是否需要覆盖多个 Repository。
- 是否影响数据库结构、API 契约、Refit 接口或 WPF 绑定。
- 是否可以小步修改，避免一次跨多个业务模块。

## 禁止事项

- 禁止 ViewModel 或 Controller 绕过 Application Service 直接调用 Repository。
- 禁止 Service 直接调用 Dapper。
- 禁止直接 `new` Repository、连接或事务对象。
- 禁止把批量保存、审批、导出等多个用例塞进一个巨大方法。

