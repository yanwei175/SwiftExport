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
| 审计 | `AuditStampService`、`BusinessOperationLog` + 仓储 | 保留，统一调用点 |
| 关联 ID | `CorrelationIdMiddleware` + 客户端自定义关联头 | 与框架 TraceId 重复（决策 #26） |
| 映射/校验 | `MapsterConfig`、`CommandRequestValidator` | 校验框架需与 FluentValidation 统一 |
| 中间层命名 | 大量 `*Support`、`*Builder`、`*Policy`、`*Manager`（如 `ProcurementOrderCommandSupport`、`PurchaseDraftPreviewBuilder`、`ProductImageTargetLockManager`） | 属于模块重构时消化，**不在本任务处理** |

## 2. 目标形状（每项一个实现）

| 关注点 | 目标 |
|---|---|
| 权限 | 端点上声明：`[HasPermission(Permissions.Xxx)]`（决策 #22；ASP.NET Core 授权策略 + 一个 `AuthorizationHandler` 调 `IPermissionEvaluationService`）。删除字典。未标注且非 `[AllowAnonymous]` 的端点启动时报错（测试断言）。 |
| 错误 | 一条通道：业务失败抛 `BusinessException(code, message, field)`；`IExceptionHandler` 输出 ProblemDetails（`code`/`field` 放扩展字段）。`ServiceErrorCodes` 保留。模块异常并入。 |
| 响应格式 | ProblemDetails；成功直接返回 DTO。`ServiceResult<T>` 按决策 #25 分三批淘汰（见第 3 节第 2 步），客户端改为按 HTTP 状态 + ProblemDetails 处理。 |
| 是否生效 | 只有 `IUnitOfWork`/`CommandExecutor` 判定事务阶段（决策 #27）：提交前失败 = 确定未生效；提交本身失败 = 503 `outcome_unknown`；500 `unexpected` 不承诺未生效，由事务阶段和回执判定。`transient` 仅限提交前的序列化失败/死锁/锁超时。 |
| 命令 | 一个 `CommandExecutor.ExecuteIdempotentAsync(commandId, commandType, targetUid, request, work)`，规则见决策 #28：开事务 → **在事务内**查 `command_receipt` → 校验回执 → 执行 → 写回执 → 提交。`ICommandExecutor` 只有这一个方法；普通写入直接用 `IUnitOfWork.ExecuteAsync`。`CommandPipeline` 与 `BusinessCommandExecutor` 合并为它。 |
| 锁 | 删除 `PostgreSqlCommandLock`/`PostgreSqlSourceDemandLock`，前提是满足决策 #29 的替代清单验收；并发由行锁 `SELECT ... FOR UPDATE`（写在仓储）+ 唯一约束 + `Version` 保证。同 CommandId 并发由 #28e 处理。 |
| 事务 | 极薄 `IUnitOfWork`（基于现有 `DbSession`）；只在 Application Service 和 CommandExecutor 中开启；仓储不开事务。现有 `BeginTransaction` 调用点全部迁移。这不是编译期保证，由架构测试兜底（决策 #30）。 |
| 基类 | 删除 `ServiceBaseContext`（直接注入 `IDbSession`/`ILogger<T>`）；`BaseService` 与泛型 CRUD 不再被新代码继承，旧代码随模块重构退出。 |
| 防重复 | 有 CommandId 的端点不再挂 `PreventDuplicateRequest`；其余端点评估后删除该特性。 |
| JSON | 只用 System.Text.Json；现有 decimal 输出转换器保留。 |
| 日志 | Serilog；不新增日志 Helper。 |
| 关联 ID | 删除 `CorrelationIdMiddleware`（决策 #26）：关联 ID = `Activity.Current.TraceId`（无 Activity 时 `HttpContext.TraceIdentifier`），Serilog 记录，ProblemDetails `traceId` 返回。现有表中的 CorrelationId 列结构不改，改为写入该 TraceId（列类型不兼容则停下问用户）。客户端不再发送自定义关联头。 |
| DI | 每个项目一个 `DependencyInjection.cs`；模块内服务按约定注册，不新增注册框架。 |

## 3. 执行顺序（每步独立可交付、可回滚）

1. **权限声明化**：新增特性 + 策略 + Handler；用脚本把字典逐条转换为端点特性；加启动/测试断言；删除字典 Filter。权限键与行为一一不变。
2. **错误通道统一**（决策 #25，分三批，本任务只做第①批）：
   - ① 引入 `BusinessException` + `IExceptionHandler` + ProblemDetails；删除 `GlobalExceptionMiddleware`、`ServiceErrorHttpMapper`、`ServiceErrorResponse`。`ServiceResultHttpFilter` **暂留为过渡适配器**：失败结果输出与异常通道相同的状态码 + ProblemDetails（共用同一映射），成功体暂不变；过渡 Filter 不得新增其他逻辑。客户端一次性切换为统一 ProblemDetails 错误解析。验收：返回 `ServiceResult` 的每个端点失败时都返回非 2xx（测试覆盖，不允许失败返回 200）。
   - ② 随模块重构逐模块移除 `ServiceResult`（Service 抛异常、Controller 返回 DTO），该模块客户端成功体解析同批修改。
   - ③ 全仓不再有 `ServiceResult` 返回值时，同批删除过渡 Filter 与 `ServiceResult<T>`；架构测试断言没有 Action 返回 `ServiceResult`。
   - 新代码禁止返回 `ServiceResult`。
   - 同步删除 `CorrelationIdMiddleware` 与客户端自定义关联头（决策 #26），ProblemDetails 带 `traceId`。
3. **CommandExecutor 合并**（决策 #28）：合并 `CommandPipeline` + `BusinessCommandExecutor`，`ICommandExecutor` 只保留 `ExecuteIdempotentAsync`；原先只为事务而经过执行器的普通写入改为直接 `IUnitOfWork.ExecuteAsync`。
   - 顺序：开事务 → 在事务内查 `command_receipt` → 校验回执 → 执行 → 写回执 → 提交。
   - 回执校验依次比对 `FingerprintVersion`、`CommandType`、`ActorUid`、`RequestTargetUid`、`RequestFingerprint`：`FingerprintVersion` 或 `ResultVersion` 与当前不同 → 409 回执不可读，不重新执行；其余不同 → 422 `command_id_reused`。
   - 指纹沿用现有实现的规范化方式、算法与 `FingerprintVersion` 当前值；ReferenceDesign 中的 `SHA256(JSON)` 与 `=1` 仅为示意。
   - 同 CommandId 并发：服务端不加锁、不改表、不回查；后到请求因回执唯一索引冲突（409 `command_in_flight`）或单据 Version 冲突（409）失败并整体回滚，客户端用同一 CommandId 重试。
   - `command_receipt` 字段与重放行为不变；调用点机械替换。
4. **IUnitOfWork**（决策 #27、#29、#30）：基于 `DbSession` 提供，迁移全部 `BeginTransaction` 调用点；提交前失败与提交本身失败按 #27 区分。
   - 删除命令锁类前，先产出锁替代清单并经用户确认：每个加锁调用点 → 锁键 → 原锁保护的约束/不变量 → 替代手段（具体 `SELECT … FOR UPDATE` 的表与行、已存在的唯一约束名，或带条件的 UPDATE/INSERT SQL）→ 对应并发测试。命令锁的“同 CommandId 并发”由第 3 步规则替代。替代手段需要新增唯一约束或改表时停下问用户；清单未覆盖的调用点不得删除锁。
5. **删除 `ServiceBaseContext`**：机械替换构造参数。
6. **防重复特性评估**：列出使用点与 CommandId 覆盖情况，交用户确认后删除。
7. **残留检查与文档**：更新 `.agents/references` 中过期的工程/架构文档（重写或删除），与本文一致。

每步验收：解决方案构建通过；现有测试通过；另有各步专项验收：
- 权限：测试断言“每个端点都有权限声明”。
- 错误：响应 JSON 快照测试；返回 `ServiceResult` 的端点失败均非 2xx。
- CommandExecutor：用现有实现生成的回执样本做“同输入同指纹”的金样测试；回执各字段不匹配时分别得到 409/422。
- IUnitOfWork：架构测试断言除 `DbSession`（`IUnitOfWork` 实现）外，全仓不出现 `BeginTransaction`/`Commit`/`Rollback` 调用；被删除的每个锁都有清单中对应的并发测试。

## 4. 已决定事项

- 已决定：ProblemDetails、淘汰 `ServiceResult<T>`（三批，#25）、删除自写关联 ID 中间件（#26）、错误“是否生效”口径（#27）、CommandExecutor 规则（#28）、删除命令锁类（以 #29 清单为前提）、事务全部迁移并以架构测试兜底（#30）。
- 执行中如发现某个锁对应真实并发场景无法用行锁/唯一约束覆盖，停下报告用户。

## 5. 禁止

- 改 Core 实体、表结构、`command_receipt` 字段、权限键值、错误码值。
- 改业务规则或顺手重构业务模块。
- 新增 Pipeline/Mediator/Support/Helper/Manager 类。
