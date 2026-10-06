# P06 开发身份PG窄查询主控验收

2026-10-05；B本人STOPPED（10:47:50）、聊天completed后四文件固定SHA接回。主控完整核对原DevUserMiddleware与新查询、两测试及B报告。查询08B425511B0C58D190821096C95EFA2D3F3A2B337FFCD936B3C0643045FC22A1，离线6CE8287D20D946F35AA57BAF119E30CAD0BC1024CC3723784DFB8C73207A8080，原生9EF1F0084A1D9473490A25707C1AF32D739FDF075C70683B65743807F04F5A74，报告DA68076A2B40D441DEBE814DFB33DC361DF19F6F5A828B7CE0CBAD867D711FB6。

## 语义与边界

新增Infrastructure具体查询FindUidAsync，仅Uid、精确原用户名参数、活动/未删资格、无序LIMIT1、NULL缺失与异常传播；元数据取固定物理名，明确text参数，不建Core接口/通用框架。原不传Transaction/token/无自身事务形式保留；Npgsql会按提供者规则关联连接上已有父事务。原NOLOCK脏读在PG无对应，此窄读取为PG快照；不改其他服务隔离或加业务锁。用户名沿既有Auth的ASCII尾空格/ICU谓词，全域B10仍开放。

不改DevUserMiddleware、四条件、缓存、claims、生产阻断或DI。只新增未来单PG接线使用的具体读取，不能宣称已接入运行；P07统一替换，当前SQL工厂不混用PG语句。

## 本轮实际验证

B报告11离线通过/14原生仅编译；主控接回后实际11离线与14真实PG均通过，0失败/0跳过，TRX Counters核实。必要编译0错误，4既有NU1900及7既有分析警告；没有改新测试/原有效断言或业务源码来通过测试。原新查询固定08B42551及Middleware原B6A08162保持。

实际命令：

```powershell
# 清所有相关PG/SQL运行开关，必要编译及新离线
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlDevUserQueryMigrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p06-dev-user-first-offline.trx' -v minimal
# 仅启用严格failure连接，沿授权环境静默派生，不输出凭据
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~PostgreSqlDevUserQueryMigrationIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p06-dev-user-first-real.trx' -v minimal
```

14真实用例覆盖活动/停用/删除/缺失、ASCII大小写和两端各自尾空格、前导空格原值与错配、尾tab、NULL、SQL特殊字符、空串/纯空格，并确认Npgsql已有父事务关联及独立连接零残留。实际全50表/17601行前后MD5完全相同，[摘要](P06-开发身份PG查询-前后摘要.csv)、[初始化前14例UID](P06-开发身份PG查询-UID登记.json)。没有全量测试；完整Unicode等价、真实历史NULL（物理列非空）、脏读对应语义、缓存/claims/HTTP宿主及生产入口仍未验。

## 数据库登记

主控独占192.168.3.12:5432/dv_next_pg_failure_tests_20261004，运行前完整50表摘要已保存。14原生用例每例仅新随机合法sys_user一行，父ReadCommitted种入、finally回滚、独立observer按本例UID确认零残留；空白前置只计数、不使用既有身份。新随机测试BCrypt哈希不输出，既有管理员/密码/角色/目录/其他49表不写。无提交、DELETE、DDL、取号、回执、备份或dv_next写入。窗口A/B不连接数据库，其他写入测试排队。
