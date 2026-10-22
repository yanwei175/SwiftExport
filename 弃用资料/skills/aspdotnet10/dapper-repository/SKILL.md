---
name: aspdotnet10-dapper-repository
description: 新增或修改 SwiftExport Dapper 仓储、SQL 与仓储持锁实现；仅调用既有仓储不触发完整基础设施设计。
---

# Dapper Repository Skill

## 必读

公共启动沿用 `AGENTS.md`；已读约束复用，只补齐本次改变的职责：

- 改变分层、公共依赖或边界不明时读 `.agents/references/工程架构总纲.md` 相关章节
- 改变实体字段归属、状态或数量口径时读 `.agents/references/数据模型与状态规则.md` 相关章节
- 修改连接/事务生命周期或 `DbSession` 实现时读 `../infrastructure-db-session/SKILL.md`
- 修改 `IRepository`、`DapperRepository`、`RepositoryBaseContext` 或其通用 CRUD 语义时读 `../infrastructure-repository-base/SKILL.md`
- 修改审计打戳行为时读 `../infrastructure-audit-stamp/SKILL.md`
- 修改 DI 注册时读 `../infrastructure-di-registration/SKILL.md`
- 涉及复杂单据、业务命令、CommandId、幂等回执、锁顺序或多仓储事务时读 `../application-document-command/SKILL.md`
- 涉及具体业务 Repository 的职责或文件拆分时读 `../backend-file-organization/SKILL.md`
- 目标模块 Repository、Interface、Entity、DTO、Service

## 分层位置

- Repository Interface：`SwiftExport.Core/Interfaces`
- Repository 实现：`SwiftExport.Infrastructure/Repositories/<Module>`
- Repository 基类：`SwiftExport.Infrastructure/Repositories/Base/PostgreSqlDapperRepository.cs`
- Repository 基础依赖：`SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs`
- Entity：`SwiftExport.Core/Entities/<Module>`
- API/前端 DTO：`SwiftExport.Contracts/DTOs/<Module>`

## Dapper 规则

- 使用 `RepositoryBaseContext`。
- 使用当前 `Connection`。
- 使用当前 `Transaction`。
- 使用 `Dapper` 执行参数化 SQL。
- PostgreSQL 连接使用 `Npgsql`。
- 日志使用 `Microsoft.Extensions.Logging`。
- 查询必须参数化。
- 禁止拼接用户输入。
- 列表查询不使用 `SELECT *`。
- 分页查询必须有稳定排序。
- DTO 查询必须显式字段别名。
- 专用 SQL 使用 `public` 下的 PG 小写表列名；泛型 CRUD 复用唯一 `EntityMetadataCache`，参数及返回别名保留 CLR 名称。

## DTO / Entity 边界

- 写入核心表时优先使用 Entity。
- API/前端只读联表查询优先直接返回 Contracts DTO。
- 不为了页面字段污染 Entity。
- Core ReadModel 不用于前端可见模型。
- Repository 返回 DTO 时，SQL 字段别名必须匹配 DTO 属性。

## 事务规则

- 多仓储业务事务由 Application Service 控制。
- Repository 执行 SQL 时传入当前 `Transaction`。
- 不在 Repository 中 new 连接。
- 不默认引入新的 UnitOfWork 模板。

## Infrastructure 基础依赖

- `DbSession` 管理当前连接和当前事务。
- `RepositoryBaseContext` 聚合 `DbSession`、`ILoggerFactory`、`IAuditStampService`、`ICurrentUser`。
- `PostgreSqlDapperRepository<T>` 负责轻量通用 CRUD、乐观并发、软删除、审计打戳。
- 复杂查询和批量写入放具体业务 Repository，不扩大泛型仓储。

## 禁止事项

- 禁止 Controller 写 SQL。
- 禁止 Application 直接写 Dapper SQL。
- 禁止 Infrastructure 引用 Application。
- 禁止硬编码连接字符串。
- 禁止为单个查询扩大泛型 Repository 能力。
