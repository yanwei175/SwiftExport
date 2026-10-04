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

## 后续依赖队列（未创建窗口，未派发）

- 主数据：产品/型号/中性产品/BOM、客户及客户产品、供应商及供应商产品、仓库候选及管理。先建立引用冻结与比较规则边界，再并行分配不重叠文件。
- 公共命令基础：取号、命令回执、审计；明确取号竞争、CommandId重复与请求指纹语义后，由主控决定复用现有执行窗口或新建。
- 销售与售后、需求计划、采购、库存出入库、PackingList及ForExcel订单查询：依赖公共命令基础及主数据适配。按现有用例切片，不把同一事务拆成多个独立提交。
- 收尾：后台任务与启动代码SQL、完整DI闭包、真实API链路、剩余有效基础数据、SQL Server回归及PG切换验收；Linux部署独立处理。

## 既有窗口选择记录

已通过list_threads检查现有任务。当前没有已核实可直接复用的PG迁移执行窗口，因此新建上述三窗；旧采购、库存、权限业务窗口未被改派、未获得本批写入权。后续需要旧任务上下文时先read_thread核实内容及写入状态，再决定是否继续该任务；不把旧标题或摘要当源码事实。
