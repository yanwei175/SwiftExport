1. # Skill: Dapper 动态查询与仓储模式

   ## Description
   当用户需要“编写数据库查询”、“使用 QueryCriteria”、“实现复杂过滤”或“操作 Repository”时使用。

   ## Instructions
   - **QueryCriteria 使用**：
     - 使用 `criteria.Add` 处理 AND 条件。
     - 使用 `criteria.AddOrGroup` 处理复杂的 (A OR B) AND C 逻辑。
     - 操作符支持：等于、模糊匹配、包含某值(IN)等。
   - **分页规范**：必须调用 `GetPagedListAsync`，并确认传入 `orderBy` 字符串。
   - **SQL 追踪**：仓储层操作应通过 `Logger.LogDebug` 记录生成的动态 SQL。