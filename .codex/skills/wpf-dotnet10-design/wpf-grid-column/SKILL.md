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
- 需要直接点击和 Header 全选时，页面优先直接使用原生 `DataGridTemplateColumn`，分别在 HeaderTemplate 和 CellTemplate 中放置 CheckBox，并显式绑定 Item/All 状态。不要约定 BaseLineModel 属性名，也不要为了微调对齐建立参与测量的多层包装模板。
- Enum 优先组合原生 `DataGridComboBoxColumn`；Numeric 优先组合 `DataGridTextColumn`、Binding 转换和验证。
- 便利列只负责呈现/编辑模板，不加载 API、不缓存业务字典、不写业务状态。
- DataGridColumn 不在可视树中，禁止假设它自动继承 DataContext；ItemsSource/Provider 必须是显式 Binding 或宿主服务。

## 静态枚举列

- 静态枚举优先使用原生 `DataGridComboBoxColumn`，`ItemsSource` 通过 `x:Static` 或应用级稳定资源提供；禁止让列的候选源依赖页面 DataContext 中会随卸载变为 null 的临时 `CollectionViewSource`。
- 字符串枚举需要兼容历史未知值时使用 TwoWay `TextBinding`，不要使用 `SelectedItemBinding`。候选源被清空时，原生 `SelectedItemBinding` 可能把空值回写业务属性并制造误脏。
- 原生非编辑元素对 ItemsSource 之外的旧值可能显示为空；仍保留 `DataGridComboBoxColumn`，仅通过轻量 `ElementStyle` 显示绑定文本，编辑态继续使用原生 ComboBox。
- 用户主动选择候选项才允许回写并推进 Modified；页面加载、卸载、重建和候选源初始化不得修改业务值。

## Lookup 契约

分离 `SelectedValue`、`DisplayMember`、搜索文本和候选源。远程查找通过 VM/Provider 接口完成，支持取消、去抖、空值、无匹配与显示值回填，不把 Supplier 等业务类型写进框架。

## 共同能力

列标识必须跨重排稳定；筛选、导出、ColumnChooser 使用同一元数据。动态列变更、DisplayIndex 冲突、FrozenColumn、隐藏列和模板列无 Binding 元数据时必须有明确降级策略。
