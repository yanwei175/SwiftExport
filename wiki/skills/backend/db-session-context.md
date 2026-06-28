# Skill: SwiftExport DbSession 与 RepositoryBaseContext 使用规范

## Description

当任务涉及以下内容时使用本 Skill：

- `RepositoryBaseContext`
- `DbSession`
- `Connection`
- `Transaction`
- Dapper Repository 构造函数
- 开启、提交、回滚事务
- 多个 Repository 共用同一事务上下文

本 Skill 是后端 Dapper 仓储类 Skill 的基础规则。

## Instructions

### 1. 核心对象

本项目后端仓储层通过 `RepositoryBaseContext` 聚合基础依赖。

`RepositoryBaseContext` 提供：

- `Session`
- `LoggerFactory`
- `AuditStamp`

`DbSession` 提供：

- `Connection`
- `Transaction`

Repository 不应自行创建数据库连接，应使用当前 `DbSession`。

### 2. 必读文件

处理连接或事务前，必须先阅读：

- `wiki/index.md`
- `wiki/project/project-map.md`
- `SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs`
- `SwiftExport.Infrastructure/Data/DbSession.cs`
- `SwiftExport.Infrastructure/Repositories/Base/DapperRepository.cs`
- 当前业务模块已有 Repository

### 3. Repository 构造规则

优先使用 `RepositoryBaseContext`：

```csharp
public XxxRepository(RepositoryBaseContext context) : base(context)
{
}
```

如果是特殊仓储，没有继承 `DapperRepository<TEntity>`，也应优先注入 `RepositoryBaseContext` 或 `DbSession`，保持同一个连接与事务上下文。

### 4. 事务开启规则

本项目当前仓储事务模型以 `DbSession.Transaction` 为准。

开启事务：

```csharp
// 中文注释：在当前 DbSession 上开启事务，确保后续仓储操作共享同一个事务上下文。
_session.Transaction = _session.Connection.BeginTransaction();
```

Dapper 执行 SQL 时必须传入当前事务：

```csharp
await Connection.ExecuteAsync(sql, parameters, transaction: Transaction);
```

查询也应按现有仓储基类模式传入事务：

```csharp
var item = await Connection.QueryFirstOrDefaultAsync<XxxDto>(
    sql,
    parameters,
    transaction: Transaction);
```

### 5. 提交与回滚规则

提交事务：

```csharp
// 中文注释：提交后必须释放事务对象，并把 Session 上的事务引用清空，避免后续操作误用旧事务。
_session.Transaction?.Commit();
_session.Transaction?.Dispose();
_session.Transaction = null;
```

回滚事务：

```csharp
// 中文注释：异常时回滚当前 DbSession 事务，确保本次业务保存不会残留半成功数据。
_session.Transaction?.Rollback();
_session.Transaction?.Dispose();
_session.Transaction = null;
```

异常处理推荐结构：

```csharp
try
{
    // 中文注释：事务开启在当前 DbSession 上，保证同一作用域内的仓储操作共享事务。
    _session.Transaction = _session.Connection.BeginTransaction();

    await SaveHeadAsync(head);
    await SaveLinesAsync(lines);

    _session.Transaction.Commit();
}
catch
{
    _session.Transaction?.Rollback();
    throw;
}
finally
{
    _session.Transaction?.Dispose();
    _session.Transaction = null;
}
```

### 6. 与 UoW 的关系

项目中存在 `IUnitOfWork` 和 `UnitOfWork` 文件，但新增代码不要默认以 UoW 作为首选事务方案。

规则：

- 优先观察当前业务模块已有事务写法。
- 如果模块使用 `DbSession.Transaction`，继续沿用。
- 如果模块明确使用 `IUnitOfWork`，再按模块既有模式处理。
- 不要在同一个业务流程中混用两套事务风格。

### 7. 禁止事项

严禁：

- 在 Repository 中硬编码连接字符串。
- 绕过 `DbSession` 自己 new `SqlConnection`。
- 开启事务后执行 SQL 时忘记传 `transaction: Transaction`。
- 多个 Repository 各自创建连接来完成同一笔业务保存。
- 在 Repository 中提交不属于自己的外层事务。
- 在 Infrastructure 中引用 Application。

### 8. 最终回复要求

涉及本 Skill 的任务完成后，用中文说明：

- 是否使用了 `RepositoryBaseContext`。
- 是否使用了当前 `DbSession.Transaction`。
- 是否有开启、提交、回滚事务。
- 是否避免了独立创建连接。
