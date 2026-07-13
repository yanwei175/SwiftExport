---
name: wpf-grid-column
description: SwiftExport WPF DataGrid 列扩展技能。用于原生 DataGridColumn 元数据、稳定列标识、LookupColumn、EnumColumn、NumericColumn、列选择器与导出筛选协作。
---

# WPF Grid Column

## 兼容策略

- 任意功能必须先支持 `DataGridColumn`、`DataGridTextColumn`、`DataGridCheckBoxColumn`、`DataGridTemplateColumn`。
- 使用 `GridColumn.Key`、`FieldPath`、`ExportHeader`、`CanFilter` 等附加元数据连接导航、筛选和导出。
- 不创建新的 EnterpriseColumn 基类，不要求页面把已有列迁移到自定义体系。

## 便利列边界

- 业务列默认使用原生 `DataGridTextColumn`、`DataGridComboBoxColumn`、`DataGridCheckBoxColumn`，视觉修改走 `ElementStyle/EditingElementStyle`。
- 常规页面只允许图片列和需要直接点击/表头全选的复选列使用 `DataGridTemplateColumn`；复杂远程 Lookup 若必须使用模板编辑器，需作为明确例外评审，不能成为普通列默认写法。
- 需要直接点击和 Header 全选时，使用基于 `DataGridTemplateColumn` 的 `CheckBoxDataGridColumn`；Item/All Binding 必须由宿主显式提供，不能约定 BaseLineModel 属性名。
- Enum 优先组合原生 `DataGridComboBoxColumn`；Numeric 优先组合 `DataGridTextColumn`、Binding 转换和验证。
- 便利列只负责呈现/编辑模板，不加载 API、不缓存业务字典、不写业务状态。
- DataGridColumn 不在可视树中，禁止假设它自动继承 DataContext；ItemsSource/Provider 必须是显式 Binding 或宿主服务。

## Lookup 契约

分离 `SelectedValue`、`DisplayMember`、搜索文本和候选源。远程查找通过 VM/Provider 接口完成，支持取消、去抖、空值、无匹配与显示值回填，不把 Supplier 等业务类型写进框架。

## 共同能力

列标识必须跨重排稳定；筛选、导出、ColumnChooser 使用同一元数据。动态列变更、DisplayIndex 冲突、FrozenColumn、隐藏列和模板列无 Binding 元数据时必须有明确降级策略。
