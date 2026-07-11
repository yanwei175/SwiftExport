# 后端 API 工作流

## 适用

新增或修改 WebApi、Application Service、Contracts DTO/API、Core Repository Interface、Infrastructure Dapper Repository。

## 步骤

1. 先读 `AGENTS.md`、`.agents/router.toml`、`.agents/backend.toml`。
2. 先读 `.codex/skills/aspdotnet10/index/SKILL.md`，再按任务读取：
   - API / Application Service：`.codex/skills/aspdotnet10/api-application-service/SKILL.md`
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
3. 读取 `.agents/references/工程架构总纲.md`；涉及单据、库存、需求链路时读取业务架构和数据模型参考。
4. 读取相关 Controller、Application Interface/Service、Contracts API/DTO、Entity、Repository。
5. 先设计或确认 Contracts DTO/API/枚举。
6. 在 Application Interface 声明方法，在 Service 实现业务编排。
7. 优先复用现有 Repository；必要时才新增 Core Interface 和 Infrastructure 实现。
8. Controller 只做 HTTP 转发。
9. 运行 `dotnet build .\SwiftExport-WebAPI.slnx`，或说明未运行原因。

## 边界

- WebApi 不写 SQL。
- Application 不直接创建连接。
- Infrastructure 不引用 Application。
- Core 不放前端可见 DTO/枚举。
