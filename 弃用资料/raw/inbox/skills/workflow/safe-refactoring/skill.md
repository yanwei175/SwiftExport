# Skill: Safe Refactoring

## When to use

当任务涉及大项目重构、历史代码整理、大文件拆分、模块迁移、行为保持、分阶段改造或高风险修改时使用。

## Required reading

- `guides/workflow/safe-refactoring-guide.md`
- `guides/workflow/codex-task-writing-guide.md`
- `guides/workflow/project-onboarding-guide.md`

## Workflow

1. 先定位相关文件、入口、调用链、测试和构建命令。
2. 先给计划，列出影响范围、行为保护点、阶段拆分和验证方式；除非用户明确要求直接执行，否则不要跳过计划。
3. 判断是否影响 Shared、前端、后端、数据库、API 契约和用户数据。
4. 涉及后端模板结构迁移时，先使用 `skills/backend/dotnet-webapi-template/skill.md` 判断目标架构，再分阶段修改。
5. 每次只修改必要文件，优先一个模块一个阶段。
6. 保护行为一致，先转发、兼容、验证，再考虑删除旧路径。
7. 修改后运行 `dotnet build`；有测试时运行相关测试。
8. 输出修改文件、原因、风险、验证结果和下一步建议。

## Rules

- 大项目先分析，不直接改。
- 分阶段重构，保持每一步可回滚。
- 先列影响范围，再动代码。
- 优先保护 UI 行为、API 契约和数据库结构。
- 小步提交，小步验证。
- 后端模板迁移默认对齐 `dotnet-webapi-template`，但不把项目专属业务规则写入模板。

## Do not

- 不要顺手重构无关模块。
- 不要在一次任务里同时做架构调整和业务改动。
- 不要无确认地改数据库结构或公开 API。
- 不要一次性重命名大量类型和目录。
- 不要删除旧代码，除非用户明确要求并已有验证路径。

## Validation

- 运行 `dotnet build`。
- 有测试时运行受影响模块测试。
- 检查关键入口、调用链和行为是否保持一致。
- 对无法自动验证的行为列出手工验证步骤。
- TODO：待人工确认项目级提交粒度和变更报告模板。

## Output

- 汇报修改文件、修改原因和阶段归属。
- 说明影响范围和行为保护点。
- 说明验证命令和结果。
- 标出未完成项、风险和下一阶段建议。
