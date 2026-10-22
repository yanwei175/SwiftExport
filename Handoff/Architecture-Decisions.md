# 架构决策日志（新窗口必读，只追加不改写）

> 用途：替代长对话上下文。新窗口先读本文件 + `Handoff/Claude/Backend-Architecture-Guide.md`。
> 格式：一条一行；被推翻时追加新条并标注“取代 #n”。

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
9. 权限：端点声明 `[RequirePermission(PermissionKeys.X)]` + ASP.NET Core 授权策略；删除 `EndpointPermissionFilter` 字符串字典；测试断言每个端点都有声明。
10. 客户端（WPF/VSTO/Web）全部自有：契约变更客户端同步，不做旧格式兼容。
11. JSON 只用 System.Text.Json；日志 Serilog + CorrelationId；库：FluentValidation、Mapster、Dapper。
12. 删除 `ServiceBaseContext`；新代码不继承 `BaseService`/泛型 CRUD。
13. 禁止新增 Support/Helper/Manager/Pipeline/Builder 类；现有的在模块重构时消化。
14. 模块组织：一个业务对象一个文件夹（Controller/Service/Queries/Repository/Queries.Sql/Validators），大文件按用例 partial。
15. 命名统一 `PurchaseOrder`；路由资源式 `/api/purchase-orders/{id}/{动词}`。
16. 订单跟踪（OrderTracking）是唯一跨模块追踪模块，各模块从自己切面进入。
17. 允许简化 Preview Token、预览重放、独立预检：正式命令事务内重验；影响预览改普通 GET。
18. 迁移工具 DbUp（是否已接入未验证）。
19. 顺序：先 `00-Foundation` 全局基础设施 → 再逐模块重构（首个：采购）。采购任务书在 Foundation 完成后再写。
20. 过期业务文档：由文档窗口按代码实际行为重写或删除重建。
21. 后端 skill 关系：`backend` 与旧 `aspdotnet10` 并存，维持 backend 入口现有过渡期说法（新增功能/正在治理模块用 backend；未治理旧模块只修 Bug 用 aspdotnet10、不顺手迁移；冲突时在已治理范围内以 backend 为准），不设退役时间。
22. 权限特性统一命名为 `[HasPermission(Permissions.X)]`（`HasPermissionAttribute : AuthorizeAttribute`，权限码常量类 `Permissions` 放 Contracts），以 Guide 与 ReferenceDesign 为准；取代 #9 中 `RequirePermission`/`PermissionKeys` 的命名表述，#9 其余内容不变。
23. 后端 skill 关系改为替换（取代 #21）：`backend` 成为唯一后端 skill，旧 `aspdotnet10` 及旧后端工作流整组移入 `弃用资料/`（不删除）。退役前先抽取：旧 skill 中的业务规定（库存流水不可变、反向引用原事实、库存过账守恒验收、确认/取消确认与下游关系、核心实体与追溯影响、主流程顺序）迁入 `.agents/references`；技术/安全规定（高风险命令细权限、生产禁止 DevUser 等）迁入 `backend`；废弃称呼“来源需求”不得带入。迁移后所有入口（AGENTS.md、router、agents、workflows）只指向 `backend`；新规则须写明“现有代码只确认事实，不作样板”及旧模块修 Bug 规则（最小修复、不迁移、新写代码不得使用已禁止模式）。
## 待办

- [ ] 执行 `00-Foundation.md`（执行窗口）
- [x] 核对并重写/删除 `.agents/references` 中过期工程文档：`工程架构总纲.md`、`架构治理与路由判定规则.md`、`测试与验收总纲.md`（文档窗口）
- [x] 去业务化 `Handoff/Claude/ReferenceDesign/DocumentLifecycle.md`（改为技术契约或删除）及 Guide 中的旧 Header 示例（文档窗口）
- [ ] 新 backend skill 与旧 `.codex/skills/aspdotnet10` 的关系（架构窗口决定）
- [ ] `.agents/skills/backend` 链接需管理员 PowerShell：`New-Item -ItemType SymbolicLink -Path .agents\skills\backend -Target .codex\skills\backend`
- [ ] 采购模块任务书（Foundation 完成后，文档窗口）
- [x] 按 #22 统一 Guide、ReferenceDesign、.agents/references 中的权限特性命名（文档窗口）
- [ ] 按 #23 第 1 批：抽取 `aspdotnet10` 中的业务规定迁入 `.agents/references`，技术/安全规定迁入 `backend`（文档窗口）
- [ ] 按 #23 第 2 批：AGENTS.md、`.agents/router.toml`、后端 agent/workflow 改为只指向 `backend`；补“代码不作样板”与旧模块修 Bug 规则（文档窗口）
- [ ] 按 #23 第 3 批：`aspdotnet10` 与旧后端工作流移入 `弃用资料/`，全仓检索残留引用并清除（文档窗口）
- [ ] 按 #23 验收：新 Codex 会话询问“修改采购确认接口会读哪些文件”，确认不再命中旧 skill（文档窗口）
