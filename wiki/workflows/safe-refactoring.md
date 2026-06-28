# Workflow: SwiftExport Safe Refactoring

## Description

当任务涉及大范围重构、模块迁移、大文件拆分、行为保持、分阶段改造、公共契约调整或高风险修改时使用本工作流。

## Required Reading

- [项目上下文摘要](../project/context-summary.md)
- [项目地图](../project/project-map.md)
- [Safe Refactoring Guide](../guides/workflow/safe-refactoring.md)
- [Codex Task Writing Guide](../guides/workflow/codex-task-writing.md)
- 受影响模块的真实源码、调用方、构建命令和可验证入口。

## Workflow

1. 先定位入口、调用链、受影响项目、公共契约、数据库访问和 UI 绑定。
2. 写出阶段计划：影响范围、行为保护点、每阶段修改文件、验证方式和回滚边界。
3. 每次只修改必要文件，优先一个模块一个阶段。
4. 先保持行为一致，再考虑命名、目录、抽象或删除旧路径。
5. 涉及 Contracts、WebApi、Application、Infrastructure、Core、View.FW、VSTO 任意跨层变化时，分别读取对应 skill/workflow。
6. 无用户明确要求时，不把重构任务扩大成业务功能修改。
7. 删除代码前必须确认替代路径、调用方迁移和验证方式。

## Rules

- 源码事实优先于 wiki；wiki 与源码冲突时在 `wiki/log.md` 标记可能过期。
- 公开 API、DTO、数据库结构、前端绑定和用户可见行为是重点保护对象。
- 对无法自动验证的 UI 或 Excel 行为，最终回复列出手工验证步骤。
- 发现未提交的用户改动时，围绕它们工作，不回滚。

## Validation

- 后端改动优先运行 `dotnet build .\SwiftExport-WebAPI.slnx`。
- WPF/VSTO 改动优先运行 `msbuild .\SwiftExport.slnx /p:Configuration=Debug /p:Platform=AnyCPU`。
- 仅 Markdown 或 wiki 变更不运行构建，但要做链接和范围检查。

## Output

最终回复用中文说明：

- 本阶段修改了哪些文件，属于哪个重构阶段。
- 行为保护点和影响范围。
- 运行了哪些构建/测试；未运行时说明原因。
- 剩余风险和下一阶段建议。
