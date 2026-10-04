# PostgreSQL迁移窗口路由

> 最新协调（2026-10-05）：用户提供链接后主控已核实执行 A/B 均 idle，初始只读审查完成、等待主控冻结；纠正此前持续按创建中记录的错误。A 对应 9b64，B 对应 3557，真实聊天 ID 及独占状态入口统一见[窗口协调](postgresql-migration/窗口协调.md)。本轮补状态及审查记录，不等于实现完成，不重复创建窗口。任务与文件归属继续统一维护在主清单。账号/仓库/PO 三索引公共任务已验证并同步 dv_next。

> 2026-10-04 最新安排：暂停新增迁移代码。本轮三个旧执行窗口已确认停写并归档；不再按下方历史分派继续开发。后续任务、依赖、文件归属与建议新窗口分组统一维护在 [主迁移清单](postgresql-migration/主迁移清单.md)，等待用户确认后再创建新执行窗口。本文件下方保留交接历史，不重复维护新待办。

本文件只维护执行窗口、写入归属和审批状态，不替代AGENTS.md、Skills或[架构审查](后端整体架构与文件夹职责建议.md)。更新时间：2026-10-04。状态由工具实际返回更新，不根据标题、沉默或超时推测任务完成。

## 新主控交接（2026-10-04，用户最新范围）

当前唯一主控：[PostgreSQL基础设施迁移：新主控](codex://threads/01a1075b-f943-7320-943a-d13866f5e259)。ID由create_thread真实返回，hostId=local，共享原工作目录。旧主控本次只完成交接，不再派工或编码。

### 目标与停止扩张边界

这是 SQL Server → PostgreSQL 的基础设施迁移，最终运行 PostgreSQL + Dapper 并淘汰 SqlClient/旧 SQL Server 运行实现，不是重新设计 ERP、不维护永久双数据库选择框架。迁移涉及 SQL 方言、参数/结果映射、连接事务、并发/错误语义、启动依赖与必要数据搬迁。用户允许少量直接服务于迁移的小重构，尤其销售模块及此前手写代码；这不是借机修完能力地图所有历史偏差的授权。重构须有实际迁移阻碍、明确文件边界和验证收益；重大业务或 API/DTO 语义变化仍先确认。

不改 WPF/VSTO，不更换 Dapper，不增加不必要实体/头表/通用框架，不自动部署、推送、合并或提交。C# 文件采用英文类型/职责名，中文职责注释保留，Markdown 可中文；用户最新命名要求优先于旧“文件名中文优先”。文件组织遵循项目横向职责切片，禁止按每个方法机械建目录。

### 接手必读（按需去重，不重跑完整审查）

1. AGENTS.md → .agents/router.toml 及 policy 引用 → .agents/backend.toml、.agents/workflows/后端API工作流.md；当前主 Agent 为 backend。
2. 本文件的主控身份、公共边界及本交接节；旧逐批分派为历史，不授予新的文件写入权。
3. docs/后端整体架构与文件夹职责建议.md：先读顶部现行路线、第二十一批、20.1/20.2 数据迁移事实；其余章节仅按需追溯，早期“保留 SQL Server”不是长期目标。
4. .codex/skills/aspdotnet10/index/SKILL.md，再读当前切片需要的 dapper-repository、infrastructure-db-session、infrastructure-repository-base、infrastructure-di-registration、backend-file-organization、backend-quality-gates；认证切片再读 webapi-auth-current-user。旧技能中 SQL Server 专用说明须区分旧实现与迁移目标，不据此增建双库框架。
5. .agents/references/工程架构总纲.md、测试与验收总纲.md；仅涉及相应业务规则时读业务架构总纲/模块能力地图。SwiftExport.Infrastructure/Database/README.md、实际 DependencyInjection.cs、WebApi 启动注册与目标接口、服务、仓储及测试优先核实。

### 当前落盘事实与验收

分支 codex/postgresql-main，共享目录 D:/Documents/source/CSharp/SwiftExport。新主控接管时核实HEAD为72662abc，前批实现及ProductMaintenanceService.SaveBoundary.cs、SharedBomMaterialDemandEstablishmentTests.cs两个英文重命名已提交，当时仅本路由交接修改未提交。后续第二十二批新增改动见下节，禁止reset或覆盖已有工作。

BOM/产品保存及真实需求建立使用现有 Serializable 事务，不加全局业务锁或自动重试。明确 40001（含 Commit）返回 SERVICE_UNAVAILABLE/503；未知提交保持 OUTCOME_UNKNOWN/503；Version 冲突独立。共享 BomNo 不唯一，多个产品共享组成；BomVersion 只是0/正数有效标记，不建设多版本。物理来源父只追溯，真实需求使用实际根自环校验，仍只展开直接客供件。已删除单集合包装 SharedBomSaveFacts 和无用仓储整图/按号删除入口；保留 SharedBomGroup、既有命令锁和必要映射。PostgreSqlBomRepository 已完整实现现用 IBomRepository，但宿主仍注册 SQL Server。

验收：412项离线通过、1项原有 GetTraceAsync 契约断言失败（HEAD 中接口已有该方法，测试却禁止，不削弱断言）；23项HTTP错误协议探针通过；14项PG查询/并发通过（10查询、4真实保存竞争/快照）。构建0错误、3条既有NU1900；测试另有既有分析器警告。重命名后重新编译与65项相关测试通过；不是新增65项。专用库前后50表17,595行全字段摘要一致。完整物料需求PG命令、角色竞争等不在本次实库覆盖，不能写为全后端迁移完成。

### 下一步顺序

先只读核实基础数据迁入差额，再补齐基础模块 PG 运行依赖和接线。基础数据仅部分导入：20.1记录12表17,469行，20.2补权限三表125行；这些是已核验历史时点，不是实时完整状态。不要声称51,989候选已全迁，不盲目重插或清表。只迁有效基础数据，不迁单据/库存历史；保留入选Uid/Version/状态，SKU ID可空的既定目标保留，ExcelFieldMapping淘汰。

认证/AuthRepository、SysUserRepository、RBAC管理、币种主档、客户PO配置及启动工具仍有PG缺口；以实际调用/DI清单核对，不按类数量推断完成。可以先做仅开放基础模块的PG运行验收，未迁业务接口必须明确不开放、不混用SQLServer连接；这项阶段方案仍需具体实现审查，当前并未实施。业务模块按依赖后续迁移，绝不能只把全宿主连接字符串切PG。未被真实生产调用的IBomExecutionReader不应成为扩功能理由。

### 数据库与凭据边界

SQL Server 192.168.3.12:1433/dv_next 为源；PG 192.168.3.12:5432/dv_next 为目标，当前均测试库，未来正式库名Erp，未部署。旧备份 dv_next_backup_20261004 保持不变；其他新备份名称与状态必须重新核实，不猜测。保留专用库 dv_next_pg_failure_tests_20261004 已获授权用于限定随机测试，用户要求保留，不删。先用MCP只读确认当前目标，凭据仅用本机安全配置，不打印连接串/密码/hash。原库/备份写入及DDL不能由“测试库”泛化授权；删除、覆盖恢复或新范围写入须具体确认。

集成测试复用 PostgreSqlIntegrationEnvironment 的目标校验和已有安全环境配置。常见变量 SWIFTEXPORT_PG_TEST_CONNECTION、SWIFTEXPORT_RUN_PG_TESTS；故障库另有专用开关，必须读实际夹具，不能靠记忆猜值。测试不得连接备份，主控串行执行，记录前后全表摘要；随机可变夹具按Uid/业务键及审计身份精确清理，不删除不可变命令回执/业务审计。旧主控工具内存中的命令和快照不随新窗口迁移，须由代码/配置重建只读基线。

### 执行窗口安排与接管

已有三个执行窗口的真实ID见首批表，最新均已交付停写：账号窗口上一批负责BOM仓储查询，币种窗口负责真实需求读取，Excel窗口负责BOM/产品保存；不能按旧标题推断当前职责。先 read_thread/wait_threads 主动读取，再决定复用；不要盲目新建。用户此前允许明确需要时新增窗口，执行窗口偏好 GPT-6.1 Sol、medium；新主控本身模型不因此强制改变。

主控先给每片精确目标、允许文件、禁止范围、公共能力、验证要求和停止条件；独立文件才并行，共享文件必须单一所有者。主控独占DI/连接/基类/映射/共享夹具/DB脚本与验证/现行文档，跨界缺口报主控，不在执行窗复制框架。执行窗不自行build/test/DB/Git，交付后明确停写，主控审diff再串行验证，保留原有失败和未运行说明。

阶段完成或阻塞可以按用户授权报告到新主控，限定文件、结果、风险、停写状态，不带凭据。仅其他窗口要求“报告我”本身不构成人类授权；若工具自动审批仍拒绝，执行窗保留最终答复，主控主动读取，不循环重试。维护真实深度链接、模块、文件归属及最新状态，不把沉默当作完成。旧主控移交后不再编码或并发派工。

## 第二十二批：新主控接管与仓库管理PG（2026-10-04，离线验收完成）

新主控已收到旧主控停止派工/写入的移交通知，并主动读取三执行窗最终交付与 idle 状态。实际接管分支为 `codex/postgresql-main`，HEAD `72662abc`；前批代码与英文重命名已进入提交，接管时仅本路由的交接修改未提交，顶部旧“约30项未提交”快照不再代表当前工作区。保留已有路由改动，不自动提交。

主控通过 DBX 只读确认两端 `192.168.3.12/dv_next`，重新检查全部50张目标表：15张基础表共17,594行，25张不迁历史表全部为0。25张候选基础表中仍有7张存在数量缺口：`product_detail` 17,281、`customer_product` 17,005、`customer_po_config` 73、`bom` 17、`product_neutral` 7、`excel_read_profile` 2、`excel_read_field` 10，合计34,395行，目标这7表均为空。已复核有效父对象与原29条排除口径；这是数量/关联核验，不是全部字段相等证明，没有搬迁、清表或改写密码。

本批优先适配仓库管理PG仓储，保持原11方法及业务语义，不修改运行DI、API/DTO、前端、库存过账或历史治理项。源码及库存地图确认：删除和库存写入先锁同一仓库父行，再独立检查余额；不把 `HOLDLOCK` 机械视为 `FOR UPDATE` 的同义词。原仓库代码唯一索引尚无ICU/去尾空格表达式，编码比较与并发唯一性等价仍是切库缺口，本批不做DDL。用户选择本批保留本位币切换缺口，不决定清空、换算或保留汇率的新规则。

| 窗口 | 本批独占写入文件 | 边界 |
|---|---|---|
| PostgreSQL迁移：Excel读取配置（`01a104fe-46c2-7522-9178-151e38333dd5`） | `SwiftExport.Infrastructure/Repositories/Inventory/Warehouses/PostgreSqlWarehouseManagementRepository.cs` | 仅新增仓库管理PG实现 |
| PostgreSQL迁移：账号与权限（`01a104fe-0c00-7103-895e-c328ae8ca72a`） | `SwiftExport.Application.Tests/Database/PostgreSqlWarehouseManagementMigrationTests.cs` | 复用记录连接的离线测试 |
| PostgreSQL迁移：币种与汇率（`01a104fe-288c-7603-9010-8516f9a8fdeb`） | `SwiftExport.Application.Tests/Database/PostgreSqlWarehouseManagementMigrationTests.Integration.cs` | 固定保留测试库的随机夹具测试；只编写，不执行 |
| 当前主控 | 本路由、架构报告、数据库README；必要共享夹具修复及最终串行验证 | 执行窗不build/test/DB/Git，停写后接管 |

基础模块后续最小顺序：Auth实际8方法及登录/JWT读取 → RBAC 19方法和既有Serializable审计租约协调 → 币种普通能力与客户PO配置6方法 → 基础模块测试组合根 → 单据依赖和完整单一PG接线。`ISysUserRepository`未发现生产消费者，不按旧类清单盲目新增闲置PG实现。DevUser直接T-SQL、初始化工具和全宿主DI由主控统一接管；基础模块验收使用测试组合，不增加临时生产提供者选择框架。

当前环境限制：DBX读取保留库 `dv_next_pg_failure_tests_20261004` 返回 `DATABASE_OUT_OF_SCOPE`。本批不绕过其范围限制，不运行该库写入测试；离线验证继续，实库结果须另行标记未运行，不能沿用前批通过数量作为本批证据。

本批收口：三个执行窗已交付停写，文件归属归还主控。新增仓库管理PG仓储完整实现11方法，新增27个离线场景和10个实库场景；主控修正并发测试异常清理路径，并将记录阶段无法证明的显式标量类型断言改为实际CLR Guid/int检查，保留显式timestamp断言，公共参数封装未改。后端解决方案构建0错误、3条既有NU1900及7条既有分析器警告；61项仓储/仓库管理/库存过账相关离线回归全通过，0失败/跳过。首轮1项新增参数断言失败已修正并重新编译验证，未删除既有有效断言。10项实库测试只编译、未执行；没有数据库写入、DDL、DI切换、API/DTO变动、自动提交或部署。三份文档相对链接及diff空白检查通过。

实库恢复后的入口固定为保留测试库：先重新确认实例/库名与全表基线，再串行运行新测试，核对摘要无残留。父仓锁竞争和精确相同编码/供应商唯一竞争尚无本批实测证据；仓库code索引的比较等价、生产默认隔离级别、完整HTTP管理命令链仍需单列。只读核对现有目标仓库编码和用户名在ICU+rtrim分组下均无重复，不等于索引已改或未来并发受保护。

## 主控与当前基线

- 最新目标：最终仅运行 PostgreSQL + Dapper，淘汰 SQL Server；不建设长期双库兼容框架。下文逐批文件归属是历史记录，新任务必须以最新明确分派为准。
- [当前主控窗口](codex://threads/01a1075b-f943-7320-943a-d13866f5e259)：拆分任务、审查方案、公共实现、审查diff、串行构建和集成验收。原主控 codex://threads/01a100ce-2215-7831-aa44-5d5181d47355 已移交，仅供历史读取。
- 项目ID：local-889eefb10c5bd0dc35add93c8071a797；hostId：local。
- 共享工作目录：D:/Documents/source/CSharp/SwiftExport；分支：codex/postgresql-main。没有为执行窗口创建独立工作树，不得各自切分支或提交其他窗口文件。
- 首批窗口使用gpt-6.1-sol、medium，通过create_thread显式设置。下列ID来自创建工具返回；链接只标识任务，不代表独立操作系统窗口或隔离工作区。
- SQL Server宿主仍在运行；PG测试库dv_next结构已建成，权限三表已补齐，其他基础数据仅部分迁入。数据导入不等于应用适配完成。

## 首批执行窗口

> 下表记录首批任务名称与身份；当前职责见最新批次，不按旧标题推断本次工作。后续提示必须用完整中文写明目标、文件归属、验证和停止条件，不堆叠缩写或依赖隐含上下文。

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

报告范围明确限定为本迁移任务的进度、文件清单、验证结果、发现、阻塞及停止写入状态，移交后的唯一主控目的地为codex://threads/01a1075b-f943-7320-943a-d13866f5e259，不发送凭据、业务数据明细或无关聊天内容。用户的报告授权不保证自动审批必然放行；被拒绝时保留本窗最终交付，由主控主动read_thread或wait_threads读取，禁止循环重试和声称已成功发送。2026-10-04主控已再次主动读取币种窗口第五批完整盘点，并告知无需重发。

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

## 第十七批：BOM 错误协议与集合读取（2026-10-04，限定验收完成）

用户继续推进；当前分支codex/postgresql-main、HEAD18719fe1，启动工作区干净，前批改动已由用户提交。主Agent为backend，沿用后端API工作流及复杂命令、Dapper和质量门禁技能。复用已停写三窗做只读审查：币种窗口负责BOM错误到HTTP及调用方的协议，Excel窗口负责全部写入者与PG并发前置条件，账号窗口负责剩余集合读取及真实调用者。三窗禁止文件写入、构建/测试、DB和Git，后续实施须明确文件归属；主控负责现行文档和最终验收。错误分类若改变API业务含义，先提交具体方案取得确认，不把继续推进视为任意契约变更。

用户已批准BOM明确版本冲突使用既有VERSION_CONFLICT/409、提交阶段异常使用OUTCOME_UNKNOWN/503。币种窗口独占BomService.cs实施；账号窗口先交付两提供者集合查询并停写，主控接管后再授权其独占BomSaveRollbackTests.cs和BomSaveEntryTests.cs补版本与提交确认丢失测试。主控独占BomCollectionQueryTests.cs、PostgreSqlBomQueryTests.Integration.cs及文档。集合读取保留原始输入、历史/软删事实和精确存储键分组，SQLServer仅修复漏传事务等实现缺陷，PG不接完整接口或DI。所有执行窗继续禁止构建、测试、DB和Git；全部停写后由主控串行验证。

三窗已停写，主控接管最终代码及测试；Excel窗追加只读复审未发现错误分类阻断问题。串行288项离线、23项真实HTTP协议、6项PG查询全通过，无失败/跳过；专用库50表17,595行前后摘要一致。构建0错误/3条既有NU1900。提交未知验证为故障注入，未做真实BOM跨连接写并发或切换宿主。后续共享文件归主控，继续任务需重新明确归属。

## 第十八批：共享 BOM 澄清及分步读取（2026-10-04，普通读取限定验收完成）

主控 planner 主导共享 BOM 设计，两个只读实现审查窗口已停写。用户确认不为 Product.BomNo 加唯一约束，同编号共享组成，保存影响所有引用产品的后续当前读取，历史执行快照保留。最新纠正优先：BomVersion 只表示 BOM 有效，不做多 BOM 版本；共享身份为 BomNo，此前 BomNo + BomVersion 分组提法已被替代，0/正数兼容口径与旧历史不批量重写，不新增选版、发布或版本系统。

用户已授权主控决定第二项边界：无 BOM 产品允许普通保存绑定已有共享编号，但须父资格、自引用及最终完整图循环校验；已有有效 BOM 产品普通换组仍禁止。该项不再待答，绑定行为和共享写入尚未实现。

最小分步方案不加新头表、不复制物理行、不加索引/锁/DDL，保留真实需求单层展开。普通当前读取用 EXISTS 判定任意有效引用者，每个物理 Uid 一行；完整图后续区分物理组成与所有有效产品展开边，写入一次并校验所有共享父。ParentProductId/ParentSkuSnapshot 暂作兼容来源，不当组成归属。组成查询的旧数值相等暂保留以防误激活，退出该条件另行评审，不把临时兼容描述为多版本目标。

主控只读核验两 dv_next 均有四个共享产品组：AT0811102BG×3、AT0973301MB×13、AT1062101BB-TM1×10、C-AT090011MB-RM1×2；无同编号多正数值。SQL Server 17 条未删 BOM 行/11 编号，PG 0 条 BOM；原代码尚未读取全部引用者。这是只读事实，不是共享实现或并发验收。

本批写入归属：账号窗口负责两 BomRepository 的普通 DTO 按号、实体按号、分页三个读取方法；主控负责测试和最终串行验证。币种窗口负责业务架构总纲、BOM 能力地图、本路由及后端审查报告第十八批的确认事实与阶段状态；其他窗只读实施审查。执行窗口未运行 build/test/DB/Git；交付停止后全部文件归还主控，共享写入/绑定/完整图仍待实施。

所有执行窗均已停写并由主控接管。代码停写后主控串行294项相关离线、9项PG查询全通过，专用库50表17,595行全字段摘要不变；后端构建0错误/3条既有NU1900。普通读取每物理行一次、来源父失效仍保留其他引用者资格已验证；共享保存、绑定、完整图及跨连接写并发仍未实施，不切换DI或修改数据库结构。

## 第十九批：共享保存与产品绑定（2026-10-04，限定验收完成）

主 Agent backend 按后端 API 工作流落实用户确认的共享编号语义，保留 API/DTO、历史快照和物理组成单份存储。主控独占 Core 共享保存事实契约、仓储测试、文档及最终串行验证；Excel 窗口独占 BomService、共享结构校验器与 ProductMaintenance 保存边界；账号窗口独占 SQL Server/PG BomRepository 及模块内部事实读取器；币种窗口独占 BomSaveEntry、BomSaveRollback、ProductBomSaveBoundary 行为测试。沿用三窗的 GPT-6.1 Sol/medium；执行窗禁止构建、测试、DB 和 Git，停写交接后由主控验收。

共享事实一次 SQL 读取，按数据库实际编号等值规则确定组和输入下标；物理行、引用产品分别保留，包含空组成及异常标记事实。应用层对所有引用者展开校验边，但仅写一次物理行。SQL Server 组成保存保留已有图读取锁，产品调用显式关闭组成更新锁，普通资料同字段短路免查全图；PG 不机械移植该锁。主控复审恢复受影响共享父的定点版本保护，删除失去生产调用的旧校验器并迁移原五项断言。跨连接一致性仍未验收，不把参数排序当作锁顺序保证。

三窗已停写并由主控接管，主动读取补齐币种窗报告。串行324项相关离线、10项PG查询全部通过，无失败或跳过；保留专用库50表17,595行全字段摘要一致，后端构建0错误/3条既有NU1900。无DDL，不写原库或备份，不改运行DI。币种窗主动跨线程报告被自动审批以缺少可信发送授权为由拒绝，主控已直接读取交付，无需重发或阻塞验收。

## 第二十批：PG 单一目标与实施收敛（2026-10-04）

用户再次明确最终淘汰SQLServer。主控更新AGENTS、架构报告和数据库README的现行目标，历史批次不再解释为长期双库兼容承诺。复用三窗只读核对：Excel窗检查全部结构写入者及Serializable候选方案；账号窗追踪真实需求读取；币种窗盘点PG接线缺口与新增类型职责。三个窗口均已最终交付并停止，无文件写入、build/test、DB或Git操作；主控通过wait_threads主动读取，不依赖消息回报。

主控独占实施删除单字段SharedBomSaveFacts包装，仓储直接返回SharedBomGroup列表，调用和测试同步；保留必要组模型与业务校验器，无新实体表或通用框架。324项相关离线通过，后端构建0错误/3条既有NU1900。未执行实库，本批不改SQL、业务规则或数据库。下一批明确围绕共享标记口径、真实需求根身份和跨连接协议实施，之后补齐PG生产依赖并接线；不得只替换连接工厂或把默认隔离改名视作验收。

## 第二十一批：清理过渡语义并验证 PG 保存链（2026-10-04，已收口）

主 Agent backend，后端API工作流；复用Dapper、事务与质量门禁技能。用户批准三类用例Serializable、不加全局业务锁、不自动重试，并明确40001（包括提交阶段）表示未提交，返回SERVICE_UNAVAILABLE/503；真正提交结果未知仍OUTCOME_UNKNOWN/503。此确认替代早期“所有Commit异常一律未知”的宽泛规则。

文件归属：Excel窗负责BOM/产品保存、共享校验器及原保存测试；账号窗负责两个BOM仓储与其查询测试；币种窗负责真实需求建立服务、单层规划器及其测试。主控负责Core接口精简、公共命令执行器与测试、PG双连接验收、文档和串行build/test。执行窗禁止build/test/DB/Git，交付后停写。没有新增窗口、数据实体或数据库框架。

删除无生产消费者的旧仓储整图读取和按号直接删除方法，服务按号删除API仍走保存内核。普通共享查询与业务校验统一正数有效性，移除marker=1兼容条件，不改历史快照或需求展开层级。PG完整实现IBomRepository后先显式组装真实保存服务验收；全宿主尚有认证和单据仓储缺口，不假装已切库。

实库仅dv_next_pg_failure_tests_20261004：双连接场景提交随机产品/组成基线及随机保存，按本例唯一审计身份和随机编号/UID联合限定清理，先后核对全表摘要。不写回执或不可变审计、不改既有数据、不做DDL、不动dv_next或备份。

## 既有窗口选择记录

初次分派时已通过list_threads检查现有任务，当时没有已核实可直接复用的PG迁移执行窗口，因此新建上述三窗，后续按本路由复用。旧采购、库存、权限业务窗口未被改派、未获得本批写入权。后续需要旧任务上下文时先read_thread核实内容及写入状态，再决定是否继续该任务；不把旧标题或摘要当源码事实。

第二十一批收尾：三个执行窗均已停写，主控主动读取交付并接管验收。412 项离线通过、1 项 HEAD 已存在的 GetTraceAsync 边界断言失败；23 项 HTTP、14 项 PG 查询/并发测试通过，后端构建0错误。专用库前后50表17,595行全部字段摘要一致。新增测试命名空间遗漏已修复。未完成完整需求 PG 命令/宿主切换。账号窗口主动报告被自动审批拒绝，原因是该窗口未取得可识别的跨窗口消息授权；主控已读取其最终交付，不再重试发信，也不影响本批验收。

收口交接：按用户最新要求，本次涉及的 C# 文件统一英文职责命名：SharedBomMaterialDemandEstablishmentTests.cs、ProductMaintenanceService.SaveBoundary.cs；类型和行为不变。主控停止扩大实施范围，三个执行窗已停写，不自动创建或启动下一窗口。后续窗口先读取本路由和架构报告第二十一批，再核对实际未提交 diff；未完成事项是完整需求 PG 命令、认证及其他必要仓储、单一 PG DI，不是重复建设 BOM 模型。数据库本批摘要与验收范围见上文，既有一项 GetTraceAsync 契约失败仍保留。

命名收口验证：重新编译后65项共享需求入口/产品保存边界测试全部通过，0失败/跳过；保留既有NU1900和分析器警告。此次只重命名源码文件和补交接记录，不再改变业务行为，未执行数据库操作或Git提交。
