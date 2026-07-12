---
name: wpf-grid-validation
description: SwiftExport WPF DataGrid 验证技能。用于 IDataErrorInfo/INotifyDataErrorInfo 软提示、原生转换错误、错误汇总、保存阻断、首错导航与无障碍反馈。
---

# WPF Grid Validation

## 责任边界

- 业务规则留在 Model/ViewModel/Application；框架只聚合、呈现、导航和阻止无效提交。
- 支持 WPF `ValidationRule`、`IDataErrorInfo`、`INotifyDataErrorInfo` 与 `BindingGroup`，并区分编辑期软提示和保存期硬校验。
- 异步业务验证由 ViewModel 管理生命周期，Grid 只观察可绑定错误状态。

## 交互规则

- 业务必填和跨字段错误通过 `GridCellValidationState` 呈现，不把 `ValidatesOnDataErrors` 接入 Cell 提交事务。
- 强类型转换等原生提交失败仍保持编辑与焦点；保存命令通过业务模型验证阻断。
- Row 提交前检查 BindingGroup/行错误；不得用弹窗替代单元格错误呈现。
- 错误视觉必须与 Selected/Current/RowState 共存，优先使用边框、图标和 ToolTip，不依赖整行背景。
- 提供“定位首个错误”策略时使用项与 ColumnKey，不缓存 DataGridCell 控件。

## 可访问性与性能

- 错误信息提供文本，不只依赖颜色。
- 监听项级 `ErrorsChanged` 时成对解绑，ItemsSource 重置后不泄漏。
- 不遍历未生成的全部行容器；错误汇总从数据项接口读取。

## 验收

覆盖转换错误、Cell ValidationRule、跨字段 Row 验证、异步错误到达、错误清除、Tab 提交失败、虚拟化行和多个错误的稳定顺序。
