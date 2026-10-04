# PostgreSQL迁移窗口路由

本文件只维护执行窗口、写入归属和审批状态，不替代AGENTS.md、Skills或[架构审查](后端整体架构与文件夹职责建议.md)。更新时间：2026-10-04。状态由工具实际返回更新，不根据标题、沉默或超时推测任务完成。

## 主控与当前基线

- [主控窗口](codex://threads/01a100ce-2215-7831-aa44-5d5181d47355)：拆分任务、审批方案、公共实现、审查diff、串行构建和集成验收。
- 项目ID：local-889eefb10c5bd0dc35add93c8071a797；hostId：local。
- 共享工作目录：D:/Documents/source/CSharp/SwiftExport；分支：codex/postgresql-main。没有为执行窗口创建独立工作树，不得各自切分支或提交其他窗口文件。
- 首批窗口使用gpt-6.1-sol、medium，通过create_thread显式设置。下列ID来自创建工具返回；链接只标识任务，不代表独立操作系统窗口或隔离工作区。
- SQL Server宿主仍在运行；PG测试库dv_next结构已建成，权限三表已补齐，其他基础数据仅部分迁入。数据导入不等于应用适配完成。

## 首批执行窗口

| 真实任务标题与链接 | 责任模块 | 后续可批准的文件范围 | 当前状态 |
|---|---|---|---|
| [PostgreSQL迁移：账号与权限](codex://threads/01a104fe-0c00-7103-895e-c328ae8ca72a) | Auth/Accounts、Auth/Permissions的4个仓储 | 对应目录新增PostgreSql*.cs；Database/PostgreSqlAuthMigrationTests*.cs | 权限读取2方法已交付并停止写入；构建及离线测试通过；本批实库测试通过，覆盖边界见下文 |
| [PostgreSQL迁移：币种与汇率](codex://threads/01a104fe-288c-7603-9010-8516f9a8fdeb) | Systems/Currencies、Systems/ExchangeRates | 对应目录新增PostgreSql*.cs；Database/PostgreSqlCurrencyMigrationTests*.cs | 汇率3方法已交付并停止写入；构建及离线测试通过；本批实库测试通过，覆盖边界见下文 |
| [PostgreSQL迁移：Excel读取配置](codex://threads/01a104fe-46c2-7522-9178-151e38333dd5) | Systems/ExcelReadProfiles | 对应目录新增PostgreSql*.cs；Database/PostgreSqlExcelProfileMigrationTests*.cs | 6方法已交付并停止写入；构建及离线测试通过；本批实库测试通过，覆盖边界见下文 |

仓储路径前缀为SwiftExport.Infrastructure/Repositories/；测试路径前缀为SwiftExport.Application.Tests/。当前窗口已获下文首批实施批准，表中模块总范围不代表全部方法均已批准。角色与任务转交遵循Router引用的治理规则。

## 公共边界及审批

1. 主控独占：Repositories/Base、Data、Contexts、DependencyInjection、数据库生成器/生成脚本、共享测试夹具、本路由、总报告和数据库README。执行窗口列出公共能力缺口及真实调用点，由主控判断是否必要；不复制参数绑定/映射/事务机制，不制造通用领域框架。
2. 复用Dapper、DbSession、RepositoryBaseContext、PostgreSqlDapperRepository、PostgreSqlParameters及实体映射。保持同一连接事务；SQL Server的范围锁不能机械改成仅行锁。缺失键竞争、唯一冲突、取消及重试分别说明。
3. 先逐方法盘点和提出精确文件清单，主控检查交叉引用、命名、SQL语义和验证范围，再逐窗发送实施批准；边界之外只读。业务含义或公共契约变化仍须用户确认，主控审查不能代替用户授权。
4. 沿用项目按业务模块及Query、Source/Lookup、Draft、Confirmation、Posting、Reversal、Trace等真实用例族组织的规则。只创建当前需要的目录；不按每个方法拆文件、不以partial冒充职责拆分。
5. 共享工作目录按文件独占并行编码；构建/测试由主控串行运行，避免共享bin/obj竞争。执行窗口交付精确文件、方法覆盖、风险及建议测试，完成后停止写入；主控确认停止后接管。未经真实验证不得标记通过。
6. 执行窗口禁止数据库写入、凭据输出、Git提交/推送、切分支、修改宿主注册、前端及Contracts。实库验证由主控安排已确认测试实例和明确测试记录范围。运行切换在完整适配和业务验收后单独实施。
7. 用户已于2026-10-04在主控窗口明确要求执行窗口主动报告，替代首批任务中“无需发消息”的安排。执行窗口核实该用户授权后，在阶段完成、阻塞或需要扩大边界时，通过send_message_to_thread通知主控；内容包含任务ID、模块、文件、验证结果、待决事项及是否停止写入。主控仍主动读取原始交付和diff，回报不能替代审查；未变化不重复通知，不触发窗口间回复循环。通知失败时保留本窗口结果并说明失败，不声称已送达。

报告范围明确限定为本迁移任务的进度、文件清单、验证结果、发现、阻塞及停止写入状态，唯一主控目的地为codex://threads/01a100ce-2215-7831-aa44-5d5181d47355，不发送凭据、业务数据明细或无关聊天内容。用户的报告授权不保证自动审批必然放行；被拒绝时保留本窗最终交付，由主控主动read_thread或wait_threads读取，禁止循环重试和声称已成功发送。2026-10-04主控已再次主动读取币种窗口第五批完整盘点，并告知无需重发。

## 首批盘点审阅记录

### 实库测试批次（2026-10-04，已执行）

三窗均交付并明确停止写入。主控审阅后，后端build --no-restore通过（0错误、7条既有分析器警告）；38个离线场景通过。用户配置连接并授权备份后，主控先创建dv_next_backup_20261004，核验50表/17,594行及关键结构一致，再仅对dv_next以Workers=1串行运行19个集成场景：权限15、汇率1、Excel3，全部通过，无失败或跳过。

权限验证活动账号、角色权限聚合、无效关联过滤和撤销；汇率验证真实可空参数、分页、引用查询和事务回滚；Excel验证主从归属错误回滚、双连接版本竞争和RepeatableRead快照。跨连接场景只提交随机Uid及__pgtest_前缀记录，finally精确清理。测试后全部50表的行数、全字段摘要、结构和序列状态与备份一致，无残留。详见[架构审查及逐表证据](后端整体架构与文件夹职责建议.md)。

PostgreSqlIntegrationEnvironment固定校验192.168.3.12:5432/dv_next，不允许备份库作为测试目标。完整账号/RBAC写入、币种重置、比较与唯一约束等价和其他业务仓储仍未验收，宿主继续使用SQL Server。此次只更新验收记录，不扩大执行窗口写入范围。

### 已下发的首批实施批准（2026-10-04）

- 账号权限窗口：只新增Auth/Permissions/PostgreSqlUserPermissionRepository.cs，实现权限事实与活动用户读取2方法；使用单个SELECT快照组装权限事实，不自建事务。不实现账号创建/密码更新/RBAC写入，原SysUser投影偏差待审查。
- 币种汇率窗口：只新增Systems/ExchangeRates/PostgreSqlSysDicCurrencyExchangeRepository.cs，完成汇率接口3个专用方法并继承PG通用CRUD。币种重置方法因NULL语义冲突暂不实施，不生成缺方法的币种仓储。
- Excel配置窗口：只新增Systems/ExcelReadProfiles/PostgreSqlExcelReadProfileRepository.cs，完成6个聚合专用方法；保留原Uid、主版本控制及字段归属条件。
- 专项测试分别按表内PostgreSqlAuthMigrationTests*.cs、PostgreSqlCurrencyMigrationTests*.cs、PostgreSqlExcelProfileMigrationTests*.cs新增；可使用既有PostgreSqlRepositoryTests partial类型复用私有夹具，禁止复制整套夹具或修改其他窗口测试。公共夹具不足时由主控处理。
- 三窗不运行共享构建，不改公共目录/DI/Schema/Application，不操作数据库。交付停止写入后主控串行构建测试并审查；比较、唯一约束和真实并发待验证，不把离线实现写成可切库结论。

主控同步实库只读核验：SQL Server dv_next的SysUser、SysRoles、SysPermissionDefinitions、ExcelReadProfile、ExcelReadField、SysDicCurrency、SysDicCurrencyExchange所查文本列均为Chinese_PRC_CI_AS；PG对应列collation_name为空，表示未指定列级专用规则，不能据此推断已等价。SQL Server RateToBase实际is_nullable=false，PG亦NOT NULL；因此重置写NULL冲突属于既有实现与结构矛盾，不是仅PG新增限制。查询未读取账号密码值、未修改结构或数据。

2026-10-04已通过read_thread读取三窗最终交付，工具均显示盘点turn completed、窗口idle；三窗报告无文件修改、无构建测试、无数据库操作。已收到三窗通过send_message_to_thread发送的正式回报，均确认未开始实施、无写入活动。

- 账号权限：提出审计仓储依赖、认证文本比较与唯一索引、40001错误分类、无外层事务的账号命令及多结果集一致读取问题；由主控逐项复核。执行窗口已更正方法数量为31：SysUser 2、Auth 8、UserPermission 2、RbacManagement 19；均为接口直接声明方法，无继承方法。
- 币种汇率：发现ResetAllBaseCurrencyAsync写NULL与目标RateToBase非空冲突；本位币数量约束、编码及跨表引用锁需要协调。涉及汇率业务含义不得由执行窗口自行选0、NULL或旧值。
- Excel读取配置：6个聚合专用方法，现有参数/映射/事务能力基本够用；配置名称与字段键比较、唯一索引及实库并发仍待核验。

以上是执行窗口提交的发现及主控待核验项，不表示已修复、实库验收通过或已批准运行切换。

## 第二批：公共命令基础（2026-10-04，限定范围验收完成）

本批三窗已交付并明确停止写入，由主控接管审查、修复测试生命周期和串行验证。首批已验收，原三窗均确认停止写入后复用；主控仍负责公共能力、DI、结构及文档。本批启动基线为codex/postgresql-main / d82341c8，工作区干净，后端构建通过（0错误、7条既有分析器警告）。

| 既有窗口 | 本批职责 | 当前授权 |
|---|---|---|
| [PostgreSQL迁移：账号与权限](codex://threads/01a104fe-0c00-7103-895e-c328ae8ca72a) | Commands命令回执及事务命令锁 | 新增PostgreSqlCommandReceiptRepository及其专项离线/集成测试 |
| [PostgreSQL迁移：币种与汇率](codex://threads/01a104fe-288c-7603-9010-8516f9a8fdeb) | Systems/Numbering取号 | 新增PostgreSqlDocumentSequenceRepository及其专项离线/集成测试 |
| [PostgreSQL迁移：Excel读取配置](codex://threads/01a104fe-46c2-7522-9178-151e38333dd5) | Auditing业务审计 | 新增PostgreSqlBusinessOperationLogRepository及其专项离线/集成测试 |

主控独占新增Data/PostgreSqlCommandLock.cs，供回执和审计复用。固定Guid命令身份映射为事务advisory锁，10秒竞争预算；不改变外层锁超时、不吞取消或重试数据库异常。只支持ReadCommitted及PG等效ReadUncommitted；在发SQL前拒绝RR/Serializable，待后续解决旧快照再开放。三个PG实现本批均不注册宿主。取号单条UPSERT允许无外层事务，返回真实持久Uid（现消费者仅取CurrentNo），保留原Version与UTC审计语义。审计两命令查询及Insert要求外层事务并参与同一命令锁协议，Trace不加锁。

不更改窗口标题，按真实链接记录本批模块。不得修改其他窗口文件；构建与实库执行仍由主控串行完成。命令回执和审计有不可变触发器，测试不得提交后删除、禁用触发器或清表；只允许事务内写入后回滚。取号测试若需提交，仅使用随机测试业务键并精确清理。备份库dv_next_backup_20261004保持只读。Serializable/RR快照与锁等待、比较规则等价仍须单独验证，不把行锁视为范围锁替代。

本批验证：后端构建通过（0错误、7条既有警告）；56项离线/执行器测试通过。本轮串行41实库场景首次39通过、2个纯锁测试清理失败；主控修复已结束事务引用后，受影响12个回执场景全部复测通过。取号5、审计4、跨仓储1均通过；前批19场景通过。测试前后全库50表/17,594行、结构和序列状态与备份一致，无残留。新增公共锁和跨仓储测试归主控，其余3仓储及6测试按上表归属；未改DI或现有业务契约。完整高隔离命令重放未验收，不准切库。

## 第三批：高隔离命令重放（2026-10-04，限定范围验收完成）

用户提交后的基线85d3a090，分支codex/postgresql-main、启动工作区干净。复用原三窗，命令窗口接管公共PG锁与回执实现及必要DbSession状态；币种窗口只写Commands/BusinessCommandExecutorTests.cs离线编排测试并提交异常审查；Excel窗口只新增PostgreSqlBusinessCommandExecutorMigrationTests.Integration.cs实库测试。主控独占Core可选ICommandTransactionPreparation接口、Application执行器、连接校验入口及文档，负责串行验证。执行窗口不得构建、DB操作或Git操作。

方案：PG先在同一连接业务事务外取得命令session锁，保留到业务事务释放后；业务事务仍由Application开启且保持原隔离级别。SQLServer无该可选能力，沿用原事务内锁流程。高隔离事务仅允许同身份有效预锁进入，旧直接调用仍拒绝；取消、异常和连接池清理要独立验收，不自动重试业务委托。

用户已明确批准创建专用库dv_next_pg_command_tests_20261004；主控从dv_next_backup_20261004复制成功，未断开任何连接。后续用户改为自己手动删除，因此所有窗口均禁止自动删库。提交回执测试使用独立环境变量与GetCommandConnectionString固定目标入口，原dv_next校验入口不放宽。dv_next及备份保持不变，测试失败时保留专用库排查；结束时释放我们自己的连接并报告库名，由用户删除。

三窗已完成并停止写入，主控主动读取交付及实际diff；账号和Excel窗口成功回报，币种窗口报告发送受工具审批限制，由主控读取其最终结果完成接收。主控修复新集成测试的可空性警告后，后端构建0错误、7条既有警告，75项离线测试与51项串行实库测试全部通过。新10项Serializable执行器场景在专用库通过；原dv_next测试后50表/17,594行全部字段、结构、序列状态与备份一致。专用库保留三种测试事实各10行，其余47表不变，其他连接和advisory锁均0，交用户手动删除。真实断线/提交结果未知/物理连接池丢弃尚未故障注入验证；完整业务模块和运行DI未切换。

## 第四批：失败清理及故障验证（2026-10-04）

沿用codex/postgresql-main / 85d3a090及前批未提交改动。账号窗口只读审查错误分类、API映射及清理问题；币种窗口仅增量写BusinessCommandExecutorTests.cs；Excel窗口仅新增PostgreSqlCommandFailureTests.Integration.cs。三窗完成后停止写入，主控读取交付、修复执行器和锁清理、增加内部40001/57014分类与故障库校验入口。币种窗口报告受审批限制，主控主动读取最终交付，未反复尝试通知。

主控复现10项清理失败并修复；126项相关离线测试全通过，加强关闭断言后的27项执行器复测通过，后端构建0错误/7既有警告。用户批准的dv_next_pg_failure_tests_20261004已从备份复制核验，4项故障实库场景通过，保留1条取号测试行供用户手动删库；其他连接和锁均0，原库与备份完全一致。上批dv_next_pg_command_tests_20261004已不存在，未重建。运行DI、API契约、自动重试策略均未改变；真实提交确认丢失、模块错误语义统一仍未验收。

## 第五批：仓库只读查询（2026-10-04，限定范围验收完成）

用户已明确保留dv_next_pg_failure_tests_20261004供后续测试，替代第四批“待用户删除”安排；不代表允许清库或改原库。主控记录本批前50表/17,595行全字段摘要，已有取号测试行保留。新测试仅在外层事务内插入随机supplier/warehouse_site记录并回滚，不提交、不删历史记录或数据库。

账号窗口独占PostgreSqlWarehouseQueryRepository及其专项离线/集成测试，实现IWarehouseQueryRepository六方法；Excel窗口独占PostgreSqlWarehouseCandidateRepository及其专项测试，实现采购仓库候选一方法；币种窗口只读盘点后续产品/客户/供应商写入切片。复用前述真实窗口链接，不新增窗口或修改标题。各窗不构建/运行DB/git，交付停止后主控串行验收；共享文档、连接入口与公共能力归主控。

保持管理查询与候选资格分离：管理显示允许历史快照，采购候选须关联真实活动供应商并过滤空白身份。PG表达式使用已存在erp_dictionary_ci，不改表列或索引；普通仓库关键词空格及问号保持字面，不能调用把它们转为通配的SqlLikePattern.Build。原候选仓储强制调用方事务，六方法查询不自建事务；SQL Server运行注册保持不变。

三窗均交付停止，主控读取实际代码后接管。新增两个仓储、四个测试文件，6+1方法保持原接口；构建0错误/7既有警告，相关回归65通过/5旧SQL Server实库测试未启用，新增PG仓库实库13项全通过。主控仅修正候选测试夹具的软删构造，生产SQL Server/Add语义不变。保留库前后50表/17,595行全字段摘要完全一致，结构及序列状态与备份一致，连接已释放；用户已撤销删除安排，继续保留。仓库管理写入、其他主数据和DI未切换。详细搜索比较与后续76方法盘点见现行架构报告。

## 第六批：客户与供应商并行适配（2026-10-04，查询及回滚测试通过）

用户明确允许按依赖并行开发、必要时新建窗口。主控确认前批已提交，codex/postgresql-main基线ed224755、启动工作区干净。当前复用三个窗口，不增加共享文件写入者：

| 窗口 | 本批角色与独占写入范围 |
|---|---|
| [PostgreSQL迁移：账号与权限](codex://threads/01a104fe-0c00-7103-895e-c328ae8ca72a) | 客户：新增Customers/Management/PostgreSqlCustomerRepository及必要SalesOrderDetail partial、PostgreSqlCustomerMigrationTests.cs/.Integration.cs |
| [PostgreSQL迁移：币种与汇率](codex://threads/01a104fe-288c-7603-9010-8516f9a8fdeb) | 供应商：新增Suppliers/Management/PostgreSqlSupplierRepository、PostgreSqlSupplierMigrationTests.cs/.Integration.cs |
| [PostgreSQL迁移：Excel读取配置](codex://threads/01a104fe-46c2-7522-9178-151e38333dd5) | 只读复核编码比较/唯一索引及下一批分类颜色引用锁，无写入范围 |

主控独占公共比较设计、生成器/索引方案、连接入口、文档与串行验收。两个实现依赖已有接口和PG基类，保留普通更新不写IsActive及每方法软删/Trim/投影差异；不改Application、Contracts、SQL Server仓储或DI。执行窗不构建、连库、执行DDL、提交或推送。新增实库测试统一PostgreSqlPartyIntegration，保留库仅构造本事务随机客户/供应商数据后回滚。

现已只读核验两端：SQL Server两个活动编码唯一索引使用Chinese_PRC_CI_AS；PG同名索引仍直接按原列比较。客户/供应商代码可并行实现，但编码等值查询与唯一索引的兼容验收是共同前置条件；不得仅查询COLLATE通过就注册写入仓储。rtrim+既有ICU表达式是待评审方案，任何已部署索引变更由主控给出具体影响和回退后取得确认，执行窗不自行实施。分类/颜色及后续产品引用链暂不并行写入，先解决缺失行和新增引用竞争。

三个窗口均已交付并明确停止写入，主控接管后构建0错误/7条既有警告，相关离线回归72项全部通过（本批新增13项）；客户11项、供应商4项PG实库测试全部通过。保留专用库前后50表/17,595行全字段摘要一致，无新增残留。供应商报告再次被自动审批拒绝，主控已主动读取完整最终交付，无需重复发送。未改共享接口、Application、SQL Server仓储、DI或数据库结构，两个新仓储尚未接入运行。

主控对dv_next按拟议rtrim+ICU规则只读预检，两表活动编码冲突组均为0；这不代表唯一索引已调整。ICU与源排序规则已知存在U+200B等Unicode比较差异，扩大到唯一约束前须明确接受边界；具体专用库候选变更范围及回退见现行架构报告，尚未执行。下一批分类/颜色不能使用DISTINCT加FOR UPDATE，也不能以行锁声称覆盖未来新增引用；相关写入口共享锁协议仍待审查。

## 第七批：唯一索引同步及备份（2026-10-04，限定范围验收完成）

复用三个真实窗口，全部已交付并停止：账号窗口仅新增PostgreSqlPartyUniquenessTests.Integration.cs（4方法10案例）；币种窗口只读盘点分类/颜色及全部相关引用写入口；Excel窗口只读审查索引维护锁、目录检查及严格回退方案。主控已主动读取币种窗口交付，主动报告被拒绝不视为任务未完成。

主控独占两份人工索引脚本、生成器与生成基线、公共结构测试、数据库README、总报告和本路由。用户批准后，专用库前向回滚演练→填妥回退守卫→正式提交→回退路径演练均成功；10项唯一性和15项客户供应商实库回归共25项通过，专用库50表/17,595行全字段摘要与测试前一致。随后按批准同步dv_next两个索引，数据不变；生成基线仅两行索引差异，列级比较未改变。

后端构建通过（0错误、7既有警告），相关离线97项通过（含新增2项生成器范围断言），两提供者基线漂移检查通过；无全量或SQL Server实库回归。用户选择旧备份保持原样，已新建dv_next_backup_20261004_153556，与dv_next的50表/17,594行、结构、函数和序列状态完整核验一致。此前获批终止的PID5620在执行时已不存在，实际没有断开任何连接。当前HEAD仍5a42129c，本批不自动提交或推送。分类/颜色仅完成盘点，未下发写入任务；后续遵循用户最小必要锁要求，不能将DDL排他锁带入普通业务查询。

## 第八批：分类与颜色仓储（2026-10-04，限定范围验收完成）

用户批准推进；基线7562f6da、工作区干净，启动构建0错误/7既有警告。复用已确认停止写入的三个窗口，继续原模型配置：账号窗口独占新增Products/Categories/PostgreSqlProductCategoryRepository.cs及PostgreSqlProductCategoryMigrationTests.cs/.Integration.cs；币种窗口独占新增Products/Colors/PostgreSqlProductColorRepository.cs及PostgreSqlProductColorMigrationTests.cs/.Integration.cs；Excel窗口只读审查递归失败边界、窄投影、比较与事务前置条件。前三个路径前缀仍按本文件约定，测试仅Application.Tests/Database。

分类4方法、颜色5方法保留现有接口和业务域，普通树/分页/编码读不新增更新锁；仅已有锁定方法锁命中现存行，稳定uid顺序，空输入立即返回，非空锁查询必须共享调用方事务。引用查询锁底层行后在内存去重，不DISTINCT FOR UPDATE；不把行锁称为缺失行或未来引用保护，不增加表锁/advisory锁。递归须明确SQL Server默认100层失败边界，不能无限循环或静默截断；此点由主控审查实现方案。分类/主型号锁顺序修复和ProductNeutral颜色引用域不在本批仓储窗口授权内。

主控独占公共夹具、连接入口、DI、生成器/DDL及文档；窗口禁止构建/DB/Git，完成明确停止后主控串行验收。新实库分类PostgreSqlClassificationIntegration仅用保留专用库，在外层事务内构造随机category/color及必要引用记录并回滚，不提交或修改旧行、不改任何库结构。双连接锁验收采用库中已可见的有效记录，仅SELECT与SELECT FOR UPDATE并回滚，禁止对旧行执行DML；普通读应不阻塞第二连接取行锁，锁读则实测等待及释放。不能对第二连接不可见的未提交随机新行声称锁测试成功。普通读与必要锁读分别验证，不以编译通过替代事务验收。分类/颜色唯一编码索引的CI扩展尚未审批，不接入运行。

三个窗口均已交付并停止，主控接管六个新增源码/测试文件；颜色窗口本轮主动报告被自动审批拒绝，主控主动读取交付，没有重复发送。构建0错误/7既有警告，110项相关离线通过（新增14项）。新增分类10、颜色5项实库串行首轮14通过/1失败：颜色测试误把DTO默认20当作仓储无效页大小回退50；主控保留生产行为，分别断言两者，重建后该场景复测通过。全部15项均已有通过结果，无跳过或未解决新增失败。主控另补分类自环场景及颜色锁轮询短间隔/提前失败检查；SQL Server常量链已实测100成功、101报530。

最终专用库50表/17,595行全字段摘要与测试前一致，无新增残留；原库及备份未写入，无DDL、DI或业务公共契约变化。分类/颜色原列唯一索引经MCP只读核实仍未兼容查询比较；完整引用竞争、跨入口锁顺序及颜色引用域依旧待决。本批不自动提交或推送，后续窗口不得未受派重新写入这六文件。

## 第九批：分类颜色索引方案与引用协议审查（2026-10-04，索引与新备份验收完成）

用户继续推进，HEAD仍7562f6da，第八批六个新增代码/测试文件与两份文档未提交，全部保留。复用前三个真实窗口及原配置：账号窗口仅新增Database/PostgreSQL分类颜色编码索引变更.sql与回退.sql草案；币种窗口只读审查型号/中性产品方法、父引用和锁顺序；Excel窗口只读复核比较/错误转换及两个草案。其他源码、生成器、生成基线、公共测试夹具及文档由主控持有，本批尚未批准数据库结构变更，窗口禁止DB/构建/测试/Git。

本批先形成可审查的有限方案，不把第七批客户供应商授权扩到分类颜色。主控只读核验dv_next与保留专用库的两原列索引及拟议比较冲突；源库与备份无写入。具体脚本、影响、回退和待决业务域由主控审阅后提请确认，审批前不改生成基线或运行DI。

三个窗口均完成并停止，主控已接管两SQL文件并补单次approved_database绑定及回退目录输出；默认拒绝开关和未演练的新表达式待填标记保留。币种窗口报告被自动审批拒绝，主控主动读取完成交付。两库原索引一致、拟议比较冲突均0，分类29条有效NULL码必须保留；当前仅只读证据，不是DDL或唯一性实测。主控更新现行架构报告与Database/README，记录两组反向锁顺序、23505→500及颜色中性引用域待决。增量构建0错误/0警告，本批没有新业务源码或写库测试，不重复跑前批测试冒充本批验证；专用库DDL、同步dv_next及新备份须本批明确确认。

用户随后明确批准上述执行范围。账号窗口本轮仅新增PostgreSqlClassificationUniquenessTests.Integration.cs（7方法14案例），币种窗口只读复核生成器扩展，均已停止，主控接管。主控完成专用库前向回滚/严格回退演练及正式应用、29项PG实库全通过、107项离线通过、两提供者生成漂移核验；新生成基线仅2行索引变化。构建首次测试漏using已修复，最终0错误/7既有分析器警告及3条NU1900环境警告。专用库无新增残留，dv_next已同步两索引且其余结构数据与变更前备份一致。用户后续持续授权备份受阻时关闭已核实的源库空闲客户端连接（不含活动查询、未提交事务及其他库）。确认PID5972仍符合条件后终止并复制，新备份dv_next_backup_20261004_170031已完成50表/17,594行全字段及完整关键结构/序列状态核验，旧备份不变。此段替代前文待批准/未执行状态，其余业务待决保留。

## 第十批：主型号与中性产品、引用与锁顺序（2026-10-04，限定验收完成）

用户同意三个仓储切片及有效中性产品引用阻止颜色删除的建议。保留第八/九批全部未提交修改，HEAD仍7562f6da。账号窗口独占新增ProductMasters/PostgreSqlProductMasterRepository及其MigrationTests.cs/.Integration.cs（4方法）；币种窗口独占ProductNeutrals/PostgreSqlProductNeutralRepository及其测试（3方法）；Excel窗口独占ProductNeutrals/PostgreSqlProductNeutralManagerRepository及其测试（2方法）。生产文件前缀Infrastructure/Repositories/Products，测试前缀Application.Tests/Database，均复用原接口/映射/参数/事务。窗口不构建、不DB、不Git，交付停写后由主控统一验收。

主控独占Application维护服务及partial、两提供者颜色引用保护、相关既有测试与必要新测试、Core内部说明和文档。先复现取锁顺序及中性引用遗漏，再修复必要入口；不改变DTO、前端或运行DI，不新增全局锁。数据库索引本批只读核对，需要DDL另提具体审批，前批两索引授权不扩展到主型号/中性。实库仅保留专用库随机数据回滚，旧行仅只读锁竞争，不新增提交夹具。

三仓储窗口均已交付原文件并停止，主控接管。后续币种窗口仅新增Application.Tests/MasterData/ProductMasterDetailLockOrderTests.cs，Excel窗口仅新增同目录ProductNeutralManagerLockOrderTests.cs；两个任务只写各自离线测试，不修改已接管仓储和Application。主控补颜色单条删除事务和引用保护、真实中性引用回滚测试，并负责统一串行验收。

两个后续测试也已交付停写，主控接管全部文件。相关离线145项通过；专用库32项中28通过、4项因无已提交中性行而Inconclusive，无遗留失败。后端依赖链构建0错误/7既有分析器及3环境NU1900警告。主控修复测试夹具必填字段、默认分页及软删Version断言，保持源业务契约。全库50表/17,595行摘要前后一致。主控进一步收窄新中性引用检查为只读，父颜色负责协调，不附加子行更新锁。未改DI、数据库结构、dv_next或备份；型号/中性两个编码原列唯一索引及错误分类待下一批具体方案。当前所有窗口停止写入，可在明确新归属后复用。

## 第十一批：型号与中性编码唯一性（2026-10-04，限定验收完成）

当前状态：三个窗口均已主动报告交付并明确停止写入，主控已审查接管并串行验收。账号窗口交付三个PG仓储Add/Update、模块内中性冲突辅助及PostgreSqlProductWriteConflictTests；币种窗口交付两个维护服务EnsureWrite，并扩展ProductMasterDetailLockOrderTests、ProductNeutralBatchSaveTests、ProductNeutralManagerLockOrderTests，未另建原拟ProductWriteConflictServiceTests；Excel窗口交付PostgreSqlProductUniquenessTests的Integration/Helpers/Concurrency三个partial文件。主控完成DDL、生成器/基线、FoundationTests及文档。

199项相关离线全部通过；59项专用库实测55通过/4项因无既有中性记录跳过，本批新增27项全部通过，无新增失败。专用库50表17,595行前后摘要一致。两编码索引已同步dv_next，其他结构、50表17,594行及序列状态与170031备份一致；按持续授权核实并关闭源库空闲无事务PID1436，新建dv_next_backup_20261004_181602且全结构/数据/序列核验一致，旧备份不变。后端构建、两提供者生成漂移检查通过，未改运行DI或自动提交。详细边界见架构报告；以下准备过程仅保留审批追溯，待批准/未执行描述已由本段替代。

用户要求推进第十批后续。保留现有未提交修改，运行DI与数据库结构不变。主控按backend/后端API工作流统筹；账号窗口仅新增Database/PostgreSQL型号中性编码索引变更.sql与回退.sql待审批草案；币种窗口只读审查两个维护服务的异常识别与HTTP映射；Excel窗口只读复核实体索引、生成器范围和锁边界。均复用本路由已登记真实窗口及模型配置，不另建窗口，禁止DB/构建/测试/Git；主控独占公共接口、生成器和文档。

主控已只读核验dv_next和保留专用库：两编码原列唯一索引一致，拟议rtrim+ICU冲突均0；均4条型号、0条中性。父型号+颜色组合唯一保留。专用库通过已授权本机Npgsql连接核验，不扩大MCP库权限。后端解决方案增量构建0错误、3条NU1900环境警告；未运行新DDL或写入测试。脚本默认拒绝执行，生成器及基线待本批明确批准后同步，不沿用上批两索引审批。

三窗已交付并停止，主控接管草案，补列类型/NOT NULL守卫并核查最终文件；两个只读审查均主动读取完成。精确错误方案复用RepositoryWriteResult，由三个PG业务仓储白名单识别三种业务唯一约束，两个EnsureWrite映射编码400/组合422，主键及未知异常不吞；尚未实施，待确认API可见变化。父颜色组合预检和OrdinalIgnoreCase二次比较缺口明确登记，不附带扩大修复。本批仅新增两SQL草案和更新三份现行文档，未执行数据库结构或写入测试。

用户随后明确批准两个索引及错误分类执行。复用三窗：账号窗口独占三个PG仓储、模块内中性冲突辅助函数及PostgreSqlProductWriteConflictTests离线测试；币种窗口独占两个维护服务EnsureWrite和ProductWriteConflictServiceTests及分配的既有维护测试；Excel窗口独占PostgreSqlProductUniquenessTests.Integration及必要partial实库测试。主控持有DDL、生成器/生成基线、FoundationTests与文档；窗口仍禁止DB/构建/测试/Git。专用库前向回滚和前向/回退完整演练已通过，六个回退守卫按实测填妥，随后已提交专用库两个索引，等待串行验收。dv_next与170031备份全结构、50表/17,594行及序列状态预检一致，尚未同步本批索引。

## 第十二批：产品目录、资料与包装（2026-10-04，14方法限定验收完成）

用户批准继续推进，沿用backend/后端API工作流，HEAD7562f6da及前批未提交修改保留。三个既有窗口均确认已停止后复用：账号窗口只读盘点Products/Catalog全部接口/实现/调用方；币种窗口定位产品资料与包装的实际入口，不预设存在独立仓储；Excel窗口统一盘点剩余唯一索引及引用冻结协议。此阶段无窗口文件写入，禁止构建、测试、DB及Git；主控持有文档和共享能力。待具体文件归属审查后再下发实施，不把前批两索引授权扩展到其他对象。

主控随后下发有限实施：账号窗口独占PostgreSqlProductRepository主文件及普通查询partial、PostgreSqlProductCatalogMigrationTests；Excel窗口独占该类型MasterSynchronization/Supplier partial、PostgreSqlProductSynchronizationTests；币种窗口仅新增PostgreSqlProductAttachmentsTests.Integration验证既有通用资料/包装CRUD，不新造仓储。主控独占SalesOrderDetail partial及PostgreSqlProductSalesLookupTests、客户代码待决入口、实体Cbm失真注释与文档。8普通读取＋3同步＋供应商写入＋销售选择共13方法可实施；客户代码旧列在两库均不存在，关系来源尚待用户确认，主类型暂不接IProductRepository，不留伪造实现。当前所有PG代码不注册DI。实库仅保留专用库随机夹具事务回滚，写前已记录50表基线；不提交夹具或改旧数据，无DDL。

三个执行窗口均已停止写入，主控读取交付并接管验收。Excel窗口主动报告被自动审批拒绝，已通过主控读取最终结果完成接收，无需重试发送。主控保留类型断言，补公共参数包命名UUID数组与timestamp显式绑定；相关离线225通过、34项外部集成未启用，专用库串行35项全部通过。专用库50表17,595行摘要无残留，dv_next与181602备份50表17,594行及全部结构序列一致；构建和两基线校验通过，无DDL。用户随后批准客户代码关系来源，主控补齐两提供者查询及PG接口，仍不注册DI；共享文件写入归属回到主控。

## 第十三批：双方产品关系与 BOM（2026-10-04，关系适配限定验收完成）

用户批准继续推进，主控复用三个已停写窗口，模型GPT-6.1 Sol/medium。账号窗口01a104fe-0c00-7103-895e-c328ae8ca72a盘点客户产品；币种窗口01a104fe-288c-7603-9010-8516f9a8fdeb盘点供应商产品；Excel窗口01a104fe-46c2-7522-9178-151e38333dd5盘点BOM维护及执行依据。深度链接沿文首索引，主控主动读取交付。初始只读，无构建、测试、DB、Git权限；独立文件实施须主控下发，共享基础能力、文档和最终串行验证归主控。本批不包含索引/触发器DDL，不新增笼统数据库锁；尤其BOM的HOLDLOCK范围保护不能机械改为行锁，待核对真实写入入口和隔离级别后确定可实施边界。

实施归属已下发：账号窗口独占Customers/CustomerProducts/PostgreSqlCustomerProductRepository.cs及Database/PostgreSqlCustomerProductMigrationTests.cs/.Integration.cs；币种窗口独占Suppliers/SupplierProducts/PostgreSqlSupplierProductRepository.cs及对应PostgreSqlSupplierProductMigrationTests两文件。复用PG基类并实现原接口全部7/6读取，不改SQLServer、Application、Core、Contracts、DI或共享夹具。实库用GetFailureConnectionString随机事务回滚，由主控在窗口停写后串行执行。Excel窗口BOM仅读报告，已由主控主动接收；未移植旧物理删除或无法保证空范围的锁方法，没有stub或空结果占位。BOM治理建议与未核验项已并入架构报告。

客户代码获批补充完成：主控修改两提供者查询、Core接口职责注释及PG接口接入，新增两提供者离线契约和PG真实关系测试。154项仓储离线及36项本批实库均通过，专用库摘要无残留，构建通过。主动复读三窗最新最终消息均为idle，没有遗漏交付；币种窗ColorCode夹具问题已修复并实测，Excel窗报告发送拒绝已由主控直接读取替代。

- 主数据：仓库Query/Candidate只读已验收，管理写入待适配。客户/供应商、分类/颜色、型号/中性仓储及获批编码唯一约束、型号中性精确错误分类已限定验收；既有中性行锁4项仍缺数据未验证。下一批先盘点产品目录、资料/包装及引用冻结协调，再按独立文件归属适配，随后双方产品关系和BOM；客户PO配置可在双方及币种就绪后独立推进。缺失行竞争和锁语义不以FOR UPDATE机械替换，业务引用域不擅自扩大。
- 公共命令基础下一步：Serializable同身份重放及冲突、物理断线/解锁失败清理、40001失败事务回滚及57014分类已验证。提交确认丢失、重放Commit映射和模块错误语义仍需独立兼容设计，不自动重试委托。用户保留故障测试库，后续测试先记录基线、核实目标与授权，不清既有记录或在dv_next关闭不可变触发器。
- 销售与售后、需求计划、采购、库存出入库、PackingList及ForExcel订单查询：依赖公共命令基础及主数据适配。按现有用例切片，不把同一事务拆成多个独立提交。
- 收尾：后台任务与启动代码SQL、完整DI闭包、真实API链路、剩余有效基础数据、SQL Server回归及PG切换验收；Linux部署独立处理。

## 第十四批：BOM 保存入口统一（2026-10-04，限定验收完成）

第十三批三个窗口停写并由主控验收：181相关离线与9实库全部通过，专用库50表摘要无残留，构建通过。复用既有窗口继续第十四批：Excel窗口独占Application/Services/Products/BomService.cs及必要同目录职责partial；账号窗口独占Application.Tests/Products/BomSaveEntryTests.cs及必要同名辅助分部；币种窗口独占BomSaveRollbackTests.cs及必要同名辅助分部。其他文件、文档和最终验收归主控。

用户授权保留API/DTO、统一旧写入口与现有完整图保存校验。单条Add/Update/Delete复用BatchSave内核；按号删除也不能直接物理删除，须在同一内核事务解析当前行后校验，不自动清父BOM身份、不删除历史行。合法软/硬删都经过同一图校验；不增加锁、隔离级别、DDL、PG BOM实现或新幂等机制。执行窗禁止构建/测试/DB/Git；主控在全部停写后串行测试。并发成环及执行读取一致性仍留后续，不把本批收口视为完整并发验收。

三窗均已停止写入，主控接管并串行验证：44项BOM测试全部通过、无跳过；首轮1项新增夹具失败已按真实按号查询排除历史行修正，未削弱断言。后端构建0错误/3条既有NU1900警告。未运行BOM实库并发，本批无数据库写入或结构变更。下一步普通BOM读取适配及整图并发方案分别推进。

## 第十五批：BOM 普通读取（2026-10-04，限定验收完成）

确认前批三窗停止后复用：Excel窗口独占新增 Products/PostgreSqlBomRepository.cs 及必要同名查询分部，适配按号DTO、按号实体、编号版本、HasActive、分页和IsBomItemExist六个读取；账号窗口独占PostgreSqlBomQueryTests.cs/.Integration.cs；币种窗口只读审查调用与语义。继续使用原模型GPT-6.1 Sol/medium。主控独占文档、共享能力与最终构建及实库串行验证，所有执行窗禁止运行构建/测试/DB/Git。

实现继承既有PG基类但暂不声明完整IBomRepository，不注册DI、不造未完成方法占位。集合查询、整图保护、按号物理删除及ExecutionReader不在本批实施范围。保留版本/软删/当前展示与快照差异，普通查询不新增锁或DDL。实库仅使用已授权保留库dv_next_pg_failure_tests_20261004随机事务回滚，先后核验50表行数和摘要，不写dv_next或备份。

三窗均已明确停写，主控主动读取实现和只读审查交付；Excel窗主动报告被自动审批拒绝已用读取替代，不影响验收。主控为PostgreSqlRepositoryTests的记录连接增加可选ResultSets支持，保持既有默认行为；未要求生产改成两个单独查询。223项相关离线与4项BOM实库串行全部通过，无失败或跳过；专用库50表17,595行摘要不变。后端构建0错误/3条既有NU1900警告。下一步先评审整图并发与执行读取，当前六方法未注册运行DI。

## 第十六批：产品 BOM 保存边界与清理（2026-10-04，限定验收完成）

复用原三窗只读审查：Excel窗口核对全部BOM/产品图写入者；账号窗口追踪真实需求建立、执行读取和命令隔离/重放；币种窗口检查BOM错误协议与事务清理。主控汇总最小方案，先区分可直接修复的资源清理问题与须确认的业务/API变更，不机械移植范围锁，不修改数据库结构。执行窗初始无文件写入、构建/测试/DB/Git权限，后续具体写入归属由主控明确下发。

后续授权：币种窗口独占BomService.cs清理/诊断及BomSaveRollbackTests.cs故障注入，不改变提交错误分类。用户随后批准先补产品保存边界：Excel窗口独占ProductMaintenanceService.cs及同目录职责分部，必要的IBomRepository和SQLServer/PG BomRepository窄相关读取；账号窗口独占新ProductBomSaveBoundaryTests及必要同名分部。禁止新增全图加锁查询调用、额外事务框架、自动重试、DDL或改变需求单层展开。主控拥有新增仓储方法验收、文档与串行构建测试。

三窗已停写，主控接管最终文件。币种窗另做只读复审，发现C#比较会漏掉数据库全半角等价当前组成；主控将新查询收窄为数据库联接判定的当前引用，历史激活复用既有指定版本查询，不再由服务重判编号归属。账号窗补齐34项产品保存场景并按真实泛型仓储纠正新增身份和软删版本夹具。主控串行验证289项相关离线、5项PG查询全部通过，无失败或跳过；保留专用库50表17,595行前后摘要一致。后端构建0错误/3条既有NU1900警告。没有DDL、运行DI切换或新锁，BOM跨连接写并发与真实需求读取一致性仍待验收。

## 既有窗口选择记录

初次分派时已通过list_threads检查现有任务，当时没有已核实可直接复用的PG迁移执行窗口，因此新建上述三窗，后续按本路由复用。旧采购、库存、权限业务窗口未被改派、未获得本批写入权。后续需要旧任务上下文时先read_thread核实内容及写入状态，再决定是否继续该任务；不把旧标题或摘要当源码事实。
