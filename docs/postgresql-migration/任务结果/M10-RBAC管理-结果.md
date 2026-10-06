# M10 RBAC 管理仓储结果

## 当前交付：M10-RANGE-REVIEW-01（只读范围语义评审）

- 开始：2026-10-05 02:02:06 +08:00；分析与只读验证完成：2026-10-05 02:06:08 +08:00。
- 结论：**当前冻结 ICU 普通排序不能保证原 Chinese_PRC_CI_AS 的任意 SQLServer LIKE 范围等价；没有已证明的最小通用修正可交付。** 不为两个反例加特判、不降原断言、不将57通过声称为全字符域兼容。
- 本轮只更新本结果/本人状态。仓储、两份测试、服务、接口、DDL和公共框架完全未改；未运行构建/测试，因本轮是只读语义评审，没有源码改动。未写数据库、提交、推送或启动子窗口。
- 主控新实库证据：59项 **57通过/2失败**，`m10-like-syntax-corrected-real-pg.trx`；42601已消失，仅 z/[a--c]、z/[a---] 源期望true、当前实际false。完整服务8项真实PG通过保持。这些均为主控结果，不算本窗口测试。

### 原真实列及目标比较器核实

只读 SQLServer `dv_next` 的 `sys.tables/sys.schemas/sys.columns/sys.types`，仅四个指定列元数据，无业务行读取：

| 原真实列 | 类型/容量 | 实际 collation |
|---|---|---|
| dbo.SysUser.UserName | nvarchar(500)，max_length=1000字节 | Chinese_PRC_CI_AS |
| dbo.SysUser.NickName | nvarchar(500)，max_length=1000字节 | Chinese_PRC_CI_AS |
| dbo.SysRoles.RoleCode | nvarchar(300)，max_length=600字节 | Chinese_PRC_CI_AS |
| dbo.SysRoles.RoleName | nvarchar(500)，max_length=1000字节 | Chinese_PRC_CI_AS |

因此原检索是 Unicode LIKE，不应拿 varchar/ASCII 的尾空格或码页行为替代。微软文档区分 Unicode 与 ASCII LIKE，并说明范围取决于 collation；CI_AS 为大小写不敏感、重音敏感，未指定 KS/WS 时相应比较不敏感。[SQLServer LIKE](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/like-transact-sql?view=sql-server-ver17)，[SQLServer collation 与 Unicode](https://learn.microsoft.com/en-us/sql/relational-databases/collations/collation-and-unicode-support?view=sql-server-ver17)。

PG故障库只读 `pg_collation`：`public.erp_dictionary_ci` 为 ICU（provider=105/i）、`und-u-ks-level2`、deterministic=false；记录版本和实际版本均153.128。当前无版本漂移证据，但版本一致不证明等同Windows权重或SQLServer匹配编译器。

### 有限常量探针与可靠结论

本轮限于一组35行常量矩阵（7字符×5模式）、一组6行上下文/普通比较对照、一个4值排序诊断；加四列元数据。PG仅1行collation元数据及6行普通范围/成员对照。没有全Unicode枚举、海量试错、业务数据扫描或表写入。

| 探针 | Chinese_PRC_CI_AS 源LIKE | Latin1_General_100_BIN2 对照 | 说明 |
|---|---|---|---|
| z、é、一 / %[a--c]% | 均1 | 均0 | 差异超出两个测试字面，不可硬编码只补z。 |
| c / %[a--c]% | 1 | 1 | c作为额外成员可被两边接受。 |
| -、!、空格 / %[a--c]% | 均0 | 均0 | 不能把异常范围扩大成任意字符。 |
| z、c、é、一 / %[a---]% | 均1 | 均0 | 连续标记与非二进制排序交互。 |
| - / %[a---]% | 1 | 1 | 末尾字面连字符仍参与。 |
| z、é、一 / %[a-!c]%、%[a- c]%、%[a-^c]% | 均0 | 均0 | 不支持“所有标点上界都视为开放范围”的推论。 |
| c / 上述三模式 | 均1 | 均1 | 仍是额外成员匹配。 |
| a、c、z、é、一 / %[a--]% | 均0 | 本组不重复BIN2 | 同一 a..- 片段，是否有组内后续内容改变结果；不能只拿端点普通比较解释。 |
| a、c、z、é、一、- BETWEEN a AND - | 源普通比较均0 | PG ICU普通比较也均false | 原LIKE为真而两个普通比较器都不能推导。 |

其他实际常量：源 `N'a-c' > N'ab'` 在 Chinese_PRC_CI_AS 为1，BIN2为0；源 `N'-' = N''` 为0、`N'a' < N'-'` 为0。PG6行对照的 `= 'c'` 仅 c 为true。因此“先删除-”“换成二进制”“把-统一移到字母之后”都不能自动推导完整语义。

可复现的最小只读源查询（不依赖业务行）：

```sql
SELECT v.s,
 CASE WHEN v.s COLLATE Chinese_PRC_CI_AS LIKE N'%[a--]%' THEN 1 ELSE 0 END AS no_tail,
 CASE WHEN v.s COLLATE Chinese_PRC_CI_AS LIKE N'%[a--c]%' THEN 1 ELSE 0 END AS tail_c,
 CASE WHEN v.s COLLATE Chinese_PRC_CI_AS BETWEEN N'a' AND N'-' THEN 1 ELSE 0 END AS ordinary_range
FROM (VALUES(N'a'),(N'c'),(N'z'),(N'é'),(N'一'),(N'-'))v(s);
-- 前五行 0/1/0；连字符行 0/0/0。
```

### 机制解释：事实与推断分开

**已证事实**：LIKE是模式编译与匹配，不是单纯字符串比较；连字符同时是范围标记及可处在首尾的成员。同一源collation内部，普通BETWEEN不等同LIKE范围；在这些有限样例上，改成BIN2会消失扩展匹配。原实现将组拆成若干普通端点区间和成员，丢失了源引擎中涉及连续标记/组内后续内容的匹配语义。

**文档可支持的背景**：Windows语言排序默认采用word sort，hyphen与apostrophe区别于其他标点；SORT_STRINGSORT改用另一位置关系。NLS排序键还依赖版本、组合元素及可排序字符。这解释了为何不能假设标点按Unicode码位排序，但**不等于证明SQLServer LIKE内部使用某个具体NLS标志或某个开放范围算法**。[Windows排序官方说明](https://learn.microsoft.com/en-us/windows/win32/intl/handling-sorting-in-your-applications)。

**仍是推断**：异常来自源字符组编译/匹配与连字符特殊权重共同作用，而非只有端点普通比较差异。上述同片段不同组内后续内容、BIN2对照支持这个方向，但未取得SQLServer内部编译器或可证明等价的权重提取接口。本轮查阅的官方LIKE文档没有为这些连续标记模式提供足以逐项复刻的算法；不能把推断写成已确认的内部实现。

PG18文档明确：LIKE仅%/_通配，非通配片段按collation比较；正则和SIMILAR TO不支持非确定性collation。因此换为正则/二进制或者只替换ICU locale，既不能解决源字符组解析又会影响现有CI/AS边界，不是等价的局部补丁。[PG18模式匹配](https://www.postgresql.org/docs/18/functions-matching.html)。

### 可评审方案与当前不可保证范围

1. **保持完整原契约**：需要源模式解析与范围成员关系的已证明模型，而不是只移植普通CompareString/ICU排序键。一个可研究的有限方案是：对经业务明确收敛的“有限整组模式集合+有限字符域”，以**整组模式**为键，用固定源版本LIKE oracle离线生成成员位图，PG递归只作成员查表并保留否定/位置语义。它不依赖长期SQLServer运行，但须先证明闭域、单字符/组合边界及版本固定；当前关键词任意、原列允许Unicode，没有这个授权或事实，不能直接上线。只按范围端点生成矩阵会漏掉上面的组内上下文差异。
2. **任意原模式/Unicode完全等价**：整组可能集合无现实的有限预计算覆盖；需要相当完整的源LIKE兼容实现及版本化Unicode/UTF16模型，超出当前局部仓储修正，也可能违反“不新通用框架”边界。换Windows本机NLS API也不能直接保证与原SQLServer未带版本号的collation及LIKE编译器一致；不提出长期连回SQLServer作为PG迁移方案。
3. **接受目标搜索规范变化**：若用户另行明确选择ICU范围语义、收敛允许的范围语法或字面关键词，需要主控提出具体前后行为并取得业务决策，再单独授权改有效断言/接口边界。本轮没有该决定，不能默认为已接受迁移差异。

建议当前继续保留两个原源期望失败及独立范围缺口登记，由主控/用户决定“完整兼容的新增实施范围”或“显式目标搜索规范”。本轮没有能证明完整等价的最小修正，不以常见57项通过关闭此缺口。

**未验证边界**：所有BMP/辅助字符、非SC UTF16代理对、组合音标与规范等价、字符扩展/收缩与多字符权重、其他汉字/假名/全半角、忽略字符/零宽字符、apostrophe范围、控制/未定义字符、非字符、任意连续标记组合、反组补集、模式长度上限和源/ICU版本升级。不通过少量探针扩大为这些全域验收。真实列无SC后缀及前轮emoji探针只作为风险线索，不宣称其完整代理对行为已经证实。

### 代码守卫与交付

本轮未写三份交付源码；只读记录SHA256：仓储 `F0D737C3E62B724D32F42D30E68702E2BE78BB42AF77330ED75C5F23FCB719F8`，离线测试 `19082549E66AE0F2577393775BF0707D074B8CE2F1974EA43E1429C00F4137D5`，实库测试 `0CF65A2445026C9BF6EB043A80B02C73B0561A438020C53F5937C19666722EB3`。未触碰其他窗口或主仓库。数据库仅上述只读SELECT，无DDL/事务占用或秘密输出。

结果先写，再更新本人唯一状态页停写；以下各轮证据保留为历史。

## M10-LIKE-FIX-01 历史交付（以下不是本轮最新状态）

### M10-LIKE-FIX-01（42601 语法回归修正）

- 开始：2026-10-05 01:54:06 +08:00；代码及只读验证完成：2026-10-05 01:57:36 +08:00。本轮运行完毕，交付主控串行实库验证。
- 主 Agent/工作流：Backend / 后端 API 工作流。沿用已读规则；不启动子 Agent。授权为主控本轮分派，允许 PG 故障库只读常量 SELECT/EXPLAIN，仍只写原五独占文件范围。
- 实际修改四文件：本仓储、本离线测试、本结果和本人状态；独立实库测试文件及其59项有效断言未改。没有改业务服务、接口、公共框架、DDL或主仓库，没有运行实库测试、写库、提交或推送。
- 主控外部反馈：主仓库89离线通过；新独立 RBAC 库服务8/8真PG通过，原 Forbidden/BusinessBlocked 两失败已闭合，因此本轮**完整保留首快照前角色目录锁**。搜索仓储59项14通过/45失败，`m10-like-lock-real-pg.trx` 的字符组路径统一42601 at COLLATE（普通1707/EXPLAIN1743）。反馈不计入本窗口通过数。

### 根因与精确修改

原范围条件 `BETWEEN (bounds.value ->> 'start') COLLATE ... AND (bounds.value ->> 'end') COLLATE ...` 在 PG 的 BETWEEN 边界语法下不可解析，所有字符组查询在执行前都失败，包含无范围的字符组也受固定 SQL 影响。本窗口在 PG 故障库只读常量 SELECT 复现相同 COLLATE 语法错误，修正后同一常量返回 true。

唯一生产表达式修改为 `BETWEEN ((bounds.value ->> 'start') COLLATE public.erp_dictionary_ci) AND ((bounds.value ->> 'end') COLLATE public.erp_dictionary_ci)`，即括住包含 COLLATE 的整个边界。补中文原因说明；不修改解析/字符组范围含义、COUNT/分页事务边界或任何锁。离线追加精确表达式防回归检查，原断言全部保留。

### 实际完整 SQL 的只读 PG 验证

离线测试的记录连接捕获 **实际 SearchUsersAsync/SearchRolesAsync 发出的 COUNT 和分页四条完整命令及其参数**，以 `M10_PG_PLAN:` JSON 输出；不是手写简化谓词。固定测试关键词包含三组（正集合、反范围、左括号）、%/_、反斜杠及单引号/分号攻击文本，全部仍作为参数值。诊断时按 PG 字符串双单引号规则绑定固定参数，再仅加 `EXPLAIN (FORMAT TEXT)` 前缀，通过 DBX 在 `192.168.3.12:5432/dv_next_pg_failure_tests_20261004` 读取计划。

| 实际命令 | 只读结果 | DBX报告耗时 |
|---|---|---|
| 用户 COUNT | 30行计划；Aggregate、Recursive Union、WorkTable Scan、JSON成员/范围与尾部LIKE全部成功解析。 | 3ms |
| 用户分页（含完整IsAdmin投影） | 40行计划；Limit、Sort、递归匹配及管理员关联子计划成功解析。 | 4ms |
| 角色 COUNT | 30行计划，完整递归与JSON范围成功解析。 | 3ms |
| 角色分页 | 32行计划，完整递归、原排序和LIMIT/OFFSET成功解析。 | 3ms |

没有 EXPLAIN ANALYZE，因此以上是计划读取耗时，**不是实际搜索执行耗时或性能验收**。计划估算两个 COUNT 总 cost 46707.14..46707.15、递归81状态，仅为优化器估算，不把成本或估算状态当成真实运行性能证明。

另将上述两条实际 COUNT 的 FROM 替换为一条固定 VALUES 常量行，完整 WHERE/CTE/JSON/递归匹配表达式保持不变；候选 `x'AqZ\[];DROP TABLE sys_user;--`，第二列 NULL。用户/角色只读 SELECT 分别 count=1，DBX各报告2ms，验证递归与JSON表达式不只是可解析而且可执行。攻击文本只存在于引用完整的常量和JSON值，未执行任何DDL。这项常量诊断不是原59项真实实体场景的替代。

### 编译、边界与后续

清除三个 PG 执行标志后，执行 `dotnet test ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter '(FullyQualifiedName~PostgreSqlRbacManagementMigrationTests&TestCategory=PostgreSqlRbacOffline)|FullyQualifiedName~SwiftExport.Application.Tests.Authentication.RbacManagementServiceTests' --logger 'console;verbosity=detailed'`：**66项通过（58仓储离线+8既有服务），0失败/跳过，总时间0.5926秒**；两份独立测试均编译，7条既有MSTest警告，新代码无警告。必要详尽输出用于取得四条实际捕获SQL；本窗口无实库测试运行。

特殊标点 `[a--c]`/`[a---]` 的源真值缺口仍按前轮保留，59项实库有效断言不更改、不跳过。普通字符组修正后是否全部通过仍需主控复跑；本窗口只证明42601根因和完整 SQL 解析/常量执行成功。首快照锁8/8服务实库证据属于主控，不回退也不重复运行。未输出连接字符串、用户名密码；没有数据库写入/初始化/清理或占用事务。

主控按精确diff接回并串行实库；本窗口先写此结果，再更新唯一状态停写。下面保留前轮66与历史38等证据，均为当轮结果，不累加覆盖数。

## M10-LIKE-01 历史交付（以下不是本轮最新状态）

### M10-LIKE-01 与首快照前角色目录锁

- 开始：2026-10-05 01:31:51 +08:00；完成代码与离线验证：2026-10-05 01:44:57 +08:00。
- 状态：本轮代码交付待主控实库验证；完整 LIKE 兼容目标未完成，特殊标点范围存在明确反例，不得按全兼容合入或验收。
- 主 Agent/工作流：Backend / 后端 API 工作流。沿用已读 Dapper、数据库会话、认证和后端质量门禁规则；不启动子 Agent。
- 授权：主仓库 `RBAC搜索方言补齐分派.md`，及主控关于源库常量只读探针、首条角色目录锁的补充分派。仍仅修改本仓储、自己的两份独立测试、本结果和本状态五文件。
- 修改了 Infrastructure 持久化搜索及并发锁行为，没有修改业务服务、接口、Contracts、DI、公共框架、DDL、主仓库或其他窗口文件。没有提交、推送、部署。

### 优先交付的并发修正

`LockAndCheckAdministratorAsync` 在 `RequireTransaction` 后、任何本方法 SELECT/DML 前，单独执行 `LOCK TABLE "public"."sys_role" IN SHARE ROW EXCLUSIVE MODE`，借用外层事务与取消令牌，再维持内置角色行锁→actor行锁→关系查询顺序。锁持续至调用方事务结束，同时阻止并行 RBAC 管理事务及角色目录写入；不是全局业务锁，不改隔离级别、不重试、不盲映 40001。

调用方必须在该 Serializable 事务首次 SELECT/DML 前调用。已经建立的快照无法靠晚到的表锁刷新，仓储也无法仅靠当前接口检查快照是否已创建。依据 [PostgreSQL 18 LOCK 官方说明](https://www.postgresql.org/docs/18/sql-lock.html)：RR/Serializable 首次查询或写入冻结快照，表锁必须在此前执行；有写入时应使用 SHARE ROW EXCLUSIVE 避免 SHARE 升级冲突。

离线保留原短路和成功断言，新增首命令完全匹配、无查询参数、同事务、取消令牌传递及首锁取消/失败不继续查询、不回滚调用方事务。原预建旧快照的互撤实库场景仍保留，不将新表锁声称可刷新已有快照。

主控外部反馈：前版仓储实库 12 通过/1 失败（仅原字符组断言）；新服务独立库 8 项中 6 通过/2 失败，自撤和互撤返回 Unexpected 而非原业务返回。上述为主控证据，本窗口不计入自己的通过数；本轮首锁修正尚未运行主控服务实库，不能声称已修复这两个返回结果。

### 字符组局部实现与明确剩余缺口

无左括号保持 ICU LIKE 快路径。存在左括号时，私有解析器将每组前 LIKE 片段、正/反集合、范围及末尾片段作为 JSON/文本参数传入；固定 SQL 使用关联递归 CTE，状态仅为组序号和消费位置，UNION 去重。每步消费一组，状态数至多 `(组数+1)*(字符数+1)`，候选枚举最坏仍为组数乘字符数平方；有界不等于大数据性能已验收。没有正则、SIMILAR TO、公共匹配框架、DDL、全表取回内存或集合笛卡尔展开。

保留 NULL 不匹配、Trim、%/_通配及反斜杠普通字符；未闭合和空正集合不回退成字面，空反集合消费一个字符。两分页 COUNT 与数据读取继续独立命令，复用同一谓词和参数、同一已有事务，不新增 Npgsql 批次隐式事务。请求文本不进入 SQL，列名仅来自仓储固定调用。

本窗口通过 DBX 只读 SQLServer `dv_next` 的常量 SELECT 探针核对 `Chinese_PRC_CI_AS`，无表数据读取或写入。除主控既有样例外，实际确认：

| 字符/模式（均外包 %） | 源库真值 | 处理/验收边界 |
|---|---|---|
| d / [a-c-e]；e / [a-c-e] | 0；1 | a..c 后中间 - 非字面，e 是独立成员，不能误扩成 c..e。 |
| d / [a-b-c-e] | 1 | 还包含第二个 c..e 范围。 |
| - / [a-b-c]；c / [a-b-c] | 0；1 | 原主控要求的连续范围样例保留。 |
| c / [a-b--cd]；d / [a-b--cd] | 0；1 | 范围后连续 - 消费 c，d 保留；已补局部解析及验收断言。 |
| c / [a-b---cd]；d / [a-b--c-e]；e / [a-b--c-e] | 1；0；1 | 进一步直接核对，不靠猜测中间连字符字面化。 |
| - / [a-b---]；- / [a-b--] | 0；1 | 中间连续标记和末尾字面 - 分开。 |
| z / [a--c]；z / [a---] | 1；1 | **未解决的源 LIKE 特殊标点范围**；当前 ICU 普通范围方案不能证明相同。 |

最后反例可只读复现：

```sql
SELECT
  CASE WHEN N'z' COLLATE Chinese_PRC_CI_AS LIKE N'%[a--c]%' THEN 1 ELSE 0 END AS like_range,
  CASE WHEN N'z' COLLATE Chinese_PRC_CI_AS BETWEEN N'a' AND N'-' THEN 1 ELSE 0 END AS ordinary_range;
-- 实际返回 1、0。
```

解析该组的普通范围 a..- 加字面 c，无法仅通过普通 collation BETWEEN 推导出源 LIKE 真值。当前 ICU 为冻结 `und-u-ks-level2`，本窗口不得修改 DDL/公共比较边界，也没有源 LIKE 范围权重映射；因此不以猜测或特判宣称完整 Unicode/标点兼容。实库 `SearchPunctuationRanges_KeepSourceExpectations` 两断言仍要求源库真值，**没有删除、跳过或改成当前实现结果**。它们由主控判定；本轮不声称能通过。另探得源非 SC 排序的 emoji 通配存在边界，普通 Unicode/代理对全域等价也未验收，不扩大本轮修改。

### 当前实际验证与待执行场景

- 编译与最终离线：清除三个 PG 执行标志后，执行 `dotnet test ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter '(FullyQualifiedName~PostgreSqlRbacManagementMigrationTests&TestCategory=PostgreSqlRbacOffline)|FullyQualifiedName~SwiftExport.Application.Tests.Authentication.RbacManagementServiceTests' --logger 'console;verbosity=minimal'`。
- 最终 **66 通过（58 仓储离线 + 8 既有服务），0 失败/跳过，187ms**；本轮两份测试全部编译。7 条既有 MSTest 警告保留，新代码无编译警告。本轮无需提升权限即可完成编译。中间 58、64 是各阶段通过数，不累加。前轮最终38证据保留于下方历史区。
- 静态检查：实际仓储 diff 仅两搜索入口、私有局部解析/谓词及管理员首锁；其余17方法行为未改。`git diff --check` 通过；未跟踪的三份源码另检查 UTF-8 U+FFFD 和尾空白均为0。
- 独立实库类现在 **13 方法、59 展开场景**：前版13保留；新增41字符组样例（每样例用户/角色、count/page及第二页），2标点范围源真值缺口，1 NULL/空白边界及2查询计划场景。原 `SearchBracketClass_ExposesSqlServerContractGap` 原断言完整保留，应由基础实现修正后通过。
- 新实库默认只在原随机聚合夹具事务内回滚，无新增提交/清理或既有表写入范围。新增 `SearchPlans_RecordFastPathAndMultipleGroups` 用反射取得**仓储实际**私有谓词、参数及投影，对无括号和六组模式分别运行 COUNT/分页 EXPLAIN ANALYZE BUFFERS，十五秒命令上限，计划与墙钟写 TestContext/TRX；**代码已编译，未运行，没有真实 PG 计划/耗时证据**。
- 本窗口不运行 PG，不连接/初始化/清理 PG；仅按补充授权运行源 SQLServer 常量只读探针。未输出连接字符串或秘密。

下一步由主控精确合入首锁修正并串行运行自己的8项服务验收；局部 LIKE 实现须另审，完整字符组/特殊标点差异及计划性能未通过前不标完整迁移成功。本窗口按最新分派先交付并停写；结果先写、后更新本人唯一状态页。

## M10-IMPL-01 历史交付与当时证据（以下不是本轮最新状态）

- 轮次/状态：M10-IMPL-01，代码完成待主控验证；查询方括号LIKE兼容缺口保留，不宣称完整查询契约验收。
- 窗口/负责人/真实ID：PostgreSQL执行A：登录与账号 / 执行A / 01a10790-07d0-7a61-91e3-ed83ca6e34d4。
- 工作目录：C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport；基线56209d97a00533c938dd527fa9d37683064a11e8，detached HEAD。
- 主仓库当前HEAD由派工确认40d166；本窗口不重置、切分支或复制公共改动。
- 开始：2026-10-05 01:06:06 +08:00。授权依据：主仓库 docs/postgresql-migration/RBAC实施分派.md、窗口协调.md及本轮主控两次补充。
- 本轮主Agent/工作流：Backend / 后端API工作流；应用Dapper、认证、事务/上下文、职责组织和后端质量门禁技能。
- 开始时已有修改：M09仓储、两份M09测试、M09结果及执行A状态均未跟踪；完整保留，不在本轮改M09文件。

## 精确交付文件和写入边界

本轮新增四份文件，更新自己的状态，共五份：

1. SwiftExport.Infrastructure/Repositories/Auth/Permissions/PostgreSqlRbacManagementRepository.cs。
2. SwiftExport.Application.Tests/Database/PostgreSqlRbacManagementMigrationTests.cs。
3. SwiftExport.Application.Tests/Database/PostgreSqlRbacManagementMigrationTests.Integration.cs。
4. docs/postgresql-migration/任务结果/M10-RBAC管理-结果.md。
5. docs/postgresql-migration/窗口状态/执行A.md。

十九方法全部有实现；不改原仓储、Core接口、服务、Contracts、DI、公共参数/映射/锁、DDL、主清单或其他窗口文件。新增Infrastructure持久化代码，没有扩展业务功能或更改服务业务。未提交、推送、部署，未运行共享数据库。

## 当前主源码和调用依据

本轮重新读取主仓库 IRbacManagementRepository、SQL Server RbacManagementRepository、RbacManagementService、现有RbacManagementServiceTests，并读取Contracts RBAC DTO、实体和元数据。原仓储及接口与worktree逐行Compare-Object均无差异；原仓储SHA256不同是字节格式差异，没有把旧报告当当前事实。

- SwiftExport.Core/Interfaces/Auth/Permissions/IRbacManagementRepository.cs:7：十九方法、调用方事务、聚合锁与版本契约。
- SwiftExport.Infrastructure/Repositories/Auth/Permissions/RbacManagementRepository.cs:13–307：原查询筛选、通配、顺序、版本、软删、审计及事务门禁。
- SwiftExport.Application/Services/Auth/Permissions/RbacManagementService.cs：UpdateUser/SetStatus/ReplaceUserRoles、Create/Update/DeleteRole和ReplaceRolePermissions实际消费者；删除角色先锁目标、核查CountRoleUsers；用户停用/撤管理员角色先校验CountActiveAdministrators；权限可分配性来自目录。
- WebApi/Controllers/Systems/RbacUsersController.cs、RbacRolesController.cs、RbacPermissionsController.cs：用户/角色管理HTTP和只读权限目录，不改路由。
- SwiftExport.Contracts/DTOs/Auth/Permissions/RbacManagementDtos.cs：管理DTO不含密码，整组保存以目标Version为依据。

已只读主控本轮最新服务：WriteAsync生成非空CommandId，经可选ICommandTransactionPreparation准备租约后开始原Serializable；AuditAsync使用该同一CommandId，事务清理后释放租约。主控另报21离线及5真实审计租约通过，本窗口仅引用，不合并为自己的数量。worktree仍保留旧服务/审计实现，没有复制主控公共文件；本窗口服务回归针对本worktree既有8个场景，不声称证明主控新P04路径。

## 十九方法实现矩阵

| 方法 | PostgreSQL实现及保留语义 |
|---|---|
| SearchUsersAsync | 独立COUNT与DTO查询，未删域含停用，Keyword空白变null/Trim，状态可空；ICU LIKE保留%/_、ESCAPE ''保留反斜杠字面；用户名rtrim+ICU、Uid稳定排序。 |
| GetUserAsync | 明确非凭据列及IsAdmin投影，未删筛选，不加更新锁。 |
| GetUserForUpdateAsync | 已有事务内FOR UPDATE OF u，仅锁目标聚合。 |
| GetUserRoleIdsAsync | 未删关联、活动未删角色，按RoleId排序。 |
| SearchRolesAsync | 独立COUNT与DTO查询，含停用；编码/显示名ICU LIKE，编码rtrim+ICU、Uid排序。 |
| GetRoleAsync | 完整管理DTO，未删筛选，普通读无锁。 |
| GetRoleForUpdateAsync | 已有事务内FOR UPDATE OF r。 |
| GetActiveRolesAsync | 单uuid数组ANY，活动未删，按Uid稳定共享锁FOR SHARE；空集合不发SQL。 |
| GetRolePermissionIdsAsync | 有效关系、活动未删权限，按PermissionId排序。 |
| GetPermissionsAsync | 未删目录含停用项，SortOrder及PermissionKey的CI排序；IsAssignable仅从代码目录计算。 |
| GetActivePermissionsAsync | 单uuid数组、活动未删、按Uid共享锁FOR SHARE；空集合零命令；目录控制可分配性。 |
| LockAndCheckAdministratorAsync | 要求事务；独立命令先锁活动未删且IsSystem的SYSTEM_ADMIN角色FOR UPDATE，再锁活动未删actor FOR UPDATE，再读有效关联。 |
| CountActiveAdministratorsAsync | 事务内COUNT DISTINCT有效用户；保留原RoleCode规则，不擅加IsSystem条件。 |
| UpdateUserAsync | 预期Version、未删条件；资料模式可清空邮箱电话，启停模式保留资料；Version+1，不改CredentialVersion。 |
| ReplaceUserRolesAsync | 先Version+1，失败原异常；旧关系软删且Version+1，再整组新增；空集合清空；外层负责回滚。 |
| CreateRoleAsync | 生成Uid、活动普通角色、初始Version和审计；唯一失败上抛。 |
| UpdateRoleAsync | 不改RoleCode，保留描述/状态分支，普通未删Version匹配；软删和Version+1；拒绝内置角色更新。 |
| CountRoleUsersAsync | 所有未删关联COUNT，停用用户的关系仍阻断删除，不新增隐式事务。 |
| ReplaceRolePermissionsAsync | 普通未删角色的Version门禁，旧关系软删，再插入整组；空集合清空，失败上抛外层。 |

所有命令传递当前Session.Transaction和取消令牌；审计时间显式timestamp，UUID数组及实体插入复用现有PostgreSqlParameters/Metadata。可空布尔及文本表达式显式CAST，避免PG未定类型参数。无自行提交/回滚、自动重试、降级隔离或全局业务锁。

主控预审修正：初稿分页沿用QueryMultiple count+data，Npgsql会在没有外层事务时将batch作为隐式事务，与源SQLServer autocommit逐语句边界不同。两方法已改成ExecuteScalarAsync<int>再QueryAsync<DTO>，同参数/token/当前事务；无外层仍逐语句，有外层Serializable仍同一事务。离线测试新增两命令顺序、相同参数和有/无事务覆盖，保留全部业务断言。

## 验证证据（本窗口无数据库）

所有命令执行前清除进程SWIFTEXPORT_RUN_PG_TESTS、SWIFTEXPORT_RUN_PG_FAILURE_TESTS、SWIFTEXPORT_RUN_PG_COMMAND_TESTS；不打印或持久化连接配置。编译使用已授权require_escalated解决已知worktree输出权限限制，复用既有NuGet assets，没有修改项目或清构建目录。

| 版本/实际完整命令 | 结果 |
|---|---|
| 初版仓储：dotnet test ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~PostgreSqlRbacManagementMigrationTests&TestCategory=PostgreSqlRbacOffline' --logger 'console;verbosity=minimal' | 28离线通过，0失败/跳过，147ms。 |
| 实库场景首次编译：dotnet test ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter '(FullyQualifiedName~PostgreSqlRbacManagementMigrationTests&TestCategory=PostgreSqlRbacOffline)\|FullyQualifiedName~SwiftExport.Application.Tests.Authentication.RbacManagementServiceTests' --logger 'console;verbosity=minimal' | 36通过（28新+8既有），0失败/跳过，175ms；实库不运行。 |
| 追加角色删除/分配竞争，同一完整命令 | 36通过，0失败/跳过，180ms；新增场景编译成功。 |
| 最终分页修正版，同一完整命令 | 38通过（30新+8既有），0失败/跳过，175ms；两独立测试文件完整编译。 |
| git status --short；git diff --check；十九public方法静态扫描；新增源码独立编码/尾空白检查 | 仅授权新增及前轮保留文件；未改跟踪文件；无U+FFFD和尾空白。git diff --check不覆盖未跟踪文件，另扫描本轮新增内容。 |

Markdown转义竖线的反斜杠不是实际过滤器参数。输出在聊天工具结果，未创建TRX日志。7条既有MSTEST0017/0032/0044编译警告保留；本轮新增文件没有编译警告，未运行全量或历史GetTraceAsync。上述28/36/38是各版本实际数量，不累加为覆盖数；最终30仓储离线+8服务=38。

30离线场景覆盖十九方法DTO映射、null、通配参数、事务及取消、role→actor锁顺序和短路、共享锁/UUID数组、目录IsAssignable、计数口径、版本与审计、整组替换/清空/陈旧版本异常、普通角色创建及唯一错误上抛。字符串/模拟证据不等于真实SQL/锁安全。

## 实库场景和精确写入清理范围（仅编译）

独立类PostgreSqlRbacManagementMigrationIntegrationTests：9方法、展开13场景，DoNotParallelize，唯一入口GetFailureConnectionString，限定192.168.3.12:5432/dv_next_pg_failure_tests_20261004。本窗口未运行其中任何场景，无共享库占用。

场景包括十九方法功能/DTO、资料/状态/软删、整组替换清空、重复关系23505后整体回滚及跨连接核对、双管理员互撤时旧快照拒绝或40001、角色/权限共享锁和目标更新锁实际阻塞、回滚释放、角色删除提交/回滚与分配校验竞争、提交后新连接读取新Version。通过pg_blocking_pids确认等待，有10秒观察/15秒命令上限；不以sleep假称锁已等待。捕获40001只作明确并发结果，其他PG错误不吞掉。

13中含1个PostgreSqlLikeCompatibilityGap分类，SearchBracketClass_ExposesSqlServerContractGap故意保留SQLServer期望：[Aa]字符组匹配专用Alpha昵称。PG原LIKE不会匹配，预期暴露契约失败；不能改为字面匹配、删除断言或记跳过来伪造迁移通过。主控可单独筛选该场景，其他12场景也仍待实测。

默认Fixture在Serializable事务内创建并finally回滚。跨连接竞争/失败回滚观察/提交释放场景先提交随机夹具，仅以下对象：public.sys_user的Actor/Target两随机Uid，public.sys_role的Role随机Uid，public.sys_permission的Permission随机Uid，及属于这两个UserId的sys_user_role、属于该RoleId的sys_role_permission全部本场景新旧关系。关系替换的Uid由仓储生成，清理限定这三个已知聚合Uid，不按名字或全表清理。finally先关系后实体删除、同事务提交，再核对五表残留为0。内置SYSTEM_ADMIN只读借用/事务锁定，不编辑/删除其内容；不触碰既有账号、权限、备份或不可变审计表。

Seed或竞争失败同样finally清理，清理异常作为失败保留。主控运行前须审核这个提交/清理范围，记录前后全字段摘要、结构/序列及锁释放。测试代码中的断言尚未运行，不能充作残留清理成功。无初始化、清库、DDL、备份恢复或实库摘要证据。

## 已登记迁移缺口、并发风险和建议验收

1. **M10-LIKE-01：已知查询契约缺口**。PG LIKE保留%/_/反斜杠，却不支持SQLServer []/[^]字符组；主控已明确要求保留缺口和真实展示，禁止新公共正则框架或DDL。查询未完整验收，后续决定由主控持有。
2. **M10-SSI-01：固定快照风险**。服务保持Serializable，内置角色锁等待不会刷新已建立快照；actor/目标FOR UPDATE及SSI可能返回40001，不能写成读到最新授权或完全等价锁式Serializable。仓储不改错误码、降级或自动重试；互撤场景只接受拒绝或40001，主控应记录映射和最终关系。
3. **M10-LASTADMIN-01：最后管理员实库缺口**。已有服务替身覆盖最后一位拒绝，本窗口不通过停用既有管理员构造实库条件。建议主控在另行获准的仅1–2有效管理员隔离夹具中，跨连接并发停用/撤销最后两人，核对至少一人保留、双命令结果及40001/提交失败全回滚；当前库管理员存在不能假称已覆盖该边界。
4. **M10-ROLE-01：角色删除/分配**。已有角色FOR UPDATE与活动角色FOR SHARE竞争代码；还应主控通过真实服务交错删除（CountRoleUsers=0）和分配，保证没有成功分配删除角色，验证空关系谓词/SSI与业务拒绝、Version及40001。
5. **M10-PERM-01：权限停用/分配**。已有随机定义共享锁阻止另一连接停用的场景；随机键不在代码目录，不可拿它声称服务IsAssignable端到端通过。建议主控在合法可分配目录夹具条件下交错显式同步停用与角色保存，记录共享锁、旧快照40001/校验拒绝和权限投影，不改现存权限来方便测试。
6. **M10-AUDIT-01：服务与审计原子性**。只读确认主控新准备租约方案，本窗口测试仓储中途唯一失败回滚；主控还需合入后执行新服务同CommandId审计、准备失败/取消、BeginTransaction失败及审计失败的端到端回滚。不得为了清理测试删除不可变审计日志；本窗口没有写服务成功审计的提交场景。
7. **M10-CI-01：比较范围仍需实库核对**。查询显式沿用既有ICU、排序尾空格处理，未改角色/权限唯一索引。主控需核对源角色/显示文本/权限列比较规则、空值和重音/宽度/尾空格边界，不能把用户名P03证据扩大为所有字段实测。

未发现需要本窗口修改公共接口、服务、DDL或锁核心的实施阻塞。已知查询缺口按主控决定保留；并发/服务/运行验收未通过前保持“代码完成待验证”。DI仍由主控接线，生产未切换，未改变业务权限目录。

## 主控复核与后续

结果先写，再更新执行A状态停写。主控按精确文件合入并串行实库；本轮不改主清单。接口/锁/事务/映射范围、当前diff、查询缺口接受与完整验收状态由主控判定。唯一状态页为C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport/docs/postgresql-migration/窗口状态/执行A.md，不维护主仓库初始化副本。
