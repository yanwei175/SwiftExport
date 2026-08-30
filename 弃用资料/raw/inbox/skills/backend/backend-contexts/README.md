# Backend Contexts Skill

用于 WebAPI 默认后端上下文链路：

```text
HTTP Request Scope
  -> DbSession
  -> ServiceBaseContext
  -> Service
  -> RepositoryBaseContext
  -> Repository
```

## 必读

- `guides/backend/application-service-guide.md`
- `guides/backend/dapper-repository-guide.md`
- `guides/backend/unit-of-work-guide.md`

## 重点

- `DbSession` 默认 scoped。
- Service 和 Repository 共享同一个 request scope 的 `DbSession`。
- Service 管事务，Repository 用当前连接和事务。
- `DbSession` 默认不抽接口，除非确实有多实现或严格分层需要。
