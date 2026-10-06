# P04 / M10 实施分派

2026-10-05 01:04 +08:00：两个现有执行聊天均经 wait_threads 核实 idle/completed 后重新分派。主控继续后台目标，未启动额外窗口。

本轮保留 SQL Server 的业务校验、Serializable、内置角色先锁、目标聚合版本、最后管理员及同事务审计。PG 对应实现必须实测，不以 SQL 文本断言代替实库。禁止降低隔离、自动重试或新增全局业务锁；40001/锁等待差异须报告主控，不自行改变错误码。

| 轮次 / 负责人 | 独占文件 | 交付及验证 |
|---|---|---|
| M10-IMPL-01 / 执行 A | Infrastructure/Repositories/Auth/Permissions/PostgreSqlRbacManagementRepository.cs；Application.Tests/Database/PostgreSqlRbacManagementMigrationTests.cs 及 .Integration.cs；自己的 M10 结果与状态 | 19 方法翻译、中文职责注释、必要编译及离线验证。新增实库测试由主控串行运行。不得改原仓储、接口、服务、DI、公共参数/映射/锁或 DDL。 |
| P04-TEST-01 / 执行 B | Application.Tests/Authentication/RbacTransactionPreparationTests.cs；自己的 P04 结果与状态 | 为下面冻结的可选准备协议验证顺序、命令身份、成功/业务拒绝/异常/取消/超时/BeginTransaction 失败及租约释放。使用现有接口的独立夹具，不改已有测试或业务代码。主控同步服务实现后定向运行。 |
| P04-IMPL-01 / 主控 | Application/Services/Auth/Permissions/RbacManagementService.cs；Infrastructure/Repositories/Auditing/PostgreSqlBusinessOperationLogRepository.cs；必要的独立 PG 审计测试；本目录主文档 | 接入已有 ICommandTransactionPreparation，不增加接口。审查并精确同步交付，再串行真实 PG 验收和阶段构建。 |

表内源代码路径分别以 SwiftExport. 项目名前缀补齐。各窗口仅自己的 worktree 写入；公共文件仍由主控一人负责。

冻结协议：WriteAsync 每次生成一个非空 CommandId；auditLogs 若实现 ICommandTransactionPreparation，则在无本地事务时调用 AcquireBeforeTransactionAsync，取消令牌原样传递；取得租约后开始 Serializable，再锁验管理员、执行原校验/写入及审计。所有 AuditAsync 使用本次同一 CommandId，不用服务可变字段保存。未实现准备接口的 SQL Server 日志保持原路径。空租约视为准备超时，返回既有 Unexpected，不创建事务或写入；请求取消原样传播。事务提交/回滚、释放并清空本层 Session.Transaction 后才释放租约。准备或 BeginTransaction 失败也释放本次租约；清理异常不得掩盖已确定结果。日志仓储实现准备接口并复用 PostgreSqlCommandLock，不改锁核心。

锁协议由 A 在现有接口内翻译：内置角色 FOR UPDATE → 有效操作者 FOR UPDATE → 目标聚合锁；读取计数与关系仍在同一 Serializable 事务。FOR UPDATE 与 PG 固定快照不能等同 SQL Server 锁式 Serializable：针对并发撤权、互撤管理员、最后管理员、角色删除/分配、权限停用/分配安排跨连接实测，记录实际拒绝和 40001，主控审核返回行为。不用模拟证明并发安全。

并行分类：M10 整体仍为“不可并行，主控串行分配”。本轮仅在现有公共接口及上述生命周期协议冻结后，拆出互不写同文件的 A 仓储和 B 服务测试两项可并行工作；运行接线、DDL、公共实现及数据库写入仍串行。B 依赖 P04-IMPL-01 才能验证新增成功条件；先写测试，可记录预期失败，不谎报通过。

测试库：仅主控占用 dv_next_pg_failure_tests_20261004；本轮不清库、不改已有业务数据，使用随机夹具及可追溯清理。若必要变更共享结构，先主控核验、测试库验证，再同步 dv_next；所有备份只读。A/B 不运行真实数据库。

前一阶段基线：54 项合入离线、30 项真实 PG 通过；未全量重跑历史 GetTraceAsync 契约失败。每项记录实际命令及未验证内容，新增失败先分类，不扩大修复范围。成果保留，未授权提交或推送。
