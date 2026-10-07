---
name: aspdotnet10-contracts-common-helpers
description: SwiftExport Contracts Common 工具技能。用于 SwiftExport.Contracts/Common、GuidSequentialGenerator、SaveItemResultFactory、前后端共享静态工具、保存结果转换、顺序 Guid、避免放入业务逻辑或平台专属代码的任务。
---

# Contracts Common Helpers Skill

## 必读

- `../contracts-netstandard20/SKILL.md`
- `SwiftExport.Contracts/Common/GuidSequentialGenerator.cs`
- `SwiftExport.Contracts/Common/SaveItemResultFactory.cs`
- 涉及保存结果时读 `../contracts-wrappers/SKILL.md`

## 什么时候使用

- 修改 `SwiftExport.Contracts/Common/**`。
- 使用或修改顺序 Guid 生成。
- 使用或修改 `SaveItemResultFactory`。
- 新增前后端共享纯工具函数。

## 核心规则

- Common 只放极少量前后端共享、无基础设施依赖的静态工具。
- 不放业务流程编排。
- 不依赖 WebApi、Application、Infrastructure、WPF。
- `GuidSequentialGenerator` 是当前 PG 仓储仍使用的共享 Uid 生成工具；保留现有算法，不把旧 SQL Server 排序设计等同于 PG 索引局部性保证。
- `SaveItemResultFactory` 用于把 Repository 写入结果转换为保存结果。

## 详细参考

- `references/Common工具规则.md`
