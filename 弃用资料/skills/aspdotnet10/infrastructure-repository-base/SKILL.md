---
name: aspdotnet10-infrastructure-repository-base
description: SwiftExport Infrastructure 仓储基础技能。用于 IRepository、DapperRepository、RepositoryBaseContext、Dapper SQL、EntityMetadata、泛型 CRUD、乐观并发 Version、软删除、RepositoryWriteResult、Repository 与 Core Interface 边界相关任务。
---

# Infrastructure Repository Base Skill

## 必读

- `SwiftExport.Core/Interfaces/IRepository.cs`
- `SwiftExport.Infrastructure/Repositories/Base/PostgreSqlDapperRepository.cs`
- `SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs`
- `../infrastructure-db-session/SKILL.md`
- `../infrastructure-audit-stamp/SKILL.md`

## 什么时候使用

- 新增或修改 Repository Interface。
- 新增或修改 Dapper Repository。
- 修改泛型 `IRepository<T>` 或 `PostgreSqlDapperRepository<T>`。
- 处理 `RepositoryWriteResult`、`Version`、软删除、审计字段。
- 写 SQL 查询、分页、批量、复杂联表。

## 核心规则

- Repository Interface 放 `SwiftExport.Core/Interfaces`。
- Repository 实现放 `SwiftExport.Infrastructure/Repositories/<Module>`。
- 泛型 `IRepository<T>` 只保留所有实体稳定适用的单实体读写能力。
- `PostgreSqlDapperRepository<T>` 只承载单实体 CRUD 和基础 Uid 查询。
- 批量保存、动态查询、分页查询、复杂联表放具体业务 Repository。
- 命令回执、CommandItemId、不可变流水等幂等事实使用真实业务唯一索引；不能只靠 Application 先查询再写入。
- 需要并发保护时提供语义明确的锁定方法，并由 Application 决定锁集合和稳定顺序；Repository 不自行编排跨领域流程。
- Repository 使用 `RepositoryBaseContext`，不直接散落注入连接、日志、审计。
- Repository 返回 Entity 或 Contracts 只读 DTO，不依赖 Application。

## 详细参考

- `references/仓储基类与Dapper规则.md`
