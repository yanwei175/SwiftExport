# Dapper Repository Guide

## 适用场景

适用于使用 Dapper 访问数据库的 Repository 实现，包括查询、插入、更新、删除和复杂 SQL 读取。

## 推荐做法

- Repository 只负责 SQL、Dapper 调用、数据读取和持久化。
- 接口放在应用抽象层，实现放在 Infrastructure 或项目既有基础设施层。
- SQL 优先保持清晰可读，字段名和参数名稳定。
- WebAPI 默认后端优先使用 scoped `DbSession -> RepositoryBaseContext -> Repository` 链路。
- Repository 通过 `RepositoryBaseContext` 使用当前 request scope 的 `DbSession.Connection` 和 `DbSession.Transaction`。
- `RepositoryBaseContext` 可以聚合 `DbSession`、`ILoggerFactory`、`IAuditStampService`，但只属于 Infrastructure 实现层。
- Repository 使用当前连接/事务，不自己开启业务事务。
- 大文件拆分时按业务模块逐步抽接口和实现，旧方法可先转发到新实现。
- Web API 场景中的审计打戳、软删除、乐观并发可以作为项目约定，但不要默认强加给所有项目。
- Add/Update 前可以调用 `AuditStamp` 填充审计字段；审计打戳不等于审批或业务状态流转。

## 禁止事项

- 禁止 Repository 调用 ViewModel、Controller 或 Application Service。
- 禁止 Repository 承载复杂业务流程。
- 禁止在 Repository 内部自行 `BeginTransaction` 决定业务事务。
- 禁止让 Application/Domain 依赖 `RepositoryBaseContext` 或 `DbSession`。
- 禁止引入过度复杂的动态 SQL 魔法框架。
- 禁止为了“通用”强制所有表继承同一泛型仓储模型。

## Codex 修改前检查清单

- 数据库 provider、连接/事务抽象、现有 Repository 风格是否已确认。
- 项目默认链路是 `DbSession + RepositoryBaseContext`，还是通用 UOW/连接工厂。
- `DbSession`、`RepositoryBaseContext`、`AuditStamp`、`LoggerFactory` 的 DI 生命周期是否已确认。
- 表名、字段名、主键、软删除、审计字段和并发字段是否已确认。
- SQL 是否会影响现有数据结构或查询性能。
- 是否已有 Dapper 参数化惯例，避免字符串拼接用户输入。
- 是否需要与 Application Service 的 UOW 共享事务。

## 常见错误

- Repository 文件超过可维护范围，混入多个业务模块。
- Update 时漏掉乐观并发或审计字段的项目约定。
- 查询 DTO 和持久化 Entity 混用，导致层边界不清。
- SQL 使用字符串拼接参数，带来注入风险。
- 把通用泛型仓储当成所有业务仓储的唯一入口。
- Service 和 Repository 拿到不同 `DbSession` 实例，导致事务无法共享。
