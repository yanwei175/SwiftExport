# 实体驱动数据库基线

数据库结构以 `SwiftExport.Core` 中带 `[Table]` 的持久化 Entity 及其 `DatabaseSchema` 元数据为唯一事实源。表和字段的 SQL Server `MS_Description` 取自实体类型及属性的中文 XML `<summary>`；未编写 `<summary>` 的成员会生成稳定占位说明，避免出现无描述的表或字段。结构基线只由 `Generated/SwiftExport.Schema.sql` 承担，该文件禁止手工修改，也不承担旧库迁移、数据回填或种子数据职责。人工维护的数据搬运脚本必须与结构基线分离，并明确限定数据范围。

## 生成与验证

1. 修改 Entity 的列映射、`DbIndex`、`DbCheck`、逻辑关系、序列或不可变规则。
2. 运行 `dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj` 重新生成基线。
3. 运行 `dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj -- --verify` 检查提交文件是否漂移。
4. 先确认明确隔离的实例与库名，再执行数据库写入验证，并核对表、列、`MS_Description`、索引、检查约束和序列；`sys.foreign_keys` 必须为零。随机库名或已有备份不能替代目标确认。
5. Entity 元数据与生成文件一并提交。

生成器只面向已经创建且没有用户表的 SQL Server 数据库；不会创建或删除数据库。固定、自引用和多态关系只生成可追溯注释与必要索引，严禁生成 `FOREIGN KEY`。业务种子继续由 `_tools/DbSeed` 显式执行。

建表器与通用仓储现共用 `EntityMetadataCache`，分别保留属性名、物理列名、schema/表名及参数名。当前 `PurchaseOrderLine.Quantity` 仍映射物理 `QTY`；本次解耦不重命名对象，生成结构基线未改变。实体描述代码预期，实际数据库必须另行只读核验，不能把生成文件视为部署完成证明。PostgreSQL 建表、类型、命名与数据迁移尚未实施，核验状态见[架构审查](../../docs/后端整体架构与文件夹职责建议.md)。

## 重建库时复制主数据

同一 SQL Server 实例内重建数据库时，先在空目标库执行生成基线，再修改并执行 `主数据跨库复制.sql` 顶部的源库名和目标库名。该脚本只复制明确列出的主数据、账号和权限关系，要求目标主数据表为空，不复制业务单据、库存事实或操作日志。
