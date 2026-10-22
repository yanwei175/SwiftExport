# 后端 API 工作流

## 适用

新增或修改 WebApi、Application Service、Contracts DTO/API、Core Repository Interface、Infrastructure Dapper Repository。

## 步骤

小范围且授权、目标和边界明确时直接实施，复用有效的架构/契约结论；下列设计项仅针对本次新增或改变的边界，不为局部修正重新创建整套方案。读取和偏差处理统一遵循 Router policy。

1. 公共启动和授权边界沿用 `AGENTS.md`，不重复展开已读入口。
2. 从目标实现、直接调用方和相关测试确认必要的 Controller、Application、Contracts、Entity、Repository；先定位事实，再选择专项规则。
3. 按 [后端技能入口](../../.codex/skills/backend/index/SKILL.md) 选择本次改动所需子技能与专项规定，修改前补齐技术约束；涉及 PostgreSQL 技术任务时按 [数据访问规定](../../.codex/skills/backend/index/references/数据访问规定.md#postgresql-插件技能) 接入 `postgres-skills`。现有代码只说明实现事实，不作样板；形状以 Guide 与 ReferenceDesign 为准。未治理旧模块修 Bug 只做最小修复，不顺手迁移（决策 #23）。
4. 改变分层/公共依赖或当前边界不明时读取工程总纲相关章节；数据库、仓储、SQL、连接、结构及工具任务补齐当前 PostgreSQL 基线。单据状态、库存数量或需求交接实际变化时补读对应业务/模型章节，不展开无关总纲。
5. 改变模块职责、API/DTO语义或跨域交接时按模块索引读取命中地图；只有新增或重构WPF页面纵向切片时读取切片教程。改变采购—入库—库存交接时核对三方契约；没有有效偏差ID时记录来源位置，不恢复旧表。
6. 偏差按 `.agents/references/架构治理与路由判定规则.md` 分类；过期事实和无关历史偏差不阻断当前实现，需要改变有效目标、扩大授权或豁免门禁时才等待决定。
7. 先设计或确认 Contracts DTO/API/枚举；新增或复用 DTO 时读取 [契约与DTO规定](../../.codex/skills/backend/index/references/契约与DTO规定.md)，搜索现有契约及其调用方，记录复用或新建理由。
8. DTO 默认按 API 用例独立设计；只有语义、字段口径、权限暴露、生命周期一致且已有真实多消费者时复用，不建立跨用例万能 DTO。“窄”指契约职责单一，不要求最少字段；查询 DTO 必须完整支撑目标用例。拆分现有 DTO 前建立字段处置矩阵，调用方迁移并验证前不得删除仍需字段。
9. 按 backend 子技能的固定角色文件实现（`XxxService` 写、`XxxQueries` 读），一个用例在一个 Service 方法内从上到下读完；不按页面建立宽 Manager Service，不新增 Support/Helper/Pipeline 等中间层。
10. 正式命令按 [backend-document](../../.codex/skills/backend/backend-document/SKILL.md)：`CommandExecutor` + `CommandId`（请求体）、目标行 `FOR UPDATE` + `Version`、固定锁顺序、审计同事务；不做预览 Token 或命令锁服务，正式命令在事务内重新校验（决策 #5、#7、#17）。
11. 优先复用现有 Repository 接口；必要时按 ReferenceDesign 新增专用仓储。命令唯一性必须有数据库约束或等价持久化防线，不能只靠先查后写。
12. Controller 只做 HTTP 转发，权限用 `[HasPermission(Permissions.X)]`（决策 #9、#22），Service 不查权限。防抖只作体验补充，不替代正式幂等。
13. 按测试总纲和 [backend-delivery](../../.codex/skills/backend/backend-delivery/SKILL.md) 完成受影响项目编译与定向测试；共享依赖/跨模块或阶段验收再扩大消费者、解决方案构建及回归。复用同批有效证据，失败先重跑失败用例。未运行项说明原因和风险；实质业务契约/架构边界变化时同步对应权威文档。

## 边界

- WebApi 不写 SQL。
- Application 不直接创建连接。
- Infrastructure 不引用 Application。
- Core 不放前端可见 DTO/枚举。
