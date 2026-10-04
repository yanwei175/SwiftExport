# 实体驱动数据库基线

数据库代码预期以 `SwiftExport.Core` 中带 `[Table]` 的持久化 Entity 及其 `DatabaseSchema` 元数据为源，实际部署必须另行核验。当前运行用的SQL Server结构基线是 `Generated/SwiftExport.Schema.sql`；PostgreSQL评审基线是 `Generated/SwiftExport.PostgreSql.Schema.preview.sql`，后者尚未达到部署门槛。两者禁止手工修改，不承担旧库迁移、数据回填或种子数据职责。表和字段中文说明同源于Core XML `<summary>`，分别生成MS_Description和COMMENT。人工维护的数据搬运脚本必须与结构基线分离，并明确限定数据范围。

## 生成与验证

1. 修改 Entity 的列映射、`DbIndex`、`DbCheck`、逻辑关系、序列或不可变规则。
2. 运行 `dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj` 重新生成基线。
3. 运行 `dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj -- --verify` 检查提交文件是否漂移。
4. 先确认明确隔离的实例与库名，再执行数据库写入验证，并核对表、列、`MS_Description`、索引、检查约束和序列；`sys.foreign_keys` 必须为零。随机库名或已有备份不能替代目标确认。
5. Entity 元数据与生成文件一并提交。

生成器只面向已经创建且没有用户表的 SQL Server 数据库；不会创建或删除数据库。固定、自引用和多态关系只生成可追溯注释与必要索引，严禁生成 `FOREIGN KEY`。业务种子继续由 `_tools/DbSeed` 显式执行。

SQL Server建表器与通用仓储共用 `EntityMetadataCache`，分别保留属性名、物理列名、schema/表名及参数名。当前 `PurchaseOrderLine.Quantity` 仍映射物理 `QTY`；现有对象和SQL Server基线未改变。PostgreSQL通过独立映射和生成器产出小写名称的结构评审脚本，目标测试库已建表并部分导入有效基础数据及账号权限；通用CRUD与部分业务仓储已验收，但运行宿主尚未切换。实体描述代码预期，生成文件不是部署完成证明，逐批核验状态以[架构审查](../../docs/后端整体架构与文件夹职责建议.md)为准。

PostgreSQL本地生成与漂移检查（不连接数据库）：

```powershell
dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj -- --provider postgresql
dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj -- --provider postgresql --verify
```

preview脚本已在测试库落地并完成结构对照和部分写入验收，不代表全业务可切库。产品引用冻结只检查可见事实，缺少并发新增引用协调；非字典编码唯一索引、完整Unicode比较及模块错误协议仍有未完成项。因此不能直接拿此脚本初始化正式环境。默认宿主仍注册SQL Server连接和仓储。

PostgreSqlDapperRepository已实现独立通用CRUD，并复用DbSession与共享映射；PostgreSqlParameters显式处理uuid数组、枚举整数、timestamp与UTC时间绑定。通用CRUD、部分业务查询及限定事务/并发场景已通过实库验证，未注册到运行宿主，不能仅替换连接工厂就启用PG。具体覆盖及未运行项见架构审查顶部逐批验收，不把历史测试数量当作当前全量结果。

字典类型和明细现已各有独立PG专用仓储（尚未注册到宿主）。预览生成器只为这两个实体的文本列增加ICU二级比较规则及建表前自检，DictCode查询与索引统一忽略ASCII尾空格；客户供应商仅对下述两个编码索引采用显式规则，不改变其列级比较。完整Unicode及其他用例不能推广成“全后端已兼容”，逐批覆盖以架构审查为准。

## 客户供应商编码索引（2026-10-04，限定范围验收完成）

[变更脚本](PostgreSQL客户供应商编码索引变更.sql)和[回退脚本](PostgreSQL客户供应商编码索引回退.sql)固定192.168.3.12:5432的dv_next_pg_failure_tests_20261004与dv_next。用户批准后已先在专用库演练、提交及测试，再同步dv_next两个活动编码索引，数据与列未变。固定目标、版本、ICU或实际索引状态不符会拒绝；脚本不是幂等启动逻辑，不得对已完成库盲目重复执行。它们是已有库人工迁移文件，与Generated空库基线分开。

采用rtrim(code,' ')与既有ICU的Unicode差异已获确认；它不完全仿真SQL Server。普通索引替换短时阻断两表读写，锁等待每次至多5秒、单语句30秒、事务60秒；失败不得吞异常后继续提交，提交确认丢失先只读核实状态。回退守卫已填入演练真实表达式，回退路径演练通过。唯一性10项及客户供应商15项实库回归通过且无新增残留；生成器与PG基线已同步，生成文件仅两行索引变化，SQL Server基线不变。

旧dv_next_backup_20261004保持变更前恢复点；新dv_next_backup_20261004_153556包含更新后的结构，与dv_next的50表/17,594行、全部列索引约束、触发器函数、排序规则和序列状态一致。运行库与代码基线同步，旧备份不追随迁移修改。已有备份不代表未来DDL自动获批；本次授权只覆盖这两个索引与新备份，应用仍运行SQL Server。

## 重建库时复制主数据

最新补充（2026-10-04）：权限数据已补齐，sys_permission 111条、sys_role_permission 12条、sys_user_role 2条在同一事务导入，逐字段差异及有效引用缺失均为0；已有账号和角色未覆盖，停用用户的关联仍排除。其他基础数据迁移仍未完成，应用未切换。下文空表和17,469条为此前时点，当前权限验收详见架构审查第20.2节。

2026-10-04最新执行状态：用户已手动执行PG建表脚本。MCP只读核验dv_next实际50表/1076列，逐列类型、可空性、默认值及字典排序规则无差异；93额外索引、50主键、110CHECK、10触发器/函数、1序列数量符合预期，10函数体逐一匹配。7项ICU比较样例通过，50表均0行。结构已落地，但写入、唯一冲突、事务并发和数据迁移尚未验收，应用仍默认SQL Server。当前事实见架构审查第19节，第18节工具阻塞仅为历史记录。

后续实库DML验证已通过插入提交、失败批次回滚、大小写/尾空格唯一冲突和双连接Version竞争，测试记录已按专用Uid清理。最新只读复核：12张基础表已有17,469条，余下38表为空，候选51,989条尚未全部迁完。DATABASE_OUT_OF_SCOPE可用不访问表的多层CTE常量查询复现，单层写法及普通表读取成功，证据指向DBX范围解析误判，不需要用户继续放宽权限。继续迁移前重新取得必要源快照，核对已落库记录，禁止盲目重插。详见架构审查第20.1节，不将行数一致或局部写入验证当作全部业务验收。

本节脚本只适用于同版本SQL Server之间的主数据复制。用户已选择的PostgreSQL路线是“仅迁有效基础数据及账号权限，单据全部不迁”；现有脚本既不支持跨提供者，也不执行此次有效记录筛选，不能直接作为迁移工具。逐表范围、29条关联清理建议及1076字段命名方案见[架构审查第13节](../../docs/后端整体架构与文件夹职责建议.md)。PostgreSQL结构已初始化，基础数据部分迁入，应用仍默认SQL Server。

同一 SQL Server 实例内重建数据库时，先在空目标库执行生成基线，再修改并执行 `主数据跨库复制.sql` 顶部的源库名和目标库名。该脚本只复制明确列出的主数据、账号和权限关系，要求目标主数据表为空，不复制业务单据、库存事实或操作日志。
