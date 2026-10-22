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

- PostgreSQL SQL、索引、执行计划、查询性能、JSONB、分区、全文检索、连接池、扩展或数据库运维任务：使用下节的 `postgres-skills` 插件技能，并同时保留本表命中的项目专项 Skill；仅消费已有 API/DTO 或调整前端布局不触发。

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

## PostgreSQL 插件技能

- 从当前会话技能目录按名称发现并读取 `postgres-skills:postgresql-best-practices` 的 `SKILL.md`，再按实际问题选择其 reference；不复制插件正文，不把本机用户名、缓存路径或插件版本写进项目索引。SQL 审查、结构/索引规划和性能分析同样适用，不限于代码实现。
- 插件负责 PostgreSQL 通用技术建议；项目技能继续负责 Dapper 参数/映射、DbSession 共享、Application 事务所有权、业务规则及定向验收。使用插件不替代这些约束，也不新增提供者切换层。
- 只有明确涉及 Apache AGE、openCypher、知识图谱或属性图任务时，才发现并读取 `postgres-skills:pg-graph`；普通 BOM 树、需求关联、递归 CTE 和单据追溯不自动触发图数据库设计或扩展安装。
- 纯源码审查、SQL 设计与离线规划不为激活技能而连接数据库，使用插件通用 PostgreSQL 资料。实际数据库执行先确认已有连接、实例、库名、版本和部署类型；不因安装插件就假定存在 `postgres_mcp_*` 工具，不猜测连接串。插件要求的连接/能力检查无法完成时，说明限制并停止依赖该连接的执行，可继续离线分析；不得把其他工具冒充该能力检查或绕过拒绝。
- Azure 专项仅在明确的 Azure 任务或已核实 Azure 部署时采用；当前 PostgreSQL + Dapper 基线本身不证明 Azure。未确认的版本、扩展和托管能力不得写成可执行事实。
- 数据库写入仍受 `AGENTS.md` 的实例、库名、初始化/清理范围约束；插件示例不是执行授权。已有明确授权仅在原范围内复用；DDL、扩展安装、配置更改、数据导入及维护操作不能由只读任务自动触发。`EXPLAIN ANALYZE` 会实际执行语句，不能将写语句的分析当作只读检查。
- 插件未安装或技能不可读时明确报告，不假称已使用；继续不依赖插件的项目规则核对和离线分析。依赖插件且无法核实的技术结论或执行步骤保留为未完成项。

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
