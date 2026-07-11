---
name: wpf-enterprise-grid
description: 原生 WPF DataGrid 企业级增强框架技能组入口。用于整体架构、公共 API、模块边界、功能路线、技术决策和 Grid 专项代码审查，并路由到核心、选择、编辑、列、验证、状态、筛选和导出子 Skill。
---

# WPF Enterprise Grid 技能组

## 必读

- `AGENTS.md`
- `.agents/wpf-enterprise-grid-architect.toml`
- `.agents/workflows/WPF企业级DataGrid工作流.md`
- `references/总体架构.md`
- 目标 DataGrid、资源字典、行模型和绑定的真实源码

## 子 Skill 路由

- 框架入口、AttachedProperty、生命周期、Helper：读 `../wpf-grid-core/SKILL.md`。
- CurrentRow、SelectedItem、CurrentCell、CurrentColumn：读 `../wpf-grid-selection/SKILL.md`。
- 双击、F2、Enter、Tab、Shift+Tab、Esc：读 `../wpf-grid-editing/SKILL.md`。
- 列标识、Lookup、Enum、Numeric：读 `../wpf-grid-column/SKILL.md`。
- 单元格/行验证和错误导航：读 `../wpf-grid-validation/SKILL.md`。
- 业务行状态到视觉的无侵入映射：读 `../wpf-grid-state/SKILL.md`。
- FilterRow、ColumnFilter、本地/远程筛选：读 `../wpf-grid-filter/SKILL.md`。
- CSV/Excel 和数据导出：读 `../wpf-grid-export/SKILL.md`。

跨模块任务可以同时读取多个子 Skill，但由一个协调器统一事件顺序和公共 API。

## 工作方法

1. 先确认原生 DataGrid 行为、现有绑定和源码约束。
2. 先写决策、API 契约、事件顺序和验收场景，再写实现。
3. 默认使用组合模式：原生 DataGrid + 附加属性 + 每实例内部协调器。
4. 将公共 API 与内部实现分离；不因第一版方便暴露不稳定类型。
5. 每次只实现一个可独立验收的能力切片，并验证关闭该能力后恢复原生行为。

## 不可破坏约束

- 不替代 `DataGrid`，不重建 `DataGridColumn` 基类体系。
- 不定义、保存或推进业务 RowState；不引用业务 Model、Contracts 或服务。
- 不依赖 WPF-UI、HandyControl、MaterialDesignThemes 或商业 Grid。
- 不通过关闭虚拟化、扫描可视树或复制业务集合来实现功能。
- 不让多个模块无序处理同一个输入事件。
- 公开的双向附加属性必须具备重入保护、解绑和空值语义。
