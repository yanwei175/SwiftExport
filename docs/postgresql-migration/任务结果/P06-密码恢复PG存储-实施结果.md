# P06-AR-IMPL01 密码恢复PG存储实施结果

- 主Agent：Backend；工作流：后端API工作流；适用Dapper/数据库会话/后端质量/认证与当前用户技能，规则去重复用。
- 范围：新PG Store、两新专属测试、本结果及本人状态。主控同步五文件、公共/旧源码/旧测试/旧交付只读；未改Program接线、接口、结果、Hasher、Policy、SQL Store、项目或DI。
- 结果：独立PG+Dapper Store完整实现原接口，14新离线通过；工具与测试项目最终编译0错误、仅测试项目7既有警告。本轮18真实PG用例仅编写编译，**没有数据库连接/DDL/恢复种子运行**，不宣称完整PG或并发通过。
- 上一P06审查固定2940C15C及NL/Manual/其他旧成果归主控，未再编辑。不Git/代理/跨聊天消息/部署/同步，不读取或输出环境凭据。

## 1. 生产实现

文件：_tools/AdminPasswordRecovery/PostgreSqlAdminPasswordRecoveryStore.cs，136行。

1. 构造仅connectionString；原生NpgsqlConnection+同连接Serializable transaction，无DbSession/工厂注入/生产测试钩子。Open与Begin保持在try外，资源由await using释放。
2. Dapper初读LIMIT1无ORDER；使用原生DbDataReader.GetGuid/GetInt32/GetBoolean。缺行TargetNotFound并回滚；NULL版本/布尔不会被Dapper实体默认值吞掉。
3. 表/列均通过现有PostgreSqlEntityMetadata/Names取得并引用；SysRole/UserRole按实体PG名称，不从旧SQL复数表名机械转换。未复制或扩展元数据框架。
4. 用户活动、COALESCE删除false；用户名rtrim ASCII尾空格+public.erp_dictionary_ci参数比较。原始仓储输入不Trim/Validate，nullable文本显式NpgsqlDbType.Text+DBNull。
5. 管理员EXISTS为有效未删关联、活动未删角色，role_code同样rtrim+冻结字典，SYSTEM_ADMIN；**不要求IsSystem**、不按RoleName/用户名认定管理员。初读FOR UPDATE OF u。
6. Update仅Hash、CredentialVersion+1、Version+1、UpdatedDate、UpdatedBy；UID+原双版本+活动未删+**最终同角色资格EXISTS**。affected!=1回滚ConcurrencyConflict；1提交成功才Updated。
7. UpdatedBy=LocalAdminPasswordRecovery；时间clock_timestamp()::timestamp，按分派冻结PG Asia/Shanghai本地墙上时刻，不客户端时间/UTC转换/事务开始now()。环境时区原+08证据为主控分派，本窗口未再次实测。
8. text/uuid/integer参数明确类型，实例不跨命令共享；Hash/UserName NULL仍交原查询/约束决定，不造新业务校验。
9. Cancel、读取/写入/Commit异常原样传播；catch Rollback(None)不遮原失败。无40001/40P01/未知Commit成功转换、自动业务重试、命令回执或资格提升。
10. 没有写角色/关系/业务日志或其他用户列，没有DDL/schema修补；原Program仍创建SqlAdminPasswordRecoveryStore，PG尚未运行接线，SqlClient仍由主控P09收口。

原冻结边界以分派及SQL Store为准；该工具为独立运维入口，非普通Repository不得自行开连接的宿主业务场景。不将工具独立连接模式推广到Application。

## 2. 新离线验证

文件：SwiftExport.Application.Tests/Database/PostgreSqlAdminPasswordRecoveryMigrationTests.cs，134行。
真实AdminPasswordRecoveryExecutor+原PasswordPolicy；窄存储/Hasher观察端口仅离线，不伪装数据库。

| 方法/展开 | 覆盖 |
|---|---|
| InvalidInput_NeverHashesOrCallsStore，6 | null/空白用户名；null/空白/短密码；确认不一致；Hash/Store均0调用 |
| StatusAndInput_PreservePolicy，4 | Updated/缺失/非Admin/并发状态；用户名Trim、密码不Trim、token原样、只Hash一次，只Updated成功 |
| StoreFailure_IsNotSwallowedOrRetried，2 | 原取消/数据库失败实例保持，只一次Store调用 |
| HasherFailure_NeverWrites，1 | Hash失败不访问Store |
| RealHasher_KeepsPasswordAndRejectsCorruption，1 | 真实BCrypt空格密码与损坏格式，不输出Hash |

实际：14通过、0失败、0跳过。不是SQL录制或Store PG运行证据；没有为了离线SQL引入生产连接工厂/钩子。
旧三个恢复测试仅主控已纳入项目，本窗口保持只读，未单独运行；不把旧测试计入14。

## 3. 新真实PG测试（只编写编译，18项未运行）

文件：SwiftExport.Application.Tests/Database/PostgreSqlAdminPasswordRecoveryMigrationTests.Integration.cs，424行。
固定使用既有PostgreSqlIntegrationEnvironment.GetFailureConnectionString：SWIFTEXPORT_RUN_PG_FAILURE_TESTS=1且连接存在；192.168.3.12:5432/dv_next_pg_failure_tests_20261004/public；无默认凭据/DDL。不在本窗口启用。
拒绝Multiplexing/NoResetOnClose；Store专用随机ApplicationName、Pooling=false，与owner/monitor原生独立连接，SHOW TimeZone必须Asia/Shanghai。数据库目录/用户密码值不打印。
初始化只三行：随机SysUser、SysRole、SysUserRole。角色使用system_admin+随机尾空格的独立普通text键并查精确候选碰撞，只扩展候选尾空格、不改已有SYSTEM_ADMIN；IsSystem=false且冻结字典仍认管理员。用户名p06_ar_随机GUID；所有实体/版本/审计合法。
成功每例KeepCommitted，JSON清单只User/Role/Link精确UID、Phase、KeepCommitted；失败/拒绝夹具finally仅删除这三个预声明随机身份，带username/role_name/关联身份保护。禁止任意清库、改真实管理员、删已有内置角色、清目录或重置全局号。

| 方法/展开 | 已编写待验 |
|---|---|
| RealPg_InvalidTargetDoesNotWrite，8 | missing/ordinary/inactive-user/deleted-user/inactive-role/deleted-role/deleted-link/missing-link；原状态，Hash/版本/审计不变 |
| RealPg_RecoveryCommitsOnlyFiveFieldsAndInvalidatesOldCredential，2 | 真实非IsSystem管理员；username大小写/Trim入口；更新五字段、原其他字段/角色/关系JSON不变；BCrypt新密码成功旧密码失败、双版本/本地时刻 |
| RealPg_NullTextAndConstraintFailurePreserveUser，1 | NULL用户名typed text缺失；NULL Hash真实NOT NULL23502父回滚；直接仓储前导空格不自行Trim |
| RealPg_RawStoreDoesNotValidateOrTrimHash，1 | 尾空格字典用户名命中、仓储不校验或裁剪Hash，成功保留 |
| RealPg_CancelWhileServerWaitsDoesNotWrite，1 | 独立backend真实user FOR UPDATE等待后Cancel，OCE原样/凭据不变/专用连接释放 |
| RealPg_FinalVersionOrQualificationCompetitionRollsBack，5 | version/credential/user/role/link竞争；owner Serializable提交，Store首快照在锁等待已形成；40001原样或原拒绝/冲突、无恢复更新/重试 |

并发同步要求pg_stat_activity active/Lock、pg_blocking_pids含owner、backend_xmin非NULL，且owner/store pid不同；Task.Delay仅短轮询预算，不单独充等待证明。所有竞争/SSI结果尚未运行，尤其role/link竞争的原最终资格与高隔离实证须主控验，不能用编译代替验收。
Store直接受约束独立提交，失败后owner自己竞争更新可能保留到精确fixture清理；测试只断恢复五字段没写入并另断owner预期增量，不把owner变化冒称Store变化。

### 凭据服务验证边界

3557未同步另一窗口的PostgreSqlAuthRepository；B没有同步或复制生产实现。
新测试内CredentialReadPort仅直接真实PG读取账号/凭据（共享实体映射），其余授权/写入方法全部抛错；将其接入**真实AuthSessionService、BCrypt和CredentialVersionTokenValidator**。覆盖真实数据库当前版本/账号与旧新声明、实际登录密码，不模拟数据库或授予权限。
没有声称验证未同步的生产PG认证仓储、签发JWT签名/宿主完整Bearer管道或/me权限投影；主控接回后可用自己的PGAuth整链另验。本例给PermissionEvaluation未用端口null，只用于登录/凭据路径，不能用于其他服务。
测试没有添加Store hooks/数据触发器，未初始化schema或删除约束。

### 主控实际运行的精确持久范围

整批18展开各运行一次且全通过，成功3例各保留User/Role/Link各1：理论增量sys_user+3、sys_role+3、sys_user_role+3，合计9行。恢复真实各双版本+1；其余15例随机三行按精确身份清理；无Permission/RolePermission/日志/机器回执/序列增量。
这只是测试设计计数，**本窗口实际增量0（未连接）**；重复运行成功清单累积，不按理论清掉任何旧批。主控按TestContext清单/测试身份去重，逐项确认成功/失败/TRX状态和50表摘要，其中三表精确增量、其余47表不变。
若断言发生于Store成功之后仍Keep保留，主控需据真实TRX核验；不因整体测试失败误认为全部恢复已回滚。初始化/成功保留/失败清理均测试内显式声明。运行前主控另登记库占用及该范围，B不授权运行。

## 4. 实际命令与结果

所有构建/测试调用均清空：
SWIFTEXPORT_RUN_PG_TESTS、SWIFTEXPORT_RUN_PG_FAILURE_TESTS、SWIFTEXPORT_RUN_PG_COMMAND_TESTS、SWIFTEXPORT_RUN_PG_RBAC_TESTS、SWIFTEXPORT_RUN_SQL_TESTS。
未读取连接变量值，不dotnet run，不任何实库探针。

1. 工具build --no-restore：初次NETSDK1004缺工具assets，1错误，未编译成功；原输出保留，不冒称首轮通过。
2. 必要restore --ignore-failed-sources与--source D:/DevCaches/NuGet两次尝试均被环境NUGET_SCRATCH指向只读MAIN的锁权限阻断；最终两命令都exit1，无删除共享锁。
3. 设置**仅进程**NUGET_SCRATCH=<3557 worktree>/.artifacts/nuget-scratch，dotnet restore _tools/AdminPasswordRecovery/AdminPasswordRecovery.csproj --source D:/DevCaches/NuGet --ignore-failed-sources -p:NuGetAudit=false -v:minimal，exit0。再对Application.Tests同样必要restore，exit0；不改公共NuGet.Config/项目，不安装新包、不全量restore。
4. dotnet build _tools/AdminPasswordRecovery/AdminPasswordRecovery.csproj --no-restore -p:UseSharedCompilation=false -v:minimal：0错误0警告。
5. 首测试项目build：1本轮错误（缺未同步PGAuth类型）+2本轮CS8619+7既有警告。仅在新测试改真实PG窄读取端口、可空返回，未改依赖/旧断言；移除新文件未用using。
6. 修后及最终两次 dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false -v:minimal：均0错误，7既有测试警告，本轮无新增。全新18实库测试已编译，但未运行。
7. dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~PostgreSqlAdminPasswordRecoveryMigrationTests' --logger 'trx;LogFileName=P06-AR-IMPL01-offline.trx' --results-directory .artifacts/test-results/P06-AR-IMPL01 -v:minimal：14通过/0失败/0跳过，仅新离线类。
TRX：C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/.artifacts/test-results/P06-AR-IMPL01/P06-AR-IMPL01-offline.trx。最后只移除未用using后重新编译，未改离线逻辑，不无意义重跑14。
曾尝试只识别两条失败restore进程的CIM读取，环境拒绝；没有停止其他进程/申请提升。随后两原restore自然结束exit1，未遗留本轮运行restore。
必要restore产生obj/assets/bin/.artifacts等构建产物，不是修改公共源码/配置。没有Git，最终源码逐文件读核及固定SHA，不宣称全树未提交改动核净。

## 5. 固定SHA与交付

| 新文件 | SHA256 |
|---|---|
| _tools/AdminPasswordRecovery/PostgreSqlAdminPasswordRecoveryStore.cs | 950BB49D8383662AE8DEE7B4921E906F338CDAB679688B74C18EA8244DF678A2 |
| SwiftExport.Application.Tests/Database/PostgreSqlAdminPasswordRecoveryMigrationTests.cs | BD623D0F811EE9DC6CAB1FC117D8B15B0EF5DE79FAFEAA4790FD70D15DAC8ED9 |
| SwiftExport.Application.Tests/Database/PostgreSqlAdminPasswordRecoveryMigrationTests.Integration.cs | 1737B6228DAE06DDFBCDFA59B77FE2475DDFE304CF83A06D0C976064CFD4F99B |

三新文件0替换字符/0尾空格。以下12只读依赖读阶段和最终SHA完全一致；其中主控同步五项（项目前两入口/旧测试/App.Tests项目）与MAIN精确相同。其余为本树已有只读版本，不谎称全部跨树相同，未经同步不改。

| 本树相对路径 | SHA256 |
|---|---|
| _tools/AdminPasswordRecovery/AdminPasswordRecovery.csproj | 962FE9925DB73D0BDF9DF7F13452DDB14E5AD12828FF70131EDA415F2A32185B |
| _tools/AdminPasswordRecovery/Program.cs | 3C4BDBB16587DA1EF07FDC03B7A70B2A2668B5356AF892B52A976BE46F81856F |
| _tools/AdminPasswordRecovery/AdminPasswordRecoveryExecutor.cs | 4CDEC77B74AFE5EC7F9BC55DBFD79291A63617535A599F048E66111199033EAE |
| SwiftExport.Application.Tests/Authentication/AdminPasswordRecoveryTests.cs | F2F569A381F15EF36680B56684DE7510E521378F5EFB33031C31C0B253067089 |
| SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj | B11F06CCB02BB12A836C5CC1B2180E863637F9AEE5D644BD85905F3787B3C8A6 |
| _tools/AdminPasswordRecovery/SqlAdminPasswordRecoveryStore.cs | 6BC78EE74FB6B5F170AC9BBE006D11587AD7F4039A127DD9A2BE9F1008ED4525 |
| _tools/AdminPasswordRecovery/IAdminPasswordRecoveryStore.cs | 314B163643ACB4D061619D268D30EACFD7B5EBE1B751AD25A3D3E6D41DD3C327 |
| _tools/AdminPasswordRecovery/AdminPasswordRecoveryResult.cs | 64BB3CA0C1DA27409BFFAC619908D2604651CCBAC4FDEDB5C508BDF9B2C36691 |
| SwiftExport.Application/Services/Auth/Passwords/BcryptPasswordHasher.cs | 3D3D2AD93855C9841FC8CA72D28B51F47BD08570AF907BDA271934564522B63A |
| SwiftExport.Application/Services/Auth/Passwords/PasswordPolicy.cs | 5195F115623544C15FEA9F8D1EBA5DF1622E5D154EB765315EF9E83AF9DC9B3F |
| SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlNames.cs | E2E52C228EFF31906DB1B0667988BA3317FD304A24471431CF7B80DC583D4F1D |
| SwiftExport.Infrastructure/Repositories/Base/PostgreSqlEntityMetadata.cs | 8D0E928B456E2C7261CDC7A7626414D3A398D28B92BF6DB68681D03CFF3B2A2F |

本结果及本人状态固定SHA在最终交付登记。源业务恢复资格/密码行为未调整；新增PG存储生产代码，未启用运行。真实PG/时区/最终SSI/连接释放与数据摘要仍主控验收前置，不放行Program切PG、SqlClient删除或部署。

