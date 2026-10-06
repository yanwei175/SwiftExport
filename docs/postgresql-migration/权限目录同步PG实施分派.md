# P06-PC-SYNC-IMPL01 权限目录同步PG实现

执行B，现3557 worktree；Backend/后端API工作流、Dapper/会话/权限/质量技能。旧首锁测试FBF86CBC、结果4013BAAC已STOPPED/completed固定接回归主控；当前MAIN已最小加角色首锁，14离线及修正后2首锁+5竞争原生通过。旧报告不续改。

## 唯一写入

- 新 `_tools/DbSeed/PostgreSqlPermissionCatalogSync.cs`。
- 新 `SwiftExport.Application.Tests/Database/PostgreSqlPermissionCatalogSyncMigrationTests.cs`。
- 新 `SwiftExport.Application.Tests/Database/PostgreSqlPermissionCatalogSyncMigrationTests.Integration.cs`。
- 新 `docs/postgresql-migration/任务结果/P06-权限目录同步PG-实施结果.md`。
- 本人状态。

原Program、PermissionCatalogSync、项目文件、公共/生产恢复/旧测试/报告全部只读。主控在B停写期只同步两个必要项目引用：DbSeed→Infrastructure；Application.Tests→DbSeed，SHA见协调文档。B可按缓存D:/DevCaches做必要restore/编译，只用本人NUGET_SCRATCH，不能删除共享锁、禁审计/屏蔽警告或自行改项目。其他缺依赖先报告。

## 精确迁移合同

原 `_tools/DbSeed/PermissionCatalogSync.cs` 为唯一业务依据，2940/933A审查复用。新类namespace `SwiftExport.Tools.DbSeed`，具体接DbSession，提供`RunAsync(bool dryRun, CancellationToken cancellationToken=default)`。不新建Core接口/DI/通用工具框架，不实现默认引导。本轮Program仍SQL；主控在类原生验收后统一切工具两入口并退出SqlClient，不建设provider开关。

工具拥有独立Serializable事务；拒绝已有Session.Transaction，借当前连接，不再新建第二连接。第一批命令按固定元数据role SRE→permission SRE，均在Schema/目录SELECT/DML之前；dry-run也相同取锁/事务。后续每条Dapper命令明确transaction/token。finally Dispose事务后清Session.Transaction；失败Rollback(None)/保留原异常，不自动重试或把失败当dry-run成功，清理异常不遮盖原失败。不得在取锁前加SELECT日志/数据库名探测/设置会话语句。无新advisory/业务全局锁。

保留原代码目录OrdinalIgnoreCase重复拒绝、数据库全量定义字典OrdinalIgnoreCase重复拒绝；已有命中键再Ordinal精确拼写拒绝，状态/软删行同样读入。目录外按原排序报告并保留。原Console计划/新增/更新/目录外/完成输出保持，不打印配置、hash或账号。

只新增缺失定义（Uid随机、Version1、IsDeleted=false、IsActive=true、SortOrder原index+1、Created/Updated原UTC墙上时间及DbSeed）。已有定义仅DisplayName/GroupKey/SortOrder三列；不改UID/Key/Version/活动删除/审计、RP/角色/用户/UR，不补管理员关系。不能因相似度/CI命中自动改拼写或合并键。

复用公开PG实体metadata/Names，固定定义/RP/role物理名称，不直接蛇形旧复数表名。PostgreSqlParameters若对工具不可见，采用本工具固定语句typed DynamicParameters/Npgsql参数；不能改公共可见性或复制通用框架。文本NULL明确text/版本integer/Uid uuid，UTC时间要保持源SYSUTCDATETIME的timestamp墙上UTC（如`clock_timestamp() AT TIME ZONE 'UTC'`），不改会话时区/审计业务。

Schema仍只检查、不DDL；原sync验证权限定义/RP表和关键列存在，新增role首锁作为技术必要前置。缺表第一LOCK42P01可带原异常转换成原“目标库缺少当前权限定义或角色权限关系结构；目录同步不迁移结构。”诊断，其余40001/40P01/23505/取消原样，不扩大转换。实际RoleCode/PermissionKey索引是原始列唯一，B10仍开放，不能在本类修索引/放宽政策。空范围依表锁及最终约束，不把FOR UPDATE空行当保护。

不为旧CLI默认无token调用擅自加新全请求30秒预算；保留当前驱动命令超时，token贯穿整个调用，测试用调用方截止令牌有界。完整预算/未知提交矩阵另主控冻结，不能宣称已验。提交失败不重试/自动删成功事实。

## 验证及实库测试

必要工具/受影响测试项目编译，精确新离线测试，全部数据库开关清除；不全量、DB/DDL、运行CLI/种子恢复、Git/同步/新窗口代理。离线验证真实类的原比较/计划/三列更新边界、类型绑定、事务/首锁顺序/清理及失败，传输替身不算PG。

原生只编写/编译、严格既有Failure门禁和DoNotParallelize。全代码目录为空、全量引导或重复键需要独立工具库，**本轮不造/清现有目录、不运行这类案例**，报告必要前置，不修改原始库约束。

可在故障库合法既有目录基础下写：全量dry-run的50表零写；有原父事务全读视图不允许借用（拒绝已有事务）；role owner SRE→工具等待，等待后读到owner对本例目录键已提交的展示变更/同原三列更新，不改Key；取消首role或后permission等待、物理释放/第三backend；外部状态行完整保留和精确拼写门禁。任何既有目录改动需完整快照/精确主控授权后才实跑，B禁止DB，报告需初始化/恢复范围，不自授权。工具自有事务不能靠外部父rollback假称已清理；完整目录新增成功留存UID预登记方案交主控，不自动DELETE。

不为测试注入目录覆盖功能或公开内部规划API；可以窄纯只读反射检查/真实Dapper传输测试，保留生产PermissionCatalog.All来源。不制造非法历史NULL，不改约束、删断言或用硬编码样本代替比较。

开始本人RUNNING；结束STOPPED、四新文件固定SHA、关键依赖起止SHA、实际restore/构建/测试命令及失败/最终结果、未运行矩阵/结构与留存前置。公共修改先报告主控。
