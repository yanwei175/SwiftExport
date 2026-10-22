---
name: aspdotnet10-infrastructure-di-registration
description: SwiftExport Infrastructure 依赖注入注册技能。用于 DependencyInjection.cs、AddInfrastructureServices、DbSession/RepositoryBaseContext/IAuditStampService、泛型 IRepository/DapperRepository、业务 Repository 注册、Infrastructure NuGet 依赖相关任务。
---

# Infrastructure DI Registration Skill

## 必读

- `SwiftExport.Infrastructure/DependencyInjection.cs`
- `SwiftExport.Infrastructure/SwiftExport.Infrastructure.csproj`
- 涉及 Repository 时读 `../infrastructure-repository-base/SKILL.md`
- 涉及 DbSession 时读 `../infrastructure-db-session/SKILL.md`

## 什么时候使用

- 新增 Repository 实现后注册 DI。
- 修改 `AddInfrastructureServices`。
- 修改 `DbSession`、`RepositoryBaseContext`、`IAuditStampService` 生命周期。
- 新增或调整 Infrastructure NuGet 包。
- 排查 WebApi 启动时服务未注册。

## 核心规则

- Infrastructure 对外暴露 `AddInfrastructureServices(IServiceCollection services)`。
- `DbSession`、`RepositoryBaseContext` 使用 scoped。
- 泛型仓储注册为 `IRepository<> -> PostgreSqlDapperRepository<>`。
- 业务仓储接口注册为 scoped。
- `IAuditStampService -> AuditStampService` 注册在 Infrastructure。
- `ICurrentUser` 具体实现由 WebApi 注册，Infrastructure 只依赖接口。

## 详细参考

- `references/基础设施DI注册规则.md`
