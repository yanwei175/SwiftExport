# 00 公共基础设施治理（模块重构前置）

> 范围：只治理横切技术，不改任何业务规则、Core 实体、表结构。
> 执行 Agent 必读：`.codex/skills/backend/index/SKILL.md`、`backend-refactor`、`backend-delivery`。
> 原则：4-5 名用户的小型 ERP，**简单优先**；每个横切关注点全仓只有一个实现。
> **目标以 `Handoff/Claude/Backend-Architecture-Guide.md` 为准**；本文只描述现状差距与迁移顺序，不得因现有代码而降低目标。客户端（WPF/VSTO/Web）全部自有，契约变更与客户端同步修改，不做旧格式兼容。

## 1. 现状盘点（执行前须复核）

| 关注点 | 现状 | 问题 |
|---|---|---|
| 权限 | `WebApi/Authentication/EndpointPermissionFilter.cs`（452 行）用 `"Controller.Method"` 字符串字典映射 `PermissionKeys` | 改类名/方法名即静默失效；权限不在端点上可见；模块重构必然大面积断裂 |
| 认证 | JWT + `CredentialVersionTokenValidator` + `CurrentUser` + 开发 `DevUserMiddleware` | 基本合理，保留 |
| 错误 | `GlobalExceptionMiddleware` + `ServiceResultHttpFilter` + `ServiceErrorHttpMapper` + `ServiceErrorResponse` + `ServiceResult<T>` 包装 + 十余个模块异常（`ProcurementOperationException`、`PackingListException`、`InventoryPostingException`、`BusinessCommandException`、`CommandValidationException` 等） | 两条错误通道（返回值 + 异常）并存；异常类型按模块膨胀 |
| 命令 | `CommandPipeline` → `BusinessCommandExecutor` → `ICommandReceiptRepository` + `PostgreSqlCommandLock`（274 行）+ `ICommandTransactionPreparation`/`ICommandRelatedTransactionPreparation`；约 31 个文件引用 | 两层执行器 + 准备接口，链路长 |
| 事务 | `DbSession`/`IDbSession`；约 35 个 Application 文件直接 `BeginTransaction` | 事务开启方式不统一 |
| 基类 | `BaseController`、`BaseService`/`IBaseService`、`ServiceBaseContext`（只包 Session + LoggerFactory）、`RepositoryBaseContext`、`PostgreSqlDapperRepository` | 上下文包装类无价值；泛型 CRUD 基类掩盖实际 SQL |
| 防重复 | `PreventDuplicateRequestAttribute` | 与 CommandId 幂等职责重叠 |
| 审计 | `AuditStampService`、`BusinessOperationLog` + 仓储、`CorrelationIdMiddleware` | 保留，统一调用点 |
| 映射/校验 | `MapsterConfig`、`CommandRequestValidator` | 校验框架需与 FluentValidation 统一 |
| 中间层命名 | 大量 `*Support`、`*Builder`、`*Policy`、`*Manager`（如 `ProcurementOrderCommandSupport`、`PurchaseDraftPreviewBuilder`、`ProductImageTargetLockManager`） | 属于模块重构时消化，**不在本任务处理** |

## 2. 目标形状（每项一个实现）

| 关注点 | 目标 |
|---|---|
| 权限 | 端点上声明：`[RequirePermission(PermissionKeys.Xxx)]`（ASP.NET Core 授权策略 + 一个 `AuthorizationHandler` 调 `IPermissionEvaluationService`）。删除字典。未标注且非 `[AllowAnonymous]` 的端点启动时报错（测试断言）。 |
| 错误 | 一条通道：业务失败抛 `BusinessException(code, message, field)`；`IExceptionHandler` 输出 ProblemDetails（`code`/`field` 放扩展字段）。`ServiceErrorCodes` 保留。模块异常并入。 |
| 响应格式 | ProblemDetails；成功直接返回 DTO。`ServiceResult<T>` 淘汰，客户端同步改为按 HTTP 状态 + ProblemDetails 处理。 |
| 命令 | 一个 `CommandExecutor.ExecuteIdempotentAsync(commandId, commandType, targetUid, request, work)`：查 `command_receipt` → 开事务 → 执行 → 写回执 → 提交；并发重复由 `command_receipt.CommandId` 唯一索引兜底。`CommandPipeline` 与 `BusinessCommandExecutor` 合并为它。 |
| 锁 | 删除 `PostgreSqlCommandLock`/`PostgreSqlSourceDemandLock`；并发由行锁 `SELECT ... FOR UPDATE`（写在仓储）+ 唯一约束 + `Version` 保证。确需保留须写明具体并发场景并经用户确认。 |
| 事务 | 极薄 `IUnitOfWork`（基于现有 `DbSession`）；只在 Application Service 和 CommandExecutor 中开启；仓储不开事务。现有 `BeginTransaction` 调用点全部迁移。 |
| 基类 | 删除 `ServiceBaseContext`（直接注入 `IDbSession`/`ILogger<T>`）；`BaseService` 与泛型 CRUD 不再被新代码继承，旧代码随模块重构退出。 |
| 防重复 | 有 CommandId 的端点不再挂 `PreventDuplicateRequest`；其余端点评估后删除该特性。 |
| JSON | 只用 System.Text.Json；现有 decimal 输出转换器保留。 |
| 日志 | Serilog + CorrelationId 中间件保留；不新增日志 Helper。 |
| DI | 每个项目一个 `DependencyInjection.cs`；模块内服务按约定注册，不新增注册框架。 |

## 3. 执行顺序（每步独立可交付、可回滚）

1. **权限声明化**：新增特性 + 策略 + Handler；用脚本把字典逐条转换为端点特性；加启动/测试断言；删除字典 Filter。权限键与行为一一不变。
2. **错误通道统一**：引入 `BusinessException` + `IExceptionHandler` + ProblemDetails；删除 `GlobalExceptionMiddleware`、`ServiceResultHttpFilter`、`ServiceErrorHttpMapper`、`ServiceErrorResponse`；客户端统一错误解析同步修改。`ServiceResult<T>` 返回值可随模块重构逐个移除，但新代码禁止使用。
3. **CommandExecutor 合并**：合并 `CommandPipeline` + `BusinessCommandExecutor`，保持 `command_receipt` 字段、指纹算法版本和重放行为不变；调用点机械替换。
4. **IUnitOfWork**：基于 `DbSession` 提供，迁移全部 `BeginTransaction` 调用点；删除命令锁类。
5. **删除 `ServiceBaseContext`**：机械替换构造参数。
6. **防重复特性评估**：列出使用点与 CommandId 覆盖情况，交用户确认后删除。
7. **残留检查与文档**：更新 `.agents/references` 中过期的工程/架构文档（重写或删除），与本文一致。

每步验收：解决方案构建通过；现有测试通过；权限步骤需有“每个端点都有权限声明”的测试；错误步骤需有响应 JSON 快照测试。

## 4. 已决定事项

- 已决定：ProblemDetails、淘汰 `ServiceResult<T>`、删除命令锁类、事务全部迁移。
- 执行中如发现某个锁对应真实并发场景无法用行锁/唯一约束覆盖，停下报告用户。

## 5. 禁止

- 改 Core 实体、表结构、`command_receipt` 字段、权限键值、错误码值。
- 改业务规则或顺手重构业务模块。
- 新增 Pipeline/Mediator/Support/Helper/Manager 类。
