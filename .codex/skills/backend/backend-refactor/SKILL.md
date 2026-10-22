---
name: backend-refactor
description: SwiftExport 后端按模块重构工作流。用于把某模块（首个为采购）的 Controller、Service、Repository、DTO 从旧过度设计迁移到目标架构，保留 Core 实体与数据库结构。
---

# 模块重构工作流

## 原则

- **一次一个模块，整条用例迁移，禁止半迁移**（同一用例不得新旧混用）。
- **保留**：Core 实体、数据库表结构、`command_receipt`。
- **替换**：Controller、Application Service、Repository、Contracts DTO/Refit、路由（改为资源式，客户端同步）、执行器/Support/Builder/Pipeline 等中间层。
- **允许简化**：预览 Token、预览重放、独立预检等技术性机制（见 backend-document）。
- **先询问再改**：表间依赖、字段引用、唯一约束、任何业务规则变化。
- 行为不变优先：重构前后业务规则、状态流转、错误语义一致；发现原行为有 Bug 时记录，单独确认后再改。

## 步骤

1. **盘点**：列出模块全部端点、Service 方法、仓储、Support/执行器、DTO、WPF/VSTO/Web 调用点、现有测试。产出“端点 → 用例 → 依赖”表。
2. **映射**：每个用例归入三种形状之一（查询 / 草稿写入 / 正式命令），指定对应样板；标出需要删除的中间层。
3. **契约**：确定 Contracts 变更。保持 netstandard2.0；若改字段或路由，列出 WPF/VSTO/Web 需同步的位置。
4. **补测试护栏**：对现有关键行为（状态流转、回写数量、幂等重放、并发冲突）先写/确认集成测试。
5. **按用例迁移**：Repository → Service → Controller → Contracts，一个用例完成并通过测试再做下一个。
6. **删除旧代码**：确认无引用后删除被替换的 Support/执行器/旧 Service；用 find_symbol 检查引用。
7. **验收**：按 [backend-delivery](../backend-delivery/SKILL.md)。

## 模块任务书

每个模块的具体范围、用例清单、必须保留的业务规则与验收项，由 `Handoff/Refactor/<模块>.md` 任务书给出。没有任务书不开始重构。

重构前须先完成公共基础设施治理（见 `Handoff/Refactor/00-Foundation.md`），模块重构只使用已落地的公共组件。

重构中发现业务文档过期：按代码实际行为重写该模块业务文档，不把业务写进技能。

## 禁止

- 修改 Core 实体或表结构来“配合”重构（需改表另走 backend-schema-change）。
- 重构过程中顺手改其他模块。
- 保留“暂时兼容”的双实现而不列入删除计划。
