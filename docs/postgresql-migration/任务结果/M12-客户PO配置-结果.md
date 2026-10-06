# 最新补验修复：M12-FIX-01

- 执行 B / 01a10790-0ef8-7663-ab9b-aeee9f7f1c0d；基线及 worktree 不变。
- 开始：2026-10-05 00:43:32 +08:00；记录：2026-10-05 00:44:27 +08:00。
- 状态：代码完成待主控复验；本窗口未执行实库。
- 据主控本轮通知：合入后 54 离线通过，PG 首轮本类 11 场景通过，DisabledCurrencyReference_IsPreserved 因无停用币种未运行；首轮 50 表摘要与基线相等，无数据残留。仅引用主控结果，不当成本窗口执行证据。
- 本轮仅更新自己的 .Integration.cs 中该场景及本结果、本状态三文件；上轮仓储、离线测试和其他集成方法均未修改，未写主仓库或 A 窗口文件。

该场景不再查询既有停用币种或因环境缺夹具 Inconclusive。改为本例既有 InRollback 事务内通过泛型 PG 仓储新增随机 SysDicCurrency，CurrencyCode 为 P 加随机 GUID 前 9 字符共 10 字符，IsEnabled=false、IsBase=false、IsSystem=false、Precision=2、RateToBase=1m。读取确认未启用/非本位，再创建 PO 引用该码并验证 DTO 保留该码。原 InRollback 的 finally 回滚两个随机实体，无提交、无既有币种修改、无结构变化；根测试环境的专用库启用和目标防线保持。

只读核对 Core 实体、EntitySchemaMetadata 和 PG 生成基线：币种码 varchar(300)，PO 币种码 varchar(10)，precision 检查 0..8，rate_to_base 为非空 numeric(18,6)，version>=1；新增未删除币种唯一索引按编码，随机短码适用，正汇率 1 不引入本位币变更。

实际验证命令（执行前本进程三个 PG 启用标志均清除）：

```powershell
$env:SWIFTEXPORT_RUN_PG_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_FAILURE_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_COMMAND_TESTS=$null
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~PostgreSqlRepositoryTests.PoConfig_' --logger 'console;verbosity=minimal'
```

结果：测试项目及改后集成文件编译成功，11 通过/0 失败/0 跳过；只运行离线用例，不将该结果记作改后实库场景通过。仅既有 MSTest 警告。另执行 git diff --no-index 对比主仓库合入的 .Integration.cs 与本文件，差异仅该场景及中文说明；退出 1 表示有预期 diff，不是编译失败。逐文件 no-index --check、编码/尾部空白及范围核对后停写，完整输出在聊天工具记录。

主控后续复验仅需该场景，串行专用库，新增/回滚表为 sys_dic_currency 与 customer_po_config；不得改现有币种。测试前后全字段/结构/序列摘要及取消/异常回滚残留仍由主控核验；本窗口没有数据库占用、没有新增实库记录，没有提交/推送/部署，未改变生产代码。

下文 M12-IMPL-01 的“只读选择既有停用币种/缺少即未运行”是上轮历史说明，已由本轮随机夹具替代；其余上轮交付和证据保持。

# M12 客户 PO 配置实施结果

## 本轮交付：M12-IMPL-01

- 窗口/身份/目录/基线：执行 B，01a10790-0ef8-7663-ab9b-aeee9f7f1c0d；C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport；56209d97a00533c938dd527fa9d37683064a11e8，detached HEAD。
- 本轮开始：2026-10-05 00:25:47 +08:00；结果记录时间：2026-10-05 00:36:35 +08:00。
- 授权依据：主控派工 M12-IMPL-01 与主仓库 docs/postgresql-migration/首组实施分派.md。已读主仓库现行 P01-P03 结果、协调协议、结果模板；公共 API/会话/异常/映射/夹具冻结。
- 开始时已有修改：上一轮本结果及执行 B 状态页未提交，均保留，无其他源码改动。
- 状态：代码完成待主控验证；未连接真实 PG、未接入运行。主控提供的锁翻译决定已落实，本窗口未改服务规则。

实际新增/更新恰好以下五个文件（相对上述绝对目录）：

1. SwiftExport.Infrastructure/Repositories/Customers/PoConfigs/PostgreSqlCustomerPoConfigRepository.cs。
2. SwiftExport.Application.Tests/Database/PostgreSqlCustomerPoConfigMigrationTests.cs。
3. SwiftExport.Application.Tests/Database/PostgreSqlCustomerPoConfigMigrationTests.Integration.cs。
4. docs/postgresql-migration/任务结果/M12-客户PO配置-结果.md。
5. docs/postgresql-migration/窗口状态/执行B.md（唯一实际状态入口，前轮主库写入拒绝后按协议回退）。

修改基础设施仓储源码和新增测试，未修改 Application 业务服务、Core/Contracts/API、DI、SQL Server 实现、客户/供应商/币种仓储、公共夹具、生成器或 DDL。未提交、推送、部署。旧 worktree 的索引生成基线不更新、不复制；主控三索引已同步的事实继续有效。

### 实现行为与锁边界

六方法已实现，继承 PostgreSqlDapperRepository<CustomerPoConfig> 九个泛型方法。完整 snake_case 投影使用元数据及 CLR 别名；文本集合显式 Npgsql text 数组，空集合不执行 SQL；查询始终传调用方 Transaction。

UID 优先且 Guid.Empty 不当 null；仅缺 UID 时按冻结的源编码等值规则解析，双方候选各 count(*) OVER()=1 才一次性整体回填当前编码，失败不部分修改实体。可空 UUID/text 参数在 SQL 中显式 CAST。保持未删除/停用差异。三个内存组合检查继续各自的 Trim、大小写、软删和逐项自身排除口径，未统一旧校验；分页保留 50/500、原 SqlLikePattern.Build、稳定 CreatedDate/Uid 降序及日期 NULLS LAST。

withUpdLock=false 仅查询，不锁表；true 必须已有 ReadCommitted 或 ReadUncommitted 事务，先 LOCK TABLE public.customer_po_config IN SHARE ROW EXCLUSIVE MODE，再发独立完整 DTO SELECT，两个命令均传取消令牌和同一事务。无事务及其他隔离级别明确拒绝，不降隔离、不重试、不自行提交/回滚或释放事务。使用单 PO 配置表锁保护空结果，**采购提交期间 PO 配置维护写入和锁定读取会串行**，这是主控批准的较宽锁粒度；普通预览不占该锁。

未发现原生产调用链在同一事务先写配置再升级表锁；本次没有新增生产消费者。真实锁兼容/默认事务/取消清理/吞吐效果仍由主控实库验收，不把离线字符串断言写成实库锁证明。

### 本轮实际验证

所有 dotnet test 命令前均只清除当前命令进程的三个启用标志，不输出凭据：

```powershell
$env:SWIFTEXPORT_RUN_PG_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_FAILURE_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_COMMAND_TESTS=$null
```

| 实际完整命令/阶段 | 实际结果 |
|---|---|
| dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~PostgreSqlRepositoryTests.PoConfig_' --logger 'console;verbosity=minimal' | 首次编译失败 CS0103：新测试缺 SqlLikePattern 命名空间；补自己的 using 后同命令 11 通过/0 失败/0 跳过。 |
| dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~PostgreSqlRepositoryTests.PoConfig_\|FullyQualifiedName~CustomerPoConfigDefaultsContractTests\|FullyQualifiedName~PurchaseDraftHeaderMappingTests' --logger 'console;verbosity=minimal' | 新增集成文件后编译失败 CS9035：新 Customer 夹具漏 required DocRelateNo；只修本测试夹具，同时修自己的 nullable 警告。 |
| 上行同一完整命令（实际 OR 字符不带反斜杠） | 修正后 18 通过/0 失败/0 跳过；后续补停用币种实库场景后，再次编译并同范围 18 通过/0 失败/0 跳过，最终版本对应最后一次结果。 |
| git diff --check；git -c core.quotePath=false status --short --untracked-files=all；逐一读取五个新增/更新文件检查替换字符、尾部空白；rg -n 'LOCK TABLE\|BeginTransaction\|Commit\|Rollback\|ON CONFLICT\|Execute\|Query' SwiftExport.Infrastructure/Repositories/Customers/PoConfigs/PostgreSqlCustomerPoConfigRepository.cs | 范围恰为五个授权文件；新增文件另查编码和空白通过（diff --check 不覆盖未跟踪文件）。仓储只读锁及查询传当前事务，无事务创建、提交、回滚代码。另对五文件执行 git diff --no-index --check -- NUL，发现结果文档 EOF 多余空行后已修正；LF/CRLF 提示不属于编码损坏。 |

18 项包含 11 个新离线场景（分页两行数据用例计两项）、5 个既有 CustomerPoConfigDefaultsContractTests、2 个既有 PurchaseDraftHeaderMappingTests。未累加重复运行次数。dotnet test 执行了 Infrastructure、Application、WebApi 及测试项目必要编译，0 编译错误；最终输出仅既有 MSTEST0017/0032/0044 警告，不修改其断言或无关源码。复用现有还原资产，无全量测试、无单独重复全后端 build、无实库执行；原始命令输出保留在本聊天工具记录，没有生成 TRX。

### 实库场景代码及主控执行前须审查的范围

集成类为 PostgreSqlCustomerPoConfigMigrationIntegrationTests，类别 PostgreSqlCustomerPoConfigIntegration，DoNotParallelize；通过既有 GetFailureConnectionString 严格限定 192.168.3.12:5432/dv_next_pg_failure_tests_20261004。窗口未启用该类别。共 9 个方法、12 个参数展开场景，只编译不运行：

- 身份优先、当前编码回填、无 UID 按编码、Guid.Empty、删除/停用边界。
- 九个继承方法、审计、完整 DTO、Version、软/硬删除。
- 三组合检查差异、交叉组合、已删除记录、分页通配字面匹配。
- INSERT/UPDATE 大小写及尾空格冲突、NULL 组合索引冲突（保存点隔离预期错误）。
- 已停用币种旧引用保留：只读选择既有停用币种，缺少时标记未运行，不修改币种。
- 缺失组合锁阻塞 INSERT，提交/回滚释放；普通预览不阻塞。
- 等待既有写事务结束后，锁定查询看到刚提交新配置。
- 现存配置锁阻塞 UPDATE/DELETE；唯一竞争等待胜者提交并返回准确 23505/PO 索引。

普通场景只在回滚事务插入随机 customer_po_config；身份解析另在同一回滚事务插入随机 customer/supplier，并软删该随机 supplier；不改旧行、不清库、不改约束。并发写事务使用 statement_timeout，等待关系以 pg_blocking_pids 观察实际持有者，15 秒取消/超时兜底，不只依赖睡眠猜测。

三类竞争场景需要暂存提交随机 PO 配置（等待写事务后读取、现存 UPDATE/DELETE、唯一胜者提交），只涉及 customer_po_config，新 UID 由仓储生成且保存于本例对象；finally 等待命令收束、释放两个事务后，按精确 @Uid 删除本例提交记录并 count(*) 核对零残留，绝不按前缀删除或枚举旧记录。没有共享结构初始化。主控执行前仍须复核备份、前后全字段/结构/序列摘要、精确清理范围及异常退出风险；本窗口未产生实库记录，不能声明清理和释放已经通过。

非唯一有效客户/供应商编码无法在现有唯一索引下用合法新记录制造；未放宽或移除索引，count 唯一性保护保留于 SQL，真实历史歧义验收由主控根据数据决定。停用币种用例只证明本仓储保留字段，服务币种校验的完整 PG 联验仍依赖币种仓储迁移，不能宣称服务链已完成。

### 剩余验收与主控复核

- P02/P03 冻结版本接口已按分派消费；M12-LOCK-01 的实现选择由主控明确决定并落地，实际竞争/空结果/最新读取与吞吐仍未验证。
- M12-CONCURRENCY-01 的仓储 INSERT/UPDATE 唯一竞争测试代码已交付，真实执行及服务原子回滚由主控安排。
- 未改 DI，宿主仍为 SQL Server；不可据此切生产连接。公共映射、驱动真实空参数及 PG SQL 方言也待主控验收。
- 本窗口没有新增公共文件需求，无越界源码修改；测试库占用为零，无连接字符串或业务明文输出。
- 主控复核实际文件、行为、测试目标与清理范围后合入并串行实库验收；本窗口交付后停写，状态页登记结束时间。是否已验证/接入运行仅由主控更新主清单。

## 历史：COORD-B-20261005-01 只读审查记录

下文保留初始授权、源码调用链与当时未运行事实；其中等待实现的描述仅适用于初始轮次，当前状态以上述 M12-IMPL-01 为准。

# M12 客户 PO 配置只读审查结果

- 任务编号：M12；本轮 COORD-B-20261005-01，仅补协调与初始审查交付。
- 窗口：PostgreSQL执行B：客户PO配置；负责人为执行 B。
- 真实聊天 ID：01a10790-0ef8-7663-ab9b-aeee9f7f1c0d。
- 基线：56209d97a00533c938dd527fa9d37683064a11e8，detached HEAD。
- 工作目录：C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport。
- 开始时间：2026-10-05 00:15:23 +08:00；初始审查轮次起止时间未采集，不补造。
- 记录补齐时间：2026-10-05 00:18:14 +08:00。
- 开始时未提交修改：`git status --short` 为空。
- 状态：待处理（迁移实现未放行）；初始只读审查及本轮记录补齐完毕，等待主控。
- 授权：本结果及执行 B 独占状态页；不得修改源码、公共夹具、主清单或其他窗口报告，不运行构建、测试或数据库操作，不提交 Git。
- 主 Agent / Workflow：Backend / 后端 API 工作流；已读取路由判定及 Dapper Repository、Repository Base、Backend Quality Gates 和适用依赖。
- 前置：P02/P03/M04；真实 PG 写入验收另依赖 P01。公共代码、结构、DI、Core 接口、服务、客户/供应商/币种仓储、共享夹具由主控持有，只读。最新公共结果不等于本窗口已获实现放行。

## 最新公共事实与旧基线更正

本轮读取主仓库 `D:/Documents/source/CSharp/SwiftExport/docs/postgresql-migration/窗口协调.md`、`任务结果/说明与模板.md`、`任务结果/P01-P03-基础设施与编码约束.md`。

据主控 P01—P03 结果：源 PO 两编码为 Chinese_PRC_CI_AS；PG 使用既有 `public.erp_dictionary_ci`。账号、仓库、PO 三个编码索引已在专用故障库验证并同步 dv_next。PO 两键各使用 `rtrim(code,' ')` 加该 ICU，且采用 `NULLS NOT DISTINCT`，软删除过滤保持 `is_deleted=FALSE`。本 worktree 旧生成基线中的原始 PO 索引只是基线差异，不能报告为主仓库尚未处理，也不得在本窗口重复生成或部署。

备份内容一致性及三索引变更、测试摘要见上述主控结果；本窗口未独立重跑或查询数据库。主控明确未做恢复演练、未完成全后端接线，历史通过数量不累计为本轮通过数。ICU 仍非完整仿真源库可忽略字符。

## 六方法逐项适配

接口：`SwiftExport.Core/Interfaces/Customers/PoConfigs/ICustomerPoConfigRepository.cs`；原实现：`SwiftExport.Infrastructure/Repositories/Customers/PoConfigs/CustomerPoConfigRepository.cs`。

| 方法 | 当前事实与 PG 实现计划 |
|---|---|
| GetByPartiesAsync（原实现第 20 行） | 按客户/供应商 UID 及严格未删除过滤读取完整 DTO，不连接主数据、不过滤 IsActive；保留单行断言、取消令牌和调用方事务。物理 snake_case 列显式映射 CLR 别名。锁定分支需主控冻结，不能把 FOR UPDATE 描述为空范围锁等价替换。 |
| PopulateRelationIdsAsync（第 50 行） | 非空 UID 优先，只按 UID 查询，即便编码冲突亦不回退；null UID 才按编码。Guid.Empty 属非空 UID，不擅自当 null。双方候选均唯一且未删除才整体回填 UID 和当前存储编码；任一失败返回 false，不改实体。不新增停用过滤或输入 Trim。PG 以候选集计数和唯一行投影替代 COUNT_BIG/MAX(Uid)，可空参数显式 UUID/text 类型或 CAST，避免空参数类型推断。 |
| GetExistingCustomerCodesAsync（第 83 行） | 数据库只筛未删除配置及双方编码候选；内存以双方 Trim 后的组合键、OrdinalIgnoreCase 精确配对，排除交叉组合误报。返回数据库原编码组成的字符串并忽略大小写去重，不排除自身。PG 候选查询沿冻结的源等值规则，不能把内存 Trim 下推成不同筛选。 |
| GetNonUniqueByPoConfigCheckList（第 104 行） | null/空集合返回空；数据库候选包含已删除行，不排除自身；内存完整组合键大小写敏感、不 Trim。保留这些差异。仓库扫描未发现生产调用，不能因此删接口或遗漏实现。 |
| GetPagedAsync（第 144 行） | 默认 50、上限 500、页码至少 1；软删 null 视未删。保留 SqlLikePattern.Build 和 ESCAPE '~'、客户/供应商两列关键词、完整 DTO；ORDER BY CreatedDate DESC、Uid DESC，PG 日期显式 NULLS LAST。LIMIT/OFFSET，计数和页查询传同一事务。 |
| GetUsedByCustomerCodeAndSupplierCodeAsync（第 215 行） | 输入过滤空白并 Trim、忽略大小写去重候选；数据库严格未删除；内存逐项排除自身 UID，Guid.Empty 表示新增，使用 OrdinalIgnoreCase 比较数据库原编码，不 Trim 数据库行。不能改为排除整个提交 UID 集合。返回规范化输入组合并忽略大小写去重。 |

公共泛型能力计划直接继承 `PostgreSqlDapperRepository<CustomerPoConfig>`，不扩展基类。验证 9 个 IRepository 方法及审计、Version、软硬删除和会话事务传递；公共基类缺陷交主控，不自行修复。

## 真实调用链

- 管理入口：WPF `CustomerPoConfigManagerViewModel` → `ICustomerPoConfigApi` 的 page/batch-save → `CustomerPoConfigController` → `CustomerPoConfigService`。by-parties 公开 API 转服务第 32 行再转仓储；未发现 WPF 调用该 API 方法。
- 服务批量保存：`CustomerPoConfigService.cs:98` → 第 200 行批量校验 → 新增第 278 行用 GetExistingCustomerCodesAsync、修改第 341 行用 GetUsedByCustomerCodeAndSupplierCodeAsync；第 268 行再检查混合提交组合。校验在事务前，随后开启默认事务，新建/修改分别第 130/149 行解析双方 UID，再调用继承 CRUD，整批提交或回滚。
- 服务校验先 Trim 编码/币种/备注；币种通过 ISysDicCurrencyRepository 校验未删除主数据，保留已停用币种旧引用。关系解析不新增 IsActive 检查，保留原语义；目标主数据停用准入差异由主控处理。
- 采购预览：`PurchaseDraftPreviewService.PreviewAsync` → BuildReadOnlyPreviewRequestAsync → 第 199 行 BuildHeaderFactsAsync(false) → 第 434 行按 distinct 双 UID 组合读配置。无配置时保留空默认值，不猜 CNY。
- 正式创建：`PurchaseDraftCreationService` 开启来源事务 → `BuildLockedPreviewRequestAsync` → 预览服务第 374 行 BuildHeaderFactsAsync(true) → 同一 GetByPartiesAsync。第 469 行 BuildNewHeader 将币种、折扣、包装、人员、备注形成新采购头快照。销售来源的 CustomerPoNo 来自 SalesOrderHeader；既有采购头追加使用 MapExistingHeader，不能回灌最新配置。
- 销售草稿 `SalesOrderDraftService` 直接从销售 DTO 保存 PO/币种快照；销售服务未直接依赖 PO 配置。销售来源通过上述需求转采购链间接消费。
- `ForExcelSalesOrderService` / `ForExcelPurchaseOrderService` 通过各自仓储匹配客户、产品、供应商、单位或已有订单；未发现 PO 配置依赖或 SQL 联表。导入客户/料号 Trim/大小写规则属于其他模块，不移植到 PO 仓储。
- 当前 DI：`SwiftExport.Infrastructure/DependencyInjection.cs:137` 仍注册原 CustomerPoConfigRepository；本 worktree 无 PG PO 仓储，未接线。

## 未决事项与验证计划

1. M12-LOCK-01：采购提交真实调用请求 UPDLOCK,HOLDLOCK；PG FOR UPDATE 只能锁现存行，缺失组合不能获得同等范围保护。提交读取一致性、空范围插入竞争及跨连接锁协议交主控判定；本窗口不引入全局锁或修改服务。
2. M12-CONCURRENCY-01：组合预查在事务前，必须依赖已经同步的唯一索引防并发写入。仍需验证实际仓储 Add/Update 的唯一竞争、事务失败及回滚；索引已验收不等于仓储和服务已验收。
3. M12-BASELINE-01：等待主控提供公共冻结版本及实现放行；旧 worktree 不自行 cherry-pick、改公共代码或重复 DDL。
4. 独立离线测试：完整投影/可空字段、参数类型、取消/事务、分页边界、配对交叉误报、逐项自身排除、三方法 trim/case/软删差异、UID 优先且失败不改实体。现有 CustomerPoConfigDefaultsContractTests 和 PurchaseDraftHeaderMappingTests 为相关回归起点，当前未运行。
5. 独立实库场景代码：双方缺失/删除/停用、编码非唯一、当前编码回填、保留已停用币种引用、编码比较/NULL 组合唯一性、CRUD Version、跨连接唯一竞争与锁读取。只提交可审查代码，主控核对专用库及精确初始化/清理范围后串行执行。优先回滚事务；竞争需提交随机记录时预列精确 UID 清理及摘要，未获批准不执行。

## 验证证据与交付边界

| 日期/版本 | 实际命令或检查 | 类型/目标 | 结果 |
|---|---|---|---|
| 2026-10-05 / 56209d97 | git status --short；git rev-parse HEAD；git branch --show-current | 本 worktree 静态 | 初始干净、完整 commit 如上、分支为空即 detached HEAD |
| 同上 | Get-Content D:/Documents/source/CSharp/SwiftExport/docs/postgresql-migration/窗口协调.md；同目录任务结果/说明与模板.md、P01-P03-基础设施与编码约束.md | 文档读取，无 DB 连接 | 已读，主库最新索引事实已更正 |
| 同上 | Set-Content -LiteralPath D:/Documents/source/CSharp/SwiftExport/docs/postgresql-migration/窗口状态/执行B.md | 文档写入 | Access denied；按协议回退至自己 worktree 唯一状态页，未升级 |
| 初始只读审查 / 56209d97 | Get-Content 目标接口、仓储、服务、实体、测试；rg -n --no-ignore 按 CustomerPoConfig/PoConfigCheck/方法名扫描源码（原始输出保留在聊天工具记录） | 静态源码审查 | 已完成；不计为测试通过；本轮未重复跑源码扫描 |
| 2026-10-05 / 文档交付 | git diff --check；git status --short；读取新增文档核对 | 静态文档检查 | diff --check 无错误（不覆盖未跟踪文件）；状态仅新增本轮文档目录，新增文件另行检查范围、编码和尾部空白 |
| 两轮 | 构建、单元/模拟测试、真实 PG、宿主验证、恢复演练 | 未运行 | 本轮及初始授权禁止运行，无通过数量、无测试日志或数据库残留核验 |

实际新增文件仅本结果与 `docs/postgresql-migration/窗口状态/执行B.md`。未修改业务代码、服务、契约、公共依赖或结构；没有提交、推送、部署、数据库占用或向其他聊天发消息。状态页主仓库旧初始化记录没有被覆盖，主控需登记 worktree 状态路径为唯一实际入口。

主控复核与是否可合并、实现放行、主清单更新均待主控填写；本报告不宣称 M12 代码完成、验证通过或已接入运行。
