# Workflow Skills

这里用于维护通用工作流类 Skill，例如需求澄清、重构流程、迁移流程、发布检查等可复用任务做法。

## 后端模板生成/同步工作流

涉及 WebAPI 后端模板初始化、模板同步或从旧项目迁移到 YanWei 默认后端结构时，按以下顺序处理：

1. 使用 `skills/backend/dotnet-webapi-template/skill.md` 判断目标模板结构。
2. 使用 `skills/backend/backend-contexts/skill.md` 检查 scoped `DbSession` 和上下文链路。
3. 使用 `skills/backend/dapper-repository/skill.md` 处理 Repository 和 Dapper SQL。
4. 使用 `skills/backend/dotnet-webapi-crud/skill.md` 处理 Controller、Service、DTO 和 CRUD。
5. 使用 `skills/shared/refit-contract-change/skill.md` 检查 Shared/Refit/API 契约。
6. 迁移范围较大时，使用 `skills/workflow/safe-refactoring/skill.md` 分阶段保护行为。
