# P06 权限目录同步 PG 实施结果

- 轮次P06-PC-SYNC-IMPL01；Backend/后端API工作流；执行B；2026-10-05。
- 新具体Sync类、新离线/原生测试完成；最终离线18通过、0失败、0跳过，原生7项全部未运行。
- 本轮新增生产工具代码，未修改既有业务代码/Program/默认引导/DI/Core/公共/项目/旧成果；原Program仍SQL，没有运行接入或provider开关。
- 仅四新交付及本人状态写入；四文件启动确认不存在后创建。旧FBF86/4013、933A及其他成果归主控，本轮没有续改。
- 没有DB连接、DDL、实跑PG、CLI/种子/恢复、全量、Git、同步、新代理或跨聊天消息。

## 新源码固定 SHA256

| 文件 | SHA256 | 行数 |
|---|---|---:|
| `_tools/DbSeed/PostgreSqlPermissionCatalogSync.cs` | `5DA11D1A74671E90CF093F2E4D89A6054485486B175F78F899EFAA19AE585A64` | 191 |
| `SwiftExport.Application.Tests/Database/PostgreSqlPermissionCatalogSyncMigrationTests.cs` | `BF624D5DF9E1D0644838EF1A47123BDC3909DB6B3D39D9C49D341947EBDE0FF2` | 285 |
| `SwiftExport.Application.Tests/Database/PostgreSqlPermissionCatalogSyncMigrationTests.Integration.cs` | `F3520E5AB8788BC45AC287B00687BCB5B1B97B9EF3F40871022AD1D89B9E6FFF` | 237 |

三个源码均0替换字符、0尾空格；行数包含末尾空行。中文XML覆盖职责和非显然逻辑。新文件内容/各补丁和依赖SHA直接复核，没有运行Git diff。

## 实现合同与保持的业务

具体类namespace SwiftExport.Tools.DbSeed，构造接DbSession，唯一用例RunAsync(bool dryRun,CancellationToken=default)，没有Core接口/DI/通用工具框架。所有计划仍来自PermissionCatalog.All，不接受目录覆盖参数，不公开内部计划API。

拒绝已有Session.Transaction；在当前已打开连接拥有独立Serializable事务，第一批实际Dapper命令严格：
1. 元数据sys_role SHARE ROW EXCLUSIVE；
2. 元数据sys_permission SHARE ROW EXCLUSIVE；
3. 原同步范围的Schema校验；
4. 全量typed目录读取FOR UPDATE，再计划/写入/commit。

每命令同transaction/token，锁前无SELECT、日志/库名探测/SET；无advisory租约或全局业务锁，不自建第二连接。dry-run同锁/Schema/比较/事务，只跳过DML。调用方令牌贯穿，不给旧CLI加全请求30秒预算或改驱动命令超时。测试截止令牌不等于生产新增预算。

catch尝试Rollback(None)，保留原数据库/取消/未知提交错误，不重试；finally Dispose事务后清Session.Transaction。清理失败关闭当前连接，不能覆盖已有原失败；若原操作无失败而Dispose失败则如实传播清理失败，不冒称连接可复用。借用连接平常不由类关闭，原生测试由调用方Dispose会话核验物理退出。

首批LOCK仅42P01带InnerException转换成原“目标库缺少当前权限定义或角色权限关系结构；目录同步不迁移结构。”诊断。40001/40P01/23505/42703/取消等原样，不扩大转换。Schema仅验证定义/RP表及原七个定义关键列、RP PermissionId；role存在由首LOCK保证，不执行DDL或修约束。Version/审计其他列仍在实际SQL失败时原样报错，不扩大业务校验。

目录比较保持原：
- 代码目录OrdinalIgnoreCase重复拒绝；
- 数据库全量（含停用、软删、目录外）同IgnoreCase字典重复拒绝；
- 命中后Ordinal精确拼写拒绝，不能CI找相似后改Key/并键；
- 目录外按OrdinalIgnoreCase排序报告并保留；原Console计划/逐新增/更新/目录外/完成输出保持。

缺键仅建定义，随机UID、Version1、未删/活动、原index+1 SortOrder、Created/Updated审计DbSeed。SQL两时间显式clock_timestamp() AT TIME ZONE 'UTC'，保留原SYSUTCDATETIME无时区UTC墙上时间，不改会话时区/使用本地Now。

已有定义SET严格仅DisplayName/GroupKey/SortOrder；UID/Key/Version/状态/删除/审计及全部授权关系不写。影响行数必须1，零行整体失败回滚。不会补管理员RP、引导/提升账号或重置密码。

复用公开PG metadata/Names和固定物理名，不沿旧SQL复数Table注解。工具无法引用内部PostgreSqlParameters，采用固定语句DynamicParameters：text（含NULL）/integer/uuid明确类型，不改公共可见性或复制通用参数框架。

## 实际环境、restore、构建及离线

主控停写期同步两个项目引用，本窗口未写项目。启动DbSeed.csproj重复Infrastructure引用，B保持3F2B987B；运行中主控消息说明MAIN只去重一条，改为0FFCD313，B不在RUNNING期同步。构建能通过，但不声称项目两树完全相同；去重同步归主控接回后。

首次构建NETSDK1004：新引用DbSeed缺obj/project.assets.json，未进入新代码编译。按授权只用本人scratch及D:/DevCaches/NuGet缓存做必要restore，exit0；图内9个项目资产还原，没有修改项目/配置、删除共享锁、升级权限或禁审计。仅本地源还原不能证明在线审计数据最新，不把缓存成功当全安全门禁通过。

每次构建/测试均清五集成开关：
SWIFTEXPORT_RUN_PG_TESTS、SWIFTEXPORT_RUN_PG_FAILURE_TESTS、SWIFTEXPORT_RUN_PG_COMMAND_TESTS、SWIFTEXPORT_RUN_PG_RBAC_TESTS、SWIFTEXPORT_RUN_SQL_TESTS。
构建/restore设置进程NUGET_SCRATCH为本worktree .artifacts/nuget-scratch；没有输出连接/凭据环境值。

实际命令：

```powershell
dotnet restore SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --source D:/DevCaches/NuGet --ignore-failed-sources -p:RestorePackagesPath=D:/DevCaches/NuGet
dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~SwiftExport.Application.Tests.Database.PostgreSqlPermissionCatalogSyncMigrationTests' --logger 'trx;LogFileName=P06-PC-SYNC-IMPL01-verified.trx' --results-directory .artifacts/test-results/P06-PC-SYNC-IMPL01
```

- 必要build共6次：首NETSDK1004 exit1/1错误；缓存后首源码build exit0/0错误8警告，新CS8605仅新测试GUID类型模式修正；其后4次build均0错误7既有警告，分别验证可空修正、原生新增、NULL绑定补测、DapperIdentity补测修正。受影响测试项目构建同时实际编译DbSeed.dll，未运行工具。
- 精确新离线共3次：首17/17通过；新增NULL绑定测试后17通过/1失败/0跳过，原因测试错误传null Identity引起Dapper NRE；仅新测试改用真实Dapper传输生成Identity；最终18/18通过、0失败、0跳过。
- TRX保留三个本轮记录：P06-PC-SYNC-IMPL01.trx、P06-PC-SYNC-IMPL01-final.trx、P06-PC-SYNC-IMPL01-verified.trx。最后Counters再次读取核total/executed/passed=18，failed/notExecuted/inconclusive=0，未把17+18+18累计成不同用例数。
- 新类/最终新测试无警告。旧7警告：SourceDemandReceptionServiceTests:164 MSTEST0044；PurchaseOrderProgressPartialFeedbackTests:19/50、PurchaseDraftPreviewBuilderTests:197/469 MSTEST0017；PackingListDraftApiContractTests:55/56 MSTEST0032。没有修改它们、删除断言或屏蔽警告。

18项离线覆盖：真实类的首锁→Schema→全量读取顺序与同transaction/token、Serializable/commit/清引用，dry-run零DML与原Console计划，三展示列精确SET与目录外保留，缺键只定义/UTC/初始状态/typed参数，精确拼写/IgnoreCase重复拒绝，Schema/父事务边界，42P01窄转换和其他SQLSTATE/取消原实例，零影响行数/commit失败/回滚Dispose失败不重试不遮盖原错误。

NULL text补测仅纯只读反射构造私有计划调用固定BindDefinition，再通过真实Dapper传输观察参数；不注入生产目录、运行假PG SQL或公开内部规划。代码目录重复检查按原实现保持，未修改不可变All制造代码重复；传输行和预定scalar只验证调用边界，不声称PG资格/锁/比较已通过。

## 原生7项（6方法+1额外DataRow，全部只编译）

| 项 | 范围与必须结果 |
|---|---|
| 完整dry-run | 真实角色/权限首锁和Schema/全目录，执行前后50表有序JSON服务器摘要一致 |
| 原父事务拒绝 | 保留原父事务身份，由调用方回滚，全库零变化 |
| owner展示竞争 | 先确认完整既有代码目录三展示已与代码一致，再只修改已登记一个UID三列；角色首锁目录屏障后owner提交，工具必须读新差异并只恢复原三展示列；全列及50表摘要与原快照相同 |
| 首role取消 | 确认真实LOCK等待才取消、OCE传播、事务/锁归还，调用方关闭借用非池会话，第三backend有界重取 |
| 后permission取消 | owner仅该表LOCK无DML且不再请求role，候选role→permission等待；取消/归还/第三连接同上，不造反向等待循环 |
| 既有合法停用/软删目录外 | 仅使用既有行，dry-run全部字段和50表不变；没有前置则Inconclusive，不造/清目录 |
| 既有拼写漂移 | 只读既有非重复IgnoreCase漂移，原精确拼写诊断，零变化；没有前置则Inconclusive，不改Key造条件 |

严格Failure门禁192.168.3.12:5432/dv_next_pg_failure_tests_20261004、public；拒Multiplexing/NoResetOnClose；DoNotParallelize；无默认凭据/测试库降级。

展示变更场景另要求测试授权标识SWIFTEXPORT_PG_CATALOG_SYNC_MUTATION_AUTHORIZED=1，**仅标识主控已有精确授权，不自行授予数据库写权限**；缺标识在连接前Inconclusive。完整既有目录须无IgnoreCase重复、每精确代码键存在且三展示一致，否则不初始化缺键、不全量扩写。初始化DML前输出整个SysPermission全列快照、目标UID、库和明确三列政策；Key/状态/审计/Version绝不由owner改。此标识不是生产配置/目录覆盖功能。

工具拥有独立事务，不能靠owner父rollback声称工具已清理。owner提交前异常可回滚；owner提交未知或工具未确认成功/快照断言失败只保留完整清单，不自动恢复/覆盖既有目录。工具成功恢复三列并所有断言通过才输出零变化证据；测试主控串行、静默库背景下比较摘要，跨50个查询的摘要不是生产一致读取接口。

非池原生session及候选唯一ApplicationName/PID；pg_locks同表SRE granted/未granted与pg_stat_activity当前纯LOCK、backend_xid空形成真实屏障，不用Sleep作为唯一证据。没有用backend_xmin为空错误限制。取消finally先归还owner再收候选任务，表锁回滚不依赖池；类只负责事务，连接关闭由调用方负责，第三连接lock_timeout=3秒只为测试。NULL历史非法夹具、角色/用户/UR/RP变更均没有造。

50表摘要服务器端md5有序to_jsonb聚合，只返回表名/count/hash；断言仅报表名，未下载/打印实际账号凭据/Hash。本窗口实际所有数据库读写=0，未运行摘要查询或计入旧18/14/7数。

## 独立库及尚未验收

完整目录空范围双同步、全量缺键新增成功留存、重复键约束/CI跨等价、非法历史NULL/结构错误注入、真实新增UTC审计及commit失联/连接池污染/共同预算仍未编写或实跑。不能在现有Failure/RBAC库删除目录/管理员或修改约束造条件。默认引导完全不实现。

主控需单独批准独立预建工具库（实例、库名、初始化/清理/留存实体/50表范围），再测试全量新增/空范围。代码目录All来源保持；若需要提交新增，主控运行前按计划逐键预登记随机定义UID，成功全部留存原Version/UTC/状态及50表摘要，不用自动DELETE。当前具体类沿原逐条随机Uid、不新增公开计划/目录覆盖API，因此如何在全量新增SQL前记录每个动态UID须单独冻结窄测试取证；不能把仅Console“新增键”输出当完整UID登记。

现权限键/角色码普通唯一与用户名rtrim+ICU不是同一等价口径；.NET OrdinalIgnoreCase/Ordinal门禁不因PGCI而放宽，B10继续开放。没有修索引/collation/Schema，生成器也不是部署验收证据。原生上述7项、独立库矩阵、生产CLI接线/SQL退出、首快照/取消释放/未知提交/全库留存都尚未验收。

## 关键只读依赖起止SHA

14份B依赖/项目起止一致；MAIN除主控明确去重DbSeed.csproj之外其余起止一致。表同时登记原始差异和本轮主控项目变更，不拿B旧树等同MAIN当前事实。实际必要API已满足，无公共可见性修改/依赖同步。

| 路径 | B起止SHA256 | MAIN读取阶段SHA256 | MAIN结束SHA256 |
|---|---|---|---|
| `_tools/DbSeed/DbSeed.csproj` | `3F2B987B0092A21D258312625A46F339533AC9A14F296CEB5B835488AD7D133D` | `3F2B987B0092A21D258312625A46F339533AC9A14F296CEB5B835488AD7D133D` | `0FFCD3130186C07C65FF1E838D1A6743D57B805F637C9AFDDA3DED73AC77B52C` |
| `SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj` | `2ABBAF45970D25057F969380E85637723AF0380BD209CD6FB864097765A0E52E` | `2ABBAF45970D25057F969380E85637723AF0380BD209CD6FB864097765A0E52E` | `2ABBAF45970D25057F969380E85637723AF0380BD209CD6FB864097765A0E52E` |
| `_tools/DbSeed/PermissionCatalogSync.cs` | `34FCC7FDF0CB0E7C73B5BF446CE1773CABC925109F6E1EBCEF63662EE53B3103` | `629DA95F0DE598FFDE33940BFF74CBCDE8C9BF32C7447AEFB60B5FA32B8941E1` | `629DA95F0DE598FFDE33940BFF74CBCDE8C9BF32C7447AEFB60B5FA32B8941E1` |
| `_tools/DbSeed/Program.cs` | `4076E20E5B591FBA0EF393A1F81E9F90EFB74DE09D4540BD7B29A8F8C4401E88` | `9AA56492DED4F103E16608E61478859189A31337BD446BB70B6DD15F306FBC8E` | `9AA56492DED4F103E16608E61478859189A31337BD446BB70B6DD15F306FBC8E` |
| `SwiftExport.Contracts/DTOs/Auth/Permissions/PermissionCatalog.cs` | `4868D73A41FB6F6FB09D0C4AF6478FF37A91B921FB2B9E1C331E59CDEFC43DAE` | `9C2DF576A5227F253D5C162FB89BBFBE7D3E79BB6FADE18F2CCD15A2AEA5ED04` | `9C2DF576A5227F253D5C162FB89BBFBE7D3E79BB6FADE18F2CCD15A2AEA5ED04` |
| `SwiftExport.Infrastructure/Data/DbSession.cs` | `AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077` | `AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077` | `AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077` |
| `SwiftExport.Infrastructure/Data/PostgreSqlConnectionFactory.cs` | `70B3297506CDEEF32A83E7E3F6D9984D02C78DEE9F9A6B3CDA1B71389C273D88` | `F4037B5F162517BA87C89FBF2872DEAAB98A20793E64734E8E245A7EED319EC4` | `F4037B5F162517BA87C89FBF2872DEAAB98A20793E64734E8E245A7EED319EC4` |
| `SwiftExport.Infrastructure/Repositories/Base/PostgreSqlEntityMetadata.cs` | `8D0E928B456E2C7261CDC7A7626414D3A398D28B92BF6DB68681D03CFF3B2A2F` | `1C025B0FE154139E4D7B3B81913A2997214FF967ABDAE40EC62BFCDDFF89F064` | `1C025B0FE154139E4D7B3B81913A2997214FF967ABDAE40EC62BFCDDFF89F064` |
| `SwiftExport.Infrastructure/Repositories/Base/EntityMetadata.cs` | `06565E461227653D22687D66EB395D0157A237EE54A1CE9AAF00FB0504D9309B` | `B92DA0B3DE54AE78358AF1259C5771A394ED0B126B92B848D3A1B2DEF7025448` | `B92DA0B3DE54AE78358AF1259C5771A394ED0B126B92B848D3A1B2DEF7025448` |
| `SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlNames.cs` | `E2E52C228EFF31906DB1B0667988BA3317FD304A24471431CF7B80DC583D4F1D` | `38B5C22277F7C6DB2538AD4A81990ACD4E96CD5A6477A7F941621D9075CEA0A1` | `38B5C22277F7C6DB2538AD4A81990ACD4E96CD5A6477A7F941621D9075CEA0A1` |
| `SwiftExport.Core/Entities/Auth/SysPermission.cs` | `9A71315304F265F3730A3202425659DA0F9D08368E4B6FAB0775F6F4C229BD84` | `778E1A2D7116140058196DF4AF3B111F27E4B9CF768EEFDF153040853BB4094D` | `778E1A2D7116140058196DF4AF3B111F27E4B9CF768EEFDF153040853BB4094D` |
| `SwiftExport.Core/Entities/Auth/SysRolePermission.cs` | `3ABFAB27D01CDFC26D05A70F2A1997C941453955B1F8EFE7A800EEE996B30939` | `D9DBE4EC031C1497128A63326EAEE93DBE4F09CAFF25AB3D3829F8C7AFF37F66` | `D9DBE4EC031C1497128A63326EAEE93DBE4F09CAFF25AB3D3829F8C7AFF37F66` |
| `SwiftExport.Core/Entities/Auth/SysRole.cs` | `C78B0CE0C83B6F81DE13A7DC0181425361F7BDA1DE1F578095152365B515729E` | `EAE4D1502C998215F9733292C839DA01A60BE7448B55EAA0ED41E3FE6337DCB1` | `EAE4D1502C998215F9733292C839DA01A60BE7448B55EAA0ED41E3FE6337DCB1` |
| `SwiftExport.Application.Tests/Database/PostgreSqlIntegrationEnvironment.cs` | `33594E10D1F07C029848A6B720D23D82DAF42C9438119B905B89B85418DDD6DE` | `33594E10D1F07C029848A6B720D23D82DAF42C9438119B905B89B85418DDD6DE` | `33594E10D1F07C029848A6B720D23D82DAF42C9438119B905B89B85418DDD6DE` |

早期猜测协调文档/PermissionDefinition独立路径失败，已rg定位窗口协调.md/PermissionCatalog内类型，不是必要依赖缺失，未改目录或升级权限。最终报告、三个新源固定后本人STOPPED，主控接回前不续改；项目去重窄同步由主控完成。

