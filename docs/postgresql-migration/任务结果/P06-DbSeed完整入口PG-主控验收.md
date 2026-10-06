# P06：DbSeed 实际入口接 PG

2026-10-05 14:21，主控 Backend/后端API工作流。上一轮有真实DI接线进展，本轮继续原完整迁移目标；不新开窗口、不追加业务规则或仓储分部。

## 实际实现

`_tools/DbSeed/Program.cs` 原文件内完成 Npgsql/PG 引导 SQL；`--sync-permissions [--dry-run]` 接已有 PostgreSqlPermissionCatalogSync。保留四环境变量、参数顺序、输入裁剪、密码哈希外部提供、Serializable、自有事务、目录差异拒绝、异常角色拒绝、禁止提升普通账号、不改既有管理员密码及输出/退出行为。

引导首快照前按现行 PG 账号/恢复/目录协议取得角色→权限纯表锁；缺表只转原结构诊断，其余错误向上抛出，不初始化/迁移结构、不降隔离、不重试。保留源 UTC 审计和版本字段，复用现行物理名称/CI规则。B10/P03 的极端 Unicode 比较差异仍未关闭。

实际检查 `_tools/AdminPasswordRecovery/Program.cs` 已接 PostgreSqlAdminPasswordRecoveryStore，本轮未重写。此前“恢复入口待接”的描述过期，最新源码优先。

## 命令和结果

1. `dotnet build _tools/DbSeed/DbSeed.csproj --no-restore -p:UseSharedCompilation=false -v minimal`：0 错误/3 既有 NU1900。
2. 仅本进程环境设置 `SWIFTEXPORT_DBSEED_CONNECTION` 为从用户配置安全读取、显式限定的 192.168.3.12:5432/dv_next_pg_failure_tests_20261004/public，结束恢复原值。不输出密码、不改 appsettings。
3. `dotnet _tools/DbSeed/bin/Debug/net10.0/DbSeed.dll --sync-permissions --dry-run`：退出0，发现新增1/展示更新11/目录外0，无写入。
4. 在用户已授权可修改的同一独立测试库执行 `--sync-permissions`：退出0，新增 System.ExcelFieldMapping.Manage，更新11项名称/分组/排序；没有改变授权关系或账号。仅 sys_permission 摘要变化，其他49表完全不变。
5. `dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false -v quiet`：最终0错误/10既有NU1900/MSTEST。
6. `dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~PostgreSqlPermissionCatalogSyncMigrationIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p06-dbseed-entry-and-sync-real.trx' -v minimal`：5通过/1失败/2跳过。实际启用failure门禁与本例三展示列竞争授权，未运行其他库或全量测试。
7. 首次失败为本轮新引导 SQL 的 INSERT 缺 INTO，真实PG报42601；失败事务回滚，实际查询确认没有 P06Bootstrap 测试账号。补四条生产INSERT与新增普通账号夹具INSERT，不改业务规则，原失败TRX保留。
8. 仅复验 `--filter 'BootstrapEntry_CreatesOnceAndRefusesOrdinaryUserPromotion'`，TRX `p06-dbseed-bootstrap-real-corrected.trx`：1通过/0失败/0跳过。实际启动工具子进程，验证新随机管理员、重复引导全用户记录/密码不变、普通账号被拒且未授角色；按确认UID/随机名称/创建标记精确清理本例账号与关联，不删除既有数据。

通过的现有五项覆盖dry-run、拒绝接管父事务、首角色等待后读取已提交展示差异并恢复、角色/权限两处真实backend等待取消后第三连接重取。两项因既有数据不含目录外停用定义/精确拼写漂移而跳过，未造数据、不计通过。新引导场景单独修正后通过，不称最终单次八项全绿。

## 数据边界和剩余事项

- 测试库保留本次**正常目录同步**：sys_permission从111到112，库总行数17609到17610。现有11项只改展示字段；新增一代码定义，不增加授权关系。其他49表与同步前摘要相同，测试清理后全部50表与同步后新基线完全相同。[最终摘要](P06-工具入口PG-数据摘要.csv)。后续不能继续沿用17609基线。
- 没有DDL、清库、表重建、备份库或dv_next写入。结构未变，不需要将本次测试数据同步到dv_next；实际业务环境目录是否同步由独立目录操作决定，不在本轮自动执行。
- 此次引导使用当前已有角色/目录结构验证新管理员与幂等/拒绝提升；完全空目录/缺失内置角色分支及源/PG极端比较差异未真实执行，不虚称全部边界完成。
- 两个工具启动入口均为PG，旧SQL源码仍保留，P09退出及旧源库测试接线继续由主控处理。业务服务、Core、Contracts、权限规则均未修改；成果仍未提交/推送/部署。
