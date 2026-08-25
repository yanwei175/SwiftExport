---
name: wpf-grid-selection
description: SwiftExport WPF DataGrid 选择与当前单元格技能。用于任意单元格子控件点击时同步 SelectedItem，以及 CurrentRow、CurrentColumn、CurrentCell 的 MVVM 双向桥接。
---

# WPF Grid Selection

## 真相源

- FullRow 模式以 `DataGrid.SelectedItem` 为当前行真相；Cell/CellOrRowHeader 模式以 `DataGrid.CurrentCell.Item` 为当前行真相。
- `CurrentRow` 是 MVVM 友好的当前项镜像，不创建第二套行对象。
- `CurrentColumn` 可镜像 `DataGridColumn`；跨 ViewModel 使用稳定的 `CurrentColumnKey`，避免业务 VM 依赖 WPF 类型。
- `IsCurrentCell` 仅用于宿主样式区分活动 Cell；通过 Item、Column 与 DataGrid.CurrentCell 比较，不缓存虚拟化容器。
- 不自动写业务对象的 `IsSelected` 属性；多选集合同步必须是独立、显式功能。

## 鼠标同步

- 不得在 `PreviewMouseLeftButtonDown` 阶段写入 `DataGrid.CurrentCell` 或主动聚焦目标 Cell；这会改变同一次原生鼠标路由的前置状态，使跨 Cell 单击被 `DataGridCell` 误判为编辑入口。
- 原生列先由 `DataGridCell` 完成鼠标焦点、选择和 CurrentCell 更新，再通过 `CurrentCellChanged/SelectionChanged` 合并回写。
- TextBox、CheckBox、Button、TemplateColumn 等子控件若需要点击后备同步，应在冒泡末端（包含已处理事件）或当前鼠标路由结束后执行，不得抢在 `DataGridCell.MouseLeftButtonDown` 类处理器之前。
- 仅同步焦点/选择，不因单击调用 `BeginEdit`，也不阻止 Button 命令或控件自身事件。
- 尊重 `SelectionMode`、`SelectionUnit` 和 Ctrl/Shift 多选语义；单选修复不得破坏扩展选择。

## 双向同步

- View 到 VM：SelectionChanged/CurrentCellChanged 更新附加属性。
- VM 到 View：FullRow 更新 SelectedItem；Cell/CellOrRowHeader 只移动既有 CurrentColumn 对应的 CurrentCell，不破坏 SelectedCells。
- 使用重入门闩区分 View 更新与 Binding 回写，处理 ItemsSource 重置和项被移除。
- CurrentCell 镜像不取代原生属性，不持有已虚拟化的 DataGridCell 控件实例。
- `CurrentRow` 是单值活动行，不等于 SelectedCells 涉及的所有行；没有真实批量业务需求时不输出选区集合。
- 页面可用 `GridSelectionUnitConverter` 将普通 bool 开关映射到 FullRow/CellOrRowHeader；Convert/ConvertBack 的非法输入统一回退 FullRow。

## 验收

覆盖 TextBox、CheckBox、Button、DataGridTemplateColumn、空白区域、行头、多选修饰键、虚拟化后滚动、ItemsSource 替换和同值重复赋值。
