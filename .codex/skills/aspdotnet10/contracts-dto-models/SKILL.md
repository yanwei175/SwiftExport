---
name: aspdotnet10-contracts-dto-models
description: SwiftExport Contracts DTO 技能。用于 SwiftExport.Contracts/DTOs、DTO 新建与复用决策、命名和文件组织、Auth DTO、BaseDto、前后端交互模型、Dapper 只读 DTO、Mapster 映射，以及避免 Entity/UI 状态泄露到 DTO 的任务。
---

# Contracts DTO Models Skill

## 必读

- `../contracts-netstandard20/SKILL.md`
- `SwiftExport.Contracts/DTOs/Base/BaseDto.cs`
- `SwiftExport.Contracts/DTOs/Auth/**`
- 目标 `SwiftExport.Contracts/DTOs/**`
- 新增、复用、拆分或改名 DTO 时读 `references/DTO复用与文件组织门禁.md`
- 涉及 Wrappers 时读 `../contracts-wrappers/SKILL.md`
- 涉及 Mapster 时读 `../application-mapster-mapping/SKILL.md`

## 什么时候使用

- 新增或修改 DTO。
- 新增 Auth、Base、单据头行、查询结果、Lookup DTO。
- Dapper 需要直接映射只读 DTO。
- 前端 ViewModel / LineModel 需要共享传输模型。
- 实体到 DTO 或 DTO 到实体映射变化。

## 核心规则

- DTO 放 `SwiftExport.Contracts/DTOs/<Module>`。
- DTO 默认按 API 用例和契约语义独立设计，不按页面机械合并，也不因字段相似而复用。
- 只有业务语义、字段口径、权限暴露和演进生命周期一致，并且存在真实多消费者或稳定平台语义时，才复用 DTO。
- 新增 DTO 前必须搜索现有 Contracts、API 和调用方，并在实现计划中写明“复用现有 DTO”或“新建专用 DTO”的理由。
- 默认一个公开顶级 DTO 一个文件，文件名与类型名一致；历史聚合文件只在任务触碰相关类型时小步治理。
- DTO 使用普通 `class`，不要使用 `record`。
- DTO 不继承 Entity，不返回 Entity。
- 可持久化业务 DTO 通常继承或包含 `BaseDto` 语义：`Uid`、`Version`、审计字段。
- Auth DTO 放 `DTOs/Auth`，用于登录、当前用户、注册、重置密码。
- DTO 字段名保持稳定，避免破坏 Refit、WPF Binding、Mapster 和 Dapper 映射。
- DTO 不承载 UI 状态。

## 详细参考

- `references/DTO模型规则.md`
- `references/DTO复用与文件组织门禁.md`
