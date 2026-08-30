# Dapper Repository Skill

用于 Dapper Repository 的新增、修改和审查。

## 必读

- `skills/backend/backend-contexts/skill.md`
- `guides/backend/dapper-repository-guide.md`
- `guides/backend/unit-of-work-guide.md`
- `guides/backend/database-schema-guide.md`

## 重点

- Repository 只做数据访问。
- 事务边界由 Application Service / UOW 控制。
- WebAPI 默认项目优先确认 scoped `DbSession + RepositoryBaseContext`。
- SQL 参数化，遵守 `IsDeleted`、`Version`、分页和排序规则。
