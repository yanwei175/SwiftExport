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

## DapperRepository<T>

`DapperRepository<T>` 位于 `SwiftExport.Infrastructure/Repositories/Base/DapperRepository.cs`。

当前职责：

- 使用 `RepositoryBaseContext`。
- 从 `DbSession` 取得 `Connection` 和 `Transaction`。
- 使用 `IAuditStampService` 打审计字段。
- 使用 `EntityMetadataCache` 获取表名。
- 新增时生成顺序 Guid。
- 更新时使用 `Version` 做乐观并发。
- 删除默认软删除。

## SQL 规则

- 查询参数化，禁止拼接用户输入。
- 列表查询尽量不使用 `SELECT *`；泛型基础查询除外，业务查询必须显式字段。
- 分页查询必须有稳定排序。
- 联表 DTO 查询必须显式字段别名，匹配 DTO 属性名。
- SQL 执行必须传 `transaction: Transaction`。
- `WITH(NOLOCK)`、`UPDLOCK`、`ROWLOCK` 按业务一致性选择，不要机械复制。

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
