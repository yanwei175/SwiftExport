# Skill: Backend Contexts

## When to use

当任务涉及 WebAPI 默认后端基础链路、`DbSession`、`ServiceBaseContext`、`RepositoryBaseContext`、scoped 生命周期、事务边界、审计打戳或 Repository 基础依赖时使用。新项目初始化或模板同步时，先阅读 `dotnet-webapi-template`。

## Required reading

- `skills/backend/dotnet-webapi-template/skill.md`
- `templates/dotnet-webapi/docs/backend-architecture.md`
- `guides/backend/application-service-guide.md`
- `guides/backend/dapper-repository-guide.md`
- `guides/backend/unit-of-work-guide.md`

## Workflow

1. 先定位相关文件：`DbSession`、`ServiceBaseContext`、`RepositoryBaseContext`、DI 注册、Service、Repository、审计服务。
2. 先给计划，说明当前项目采用 WebAPI scoped `DbSession`，还是 UOW/连接工厂；除非用户明确要求直接执行，否则不要跳过计划。
3. 如果是新项目或模板同步，先使用 `dotnet-webapi-template` 判断目标模板结构。
4. 检查 `DbSession` 是否 scoped，并确认 Service 与 Repository 在同一 request scope 中共享同一个实例。
5. 检查 Service 是否通过 `ServiceBaseContext` 管理事务和日志，且不直接写 SQL。
6. 检查 Repository 是否通过 `RepositoryBaseContext` 使用当前连接、当前事务、LoggerFactory 和 AuditStamp。
7. 检查事务边界：Service 开启、提交、回滚，Repository 不开启业务事务。
8. 修改后运行对应验证命令，优先 `dotnet build`。

## Rules

- WebAPI 默认链路是 `HTTP Request Scope -> DbSession -> ServiceBaseContext -> Service -> RepositoryBaseContext -> Repository`。
- 默认 DI 生命周期为 `AddScoped<DbSession>()`、`AddScoped<ServiceBaseContext>()`、`AddScoped<RepositoryBaseContext>()`。
- `DbSession` 默认不抽接口，除非项目确实需要多实现、测试替身、多租户、读写分离或严格分层。
- `ServiceBaseContext` 聚合 `DbSession` 和 `ILoggerFactory`。
- `RepositoryBaseContext` 聚合 `DbSession`、`ILoggerFactory` 和 `IAuditStampService`。
- `DbSession` 只持有当前连接和当前事务。

## Do not

- 不要让 Domain 依赖 `DbSession`、`ServiceBaseContext` 或 `RepositoryBaseContext`。
- 不要让 Repository 自己开启业务事务。
- 不要在 Service 中写 SQL 或调用 Dapper。
- 不要在 `RepositoryBaseContext` 中写业务逻辑。
- 不要把 `AuditStamp` 和审批、状态流转混在一起。
- 不要把 SQL Server 写死为所有项目唯一标准；这是 WebAPI 默认路线，不是全架构强制路线。

## Validation

- 有 `.csproj` 时运行 `dotnet build`。
- 检查 DI 注册是否为 scoped。
- 检查 Service 和 Repository 是否共享同一个 `DbSession`。
- 检查事务提交、回滚和释放路径。
- 检查 Repository 是否使用当前 `Session.Transaction`。
- TODO：待人工确认是否给 `DbSession` 统一增加 `BeginTransaction()`、`Commit()`、`Rollback()` 方法模板。

## Output

- 汇报项目采用的是 scoped `DbSession`，还是 UOW/连接工厂。
- 汇报 `ServiceBaseContext` 和 `RepositoryBaseContext` 的职责边界。
- 汇报事务边界和 DI 生命周期。
- 汇报审计字段处理方式。
- 汇报验证命令、结果和待人工确认项。
