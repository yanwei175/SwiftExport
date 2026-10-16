---
name: wpf-grid-state
description: SwiftExport WPF DataGrid 行状态视觉适配技能。用于把既有业务 RowState/LineState 通过 Binding、Converter、StyleSelector 映射为行提示且不污染框架。
---

# WPF Grid State

## 核心边界

- 框架不得定义 `GridRowState`，不得保存或推进 Added/Modified/Deleted/Unchanged。
- 不要求数据项继承控件类型或实现框架状态接口。
- 宿主使用 Binding、`IValueConverter`、`StyleSelector` 或命名 Style 将既有业务状态映射到视觉资源。
- 转换器/选择器放在客户端适配层，不放在独立 Grid 框架项目中。GridRowPresentation 及继承行为测试也归客户端 Resources/Behaviors；命名空间 SwiftExportClient.Resources.Behaviors。

## 视觉策略

- 状态提示优先放在 RowHeader 标记、左侧色条、图标或辅助文本；避免用整行背景覆盖选择和当前行视觉。
- 推荐优先级：验证错误 > CurrentCell 焦点 > 行选择 > 删除弱化/只读 > 新增/修改标记 > Hover。
- Deleted 的只读/禁用行为由业务页面策略决定，客户端样式呈现绑定结果。
- 颜色使用命名语义资源并提供文本/图标冗余，兼容高对比度。

GridRowPresentation 仅保留有读取方的 MarkerBrush 与 MarkerText；MarkerGlyph 无模板读取方已删除，不保留空属性或 Setter。

## SwiftExport 当前事实

当前行模型暴露 `State: LineState`，现有 `SwiftExportStateDataGridRow` 已直接触发 Added/Modified/Deleted。当前 `BaseListModel.Remove` 会把已删除行从可见 `Items` 移除，因此 Deleted 样式不会出现在主 Grid；如需可见墓碑行，必须由业务列表层决定是否保留或提供单独 DeletedItems，控件层不能伪造。

## 验收

覆盖状态切换、选择/悬停/错误叠加、主题与高对比度、行回收、ItemsSource 替换，以及没有状态属性的普通 POCO。
