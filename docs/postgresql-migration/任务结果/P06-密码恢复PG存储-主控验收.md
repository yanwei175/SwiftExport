# P06 密码恢复PG存储主控限定验收

2026-10-05 09:59 +08:00。存储和本批矩阵验证通过；控制台入口已改用PGStore并编译，未运行真实人工恢复命令，不标全P06或全宿主已接入运行。

B的Store固定SHA950BB49D、离线BD623D0F及原集成1737B622接回后，主控先查明随机角色夹具违反源300字符/管理员字典唯一事实，未运行旧夹具。A仅获授权修正夹具，STOPPED/completed后接回Integration `671D3516BAA26F764D51C82533067E1816021B7B64F8E1509A4A5294FC9C60A7`及结果`2DCA73E8D4A153DA6C9FFCC293D7492B54161623C23572B5EFF2F5B41F1DA61F`。主控核对原18断言、原生独立Store及finally收口任务/精确恢复；生产Store和新离线SHA均未变。

主控清空五数据库开关，必要编译及14离线：

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~PostgreSqlAdminPasswordRecoveryMigrationTests --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p06-recovery-fixture-fixed-offline.trx' -v minimal
```

14通过、0失败、0跳过；0编译错误，4既有NU1900和7既有MSTest警告如实保留。普通固定SHA复制测试文件受到Windows写入限制，仅提升相同两文件复制及必要编译权限，未回退其他成果。

独占192.168.3.12:5432、`dv_next_pg_failure_tests_20261004`。主控先只读确认唯一既有管理员角色`51de552e-d96c-4aad-9478-59e07084c43f`，每例全列快照后暂改RoleCode/IsSystem/IsActive/IsDeleted，finally结束owner及Store后恢复四字段并全列比对。仅新增随机User/Link，失败精确清理，成功保留；未新增/删除角色、DDL或清库。

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~PostgreSqlAdminPasswordRecoveryMigrationIntegrationTests --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p06-recovery-store-first-real.trx' -v minimal
```

**18通过、0失败、0跳过**。涵盖8资格拒绝、2实际BCrypt/双版本/五列提交及旧凭据失效、NULL typed及23502回滚、raw哈希边界、实际服务器行锁等待取消、5版本/用户/角色/关联竞争且不自动重试40001。凭据链使用真实AuthSession、BCrypt、CredentialVersionTokenValidator及测试窄真实PG读取端口，未据此声称生产PGAuth仓储/完整JWT管道验收。

| 保留新User UID | 保留新UserRole UID |
|---|---|
| 93051942-f8fc-4bdc-916a-a77a731ac3a4 | c6732e64-4443-460e-911b-58851fec3653 |
| ce28ea7b-906e-4cc0-8f68-07f637b8eb52 | d3a81b3f-c952-4e0c-9d5f-7a60d0ab25be |
| 6df3a5c8-2ae7-4861-a753-55f185e34b36 | 323e6a55-a7ad-4de7-8d29-240ca3d633c2 |

TRX三条真实成功清单均角色全列恢复验证true。50表总17595→17601行，仅User2→5/Link2→5；角色与其他48表MD5不变。排除上表六UID后原User2行MD5`5c412ed128633511727eaf71885215cd`、原Link2行MD5`b5fa2a1f1edd50f5d410ead0cbeff8d7`与批次前相同，证明原账号及关联未改。成功事实按约定保留，不清掉以制造摘要一致。

证据：[离线TRX](../../../TestResults/postgresql-migration/p06-recovery-fixture-fixed-offline.trx)、[真实TRX](../../../TestResults/postgresql-migration/p06-recovery-store-first-real.trx)、[逐表前后摘要](P06-密码恢复-前后摘要.csv)、[夹具结果](P06-恢复单一角色夹具-修正结果.md)。无密码、哈希或连接字符串输出。

主控仅把_tools/AdminPasswordRecovery/Program.cs的Store构造替换为PostgreSqlAdminPasswordRecoveryStore并补职责注释，环境变量、密码策略、交互和退出契约保持；同步原使用说明，明确PG结构前置和连接参数，不再指导对PG执行旧SQL Server脚本。后续Link最小编译已包含该入口；进程内清空SWIFTEXPORT_ADMIN_RECOVERY_CONNECTION后运行编译产物，缺配置消息与exit2保持、没有连接库。不运行人工恢复或种子，不删除SqlClient/旧Store，P09另收口。角色raw唯一与源CI/尾空格唯一的P03/B10仍开放，P06开发身份/目录工具和P07全接线仍待完成。
