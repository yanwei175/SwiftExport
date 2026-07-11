---
name: aspdotnet10-infrastructure-audit-stamp
description: SwiftExport Infrastructure 审计打戳技能。用于 AuditStampService、IAuditStampService、CreatedBy/CreatedDate/UpdatedBy/UpdatedDate、ICurrentUser、DapperRepository 新增更新审计字段相关任务。
---

# Infrastructure Audit Stamp Skill

## 必读

- `SwiftExport.Infrastructure/Services/Auth/AuditStampService.cs`
- `SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs`
- `SwiftExport.Infrastructure/Repositories/Base/DapperRepository.cs`
- 涉及当前用户时读 `../webapi-auth-current-user/SKILL.md`

## 什么时候使用

- 修改审计字段打戳规则。
- 修改 `IAuditStampService` 或 `AuditStampService`。
- 排查 `CreatedBy`、`CreatedDate`、`UpdatedBy`、`UpdatedDate`。
- 修改 `DapperRepository.AddAsync` 或 `UpdateAsync`。
- 改当前用户对审计字段的影响。

## 核心规则

- 审计打戳在 Infrastructure 层实现，但依赖 Core 中的 `IAuditStampService` 和 `ICurrentUser` 接口。
- 新增时填充 Created 与 Updated 字段。
- 更新时强制刷新 Updated 字段。
- 业务代码通常不手动写审计字段。
- Infrastructure 只依赖接口，不依赖 WebApi 当前用户实现。

## 详细参考

- `references/审计打戳规则.md`
