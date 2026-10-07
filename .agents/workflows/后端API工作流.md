# 后端 API 工作流

## 适用

新增或修改 WebApi、Application Service、Contracts DTO/API、Core Repository Interface、Infrastructure Dapper Repository。

## 步骤

1. 先读 `AGENTS.md`、`.agents/router.toml`、`.agents/backend.toml`。
2. 先读 `.codex/skills/aspdotnet10/index/SKILL.md`，再按任务读取：
   - API / Application Service：`.codex/skills/aspdotnet10/api-application-service/SKILL.md`
   - 复杂单据 / CommandId / Preview / 幂等回执 / 多仓储事务 / 锁顺序：`.codex/skills/aspdotnet10/application-document-command/SKILL.md`
   - Controller / Service / Interface / Policy / Repository / DTO 文件与职责拆分：`.codex/skills/aspdotnet10/backend-file-organization/SKILL.md`
   - 后端测试选择 / 权限 / 并发 / 幂等 / 回滚 / SQL 契约门禁：`.codex/skills/aspdotnet10/backend-quality-gates/SKILL.md`
   - Controller / BaseController / 路由 / HTTP 方法 / 防重复提交标注：`.codex/skills/aspdotnet10/webapi-controller-boundary/SKILL.md`
   - AuthController / JWT / ICurrentUser / DevUser / 认证授权：`.codex/skills/aspdotnet10/webapi-auth-current-user/SKILL.md`
   - Program.cs / Middleware / Filter / Serilog / Swagger / JSON：`.codex/skills/aspdotnet10/webapi-pipeline-filters/SKILL.md`
   - ServiceBaseContext / DbSession / 服务层基础依赖：`.codex/skills/aspdotnet10/application-service-context/SKILL.md`
   - IBaseService / BaseService / 标准 CRUD / 保存前校验：`.codex/skills/aspdotnet10/application-base-service/SKILL.md`
   - CreateItemRequest / SaveItemResult / BatchSave / Paged Request/Response：`.codex/skills/aspdotnet10/application-transfer-dtos/SKILL.md`
   - Mapster / Entity 与 DTO 映射：`.codex/skills/aspdotnet10/application-mapster-mapping/SKILL.md`
   - Repository / SQL / Dapper：`.codex/skills/aspdotnet10/dapper-repository/SKILL.md`
   - DbConnectionFactory / DbSession / Connection / Transaction：`.codex/skills/aspdotnet10/infrastructure-db-session/SKILL.md`
   - IRepository / DapperRepository / RepositoryBaseContext：`.codex/skills/aspdotnet10/infrastructure-repository-base/SKILL.md`
   - AuditStampService / 审计字段 / 当前用户打戳：`.codex/skills/aspdotnet10/infrastructure-audit-stamp/SKILL.md`
   - DependencyInjection / AddInfrastructureServices / Repository 注册：`.codex/skills/aspdotnet10/infrastructure-di-registration/SKILL.md`
   - Contracts 共享契约总入口：`.codex/skills/aspdotnet10/contracts-refit-dto/SKILL.md`
   - Contracts netstandard2.0 兼容 / C# 语法限制：`.codex/skills/aspdotnet10/contracts-netstandard20/SKILL.md`
   - Refit API / IGenericApi / Controller 路由对齐：`.codex/skills/aspdotnet10/contracts-refit-api/SKILL.md`
   - DTOs / Auth DTO / BaseDto / 交互模型：`.codex/skills/aspdotnet10/contracts-dto-models/SKILL.md`
   - Wrappers / ServiceResult / BatchSave / PagedResponse：`.codex/skills/aspdotnet10/contracts-wrappers/SKILL.md`
   - SysEnums / 共享枚举 / 状态枚举：`.codex/skills/aspdotnet10/contracts-shared-enums/SKILL.md`
   - Common / 静态工具 / SaveItemResultFactory：`.codex/skills/aspdotnet10/contracts-common-helpers/SKILL.md`
3. 读取 `.agents/references/工程架构总纲.md`；涉及数据库、仓储、SQL、连接、结构或数据库工具时读取 `.agents/references/PostgreSQL当前基线.md`。涉及单据、库存、需求链路时读取业务架构和数据模型参考。
4. 按 `.agents/references/业务模块能力与页面契约治理规则.md` 的模块地图索引读取命中地图；由 WPF 业务页面驱动的纵向切片同时读 `.agents/references/WPF ERP页面纵向切片开发教程.md`；跨采购—入库—库存交接须读三份地图，并检查当前事实与偏差 ID。
5. 读取相关 Controller、Application Interface/Service、Contracts API/DTO、Entity、Repository，对比能力地图和偏差账本。
6. 当前任务会修改、复制或扩大偏差时，先输出门禁偏差提醒，说明规则、事实、影响、最小重构和兼容方案，然后暂停等待用户决定。
7. 先设计或确认 Contracts DTO/API/枚举；新增或复用 DTO 时读取 `.codex/skills/aspdotnet10/contracts-dto-models/references/DTO复用与文件组织门禁.md`，搜索现有契约及其调用方，记录复用或新建理由。
8. DTO 默认按 API 用例独立设计；只有语义、字段口径、权限暴露、生命周期一致且已有真实多消费者时复用，不建立跨用例万能 DTO。“窄”指契约职责单一，不要求最少字段；查询 DTO 必须完整支撑目标用例。拆分现有 DTO 前建立字段处置矩阵，调用方迁移并验证前不得删除仍需字段。
9. 在 Application Interface 声明方法，在 Service 实现业务编排。复杂单据先按 Query、Source/Lookup、Draft、Confirmation、Posting、Reversal、Trace 等真实用例族划分职责；不按页面建立宽 Manager Service，也不机械生成全部用例族。
10. 正式命令明确 Version、CommandId/CommandItemId、请求指纹、回执、审计、事务所有者、隔离级别、稳定锁顺序、取消和失败恢复；Preview 只读，提交时重新锁定和校验。
11. 优先复用现有 Repository；必要时才新增 Core Interface 和 Infrastructure 实现。命令唯一性必须有数据库约束或等价持久化防线，不能只靠先查后写。
12. Controller 只做 HTTP 转发。防抖只作体验补充，不替代正式幂等；认证与细粒度业务权限分别校验。
13. 按 `.codex/skills/aspdotnet10/backend-quality-gates/SKILL.md` 选择与风险匹配的测试，并运行 `dotnet build .\SwiftExport-WebAPI.slnx`；未运行项说明原因和剩余风险。

## 边界

- WebApi 不写 SQL。
- Application 不直接创建连接。
- Infrastructure 不引用 Application。
- Core 不放前端可见 DTO/枚举。
