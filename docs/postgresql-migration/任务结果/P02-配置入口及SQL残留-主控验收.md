# P02 配置构造入口及 P09 残留核验

2026-10-05 16:10 +08:00。主控Backend，复用后端工作流/DbSession及门禁技能。授权仅基础设施配置入口、直接SQL对照夹具和结果文档；不修改业务行为。

## 配置入口修复

宿主DI已经通过工厂构造使用PG，但DbSession(IConfiguration)仍强制SqlConnectionFactory。真实隔离库复现 p02-configuration-pg-red.trx 1失败，SqlClient报不支持Host，未建立数据库连接。不能因默认DI已切换就说所有公开构造入口已经PG。

生产只修改DbSession.cs配置构造中的工厂及职责注释，统一为PostgreSqlConnectionFactory；共享会话开闭/事务/异常/锁实现未改。本轮diff内SourceDemandLease及Dispose为前批成果，不能误计为本轮修改。无新增提供者开关或永久双库架构。

受检源码只有原SQL测试的四个配置调用点需要显式工厂：
- Systems/ExcelReadProfileLocalDbTests.cs 两处。
- Shipping/PackingListDraftLocalDbTests.cs 一处。
- MasterData/ExecutionEligibilityPermissionSqlTests.cs 一处。

这些调用保持原SQL连接、仓储/回执、生成器、门禁及所有有效断言，仅显式SqlConnectionFactory；没有因为默认构造迁移而把原LocalDB测试接PG。本轮旧SQL测试只编译未运行，不把工厂选择和编译称为源实库通过。Q10原LocalDB+默认全量DI混用问题不属于这四处，继续保留。

## 实际验证命令与结果

本轮源码SHA256（未提交，不伪称Git版本）：DbSession.cs=`EEF80B516A59C032627DA9473475AB7406DBDE780EABBD791E786E27040DB9AE`；三个SQL夹具分别`23288BBEEA639BF5A5A21FE81B2E8B7F37327018104433E7A2613EC58B906F8D`、`3E2BE3444F02E86E1FA9FD2E6B79244366AFC2E31344BBA47B9211AC7A06D4C1`、`97A49D96BFCC4BC3192BDE5EC7A1755A35536C8AD1BE3783D4B8301CADDB19F2`；PostgreSqlRuntimeWiringTests.cs=`11B45BC4A6E76704A5D691152EA8856A787A92E704333723299F2267A7CFD842`。

```powershell
dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_ConfigurationSessionUsesPostgreSqlFactory' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p02-configuration-pg-fixed.trx' -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~DatabaseBoundaryTests|FullyQualifiedName~PostgreSqlCommandLockLifecycleTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p02-configuration-pg-affected.trx' -v minimal
```
- 修改前真实构造1失败（Host关键字），TRX保留。
- 修复后1真实PG通过/0失败/0跳过：NpgsqlConnection、PG分类器同实例、current_database为dv_next_pg_failure_tests_20261004、Session.Transaction为空。
- 34受影响普通通过/0失败/0跳过（会话边界/分类/租约），不代表真实网络断线/旧SQL测试。
- 编译0错误/10既有NU1900/MSTEST警告，未全量或运行旧测试初始化/清库。
- 原生门禁SWIFTEXPORT_PG_FAILURE_RUN=1及SWIFTEXPORT_PG_FAILURE_CONNECTION从用户配置在进程内安全读取、仅覆盖隔离库名，结束恢复原环境；凭据不输出。仅只读查询，无本轮数据或结构写入。

## 当前SQL残留与退出依赖

本轮对Application/WebApi/_tools实际.cs/.csproj及DbSession/DI搜索、旧测试具体读取；不能把静态搜索冒称外部部署消费者全部审计。

|位置|当前事实|退出条件|
|---|---|---|
|Infrastructure/DependencyInjection.cs|工厂、分类器、泛型与所有有效专用仓储均PG；别名共享会话已有原生宿主证据|不再建设SQL运行切换分支；全写链及HTTP终验仍P08|
|Infrastructure/Data/DbSession.cs|配置入口本轮PG；factory入口按明确传入工厂；无构造替身Errors仍回落SQL分类器|P09清理须同时明确保留源测试的分类依赖；本轮不机械删除|
|Infrastructure旧仓储/DbConnectionFactory/SqlServerExceptionClassifier|源行为与现有SQL对照测试仍有编译依赖；SqlClient包还在Infrastructure项目|P08后按有效消费者逐项退出，保留有效断言及历史证据|
|_tools/DbSeed/Program.cs|PG已接入，原PermissionCatalogSync.cs及SqlClient包仍保留|目录同步旧测试依赖核对后移除，不再注册旧SQL运行入口|
|_tools/AdminPasswordRecovery/Program.cs|PG已接入，SqlAdminPasswordRecoveryStore.cs及SqlClient包仍保留|原恢复语义测试仍需正确归属，P08后退出|
|_tools/SchemaGenerator/Program.cs|不连接数据库，但默认provider=sqlserver，PG须明确--provider postgresql|P06/P09后续收敛默认PG及旧生成/验证分支；本轮不改生成文件或基线|
|Infrastructure/Database/README.md|当前运行SQL的说明已失效|本轮仅更新入口事实与PG生成参数，历史批次保留，不把本地生成冒称部署|

P09仍受主清单P08最终验收前置，不贸然删除包/旧实现。完整迁移不能因为本轮公开构造修复而标为完成。

## 特殊比较重新核验及待用户取舍

SQL源连接82a24250-984a-4318-bd84-4dd79b4773d7/dv_next只读，PG连接4bba2982-bb06-4389-9005-decf007588b4/故障隔离库只读：

|样本|SQL Chinese_PRC_CI_AS|当前PG实现/collation|
|---|---|---|
|z 与 [a--c] 字符组|匹配1|当前解析a到-的ICU BETWEEN为false|
|ss/ß 等值|1|false|
|S普通空格O/S NBSP O|0|true|

PG探针首次COLLATE表达式未加括号语法失败；修正只读查询返回上述结果，不记首次为通过。源返回1/1/0、PGfalse/false/true，和旧证据一致。样本比较不能代表全部Unicode兼容验收。

B07/B10/B19保持开放。已向用户提出具体可审查取舍：继续复刻特殊比较，或明确将这些差异列为本批遗留以先收口PG运行；尚未收到答复，不能擅自缩小目标或采用PG语义。其他原业务规则与API保持源SQL。本轮没有改比较器/索引/搜索算法或约束。

执行A/B16:07再次wait_threads核实idle/latestTurn completed、cursor未变；无新派工或代理，不代写本人页。用户appsettings.json及既有未提交成果保留，未提交Git/推送/部署。整体目标active。

