# .NET Web API CRUD Skill

用于生成或修改 Web API CRUD 功能。

## 必读

- `skills/backend/backend-contexts/skill.md`
- `guides/backend/webapi-controller-guide.md`
- `guides/backend/application-service-guide.md`
- `guides/backend/dapper-repository-guide.md`
- `guides/shared/service-result-guide.md`
- `guides/shared/dto-guide.md`

## 重点

- Controller / Service / Repository / DTO 分工清晰。
- WebAPI 默认先确认 scoped `DbSession` 和上下文链路。
- 修改接口时同步检查 Shared、Refit 和前端。
- 不直接暴露 Entity。
