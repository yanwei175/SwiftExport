# P04 RBAC 审计准备：主控阶段结果

2026-10-05；主控 P04-IMPL-01。此结果仅证明审计准备切片，P04 其他调用链及 M10 完整并发仍未验收，运行 DI 仍为 SQL Server。

## 必要改动与边界

RbacManagementService 原有 Serializable 事务中才生成审计 CommandId，PG 日志写入明确拒绝无事务前租约的高隔离。现于事务前生成每次调用独有 Guid，日志仓储若实现已有 ICommandTransactionPreparation 就先取得租约，原管理写入及审计均沿用该 Guid。原 SQL Server 日志未实现准备能力，保留原路径；不加提供者开关，不改 Controller、DTO、公开接口、业务校验、权限、最后管理员或隔离级别。

PG 日志仓储增加上述可选能力，直接复用 PostgreSqlCommandLock。无事务、错误身份及无准备高隔离的旧拒绝继续保留。事务结束、Dispose 与 Session 清空先于租约释放，准备或事务开始失败也清理本次租约，取消原样传播。释放事务失败先尝试关闭连接，清理及日志次生异常不掩盖已确定结果。

B 的独立测试发现原拒绝路径直接 Rollback 时异常会覆盖已确定 Forbidden。主控将管理员拒绝和 action 失败两处改用既有 RollbackSafely，保留原拒绝条件和错误码。首次失败输出由 B 结果保留，不删除断言；这属于本次生命周期衔接的必要清理处理，不扩展其他服务。

## 文件

- SwiftExport.Application/Services/Auth/Permissions/RbacManagementService.cs（主控）
- SwiftExport.Infrastructure/Repositories/Auditing/PostgreSqlBusinessOperationLogRepository.cs（主控）
- SwiftExport.Application.Tests/Database/PostgreSqlAuditPreparationTests.Integration.cs（主控）
- SwiftExport.Application.Tests/Authentication/RbacTransactionPreparationTests.cs（B 已停写，主控核验合入）
- 本轮分派、主清单、问题及状态协调文档。

## 已执行验证

1. `dotnet test ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~RbacManagementServiceTests|FullyQualifiedName~RbacApiContractTests|FullyQualifiedName~PostgreSqlCommandLockLifecycleTests' --logger 'trx;LogFileName=p04-existing-baseline.trx' --results-directory ./TestResults/postgresql-migration -v minimal`：21 通过，0 失败，0 跳过。这是改后现有测试复验，不是改前基线。
2. 新增审计实库文件后 `dotnet build ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal`：0 错误，10 既有 NU1900/MSTest 警告；未修无关警告。
3. 进程连接从获授权环境变量读取，仅 Database 改为 dv_next_pg_failure_tests_20261004，仅启用 failure 测试入口；`dotnet test ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~PostgreSqlAuditPreparationIntegrationTests' --logger 'trx;LogFileName=p04-audit-preparation-real-pg.trx' --results-directory ./TestResults/postgresql-migration -v minimal`：5 通过，0 失败，0 跳过。RR/Serializable 租约写入读回后回滚、同 scope 再次准备、实际 pg_locks 释放、错误命令身份及无租约高隔离拒绝。
4. 清除三个 PG 启用标志，`dotnet test ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RbacManagementServiceTests|FullyQualifiedName~RbacApiContractTests|FullyQualifiedName~PostgreSqlCommandLockLifecycleTests|FullyQualifiedName~PostgreSqlRepositoryTests.AuditLog_|FullyQualifiedName~BusinessCommandExecutorTests' --logger 'trx;LogFileName=p04-shared-offline.trx' --results-directory ./TestResults/postgresql-migration -v minimal`：58 通过，0 失败，0 跳过。包含前述 21，不累加重复数量；两条拒绝安全回滚修正后的主目录合入复验待下一步执行。
5. B 已 idle/completed，主控读取最终状态/源码/diff 后精确合入两新增文件。清除 PG 标志，执行上述相关过滤再加入 `FullyQualifiedName~RbacTransactionPreparationTests`，使用 `--no-restore -p:UseSharedCompilation=false`（本次重新编译），TRX `p04-merged-offline.trx`：81 通过，0 失败，0 跳过，含 B 的 23 项和相关 58 项，两条安全回滚修正均已主目录复验。服务双方 SHA256 E736D678E5AD4F664E4DD30FAFD7E4FCA14EDAA46F531B6B97344A4C8941A3A9。

实库占用仅主控、已解除。前后独立 DBX 查询均为 other_active=0、advisory_locks=0、audit_rows=0。测试仅随机审计插入及回滚，无 DDL/清库/既有行修改，dv_next 与备份没有写入。没有全量测试、提交、推送或部署。

阶段构建：`dotnet build ./SwiftExport-WebAPI.slnx --no-restore -v minimal`，0 错误/3 既有 NU1900 警告。git diff --check 和两个新增源码的 UTF-8/行尾检查通过。未提交成果保留。

## 未验证及下一步

等待 A 的 19 方法仓储和真实场景，审查后串行服务/仓储/审计联合 PG 验收。M10 的角色锁等待后的固定快照、互撤管理员及 SSI/40001，以及搜索字符组 B07，不能用本切片通过代替。其他 P04 命令执行器、未知提交错误描述 Q05 保持既有范围，未宣称全部关闭。
