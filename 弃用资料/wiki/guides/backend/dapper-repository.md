# Dapper Repository Guide

## 适用场景

用于使用 Dapper 访问 SQL Server 的 Repository 实现，包括查询、插入、更新、删除、复杂 SQL 和分页读取。

## SwiftExport 规则

- Repository 只负责 SQL、Dapper 调用、数据读取和持久化。
- Repository 接口放在 `SwiftExport.Core/Interfaces`，实现放在 `SwiftExport.Infrastructure/Repositories/**`。
- Repository 通过 `RepositoryBaseContext` 使用当前 request scope 的 `DbSession.Connection` 和 `DbSession.Transaction`。
- SQL 必须参数化，禁止拼接用户输入。
- 列表查询不要 `SELECT *`，DTO/ReadModel 查询字段别名必须匹配模型属性。
- Repository 不自己决定业务事务；业务事务由 Application 层按模块既有方式控制。
- 审计打戳可复用 `RepositoryBaseContext.AuditStamp`，但审计不等于业务审批或状态流转。

## 修改前检查

- 表名、字段名、主键、软删除、审计字段和并发字段是否已确认。
- SQL 是否影响现有查询性能、分页稳定性或报表。
- 返回类型是 Entity、DTO 还是 ReadModel。
- 是否需要同步修改 DTO、Refit、Controller、Application Service 或 ViewModel。

## 禁止事项

- 禁止 Repository 调用 Controller、ViewModel 或 Application Service。
- 禁止 Repository 承载复杂业务流程。
- 禁止在 Repository 中硬编码连接字符串或自行创建 `SqlConnection`。
- 禁止引入过度复杂的动态 SQL 魔法框架。

