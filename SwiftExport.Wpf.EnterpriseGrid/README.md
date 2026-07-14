# SwiftExport WPF Enterprise Grid

这是一个以原生 `System.Windows.Controls.DataGrid` 为唯一宿主的企业级增强框架。它采用附加属性、每实例协调器、原生列便利类型和数据优先的导出管线，不建立第二套 Grid/Column 对象模型。

## 依赖边界

- 目标框架：`.NET 10 Windows / WPF`。
- 运行时依赖：仅 WPF/.NET；没有商业 Grid、WPF-UI、HandyControl、MaterialDesign、Office Interop 或 Excel 第三方包。
- 宿主可以继续使用 WPF-UI 的 `FluentWindow`、`ContentDialog`、Snackbar 和文件选择服务。Grid 项目不引用这些类型，因此两者可并存。
- 框架不引用业务 Model、Contracts、DTO、API 或 `LineState`，也不保存或推进业务 RowState。

## 最小启用

```xml
<DataGrid xmlns:gridCore="clr-namespace:SwiftExport.Wpf.EnterpriseGrid.Core;assembly=SwiftExport.Wpf.EnterpriseGrid"
          xmlns:gridOperations="clr-namespace:SwiftExport.Wpf.EnterpriseGrid.Operations;assembly=SwiftExport.Wpf.EnterpriseGrid"
          xmlns:gridSelection="clr-namespace:SwiftExport.Wpf.EnterpriseGrid.Selection;assembly=SwiftExport.Wpf.EnterpriseGrid"
          gridCore:EnterpriseGrid.IsEnabled="True"
          gridCore:GridVirtualization.IsEnabled="True"
          gridSelection:GridSelection.IsCellClickSelectionEnabled="True"
          gridSelection:GridSelection.CurrentRow="{Binding SelectedLine, Mode=TwoWay}"
          gridOperations:GridOperations.IsEnabled="True" />
```

每个功能都有独立开关。仅设置 `EnterpriseGrid.IsEnabled` 只创建协调器，不会自动改变全部原生交互。

## 功能入口

| 能力 | 主要 API |
|---|---|
| 选择与焦点 | `GridSelection.CurrentRow/CurrentCell/CurrentColumnKey`、`GridSelectionUnitConverter` |
| 编辑 | 完全沿用原生 DataGrid/Column 的双击、F2、Enter、Tab、Shift+Tab、Esc |
| Excel 操作 | `GridOperations`；Ctrl+Z 撤销，Ctrl+C/Ctrl+V 复制粘贴，Ctrl+D 向下填充，Delete 清空选中的可编辑文本 Cell；Grid 级操作仅在 Cell/CellOrRowHeader 下生效 |
| 验证 | `GridValidation.Errors/FirstError/FocusFirstError` |
| 行状态视觉 | `GridRowPresentation.MarkerBrush/MarkerText/MarkerGlyph` |
| 列 | `GridColumn` 元数据、`NumericDataGridColumn`、基于模板列的 `LookupDataGridColumn` |
| 行号 | `GridRowHeader.ShowRowNumber`，按当前 DataGrid 视图显示一基行号 |
| 筛选与分组 | `GridFilter`、`GridFilterSet`、`GridFilterRow`、`GridGroupDescriptor` |
| 列选择与布局 | `GridColumnChooser`、`GridLayout.State`、`GridLayoutManager` |
| 导出 | `GridExport.CreateRequest`、`CsvGridExportProvider`、`XlsxGridExportProvider` |
| 虚拟化 | `GridVirtualization.IsEnabled`，启用行列 UI 虚拟化与 Recycling |

完整约定、示例和限制见 [1.0 使用与迁移指南](docs/1.0使用与迁移指南.md)。
