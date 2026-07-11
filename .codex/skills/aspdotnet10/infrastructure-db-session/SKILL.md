---
name: aspdotnet10-infrastructure-db-session
description: SwiftExport Infrastructure 数据库连接与 DbSession 技能。用于 DbConnectionFactory、SqlConnectionFactory、DbSession、Microsoft.Data.SqlClient、连接字符串、Connection/Transaction 生命周期、多个 Repository 共享同一事务上下文的任务。
---

# Infrastructure DbSession Skill

## 必读

- `SwiftExport.Infrastructure/Data/DbConnectionFactory.cs`
- `SwiftExport.Infrastructure/Data/DbSession.cs`
- `SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs`
- 涉及 Repository 时读 `../infrastructure-repository-base/SKILL.md`
- 涉及 Service 事务边界时读 `../application-service-context/SKILL.md`

## 什么时候使用

- 修改数据库连接创建方式。
- 修改 `DbSession.Connection` 或 `DbSession.Transaction`。
- 处理连接字符串、`Microsoft.Data.SqlClient`、连接打开/释放。
- 设计多 Repository 共享事务。
- 排查事务未传递、连接被提前释放、重复创建连接。

## 核心规则

- 当前 `DbSession` 是 scoped 数据库会话，创建时打开 SQL Server 连接。
- `DbSession.Transaction` 是当前 request scope 中共享的事务引用。
- Application Service 控制业务事务开启、提交、回滚。
- Repository 执行 Dapper SQL 时必须使用当前 `Connection` 和 `Transaction`。
- 不在 Repository 或 Controller 中创建新连接。
- 不硬编码连接字符串。

## 详细参考

- `references/DbSession与连接事务规则.md`
