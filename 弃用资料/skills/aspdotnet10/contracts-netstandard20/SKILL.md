---
name: aspdotnet10-contracts-netstandard20
description: SwiftExport Contracts 兼容性技能。用于修改 SwiftExport.Contracts 项目、netstandard2.0 兼容、前后端共享契约、避免使用 record/init/nullable/file-scoped namespace/collection expression 等高级 C# 语法的任务。
---

# Contracts NetStandard 2.0 Skill

## 必读

- `SwiftExport.Contracts/SwiftExport.Contracts.csproj`
- `AGENTS.md`
- 涉及 API 时读 `../contracts-refit-api/SKILL.md`
- 涉及 DTO 时读 `../contracts-dto-models/SKILL.md`
- 涉及 Wrappers 时读 `../contracts-wrappers/SKILL.md`
- 涉及枚举时读 `../contracts-shared-enums/SKILL.md`

## 什么时候使用

- 修改 `SwiftExport.Contracts/**`。
- 新增前后端共享 API、DTO、枚举、通用响应包装。
- 排查 Contracts 编译兼容问题。
- 前端或后端任务需要改共享契约。

## 核心规则

- `SwiftExport.Contracts` 是前后端共享层，目标框架是 `netstandard2.0`。
- 不使用新 C# 语法：`record`、`init`、`required`、nullable reference type 标注、file-scoped namespace、global using、collection expression、primary constructor。
- 使用传统 `namespace { }`、普通 `class`、普通属性 `get; set;`。
- Contracts 不依赖 WebApi、Application、Infrastructure、Core Entity 或 WPF View。
- Contracts 可以被 WebApi、Application、Infrastructure、WPF 客户端共同引用。
- API/前端可见的只读联表模型优先放 Contracts DTO。

## 详细参考

- `references/Contracts兼容性规则.md`
