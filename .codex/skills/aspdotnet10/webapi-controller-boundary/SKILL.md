---
name: aspdotnet10-webapi-controller-boundary
description: SwiftExport WebApi Controller 边界技能。用于新增或修改 BaseController、普通业务 Controller、Controller 路由、HTTP 方法、PreventDuplicateRequest 标注、Controller 与 Application Service/Refit 契约对齐的任务。
---

# WebApi Controller Boundary Skill

## 必读

公共启动沿用 `AGENTS.md`；以下仅补齐当前业务任务尚未读取的依赖，不回溯已读入口。

- `SwiftExport.WebApi/Controllers/Base/BaseController.cs`
- 涉及 API/Application 时读 `../api-application-service/SKILL.md`
- 涉及 Contracts/Refit 时读 `../contracts-refit-dto/SKILL.md`
- 涉及标准 CRUD 传输模型时读 `../application-transfer-dtos/SKILL.md`
- 涉及正式业务命令、CommandId 或 Preview 时读 `../application-document-command/SKILL.md`
- 涉及 Controller 拆分时读 `../backend-file-organization/SKILL.md`

## 什么时候使用

- 新增或修改 WebApi Controller。
- 修改 `BaseController<TEntity,TDto>`。
- 对齐 Controller 路由与 Refit API。
- 给写操作加防重复提交 Filter。
- 判断 Controller 是否越过 WebApi 边界。

## 核心规则

- WebApi Controller 只做 HTTP 边界、模型绑定、调用 Application Service、返回结果。
- 普通业务 Controller 优先继承或参考 `BaseController<TEntity,TDto>` 的路由与 CRUD 约定。
- `BaseController` 必须保持 `abstract`，避免被 ASP.NET 当成真实 Controller 注册。
- 默认路由是 `[Route("api/[controller]")]`。
- 普通主数据写操作可使用 `[PreventDuplicateRequest]` 降低误点击；正式业务命令必须依赖持久化 CommandId、Version、事务和数据库约束，Filter 只能作为可选体验补充。
- Controller action 透传 `CancellationToken`；认证边界使用 `[Authorize]`，细粒度业务权限仍由 Application 校验。
- Controller 不写 SQL，不直接依赖 Infrastructure 实现，不承担复杂业务编排。

## 详细参考

需要 CRUD Controller、路由和禁止事项时读：

- `references/Controller边界规则.md`
