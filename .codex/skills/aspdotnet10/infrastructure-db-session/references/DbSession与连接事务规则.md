# DbSession 与连接事务规则

## 当前源码事实

`DbConnectionFactory.cs`：

- `IDbConnectionFactory.CreateConnection()`
- `SqlConnectionFactory` 使用 `Microsoft.Data.SqlClient.SqlConnection`

`DbSession.cs`：

- `Connection: IDbConnection`
- `Transaction: IDbTransaction?`
- 构造函数从 `IConfiguration.GetConnectionString("Default")` 创建 `SqlConnection`
- Session 创建时打开连接
- Dispose 时释放事务和连接

## 使用规则

- `DbSession` 应按 scoped 注册。
- 同一个 Web request 中的 Service 和 Repository 应共享同一个 `DbSession`。
- Repository 不拥有事务边界，只使用 `DbSession.Transaction`。
- 多仓储写入时，由 Application Service 设置 `_session.Transaction = _session.Connection.BeginTransaction()`。
- 所有 Dapper 写入和查询都传入 `transaction: Transaction`，确保参与当前事务。

## 推荐事务结构

```csharp
try
{
    _session.Transaction = _session.Connection.BeginTransaction();

    await _headRepository.SaveAsync(head);
    await _lineRepository.SaveAsync(lines);

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

## 禁止事项

- 禁止在 Repository 中 `new SqlConnection`。
- 禁止在 Controller 中创建连接或事务。
- 禁止在 Infrastructure 中读取业务配置后绕开 `DbSession`。
- 禁止多个 Repository 为同一业务命令各自开连接。
- 禁止开启事务后忘记清空 `DbSession.Transaction`。

## 依赖包

- `Microsoft.Data.SqlClient`
- `Dapper`
- `Microsoft.Extensions.Logging`
