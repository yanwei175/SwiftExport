---
name: wpf-grid-column
description: SwiftExport WPF DataGrid 列扩展技能。用于原生 DataGridColumn 元数据、稳定列标识、LookupColumn、EnumColumn、NumericColumn、列选择器与导出筛选协作。
---

# WPF Grid Column

## 兼容策略

- 任意功能必须先支持 `DataGridColumn`、`DataGridTextColumn` 和 `DataGridTemplateColumn`。
- 使用 `GridColumn.Key`、`FieldPath`、`ExportHeader`、`CanFilter` 等附加元数据连接导航、筛选和导出。
- 不创建新的 EnterpriseColumn 基类，不要求页面把已有列迁移到自定义体系。

## 列类型规范

- 文本和数值列优先使用原生 `DataGridTextColumn`，视觉修改走 `ElementStyle/EditingElementStyle`。
- ComboBox 列必须使用 `DataGridTemplateColumn`，在 `CellTemplate` 或 `CellEditingTemplate` 中显式放置 `ComboBox`；禁止使用原生 `DataGridComboBoxColumn`。
- CheckBox 列必须使用 `DataGridTemplateColumn`，分别在 `HeaderTemplate` 和 `CellTemplate` 中放置 CheckBox；禁止使用原生 `DataGridCheckBoxColumn`。
- 复选列的 Item/All Binding 由页面显式提供，不约定 BaseLineModel 属性名，也不要为了微调对齐建立参与测量的多层包装模板。
- 图片列、Lookup 和其他自定义编辑器同样使用 `DataGridTemplateColumn`；编辑器较重时，显示态使用轻量 `CellTemplate`，只在 `CellEditingTemplate` 实例化编辑控件。
- Numeric 优先组合 `DataGridTextColumn`、Binding 转换和验证。
- 便利列只负责呈现/编辑模板，不加载 API、不缓存业务字典、不写业务状态。
- DataGridColumn 不在可视树中，禁止假设它自动继承 DataContext；ItemsSource/Provider 必须是显式 Binding 或宿主服务。

## 静态枚举列

- 静态枚举使用 `DataGridTemplateColumn + ComboBox`，`ItemsSource` 通过 `x:Static` 或应用级稳定资源提供；禁止让候选源依赖页面 DataContext 中会随卸载变为 null 的临时 `CollectionViewSource`。
- 字符串枚举使用 TwoWay `SelectedValue/SelectedItem` Binding，并覆盖页面卸载、重建、空值和历史未知值场景；初始化候选源不得回写空值制造误脏。
- 用户主动选择候选项才允许回写并推进 Modified；页面加载、卸载、重建和候选源初始化不得修改业务值。

## 导出与操作

- `DataGridTemplateColumn` 无法从原生列类型推断业务 Binding；需要 Ctrl+D、筛选或导出的模板列必须显式设置 `GridColumn.FieldPath`。
- 显示值与原始值不一致时设置 `GridColumn.ValueAccessor`，导出层不得扫描 ComboBox/CheckBox 可视容器取值。

## Lookup 契约

分离 `SelectedValue`、`DisplayMember`、搜索文本和候选源。远程查找通过 VM/Provider 接口完成，支持取消、去抖、空值、无匹配与显示值回填，不把 Supplier 等业务类型写进框架。

## 共同能力

列标识必须跨重排稳定；筛选、导出、ColumnChooser 使用同一元数据。动态列变更、DisplayIndex 冲突、FrozenColumn、隐藏列和模板列无 Binding 元数据时必须有明确降级策略。
