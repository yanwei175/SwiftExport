# Skill: Dapper Repository

## When to use

当任务涉及新增、修改、审查 Dapper Repository，编写 SQL，处理 Add / Update / Delete / SearchPage，或调整软删除、乐观并发、分页查询时使用。

## Required reading

- `skills/backend/dotnet-webapi-template/skill.md`
- `skills/backend/backend-contexts/skill.md`
- `templates/dotnet-webapi/docs/backend-architecture.md`
- `guides/backend/dapper-repository-guide.md`
- `guides/backend/unit-of-work-guide.md`
- `guides/backend/database-schema-guide.md`

## Workflow

1. 先定位相关文件：Repository 接口、Repository 实现、Application Service、`DbSession`、`RepositoryBaseContext`、UOW、数据库表结构或迁移脚本。
2. 先给计划，说明涉及文件、SQL 影响、事务边界和风险；除非用户明确要求直接执行，否则不要跳过计划。
3. 判断是否影响 Shared DTO、前端绑定、后端 Service、数据库字段或索引。
4. 如果项目采用 WebAPI 默认架构，先执行 `backend-contexts` Skill，确认 scoped `DbSession`、`ServiceBaseContext`、`RepositoryBaseContext`、`AuditStamp`、`LoggerFactory` 和事务边界。
5. 新项目或模板同步时，参考 `dotnet-webapi-template`，不要恢复旧版泛型服务链或动态 QueryCriteria。
6. 只修改必要文件，优先匹配项目现有 Repository 风格。
7. 实现 Add / Update / Delete / SearchPage 时，同时检查 `IsDeleted` 过滤、`Version` 乐观并发、分页排序和参数化 SQL。
8. 修改后运行对应验证命令，优先 `dotnet build`；有测试项目时运行相关测试。
9. 输出修改文件、修改原因、事务/数据库风险和验证结果。

## Rules

- Repository 只负责数据访问，不负责业务流程。
- SQL 必须参数化，字段列表保持清晰。
- 可用 `nameof` 辅助字段名，但不要写复杂 SQL 生成器。
- 查询默认遵守项目软删除规则，例如过滤 `IsDeleted`。
- 更新已有数据时遵守项目 `Version` 乐观并发规则。
- Repository 使用外部传入或注入的当前连接/事务。
- WebAPI 默认项目中，Repository 优先通过 `RepositoryBaseContext` 使用 scoped `DbSession`。
- 默认模板依赖 `BaseEntity` 的 `Uid`、`Version`、`IsDeleted` 和审计字段。
- 默认不生成动态 `QueryCriteria` / `ParseQueryCriteria`；复杂查询写在具体 Repository 方法中。

## Do not

- 不要在 Repository 中控制业务事务。
- 不要在 Repository 中调用 Controller、ViewModel 或 Application Service。
- 不要把审批、状态流转、价格计算等业务逻辑写进 Repository。
- 不要直接拼接用户输入到 SQL。
- 不要为了通用性强行引入泛型服务。
- 不要恢复 `QueryCriteria`、`ParseQueryCriteria` 或动态查询解析器。

## Validation

- 有 `.csproj` 时运行 `dotnet build`。
- 有 Repository 或 Application Service 测试时运行相关测试。
- 检查新增 SQL 是否参数化、是否包含软删除过滤、是否符合分页和排序规则。
- 用 `rg "QueryCriteria|ParseQueryCriteria|BuildConditionSql"` 确认未恢复动态查询解析。
- TODO：待人工确认不同项目的统一 SQL 格式化命令。

## Output

- 汇报新增/修改的 Repository、接口、Service 和 DTO。
- 说明 Add / Update / Delete / SearchPage 的行为。
- 说明是否影响 Shared、前端、后端或数据库。
- 说明验证命令和结果。
- 标出未验证项和剩余风险。
