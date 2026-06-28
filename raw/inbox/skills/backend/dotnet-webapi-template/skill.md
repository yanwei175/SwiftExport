# Skill: dotnet-webapi-template

## When to use

当任务涉及创建、同步或审查 `templates/dotnet-webapi` 后端模板骨架时使用。适用于 WebAPI + Dapper + SQL Server 默认后端模板、五层目录结构、模板占位符替换、基础上下文链路和示例模块生成前置检查。

## Required reading

- `templates/dotnet-webapi/README.md`
- `templates/dotnet-webapi/docs/backend-architecture.md`
- `guides/backend/application-service-guide.md`
- `guides/backend/dapper-repository-guide.md`
- `guides/shared/refit-api-guide.md`

## Workflow

1. 先定位目标项目和模板源：确认是否使用 `templates/dotnet-webapi/src`，以及目标项目名、根命名空间、数据库配置占位符。
2. 先给计划，说明将生成或同步哪些层：Contracts、Core、Application、Infrastructure、WebApi；除非用户明确要求直接执行，否则不要跳过计划。
3. 判断任务是新项目初始化、模板对齐、示例模块替换，还是为 CRUD/Repository/Refit 任务做前置准备。
4. 从模板复制骨架时，替换 `{{ProjectName}}`，并按用户要求保留 `SampleItem` 示例模块或替换成真实模块名。
5. 检查默认链路是否完整：`HTTP Request Scope -> DbSession -> ServiceBaseContext -> Application Service -> RepositoryBaseContext -> Repository`。
6. 检查 DI 注册、appsettings、ServiceResult、DTO、Refit API、Controller 路由和 Repository 接口是否在五层中各归其位。
7. 如果任务进入具体 CRUD、Repository 或契约变更，切换到对应 Skill：`dotnet-webapi-crud`、`dapper-repository`、`refit-contract-change`。
8. 修改后运行对应验证命令；模板文件仍包含占位符时，不运行 build，只做静态搜索和路径检查。
9. 输出生成/同步的文件、模板占位符、保留的示例模块、未验证项和后续需要使用的 Skill。

## Rules

- 默认模板链路使用 scoped `DbSession`。
- 默认使用显式 Service 和显式 Controller。
- 默认保留 `BatchRequestDto`、`BatchResultDto`、`SaveDocumentRequestBase` 作为契约。
- 默认保留 `IRepository<>` 和 `DapperRepository<>`，但查询条件使用明确的 `whereSql`、`parameters`、`orderBy`。
- `Contracts` 放 DTO、Request、Response、Refit API、ServiceResult 和分页/批量契约。
- `Core` 放 Entity、Repository 接口、当前用户接口和审计接口。
- `Application` 放显式 Service、ServiceBaseContext、业务编排、事务边界和映射注册。
- `Infrastructure` 放 DbSession、RepositoryBaseContext、DapperRepository、具体 Repository 和审计服务。
- `WebApi` 放 Controller、Filter、Middleware、CurrentUser、Program 和 appsettings。

## Do not

- 不要把项目专属业务代码写回通用模板。
- 不要把本 Skill 当成 CRUD 业务实现 Skill。
- 不要恢复 `BaseService`、`IBaseService`、`BaseController` 默认泛型服务链。
- 不要生成 `QueryCriteria`、`ParseQueryCriteria` 或动态查询解析器。
- 不要让 Repository 开启业务事务。
- 不要让 Controller 直接写业务逻辑或直接调用 Dapper。
- 不要把 UI 临时状态放进 Shared DTO。

## Validation

- 用 `rg --files templates/dotnet-webapi` 检查模板骨架是否完整。
- 用 `rg "{{ProjectName}}"` 检查占位符是否按场景保留或替换。
- 用 `rg "BaseService|IBaseService|BaseController|QueryCriteria|ParseQueryCriteria"` 确认只以禁止或说明语义出现。
- 用 `rg "BatchRequestDto|BatchResultDto|SaveDocumentRequestBase"` 确认批量契约仍存在。
- 新项目已替换占位符并包含 `.csproj` 时运行 `dotnet build`。
- 模板仍含占位符时，不运行 build，并在输出中说明原因。

## Output

- 汇报本次是模板初始化、模板同步、示例模块替换，还是前置架构检查。
- 汇报涉及的层和关键文件。
- 汇报占位符替换结果和数据库配置处理方式。
- 汇报是否保留 `SampleItem` 示例模块。
- 汇报是否需要继续调用 CRUD、Repository、Refit 或安全重构 Skill。
- 汇报验证命令、结果和未验证风险。
