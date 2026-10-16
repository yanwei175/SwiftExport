---
name: wpf-enterprise-grid
description: 原生 WPF DataGrid 客户端适配技能组入口。用于整体架构、公共 API、模块边界、功能路线、技术决策和 Grid 专项代码审查，并路由到核心、选择、编辑、列、验证、状态、筛选和导出子 Skill。
---

# WPF Enterprise Grid 技能组

## 必读

- `AGENTS.md`
- `.agents/wpf-enterprise-grid-architect.toml`
- `.agents/workflows/WPF企业级DataGrid工作流.md`
- `references/总体架构.md`
- 涉及测试证据、键盘/焦点/Automation、高对比度、虚拟化性能或导出安全时，读取 `../quality-gates/SKILL.md`
- 目标 DataGrid、资源字典、行模型和绑定的真实源码

## 子 Skill 路由

- 框架入口、AttachedProperty、生命周期、Helper：读 `../wpf-grid-core/SKILL.md`。
- 原生 CurrentItem/SelectedItem、CurrentCell/CurrentColumn、原生多选集合命令参数：读 `../wpf-grid-selection/SKILL.md`。
- 原生双击、F2、Enter、Tab、Shift+Tab、Esc 兼容：读 `../wpf-grid-editing/SKILL.md`。
- 必要列元数据、原生文本/数值/模板列：读 `../wpf-grid-column/SKILL.md`。
- 单元格/行验证、聚合与软提示：读 `../wpf-grid-validation/SKILL.md`。
- 业务行状态到视觉的无侵入映射：读 `../wpf-grid-state/SKILL.md`。
- 原生集合视图与页面远程筛选：读 `../wpf-grid-filter/SKILL.md`。
- 客户端 CSV/Excel 导出边界：读 `../wpf-grid-export/SKILL.md`。
- Operations 与 Grid 提交历史已退出。Ctrl+D 由客户端薄行为和页面强类型命令负责；同时读取 communitytoolkit-mvvm 和 column/editing。原生 Ctrl+C 和模板 ClipboardContentBinding 按 column/editing 检查。Grid 级 Ctrl+V、Delete 清空已退出，编辑器内操作及业务清空命令保留。
- 业务行拖拽重排由客户端 `RowDragReorder` 负责，读 `../wpf-row-drag-reorder/SKILL.md`；Grid 不再提供上下移按钮列，不维护 LineNumber / SortNo。

跨模块任务可读取多个子 Skill，但须明确事件所有权；各客户端适配独立管理生命周期；没有总入口、协调器或选择 Behavior。

## 工作方法

1. 先确认原生 DataGrid 行为、现有绑定和源码约束。
2. 先写决策、API 契约、事件顺序和验收场景，再写实现。
3. 默认使用组合模式：原生 DataGrid + 客户端按需附加属性/薄行为。
4. 将公共 API 与内部实现分离；不因第一版方便暴露不稳定类型。
5. 每次只实现一个可独立验收的能力切片，并验证关闭该能力后恢复原生行为。

## 不可破坏约束

- 不替代 `DataGrid`，不重建 `DataGridColumn` 基类体系。
- 不定义、保存或推进业务 RowState；不引用业务 Model、Contracts 或服务。
- 不依赖 WPF-UI、HandyControl、MaterialDesignThemes 或商业 Grid。
- 不通过关闭虚拟化、扫描可视树或复制业务集合来实现功能。
- 不让多个模块无序处理同一个输入事件。
- 不新增选择状态附加属性；选区直接通过命令读取原生集合，其他真实双向状态仍需重入保护、解绑和空值语义。

## 当前精简边界

EnterpriseGrid 独立项目、专项测试项目、总开关和协调器均已退出。行号、必要软验证及有效测试迁至客户端。GridColumn.Key/FieldPath 全部删除；仅模板软验证需要 GridValidation.FieldPath，填充显式使用客户端 Field。其他能力优先原生 DataGrid 和现有页面命令；详细边界见 references/总体架构.md。
