# 仓储基类与 Dapper 规则

## IRepository<T>

`IRepository<T>` 位于 `SwiftExport.Core/Interfaces/IRepository.cs`。

当前只包含轻量通用能力：

- `ExistsIdAsync`
- `GetExistingIdListAsync`
- `GetByIdAsync`
- `GetByIdListAsync`
- `GetSingleEntityAsync`
- `GetAllAsync`
- `AddAsync`
- `UpdateAsync`
- `DeleteAsync`

不要把分页、动态查询、批量保存、复杂联表塞回泛型仓储。

## PostgreSqlDapperRepository<T>

`PostgreSqlDapperRepository<T>` 位于 `SwiftExport.Infrastructure/Repositories/Base/PostgreSqlDapperRepository.cs`。

当前职责：

- 使用 `RepositoryBaseContext`。
- 从 `DbSession` 取得 `Connection` 和 `Transaction`。
- 使用 `IAuditStampService` 打审计字段。
- 使用 `EntityMetadataCache` 与建表器共享 schema、表、属性、物理列及参数映射；例如 Quantity 写 quantity、参数仍为 Quantity。
- 新增时生成顺序 Guid。
- 更新时使用 `Version` 做乐观并发。
- 删除默认软删除。

## SQL 规则

- 查询参数化，禁止拼接用户输入。
- 通用实体查询使用显式列及 CLR 属性别名；业务查询继续显式投影。禁止为兼容 Column 添加影响全部 DTO 的 Dapper 全局映射。
- 分页查询必须有稳定排序。
- 联表 DTO 查询必须显式字段别名，匹配 DTO 属性名。
- SQL 执行必须传 `transaction: Transaction`。
- 锁定读取使用 PostgreSQL `FOR UPDATE` 等现有协议，由调用方确定事务、锁集合和稳定顺序；不写 SQL Server 锁提示。

## 写入规则

- `AddAsync`：打创建/更新审计，设置 `IsDeleted=false`，生成 `Uid`，初始化 `Version`。
- `UpdateAsync`：打更新审计，按 `Uid + Version` 更新，数据库中 `Version + 1`。
- `DeleteAsync`：默认软删除，按 `Uid + Version` 检查并发。

## 禁止事项

- 禁止 Repository 引用 Application。
- 禁止 Repository 调 Controller。
- 禁止 Repository 自己开启业务事务。
- 禁止为单个模块扩大泛型 Repository。
- 禁止把页面展示字段塞进 Entity。
