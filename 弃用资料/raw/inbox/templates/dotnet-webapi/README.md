# dotnet-webapi

这是默认 WebAPI + Dapper + SQL Server 后端模板。

模板默认链路：

```text
HTTP Request Scope
  -> DbSession
  -> ServiceBaseContext
  -> Application Service
  -> RepositoryBaseContext
  -> Repository
```

## 目录结构

```text
src/
  Contracts/
  Core/
  Application/
  Infrastructure/
  WebApi/
docs/
```

## 默认约定

- `DbSession` 注册为 scoped，同一个 HTTP request 内 Service 与 Repository 共享同一连接和事务。
- Service 负责事务边界，Repository 只负责 SQL 和 Dapper。
- `RepositoryBaseContext` 聚合 `DbSession`、`ILoggerFactory`、`IAuditStampService`。
- `ServiceBaseContext` 聚合 `DbSession`、`ILoggerFactory`。
- 默认使用显式 Service 和显式 Controller，不提供泛型 BaseService / BaseController。
- `BatchRequestDto`、`BatchResultDto` 和 `SaveDocumentRequestBase` 作为契约保留；批量保存逻辑由具体 Service 按业务显式实现。
- DTO、Request、Response、Refit API 放在 `Contracts`。
- Entity 和 Repository 接口放在 `Core`。
- Service 和业务编排放在 `Application`。
- Dapper Repository、DbSession、审计服务放在 `Infrastructure`。
- Controller、中间件、当前用户实现放在 `WebApi`。

## 占位符

生成新项目时替换：

- `{{ProjectName}}`：项目命名空间根。
- `Sample`：示例业务模块名，可替换为真实模块。
- `{{DatabaseName}}`、`{{DbUser}}`、`{{DbPassword}}`：数据库配置占位。

## 后续处理

`后端架构示例/` 是临时源素材。确认本模板完整后，可以删除该临时目录。
