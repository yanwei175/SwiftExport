# P06-AR-IMPL01 密码恢复PG存储实施分派

2026-10-05 主控分派现有执行B，3557 worktree。P06审查已STOPPED/completed按2940C15C固定SHA接回。主控拥有项目/入口/旧测试及所有真实数据库操作；不得自行新建窗口或代理。

## 唯一写入范围

- 新 `_tools/AdminPasswordRecovery/PostgreSqlAdminPasswordRecoveryStore.cs`。
- 新 `SwiftExport.Application.Tests/Database/PostgreSqlAdminPasswordRecoveryMigrationTests.cs`。
- 新同名 `.Integration.cs`。
- 新 `docs/postgresql-migration/任务结果/P06-密码恢复PG存储-实施结果.md` 和本人状态。

其他文件全部只读。主控已最小修工具Program/Executor及旧三个测试的Passwords命名空间、工具引用Infrastructure、Application.Tests引用工具并纳入旧测试；停写时只同步这些五文件，不得窗口自行改项目。Program仍创建旧SQL Store，待真实PG验收后由主控切换；不删SQL Store/SqlClient、不改交互、PasswordPolicy、Hasher、接口/结果，不生产运行工具。

## 冻结原行为

依P06审查与当前SQL Store，完整实现同一IAdminPasswordRecoveryStore，构造仅connectionString。原生Npgsql连接和Dapper，同一Serializable事务。无新增命令回执/会话锁/provider框架/业务重试。Begin仍在try外；取消、读取/写入/提交异常原样传播，catch独立None回滚不遮原失败。不把40001/40P01或未知提交转业务成功。

初读LIMIT 1且无新增ORDER，uid/version/credential_version与EXISTS管理员资格；活动用户、COALESCE删除false，关联未删、角色未删活动且SYSTEM_ADMIN，**不要求IsSystem**。用户名和角色码SQL字典比较均rtrim ASCII尾空格加既有public.erp_dictionary_ci；不改C#用户名Trim或角色业务。物理表/列全部用现有PostgreSqlEntityMetadata/Names，不机械翻译旧表名，不复制元数据框架。保留TypedProjection非空版本/布尔读失败，NULL不是成功。

初读FOR UPDATE OF u；没有原资格时对应TargetNotFound/TargetNotAdmin并rollback。更新只有Hash、双版本+1、UpdatedDate/By；Uid+原双版本+活动未删+最终同资格EXISTS。affected !=1对应ConcurrencyConflict并rollback，1commit后Updated；不改其他用户列/角色/关联/日志。

时间冻结为数据库每语句墙上时刻 `clock_timestamp()::timestamp`，非事务起始now()。主控2026-10-05 08:58只读核实SQL Server SYSDATETIMEOFFSET=+08、GETDATE-UTC=480分钟，目标PG TimeZone=Asia/Shanghai；保留数据库本地时刻，不在代码另造时区转换或客户端时间。后续环境门禁由主控记录。

字符串参数显式text，包括可空输入Hash/UserName，不对仓储输入自行Trim/Validate。源初读GETDATE/NOLOCK和当前PG隔离差异只明示，不改业务。结构无DDL/自动schema修补。

## 验证与交付

允许新离线/必要工具和受影响测试项目编译，全部五数据库运行门禁清空；`D:/DevCaches`已有资产可读，用当前资产/no-restore，若缺项目资产仅必要restore不全量。不因环境/既有失败改旧断言。

新离线可验证真实Executor与原密码政策/调用取消/状态，不为录制SQL新增连接工厂或生产钩子。三个旧有效测试只读保持。真实PG用例只编写/编译，**本窗口禁止连接数据库、运行恢复/种子、DDL或初始化清理**。

集成门禁使用既有failure库但Store成功真实提交，仅主控运行：随机账号/角色/关联和用户名；有成功保留清单，禁止真实管理员/目录/数据重置。覆盖缺失/普通/角色失效/关联软删/用户失效、真实Admin无需IsSystem、用户名与角色SQL字典、最终版本/资格竞争、取消/高隔离回滚、仅五更新字段和真实BCrypt新旧密码/JWT凭据版本。竞争需真实独立backend与服务器等待证据；原未实现hook不得为测试新增。初始化/成功清理确切随机ID必须测试内声明，不DDL、不清库。主控实际执行前再登记范围。

结束先报告真实命令/结果/未运行项、源码和测试SHA，标STOPPED；主控聊天completed与本人页一致后才接回固定SHA。不得输出密码/Hash/连接字符串，不Git提交/推送/部署，不越界同步。遇额外文件或语义修改先报告主控。
