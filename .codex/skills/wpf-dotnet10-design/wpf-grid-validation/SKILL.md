---
name: wpf-grid-validation
description: SwiftExport WPF DataGrid 验证技能。用于 IDataErrorInfo 软提示、原生转换错误、错误汇总、保存校验边界与无障碍反馈。
---

# WPF Grid Validation

## 责任边界

- 业务规则留在 Model/ViewModel/Application；客户端 GridValidation 只汇总模型软错误和呈现；原生绑定处理转换提交失败，业务命令负责保存校验。
- GridValidation 仅观察当前有业务使用的 `IDataErrorInfo`；原生 `ValidationRule`、转换错误和 `BindingGroup` 沿用 WPF 验证及共享样式，不再订阅 Validation.Error 或二次聚合。
- 异步业务验证由 ViewModel 管理生命周期，Grid 只观察可绑定错误状态。

## 挂接与映射

- 仅在模型实现 IDataErrorInfo 且页面确需软提示时启用；不能因为页面有保存校验就挂接。
- 当前仅颜色、字典数据、销售订单明细、入库行、出库行五处启用。采购候选直接绑定 ValidationDisplayMessage，已退出无显示消费者的软验证挂载。客户 PO 折扣率保留原生 ValidationRule，无需附加聚合。
- GridValidation、GridValidationError、GridCellValidationState 及转换器位于客户端 Resources/Behaviors；适配内部类独立管理生命周期，无协调器。
- 错误引用包含 Item、原生 DataGridColumn（行错误为 null）和文本，不缓存 Cell/Row 容器。
- 普通绑定列读 Binding.Path；模板列只为真实字段错误配置 GridValidation.FieldPath，当前业务仅销售 UnitSnapshot。字段映射不参与复制或填充。
- 卸载/关闭清空呈现并解绑模型，重载恢复当前数据源；不会清空模型业务错误。

## 交互规则

- 业务必填和跨字段错误通过 `GridCellValidationState` 呈现，不把 `ValidatesOnDataErrors` 接入 Cell 提交事务。
- 强类型转换等原生提交失败仍保持编辑与焦点；保存命令通过业务模型验证阻断。入出库数量无事件消费者，已删除 NotifyOnValidationError=True；其他确有业务事件处理的页面保留该声明。
- 业务软错误不参与 Row 提交阻断；原有 BindingGroup/ValidationRule 按原生契约处理，不能把软错误重新接入提交事务。
- 错误视觉必须与 Selected/Current/RowState 共存，优先使用边框、图标和 ToolTip，不依赖整行背景。
- 当前不提供首错导航。HasErrors、FirstError、FocusFirstError 无业务调用已退出；消费 Errors / ErrorCount，保留 CellState / RowState。确有导航需求时先确认页面语义，不缓存 DataGridCell。

## 可访问性与性能

- 错误信息提供文本，不只依赖颜色。
- 通过 IDataErrorInfo 读取错误并观察 PropertyChanged，连续通知合并刷新；没有 IDataErrorInfo 的数据项不建立订阅。
- 无业务调用的 INotifyDataErrorInfo 适配分支已退出；采购模型自身 ErrorsChanged、ValueChanged 和保存校验保持原有职责，不能随 Grid 挂载一起删除。
- 同一 Dispatcher 调度周期内合并错误通知；后台通知只安排刷新，模型读取与快照发布回到 UI 线程。卸载/关闭取消排队工作并解绑，换源后读取当前数据项。
- Errors/ErrorCount 是调度后更新的模型软错误快照，不承诺排序，不包含原生编辑器错误，也不能作为保存校验依据。
- 不遍历未生成的全部行容器；错误汇总从数据项接口读取。

## 验收

覆盖原生转换错误阻止提交及 ToolTip、修正/取消，软错误允许提交，通知合并、后台错误、清除、去重与列关联、换源解绑、待刷新时卸载/关闭及重载。保留已有 ValidationRule、虚拟化与页面保存校验回归，不测试已退出的排序或原生错误二次聚合契约。
