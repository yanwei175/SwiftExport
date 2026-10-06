# M10 主控阶段验收（未闭合）

2026-10-05，当前主仓库 HEAD 40d16632 及未提交新增仓储/测试。十九方法及两测试已在 A 首轮停写后精确接回；当前 A 的 M10-LIKE-01 另轮仍独占修改，主控不覆盖其活跃文件。

## 已执行证据与失败基线

- 合入离线：`dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter '(FullyQualifiedName~PostgreSqlRbacManagementMigrationTests&TestCategory=PostgreSqlRbacOffline)|FullyQualifiedName~RbacTransactionPreparationTests|FullyQualifiedName~RbacManagementServiceTests' --logger 'trx;LogFileName=m10-merged-offline.trx' --results-directory TestResults/postgresql-migration -v minimal`：61 通过。
- 仓储实库：`dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~PostgreSqlRbacManagementMigrationIntegrationTests' --logger 'trx;LogFileName=m10-first-real-pg.trx' --results-directory TestResults/postgresql-migration -v minimal`：12 通过/1 原字符组匹配断言失败，无跳过。使用故障库运行开关及进程内派生连接；五表随机提交夹具按精确聚合 UID 清理，50 表/17,595 行摘要前后不变，见 M10-主控实库基线.csv。实际锁竞争、关系/版本、唯一失败回滚通过，但旧快照拒绝/40001 不证明完整服务错误契约。
- 新完整服务文件 PostgreSqlRbacServiceTests.Integration.cs 编译：`dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false -v minimal`，0 错误、10 既有警告。
- 完整服务实库：`dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~PostgreSqlRbacServiceIntegrationTests' --logger 'trx;LogFileName=m10-service-first-real-pg.trx' --results-directory TestResults/postgresql-migration -v minimal`：6 通过/2 失败，无跳过。通过成功授权原子提交、真实无效 JSON 审计失败回滚、审计后取消回滚、最后一位停用/清空拒绝、锁等待取消。失败：两位管理员并发自撤等待后应 BusinessBlocked、互撤等待后应 Forbidden，实际 Unexpected。不放宽断言。

P04 的 81 离线与 5 真 PG 准备租约证据另见 P04 主控结果；不是上述所有服务链已通过。原搜索、失败返回与运行接线尚未闭合，不标整模块通过。

## 独立 RBAC 测试库的实际创建与保护

主控核实 192.168.3.12:5432 不存在 dv_next_pg_rbac_tests_20261005 后创建该新空库，核实无用户对象，再部署当前生成基线 SwiftExport.PostgreSql.Schema.preview.sql。仅过滤 COMMENT ON 与 -- 注释，保留完整 BEGIN/COMMIT、空库守卫、ICU、50 表、索引/函数/触发器；169 语句全部成功。首次工具返回截断脚本文本未执行，后续完整读取经长度和终止语句检查才部署。

只在新库种一条 SYSTEM_ADMIN 内置角色（UID 53b50ca2-ff5f-4afd-b28f-183b7411d130）。每个服务夹具新增三随机用户/普通角色及关联，结束仅清理本人 UID 对应实体与关系；只追加审计保留，不关闭触发器或删除日志。首轮结束实际 users=0、roles=1、user_roles=0、audit_rows=4、advisory_locks=0。未改现有故障库/dv_next 结构或任何备份；新增库使用已有生成结构，不是生产结构改版。

测试入口只接受新库全名及上述实例/public，独立 SWIFTEXPORT_PG_RBAC_TEST_CONNECTION、SWIFTEXPORT_RUN_PG_RBAC_TESTS=1；关闭其他开关。主控串行独占，A/B 不执行数据库测试。

## 修正分派

A 当前独占仓储先增加首次 SELECT/DML 之前的 sys_role SHARE ROW EXCLUSIVE 锁，保留 Serializable、角色→操作者→目标顺序；已建立快照的调用不宣称刷新。角色目录互斥沿用原 RBAC 串行边界，不增全局业务锁或自动重试。依据：[PG 18 LOCK](https://www.postgresql.org/docs/18/sql-lock.html)。主控待交付后原断言真实复验，再判 B08。

搜索局部翻译范围、复杂连续连字符反例及未跑计划验证须 A 独立交付；不能凭近似 ICU 范围宣称 SQL Server 完整一致。后续角色删除/分配、合法目录权限同步等服务交错仍待必要验证。未切 DI、提交、推送或部署。

## 01:54 当前复验

A 本人及聊天核实 M10-LIKE-01 已完成停写。审查仓储、两测试差异及结果，记录主目录四文件固定 SHA256 后守卫同步；自动审批曾拒绝无条件覆盖，改为任一哈希改变立即停止的方式后获准，保留其他未提交成果。

- 同上离线筛选，日志改 `m10-like-lock-merged-offline.trx`：89 通过/0 失败/跳过。增加参数化递归搜索、解析边界和首锁失败/取消场景。
- 同上完整服务实库筛选，日志改 `m10-service-lock-corrected-real-pg.trx`：8 通过/0 失败/跳过。原两项失败不放宽断言已通过，B08 的本次返回回归关闭；不是全部权限链已验收。
- 同上仓储实库筛选，日志改 `m10-like-lock-real-pg.trx`：59 项中 14 通过/45 失败/0 跳过。字符组谓词统一 42601，COLLATE 附近语法错误，尚不能判断所有新增字符组语义真值；特殊范围断言仍保留。普通计划和原锁/写入场景通过，不把计划仅编译记通过。

已下发 A 新轮 M10-LIKE-FIX-01：修实际谓词语法，允许 DBX 只读常量/EXPLAIN核验，不放行写库测试，不回退已通过的首快照锁。主控待停写接回后定向复验，不反复全量。B 同期 M26-S 独占新增销售读取文件；模型容量错误一轮已结束，经原模型同任务接续恢复 active，不创建第三窗口。

## 02:02 语法修正复验

A 新轮本人/聊天停写已核实，审查差异仅 BETWEEN 边界整体括号、中文原因说明及对应测试；固定哈希守卫合入三文件，集成断言文件完全未改。四条实际用户/角色 COUNT/分页只读 EXPLAIN 证据保存在 A 原结果。

- 实库命令沿前述仓储筛选，去掉 --no-build，添加 -p:UseSharedCompilation=false，日志 `m10-like-syntax-corrected-real-pg.trx`：编译0错误，59项57通过/2失败/0跳过。42601消失；原[Aa]、多组/反组/普通范围、空/非法组、CI重音及两实际计划均通过。剩余失败仅 z/[a--c] 与 z/[a---] 源期望true、PGfalse，归B07，不改断言。
- 离线定向 `dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~PostgreSqlRbacManagementMigrationTests&TestCategory=PostgreSqlRbacOffline' --logger 'trx;LogFileName=m10-like-syntax-corrected-offline.trx' --results-directory TestResults/postgresql-migration -v minimal`：58通过/0失败/跳过。
- 完整服务先前8/8仍有效，修正仅搜索表达式没有改锁或服务，不重复跑相同服务批。独立库实际 users0/roles1/user_roles0/logs8/advisory0，保留两轮成功审计。

A已分派M10-RANGE-REVIEW-01，仅只读分析及独占结果/状态，限量源常量/官方资料评估原特殊范围机制，不硬编码两个样例或扩大框架。主控继续其余迁移，M10整体不标通过。
