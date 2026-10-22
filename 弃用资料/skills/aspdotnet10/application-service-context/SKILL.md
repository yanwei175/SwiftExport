---
name: aspdotnet10-application-service-context
description: SwiftExport Application 服务上下文技能。用于新增或修改 Application Service、事务边界、DbSession 使用、ServiceBaseContext 注入、日志依赖、服务与仓储共享同一 request scope 的后端任务。
---

# Application Service Context Skill

## 必读

- `AGENTS.md`
- `.agents/backend.toml`
- `.agents/workflows/后端API工作流.md`
- `SwiftExport.Application/Contexts/ServiceBaseContext.cs`
- `SwiftExport.Infrastructure/Data/DbSession.cs`
- 涉及 Repository 时读 `../dapper-repository/SKILL.md`
- 涉及复杂单据事务、Preview、CommandId 或锁顺序时读 `../application-document-command/SKILL.md`

## 什么时候使用

- 新增 Application Service 构造函数依赖。
- 调整服务层事务、日志、DbSession 生命周期。
- 排查 Service 与 Repository 是否共享同一个 scoped `DbSession`。
- 修改 DI 注册或后端基础链路。

## 核心规则

- `ServiceBaseContext` 是 Application 服务层基础依赖聚合，当前包含 Core `IDbSession` 和 `ILoggerFactory`；Application 不引用 Infrastructure 或 SqlClient。
- Application Service 通过 `ServiceBaseContext` 获取 `_session` 和 `LoggerFactory`，不要自己创建连接或日志工厂。
- `DbSession` 应在 WebAPI request scope 内共享，Service 与 Repository 使用同一个 session。
- 业务事务边界属于 Application Service；Repository 不开启业务流程事务。
- 普通查询不机械开启事务或使用 `Serializable`；写命令按真实一致性风险选择隔离级别并记录稳定锁顺序。
- 最外层 Application 用例拥有提交权；内部服务/端口不得提交、释放或清空调用方事务。
- 事务失败和请求取消必须安全回滚，清理异常不能覆盖原始失败，最终只清理由本层创建的事务。
- Service 可以调用 Repository 接口，不写 SQL、不直接调用 Dapper。

## 详细参考

需要上下文职责、事务和 DI 检查清单时读：

- `references/服务上下文规则.md`
