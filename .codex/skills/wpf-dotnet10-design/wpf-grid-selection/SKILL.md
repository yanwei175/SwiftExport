---
name: wpf-grid-selection
description: SwiftExport WPF DataGrid 选择与当前单元格技能。用于任意单元格子控件点击时同步 SelectedItem，以及 CurrentRow、CurrentColumn、CurrentCell 的 MVVM 双向桥接。
---

# WPF Grid Selection

## 真相源

- `DataGrid.SelectedItem` 与 `DataGrid.CurrentCell` 保持原生真相源。
- `CurrentRow` 是 MVVM 友好的当前项镜像，不创建第二套行对象。
- `CurrentColumn` 可镜像 `DataGridColumn`；跨 ViewModel 使用稳定的 `CurrentColumnKey`，避免业务 VM 依赖 WPF 类型。
- 不自动写业务对象的 `IsSelected` 属性；多选集合同步必须是独立、显式功能。

## 鼠标同步

- 在 `PreviewMouseLeftButtonDown` 阶段定位最近的 `DataGridRow/DataGridCell`。
- 在 TextBox、CheckBox、Button、TemplateColumn 内容处理点击前，先同步行和当前单元格。
- 仅同步焦点/选择，不因单击调用 `BeginEdit`，也不阻止 Button 命令或控件自身事件。
- 尊重 `SelectionMode`、`SelectionUnit` 和 Ctrl/Shift 多选语义；单选修复不得破坏扩展选择。

## 双向同步

- View 到 VM：SelectionChanged/CurrentCellChanged 更新附加属性。
- VM 到 View：CurrentRow 更新时设置 SelectedItem；存在容器时再滚动和定位。
- 使用重入门闩区分 View 更新与 Binding 回写，处理 ItemsSource 重置和项被移除。
- CurrentCell 镜像不取代原生属性，不持有已虚拟化的 DataGridCell 控件实例。

## 验收

覆盖 TextBox、CheckBox、Button、DataGridTemplateColumn、空白区域、行头、多选修饰键、虚拟化后滚动、ItemsSource 替换和同值重复赋值。
