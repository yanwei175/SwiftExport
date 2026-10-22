# 仓储与查询的 SQL 约定

```csharp
// 所有 Dapper 调用使用 CommandDefinition，带上 Transaction 和 CancellationToken
db.Connection.ExecuteAsync(new CommandDefinition(sql, param, db.Transaction, cancellationToken: ct));
```

| 场景 | SQL 形状 |
|---|---|
| 读取用于修改的单据 | `SELECT ... WHERE id=@Id AND is_deleted=false FOR UPDATE` |
| 更新单据头 | `UPDATE ... SET ..., version=version+1, updated_at=now(), updated_by=@User WHERE id=@Id AND version=@Version`；影响行数 0 → `throw new ConcurrencyException()` |
| 保存明细 | 草稿行：按 Id 对比，`DELETE` 删除行、`UPDATE` 修改行、`INSERT` 新行；不要整表删除重建（会丢行 Id，破坏下游引用） |
| 库存余额 | `INSERT ... ON CONFLICT (warehouse_id, product_id) DO NOTHING` 后 `SELECT ... FOR UPDATE` |
| 多个资源加锁 | 固定顺序（按 Id 升序）避免死锁 |
| 分页查询 | `COUNT(*) OVER()` 或两条语句；`ORDER BY` 只允许白名单列；`LIMIT @PageSize OFFSET @Skip` |
| 模糊查询 | `ILIKE '%' || @Keyword || '%'`，参数化 |

禁止：
- SQL 字符串拼接用户输入。
- 仓储里判断业务状态（`WHERE status=0` 仅作为并发保险可以加，但真正的判断必须在实体中先做）。
- 仓储调用 `Commit/Rollback`。
- 使用 advisory lock（`pg_advisory_lock`），除非文档说明了行锁无法覆盖的场景。
