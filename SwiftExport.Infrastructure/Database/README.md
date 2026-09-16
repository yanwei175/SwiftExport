# 实体驱动数据库基线

数据库结构以 `SwiftExport.Core` 中带 `[Table]` 的持久化 Entity 及其 `DatabaseSchema` 元数据为唯一事实源。仓库只提交 `Generated/SwiftExport.Schema.sql`，该文件禁止手工修改，也不承担旧库迁移、数据回填或种子数据职责。

## 生成与验证

1. 修改 Entity 的列映射、`DbIndex`、`DbCheck`、逻辑关系、序列或不可变规则。
2. 运行 `dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj` 重新生成基线。
3. 运行 `dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj -- --verify` 检查提交文件是否漂移。
4. 在随机命名的空 LocalDB 中执行基线，并核对表、列、索引、检查约束和序列；`sys.foreign_keys` 必须为零。
5. Entity 元数据与生成文件一并提交。

生成器只面向已经创建且没有用户表的 SQL Server 数据库；不会创建或删除数据库。固定、自引用和多态关系只生成可追溯注释与必要索引，严禁生成 `FOREIGN KEY`。业务种子继续由 `_tools/DbSeed` 显式执行。
