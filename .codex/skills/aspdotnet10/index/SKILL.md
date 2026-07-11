---
name: aspdotnet10
description: SwiftExport .NET 10 / ASP.NET 后端技能组入口。用于后端 API、Application Service、Contracts DTO/Refit、Dapper Repository、数据库实体、事务、状态与单据流程相关任务。触发后先根据任务选择本技能组下的子 skill。
---

# SwiftExport ASP.NET 后端技能组

## 使用方式

后端任务先读：

1. `AGENTS.md`
2. `.agents/router.toml`
3. `.agents/backend.toml`
4. `.agents/workflows/后端API工作流.md`
5. 本技能组对应子 skill

## 子 Skill 路由

- 新增/修改 API、Controller、Application Service：读 `../api-application-service/SKILL.md`。
- 新增/修改普通业务 Controller、`BaseController`、Controller 路由、HTTP 方法、防重复提交标注：读 `../webapi-controller-boundary/SKILL.md`。
- 修改登录、注册、JWT、`/api/auth/me`、`ICurrentUser`、DevUser 免登录、认证授权：读 `../webapi-auth-current-user/SKILL.md`。
- 修改 `Program.cs`、中间件顺序、Serilog、Swagger、JSON、全局异常、防重复提交 Filter：读 `../webapi-pipeline-filters/SKILL.md`。
- 涉及 Application 服务基础依赖、`ServiceBaseContext`、`DbSession`、日志、事务 scope：读 `../application-service-context/SKILL.md`。
- 继承或修改 `IBaseService` / `BaseService`，实现标准 CRUD、保存前校验 hook：读 `../application-base-service/SKILL.md`。
- 使用 `CreateItemRequest`、`UpdateItemRequest`、`DeleteItemRequest`、`SaveItemResult`、批量保存、分页 Request/Response：读 `../application-transfer-dtos/SKILL.md`。
- 涉及 Entity/DTO/前端模型转换、Mapster 配置、`Adapt` 映射：读 `../application-mapster-mapping/SKILL.md`。
- 写 Repository、SQL、Dapper、事务：读 `../dapper-repository/SKILL.md`。
- 修改 `DbConnectionFactory`、`DbSession`、连接字符串、Connection/Transaction 生命周期：读 `../infrastructure-db-session/SKILL.md`。
- 修改 `IRepository`、`DapperRepository`、`RepositoryBaseContext`、泛型 CRUD、乐观并发、软删除：读 `../infrastructure-repository-base/SKILL.md`。
- 修改 `AuditStampService`、审计字段、`CreatedBy/UpdatedBy`、当前用户打戳：读 `../infrastructure-audit-stamp/SKILL.md`。
- 修改 `DependencyInjection.cs`、`AddInfrastructureServices`、Repository 注册、Infrastructure NuGet 依赖：读 `../infrastructure-di-registration/SKILL.md`。
- 改 Contracts 共享契约总入口：读 `../contracts-refit-dto/SKILL.md`。
- 修改 `SwiftExport.Contracts` 项目、处理 `netstandard2.0` 兼容和 C# 语法限制：读 `../contracts-netstandard20/SKILL.md`。
- 修改 `Apis/**`、Refit API、`IGenericApi`、Controller 路由对齐：读 `../contracts-refit-api/SKILL.md`。
- 修改 `DTOs/**`、Auth DTO、Base DTO、前后端交互模型：读 `../contracts-dto-models/SKILL.md`。
- 修改 `Wrappers/**`、`ServiceResult`、批量保存、分页、保存结果：读 `../contracts-wrappers/SKILL.md`。
- 修改 `SysEnums/**`、前后端共享枚举、业务状态枚举：读 `../contracts-shared-enums/SKILL.md`。
- 修改 `Common/**`、共享静态工具、顺序 Guid、保存结果工厂：读 `../contracts-common-helpers/SKILL.md`。
- 涉及业务流程、单据状态、库存流水、改单：读 `.agents/references/业务架构总纲.md` 和 `.agents/references/数据模型与状态规则.md`。

## 总原则

- WebApi 只做 HTTP 边界转发。
- WebApi 管道负责认证、授权、异常、序列化、Filter，不承载业务流程。
- Application 承担业务编排、校验和事务。
- `ServiceBaseContext` 是 Application 服务层基础依赖聚合。
- `BaseService` 只承载标准 CRUD；业务命令必须显式建方法。
- Contracts 承担前后端共享 DTO/API/枚举。
- Contracts 是 `netstandard2.0` 项目，不使用高版本 C# 语法。
- Core 承担 Entity 和 Repository Interface。
- Infrastructure 承担 Dapper Repository 实现。
- Infrastructure 使用 `Microsoft.Data.SqlClient`、`Dapper`、`Microsoft.Extensions.Logging`。
- `DbSession` 是当前连接和事务上下文，Repository 不自行创建连接。
- `RepositoryBaseContext` 是仓储层基础依赖聚合。
- API/前端可见只读联表模型优先放 Contracts DTO。
- Mapster 是服务层和前端都可使用的映射工具，复杂映射集中配置。
