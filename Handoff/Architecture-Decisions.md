# 架构决策日志（新窗口必读，始终保持为当前结论）

> 用途：替代长对话上下文。新窗口先读本文件 + `Handoff/Claude/Backend-Architecture-Guide.md`。
> 格式：一条一个决定；结论变化时**直接改写原条目**为最新结论，不追加“取代”条；编号保持稳定，被合并的条目保留编号并注明“已并入 #n”。

## 窗口分工

| 窗口 | 职责 | 读代码 |
|---|---|---|
| 架构 | 讨论原则、做决定、追加本日志 | 否 |
| 文档 | 按本日志修改 Handoff、skill、`.agents/references`，产出任务书 | 只读指定文件 |
| 执行 | 按任务书改代码、构建、测试（可交给其他 Agent） | 是 |

## 文档地图

- 目标原则：`Handoff/Claude/Backend-Architecture-Guide.md`（**最高技术依据**，现有代码不得反向降低目标）
- 技术样板（虚构业务，仅示意形状）：`Handoff/Claude/ReferenceDesign/**`
- 基础设施迁移任务书：`Handoff/Refactor/00-Foundation.md`
- 后端 skill：`.codex/skills/backend/**`（只含技术，无业务）
- 业务事实：`.agents/references/**`（部分过期，由文档窗口按代码行为重写或删除）
- 历史讨论（不作依据）：`Handoff/Claude/Gap-Inventory.md`

## 决策

1. 系统规模 4-5 用户：简单优先，放弃大规模并发机制。
2. 技术 skill 与业务分离：skill 不写字段/状态/数量规则；业务以 `.agents/references` + Core 实体为准。
3. Core 实体、表结构保留；表间依赖、字段引用、唯一约束、表结构变更必须先问用户。
4. 幂等沿用 `command_receipt`（`CommandReceipt`），不改名；`CommandId` 放请求体。
5. 全仓唯一 `CommandExecutor`（合并 `CommandPipeline` + `BusinessCommandExecutor`）；不用 MediatR/自建中介者。
6. 事务：极薄 `IUnitOfWork`（基于 `DbSession`），只在 Application Service / CommandExecutor 开启；现有 `BeginTransaction` 全部迁移。
7. 锁：删除 `PostgreSqlCommandLock`/`PostgreSqlSourceDemandLock`，用行锁 + 唯一约束 + `Version`；无法覆盖的场景停下问用户。
8. 错误：唯一 `BusinessException` + `IExceptionHandler` + ProblemDetails；淘汰 `ServiceResult<T>`，客户端同步改。
9. 权限：端点声明 `[HasPermission(Permissions.X)]`（`HasPermissionAttribute : AuthorizeAttribute`，权限码常量类 `Permissions` 放 Contracts）+ ASP.NET Core 授权策略；删除 `EndpointPermissionFilter` 字符串字典；测试断言每个端点都有声明。
10. 客户端（WPF/VSTO/Web）全部自有：契约变更客户端同步，不做旧格式兼容。
11. JSON 只用 System.Text.Json；日志 Serilog + TraceId 作关联 ID（细则见 #26）；库：FluentValidation、Mapster、Dapper。
12. 删除 `ServiceBaseContext`；新代码不继承 `BaseService`/泛型 CRUD。
13. 禁止新增 Support/Helper/Manager/Pipeline/Builder 类；现有的在模块重构时消化。
14. 模块组织：一个业务对象一个文件夹（Controller/Service/Queries/Repository/Queries.Sql/Validators），大文件按用例 partial。
15. 命名统一 `PurchaseOrder`；路由资源式 `/api/purchase-orders/{id}/{动词}`。
16. 订单跟踪（OrderTracking）是唯一跨模块追踪模块，各模块从自己切面进入。
17. 允许简化 Preview Token、预览重放、独立预检：正式命令事务内重验；影响预览改普通 GET。
18. 迁移工具 DbUp（是否已接入未验证）。
19. 顺序：先 `00-Foundation` 全局基础设施 → 再逐模块重构（首个：采购）。采购任务书在 Foundation 完成后再写。
20. 过期业务文档：由文档窗口按代码实际行为重写或删除重建。
21. （已并入 #23）
22. （已并入 #9）
23. 后端 skill 关系为替换：`backend` 成为唯一后端 skill，旧 `aspdotnet10` 及旧后端工作流整组移入 `弃用资料/`（不删除）。退役前先抽取：旧 skill 中的业务规定（库存流水不可变、反向引用原事实、库存过账守恒验收、确认/取消确认与下游关系、核心实体与追溯影响、主流程顺序）迁入 `.agents/references`；技术/安全规定（高风险命令细权限、生产禁止 DevUser 等）迁入 `backend`；废弃称呼“来源需求”不得带入。迁移后所有入口（AGENTS.md、router、agents、workflows）只指向 `backend`；新规则须写明样板规则（见 #24）及旧模块修 Bug 规则（最小修复、不迁移、新写代码不得使用已禁止模式）。
24. 样板含义：`Handoff/Claude/ReferenceDesign/src/**` 是唯一结构样板，照抄其分层、文件、命名、事务/幂等/错误/权限形状；其字段、状态、业务规则不作样板，按真实 Core 实体与 `.agents/references` 落地。仓库现有生产代码（`SwiftExport.*`）只用于确认字段、表、调用方等事实，不作结构样板。
25. 错误通道迁移分三批（修订 Foundation 第 3 节第 2 步）：①引入 `BusinessException` + `IExceptionHandler` + ProblemDetails，删除 `GlobalExceptionMiddleware`/`ServiceErrorHttpMapper`/`ServiceErrorResponse`；`ServiceResultHttpFilter` **暂留为过渡适配器**，失败结果改为输出与异常通道相同的状态码 + ProblemDetails（共用同一映射），成功体暂不变；客户端一次性切换为统一 ProblemDetails 错误解析。验收：返回 `ServiceResult` 的每个端点在失败时都返回非 2xx（测试覆盖，不允许失败返回 200）。②随模块重构逐模块移除 `ServiceResult`（Service 抛异常、Controller 返回 DTO），该模块客户端成功体解析同批修改。③全仓无 `ServiceResult` 返回值时，同批删除过渡 Filter 与 `ServiceResult<T>`；架构测试断言无 Action 返回 `ServiceResult`。过渡 Filter 不得新增逻辑，新代码禁止返回 `ServiceResult`。
26. 关联 ID 统一（以 Guide 第 8 节为准）：不使用自写 `CorrelationIdMiddleware`，Foundation 中删除；关联 ID = `Activity.Current.TraceId`（无 Activity 时 `HttpContext.TraceIdentifier`），由 Serilog 记录并在 ProblemDetails `traceId` 返回。现有审计等表中的 CorrelationId 列（结构不改，见 #3）改为写入该 TraceId；若列类型不兼容则停下问用户。客户端不再发送自定义关联头。
27. 错误结果“是否生效”口径（修订 Guide 7.1 表）：只有 `UnitOfWork`/`CommandExecutor` 能判定事务阶段。提交前抛出 → 已回滚、确定未生效（含 400/404/409/422/`transient` 503）；提交本身失败 → 503 `outcome_unknown`；500 `unexpected` 改为“是否生效由事务阶段和回执判定”：幂等命令用同一 CommandId 重试（已生效则重放回执），普通写入重新加载数据后再决定。`transient` 仅限提交前发生的序列化失败/死锁/锁超时。
28. CommandExecutor 规则（修订 Guide 6.3/9.2、Foundation 第 2、3 节、ReferenceDesign `CommandExecutor.md`）：
    a. 分工：`ICommandExecutor` 只保留 `ExecuteIdempotentAsync`；普通写入（草稿保存、主数据）直接 `IUnitOfWork.ExecuteAsync`，不经 Executor。
    b. 顺序：开事务 → **在事务内**查 `command_receipt` → 执行 → 写回执 → 提交。
    c. 校验回执：依次比对 `FingerprintVersion`、`CommandType`、`ActorUid`、`RequestTargetUid`、`RequestFingerprint`；`FingerprintVersion` 或 `ResultVersion` 与当前不同 → 409 回执不可读，不重新执行；其余不同 → 422 `command_id_reused`。
    d. 指纹兼容：沿用现有实现的规范化方式、算法与 `FingerprintVersion` 当前值（样板中的 `SHA256(JSON)` 与 `=1` 仅为示意）；验收须有用现有实现生成的回执样本做“同输入同指纹”的金样测试。
    e. 同 CommandId 并发：服务端不做特殊处理，不加锁、不改表、不回查。后到请求因回执唯一索引冲突（映射为 409 `command_in_flight`）或单据 Version 冲突（409）失败并整体回滚；客户端收到 409 或结果未知时，用**同一 CommandId** 重试，先到者已提交则得到重放结果。
29. 删除 advisory 锁的验收条件（修订 Foundation 第 3 节第 4 步）：删除 `PostgreSqlCommandLock`/`PostgreSqlSourceDemandLock` 前，执行窗口必须产出清单并经用户确认：每个加锁调用点 → 锁键 → 原锁保护的约束/不变量 → 替代手段（具体 `SELECT … FOR UPDATE` 的表与行、已存在的唯一约束名、或带条件的 UPDATE/INSERT SQL）→ 对应并发测试。命令锁的“同 CommandId 并发”由 #28e 替代。替代手段需要新增唯一约束或改表时按 #3 停下问用户；清单未覆盖的调用点不得删除锁。
30. 修订 Guide 第 15 节“编译期保证”表述：`IDbSession.Transaction` 只读 + 只有 `IUnitOfWork` 提供开启/提交，使事务所有权在 API 层面唯一；但 `Connection.BeginTransaction()` 仍可被调用，因此不是编译期保证，由架构测试兜底：除 `DbSession`（`IUnitOfWork` 实现）外，全仓禁止出现 `BeginTransaction`/`Commit`/`Rollback` 调用。该架构测试列入 Foundation IUnitOfWork 步骤验收。
31. Foundation 执行口径补充（文档窗口按 Codex 实施前审核整理，用户转交确认；细化 #28e、#30，不改其方向）：a. 已提交回执的重放行为保持不变；同 CommandId 并发允许后到请求先失败，再以原 CommandId 重试取得结果。命令内须先校验 `ExpectedVersion` 再做状态检查，使后到请求得到 409（版本冲突或 `command_in_flight`）而非 422，并有测试覆盖。b. 客户端重试停止条件：收到 409 `command_in_flight`/`concurrency_conflict`、503 `outcome_unknown` 或 500 时，用原 CommandId 和原请求内容（含原 ExpectedVersion）自动重试一次；得到重放即完成，仍为 `concurrency_conflict` 即真实版本冲突，停止重试，刷新后由用户重新确认并使用新 CommandId；禁止无限重试，禁止换 ExpectedVersion 复用原 CommandId。c. Foundation 第 3 节顺序调整为：先 IUnitOfWork（并验证事务处理）→ 再合并 CommandExecutor → 再按 #29 清单移除锁（#29 所指“第 4 步”改为第 5 步）。d. #30 架构测试范围限定为生产代码中的数据库事务 API 调用（按类型判定），排除测试夹具和同名业务方法。

## 待办

- [ ] 执行 `00-Foundation.md`（执行窗口）
- [x] 核对并重写/删除 `.agents/references` 中过期工程文档：`工程架构总纲.md`、`架构治理与路由判定规则.md`、`测试与验收总纲.md`（文档窗口）
- [x] 去业务化 `Handoff/Claude/ReferenceDesign/DocumentLifecycle.md`（改为技术契约或删除）及 Guide 中的旧 Header 示例（文档窗口）
- [x] 新 backend skill 与旧 `.codex/skills/aspdotnet10` 的关系（架构窗口决定）
- [x] `.agents/skills/backend` 链接需管理员 PowerShell：`New-Item -ItemType SymbolicLink -Path .agents\skills\backend -Target .codex\skills\backend`
- [ ] 采购模块任务书（Foundation 完成后，文档窗口）
- [x] 按 #22 统一 Guide、ReferenceDesign、.agents/references 中的权限特性命名（文档窗口）
- [x] 按 #23 第 1 批：抽取 `aspdotnet10` 中的业务规定迁入 `.agents/references`，技术/安全规定迁入 `backend`（文档窗口）
- [x] 按 #23 第 2 批：AGENTS.md、`.agents/router.toml`、后端 agent/workflow 改为只指向 `backend`；补“代码不作样板”与旧模块修 Bug 规则（文档窗口）
- [x] 按 #23 第 3 批：`aspdotnet10` 与旧后端工作流移入 `弃用资料/`，全仓检索残留引用并清除（文档窗口）
- [x] 按 #23 验收：新 Codex 会话询问“修改采购确认接口会读哪些文件”，确认不再命中旧 skill（文档窗口）
- [x] 按 #25–#30 修订 `Handoff/Refactor/00-Foundation.md`（错误通道三批、删除 CorrelationId 中间件、Executor 规则、锁替代清单验收、事务架构测试）；顺带按 #9 把其中 `RequirePermission`/`PermissionKeys` 改为 `HasPermission`/`Permissions`（文档窗口）
- [x] 按 #26–#28、#30 修订 `Backend-Architecture-Guide.md`（第 6.3、7.1、8、9.2、15 节）（文档窗口）
- [x] 按 #26、#28 修订 ReferenceDesign：`CommandExecutor.md`、`Program.md`、`Toolbox.md`、`Interfaces.md`、相关 Service 样板中的 `executor.ExecuteAsync`；同步 `backend` skill 与 `.agents/references` 中的对应表述（文档窗口）
