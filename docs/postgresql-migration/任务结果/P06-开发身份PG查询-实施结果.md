# P06 开发身份 PG 查询实施结果

- 轮次：P06-DU-IMPL01；执行窗口 B；日期：2026-10-05。
- 主Agent Backend；后端API工作流；依据 MAIN《开发身份PG查询实施分派》。
- 已完成窄查询、相关离线测试及真实PG测试编译；尚未运行真实PG，尚未接入运行宿主。
- 独占写入：一个新Infrastructure具体类、两份新测试、新结果及本人状态。
- 没有修改Middleware、DI、Core、Contracts、项目/公共文件或旧成果；没有数据库连接、DDL、种子、全量验证、Git、跨树同步、新代理或跨聊天消息。

## 新文件及固定SHA256

| 文件 | SHA256 |
|---|---|
| `SwiftExport.Infrastructure/Repositories/Auth/Development/PostgreSqlDevUserQuery.cs` | `08B425511B0C58D190821096C95EFA2D3F3A2B337FFCD936B3C0643045FC22A1` |
| `SwiftExport.Application.Tests/Database/PostgreSqlDevUserQueryMigrationTests.cs` | `6CE8287D20D946F35AA57BAF119E30CAD0BC1024CC3723784DFB8C73207A8080` |
| `SwiftExport.Application.Tests/Database/PostgreSqlDevUserQueryMigrationTests.Integration.cs` | `9EF1F0084A1D9473490A25707C1AF32D739FDF075C70683B65743807F04F5A74` |

三个源码分别38、159、120行（含末尾空行），均0替换字符、0尾空格。实际检查限定新源码与依赖SHA，没有使用Git diff或接管其他文件。

## 实现与原行为

具体 `PostgreSqlDevUserQuery(DbSession)` 仅公开 `Task<Guid?> FindUidAsync(string userName)`。复用scoped原连接、SysUser元数据表/列映射及标识符引用；用户输入始终是单个明确DbType.String（Npgsql text）参数，不Trim、不转大小写、不空白回退dev，不建Core接口或通用用户框架。

SQL只投影Uid，活动TRUE且COALESCE(IsDeleted,FALSE)=FALSE，LIMIT 1无ORDER。缺失返回null；数据库及取消异常原样传播，不重试、不创建事务、无token参数、不传session.Transaction，保留MAIN原ExecuteScalarAsync调用形式。Npgsql真实连接在既有父事务内仍按提供者规则执行；不因“不传事务参数”而自行提交或另开连接。

用户名等值沿MAIN现PGAuth的 `rtrim(...,' ') COLLATE public.erp_dictionary_ci`，只去ASCII尾空格。没有新DDL/collation或硬编码Unicode等价集合。原SQL Server NOLOCK脏读在PG不能实现，此处按PG快照读；没有降低其他服务隔离或加业务锁，B10全域等价继续开放。

中间件四条件、仅null配置回退dev、static Guid缓存/信号量、缺失不负缓存、四claims、异常阻断next及生产禁用均留原源码，未写入本实现。本类不宣称已注册、切库或运行接入；主控P07整链切换时接线，当前SQL工厂不能混用PG语句。

Dapper/DbSession技能的一般仓储事务透传要求与本次专用查询原行为不同；按用户/主控明确冻结的“不传事务、不新接口/Context”执行，不扩大成常规业务仓储改造。Auth技能旧Middlewares路径及Controller直连描述按MAIN实际Development源码和分派处理，没有重建过期路径。

## 实际构建与离线测试

每次编译及测试进程清除：
`SWIFTEXPORT_RUN_PG_TESTS`、`SWIFTEXPORT_RUN_PG_FAILURE_TESTS`、`SWIFTEXPORT_RUN_PG_COMMAND_TESTS`、`SWIFTEXPORT_RUN_PG_RBAC_TESTS`、`SWIFTEXPORT_RUN_SQL_TESTS`。

编译进程NUGET_SCRATCH为本worktree `.artifacts/nuget-scratch`，未restore、禁用审计或屏蔽警告。

```powershell
dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~SwiftExport.Application.Tests.Database.PostgreSqlDevUserQueryMigrationTests' --logger 'trx;LogFileName=P06-DU-IMPL01.trx' --results-directory .artifacts/test-results/P06-DU-IMPL01
```

- 编译两次：首次0错误8警告，其中新离线测试CS1690来自DbConnection派生记录类的CancellationToken字段成员访问；仅该新字段改自动属性，最终0错误7既有警告。生产实现没有编译警告。
- 新离线实际执行11/通过11/失败0/跳过0，TRX路径 `.artifacts/test-results/P06-DU-IMPL01/P06-DU-IMPL01.trx`，读取Counters复核total/executed/passed=11，failed/notExecuted/inconclusive=0。
- 离线通过真实Dapper+无网络ADO记录连接核验原输入7项（普通、空串、纯空格、前后空格、尾tab、SQL特殊字符、null）、单text参数、可空UID、无值/DBNull两项再次查询，以及数据库/取消两异常实例原样传播。验证LIMIT/单Uid/活动/COALESCE/无ORDER/锁/凭据投影、不传事务或令牌、不自动建/提交事务。
- ADO返回值只验证调用边界；没有用模拟标量结果宣称PG资格、排序、collation或真实事务验收。
- 既有7警告：SourceDemandReceptionServiceTests:164 MSTEST0044；PurchaseOrderProgressPartialFeedbackTests:19/50、PurchaseDraftPreviewBuilderTests:197/469 MSTEST0017；PackingListDraftApiContractTests:55/56 MSTEST0032。没有修改它们。

## 真实PG用例与清理（仅编译，全部未运行）

2方法14展开：活动、停用、软删、缺失、ASCII大小写、存储/输入尾空格、前导空格原值匹配/不得裁剪、尾tab不得当空格、null、SQL特殊字符及空串/纯空格。

严格共享failure门禁 `192.168.3.12:5432/dv_next_pg_failure_tests_20261004`、public，拒绝Multiplexing/NoResetOnClose。任何连接/初始化前输出无凭据JSON库名/随机User UID/回滚政策；新账号随机合法用户名、Version/CredentialVersion=1及本例新随机BCrypt哈希，不读写既有密码/管理员/角色/目录/Receipt。

每例仅sys_user随机一行，父ReadCommitted内初始化，查询保持原不显式传Transaction调用；finally回滚，session.Transaction置null，独立observer按本例UID确认零残留，无DELETE/提交/清库。未知回滚/连接错误传播并保留预登记供主控核查，不静默视为清理成功。空白场景只先计数既有空白活动账号，若存在则Inconclusive、不取既有身份或初始化本例；不假定真实库一定没有空白用户名。

若14项完整运行且前置齐备，sys_user临时各一行，最终净增0，其他49表无写入。此为计划数量，本窗口实际数据库读写均0。物理is_deleted非空结构不写非法NULL夹具；COALESCE兼容表达式仅离线核验，历史NULL真实读取未验收。

## 源码冻结与环境差异

B树没有MAIN的 `Repositories/Auth/Accounts/PostgreSqlAuthRepository.cs`，直接仅读MAIN作为比较表达式依据；本实现没有编译依赖该具体类，已有元数据GetProperty/命名/Session API足够，故无必要依赖缺失或同步请求。

12份关键源均起止稳定。B与MAIN既有版本存在差异，表中分别登记，不将它们误报完全相同；未为了差异同步或修公共。早期读取猜测路径/PowerShell通配路径失败已通过rg --files定位实际文件恢复，不是缺失实现依赖，没有遗留运行命令、升级权限或环境配置改动。

| 路径 | B起止SHA256 | MAIN起止SHA256 |
|---|---|---|
| `SwiftExport.WebApi/Development/DevUserMiddleware.cs` | `B38E87508844E4893C0A7DFC9489B0895EA0FF0EA35A2A6934F2D6772767EA0F` | `B6A08162B34D9578608EA1C984BBA9C79A38E5747F3588E11FEDF364882B34EE` |
| `SwiftExport.Infrastructure/Data/DbSession.cs` | `AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077` | `AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077` |
| `SwiftExport.Infrastructure/Data/PostgreSqlConnectionFactory.cs` | `70B3297506CDEEF32A83E7E3F6D9984D02C78DEE9F9A6B3CDA1B71389C273D88` | `F4037B5F162517BA87C89FBF2872DEAAB98A20793E64734E8E245A7EED319EC4` |
| `SwiftExport.Infrastructure/Repositories/Base/PostgreSqlParameters.cs` | `B5583A2F128A0A8AAABB275E3E812D4197F242A4628FE632110A47BADC55F675` | `AFD137139BE722A82E53BB28EFB26FF2684DF5FD9CB00140E631C484CD33B57A` |
| `SwiftExport.Infrastructure/Repositories/Base/PostgreSqlEntityMetadata.cs` | `8D0E928B456E2C7261CDC7A7626414D3A398D28B92BF6DB68681D03CFF3B2A2F` | `1C025B0FE154139E4D7B3B81913A2997214FF967ABDAE40EC62BFCDDFF89F064` |
| `SwiftExport.Infrastructure/Repositories/Base/EntityMetadata.cs` | `06565E461227653D22687D66EB395D0157A237EE54A1CE9AAF00FB0504D9309B` | `B92DA0B3DE54AE78358AF1259C5771A394ED0B126B92B848D3A1B2DEF7025448` |
| `SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlNames.cs` | `E2E52C228EFF31906DB1B0667988BA3317FD304A24471431CF7B80DC583D4F1D` | `38B5C22277F7C6DB2538AD4A81990ACD4E96CD5A6477A7F941621D9075CEA0A1` |
| `SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlSchemaGenerator.cs` | `F9EE8C717ECBD6A9EE25CB7F522246CE06B79438376EE09D315EAA5675ED5084` | `155CCEB6231F2F6324E7F461D210CC1DDACE8972378F471D48ACC50D83AC3C62` |
| `SwiftExport.Core/Entities/Auth/SysUser.cs` | `9E3A061850BB486CCEC808E7E26DA5C43D64380E60840B979C3A40217C61F73B` | `8179A8EEF870075264659420C56D7902AEF57C60F784C6F65EC628AFD1DC73CE` |
| `SwiftExport.Core/Common/BaseEntity.cs` | `B62402C82DB489A2303535B452D3567AD52A907752C24AA53CFCBD427C01D3D0` | `6CF4B7846EB0AFB5835019346AEA87CB09028B9A3BB23CE8C655E92BDEC60002` |
| `SwiftExport.Application.Tests/Database/PostgreSqlIntegrationEnvironment.cs` | `33594E10D1F07C029848A6B720D23D82DAF42C9438119B905B89B85418DDD6DE` | `33594E10D1F07C029848A6B720D23D82DAF42C9438119B905B89B85418DDD6DE` |
| `MAIN:SwiftExport.Infrastructure/Repositories/Auth/Accounts/PostgreSqlAuthRepository.cs` | 仅MAIN只读 | `3A72315FAF4F558FB50B71AD36018158341CEAFAB49CD04DA00686CE32A94BC5` |

## 待主控验证

真实14项、ICU版本与B10全域用户名等价、PG快照技术差异、真实NULL历史读取、父事务提供者关联及回滚/零残留仍未验收。缓存/claims/权限/HTTP中间件、四条件与生产阻断未接线或运行；P07主控统一处理。主控接回前不续改本轮交付或旧成果。

