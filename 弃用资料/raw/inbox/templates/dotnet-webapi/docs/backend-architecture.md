# WebAPI 后端架构说明

## 默认链路

```text
Explicit Controller
  -> Explicit Application Service
  -> Repository
  -> DbSession
  -> SQL Server
```

同一个 HTTP request 内，`DbSession` 是 scoped。Service 开启事务后，Repository 使用同一个 `Session.Transaction` 执行 Dapper SQL。

## 分层职责

- `Contracts`：Shared DTO、Request、Response、Refit API、ServiceResult、分页和批量保存契约。
- `Core`：Entity、Repository 接口、当前用户接口、审计接口。
- `Application`：显式 Service、ServiceBaseContext、业务编排、事务边界、Mapster 映射注册。
- `Infrastructure`：DbSession、RepositoryBaseContext、DapperRepository、具体 Repository、AuditStampService。
- `WebApi`：Controller、Filter、Middleware、CurrentUser、Program。

## 事务规则

- Service 负责事务边界。
- Repository 不开启业务事务。
- Repository 使用当前 `DbSession.Connection` 和 `DbSession.Transaction`。
- 事务失败必须 rollback。
- 批量保存可以使用 `BatchRequestDto` / `BatchResultDto` 契约，但默认由具体 Service 显式实现，不放入泛型 BaseService。
- 本模板不默认提供泛型 BaseService / BaseController，避免过度抽象。

## 模板来源

本模板从 `后端架构示例/` 提炼而来，去除了项目专属命名，并保留了更轻量的显式 Service / Controller 路线。
