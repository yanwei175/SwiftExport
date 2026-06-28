# SwiftExport AI 工作流上下文摘要

## 当前分层

- `AGENTS.md`：项目级 Codex 铁律，保存稳定行为规则、分层边界、写入边界和最终回复格式。
- `raw/`：原始资料层，保存 inbox 草稿、模板、对话、截图、外部资料；默认只读，不自动删除或重写。
- `wiki/`：正式知识层，保存整理后的项目知识、skill、workflow、guide、项目地图、索引和日志。
- `wiki/index.md`：唯一总入口；任务开始先读它，再按任务类型进入对应 wiki 页面和源码。
- `wiki/log.md`：AI 工作流维护日志；每次 ingest 或重要规则调整都追加记录。

## 项目架构速记

SwiftExport 是 C# ERP 项目：

- 后端：ASP.NET Core WebApi、Dapper、SQL Server、Serilog、JWT、Mapster。
- 前端：.NET Framework 4.8 WPF、MVVMLight、Refit、MateWpfThemes.FW。
- Excel 宿主：`SwiftExport.VSTO`。
- 共享契约：`SwiftExport.Contracts`，保存 DTO、Refit API、枚举和通用返回模型。

## 当前任务路由

- 后端 Dapper / Repository：先读 `wiki/skills/backend/db-session-context.md` 和 `wiki/skills/backend/dapper-repository.md`。
- WPF / VSTO 前端：先读 `wiki/workflows/frontend/wpf-vsto-workflow.md`。
- 项目结构和接手：先读 `wiki/project/project-map.md` 和 `wiki/project/onboarding.md`。
- 稳定规范：按需要读取 `wiki/guides/index.md` 下的对应 guide。

## Ingest 规则

- 用户新增模板、草稿、经验记录时，放在 `raw/inbox/**`。
- Codex ingest 时读取 raw 来源，提炼可长期复用内容到 `wiki/**`。
- 不把模板源码整段搬进 wiki；wiki 页面只保存总结、规则、检查清单和项目化取舍。
- 与 SwiftExport 当前源码或 AGENTS 冲突时，以源码和 AGENTS 为准，并在 `wiki/log.md` 记录。

