---
name: aspdotnet10
description: SwiftExport 后端任务的技能选择入口；按本次API、服务、契约或数据库改动加载专项规则。
---

# SwiftExport ASP.NET 后端技能组

## 使用方式

公共启动沿用 `AGENTS.md`；本入口只按业务任务选择对应子 Skill，不反向重读 Agent 和后端工作流。已读依赖按根规则复用。

下表是候选索引，不是逐项读取清单。以本次实际修改的职责或风险选择；仅消费已有契约、调用服务或汇报结果，不因此加载这些对象全部设计资料。局部实现复用有效结论，专项Skill的回引不启动第二轮流程。

数据库、仓储、SQL、结构及相关工具任务先读取 `.agents/references/PostgreSQL当前基线.md`。当前仅使用 PostgreSQL + Dapper；已有专项 Skill 继续负责技术细则，不另建 SQL Server/PG 双库分支。

## 子 Skill 路由

- 分析或治理 Core Entity 字段、逻辑外键、历史快照、DTO 分类与字段来源、数据库模型或 Infrastructure 实现路线：读 `../data-model-governance/SKILL.md`。该 Skill 只负责分析、治理和规划；实现 Repository、SQL、DTO 或 Entity 时继续使用对应实现 Skill。
- 新增/修改 API、Controller、Application Service：读 `../api-application-service/SKILL.md`。
- 盘点或整改应用服务可读性、用例调用主线、公共服务复用及跨层修改影响：读 `../application-usecase-readability/SKILL.md`；明确实施时按其专项工作流执行，只解释或设计时不自动修改代码。
- 涉及复杂单据草稿、确认、过账、反向、`CommandId`、Preview、幂等回执、多仓储事务或锁顺序：读 `../application-document-command/SKILL.md`。
- 规划或重构 Controller、Application Service、Interface、Policy、Repository、DTO/API 的文件和职责边界：读 `../backend-file-organization/SKILL.md`。
- 选择后端构建、契约、权限、并发、幂等、事务、SQL 或数据库验证：读 `../backend-quality-gates/SKILL.md`。
- 新增/修改普通业务 Controller、`BaseController`、Controller 路由、HTTP 方法、防重复提交标注：读 `../webapi-controller-boundary/SKILL.md`。
- 修改登录、注册、JWT、`/api/auth/me`、`ICurrentUser`、DevUser 免登录、认证授权：读 `../webapi-auth-current-user/SKILL.md`。
- 修改 `Program.cs`、中间件顺序、Serilog、Swagger、JSON、全局异常、防重复提交 Filter：读 `../webapi-pipeline-filters/SKILL.md`。
- 新增或改变 Application 服务的基础依赖、事务边界或日志上下文：读 `../application-service-context/SKILL.md`；仅调用现有服务不展开其基础设施设计。
- 继承或修改 `IBaseService` / `BaseService`，实现标准 CRUD、保存前校验 hook：读 `../application-base-service/SKILL.md`。
- 使用 `CreateItemRequest`、`UpdateItemRequest`、`DeleteItemRequest`、`SaveItemResult`、批量保存、分页 Request/Response：读 `../application-transfer-dtos/SKILL.md`。
- 涉及 Entity/DTO/前端模型转换、Mapster 配置、`Adapt` 映射：读 `../application-mapster-mapping/SKILL.md`。
- 新增或修改 Repository、Dapper SQL 或仓储持锁实现：读 `../dapper-repository/SKILL.md`；仅调用既有仓储按需核实其实际 SQL 和事务语义。
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
- 复杂单据按稳定用例族拆分，不按页面或代码行数建立万能 Manager Service。
- 正式业务命令使用 Version、持久化 CommandId 幂等、事务和数据库约束；防抖 Filter 不能替代这些机制。
- 命令回执保存可重放结果，BusinessOperationLog 保存人可读业务审计，两者职责不得默认混用。
- `ServiceBaseContext` 是 Application 服务层基础依赖聚合。
- `BaseService` 只承载标准 CRUD；业务命令必须显式建方法。
- Contracts 承担前后端共享 DTO/API/枚举。
- Contracts 是 `netstandard2.0` 项目，不使用高版本 C# 语法。
- Core 承担 Entity 和 Repository Interface。
- Infrastructure 承担 Dapper Repository 实现。
- Infrastructure 使用 `Npgsql`、`Dapper`、`Microsoft.Extensions.Logging`。
- Core `IDbSession` 是 Application 的连接事务契约，Infrastructure `DbSession` 是当前 PostgreSQL 实现；两者共享同一 scoped 实例，Repository 不自行创建连接。
- `RepositoryBaseContext` 是仓储层基础依赖聚合。
- API/前端可见只读联表模型优先放 Contracts DTO。
- Mapster 是服务层和前端都可使用的映射工具，复杂映射集中配置。
