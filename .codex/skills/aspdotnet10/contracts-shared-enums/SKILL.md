---
name: aspdotnet10-contracts-shared-enums
description: SwiftExport Contracts 共享枚举技能。用于 SwiftExport.Contracts/SysEnums、SystemEnums、StaticEnums、MaterialTypes、SupplyType、SaveOperation、单据状态、前后端共用枚举、避免 Core 私有枚举被前端需要的任务。
---

# Contracts Shared Enums Skill

## 必读

- `../contracts-netstandard20/SKILL.md`
- `SwiftExport.Contracts/SysEnums/SystemEnums.cs`
- `SwiftExport.Contracts/SysEnums/StaticEnums/**`
- 目标枚举文件
- 涉及业务流程时读 `.agents/references/业务架构总纲.md`
- 涉及状态边界时读 `.agents/references/数据模型与状态规则.md`

## 什么时候使用

- 新增或修改前后端共用枚举。
- 修改单据状态、确认状态、供给类型、物料类型。
- DTO/API 需要共享枚举字段。
- 前端页面需要下拉枚举或状态显示。

## 核心规则

- 前端需要的枚举放 `SwiftExport.Contracts/SysEnums`。
- Core 不负责承载前端需要的枚举。
- 枚举值一旦用于数据库或 API，不随意改数值。
- 单据流程不要全部塞进一个 `OrderStatus`。
- 复杂流程应区分生命周期、确认、执行进度、过账、改单状态。
- 枚举命名和值必须与既有业务文档和源码事实对齐。

## 详细参考

- `references/共享枚举规则.md`
