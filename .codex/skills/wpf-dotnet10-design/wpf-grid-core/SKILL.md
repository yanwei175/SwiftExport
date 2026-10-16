---
name: wpf-grid-core
description: SwiftExport 原生 WPF DataGrid 客户端适配技能。用于行号、软提示附加属性、独立生命周期、事件订阅、资源和原生兼容性。
---

# WPF Grid Core

## 当前边界

- EnterpriseGrid 独立项目、总入口和协调器已退出。功能归客户端 `Resources/Behaviors`，不恢复总开关、模块注册或空壳项目。
- 原生 DataGrid 是唯一宿主。`GridRowHeader`、`GridValidation` 各自按需启用，内部实例只管理自己的事件。填充行为独立管理生命周期；无调用的选择桥接已退出。
- 启停、Loaded/Unloaded/Detach 必须幂等；只处理宿主加载卸载，卸载释放模型/集合订阅与排队工作，重载读取当前数据源，关闭移除自身状态。
- 不保存业务 RowState、不修改 ItemsSource、不调用业务 API；业务写入通过已有页面命令完成。

## 附加属性与视觉

- 不建立第二套 CurrentItem/CurrentCell。仅真实双向状态使用 BindsTwoWayByDefault；错误摘要是只读呈现。
- 列识别使用原生列对象；确需 View 事件识别时用具名列引用。GridColumn.Key 已删除。
- 模板软验证映射仅用 GridValidation.FieldPath；普通绑定列读取 Binding.Path，填充许可另用 DataGridFillDownBehavior.Field。
- 客户端可复用现有 Microsoft.Xaml.Behaviors.Wpf；不为行号/验证增加 Behavior 包或第三方 Grid 依赖，不引入 DataGridExtensions。
- 命名样式沿用客户端主题规则，不将 WPF-UI 弹窗、服务或业务类型写入适配。

## 行号与验收

- GridRowHeader.ShowRowNumber 表示当前视图的一基行号；ShowRowNumberInColumn 提供只读原生列。二者同时启用时列模式优先。
- 不写业务 RowIndex/SortNo/LineNumber，不覆盖宿主 RowHeaderWidth/Style/Template，不缓存 DataGridRow，不关闭虚拟化。
- 关闭/卸载只移除适配拥有的列和行头值，保留业务列；排序、筛选、集合替换与回收后重算。
- 不安装 Copy/Paste/Delete/Undo 命令。Ctrl+D 的输入所有权按 column/editing 技能；原生删行禁用用 CanUserDeleteRows=False。
- 回归归 SwiftExport.Client.Wpf.Tests/Resources/DataGrid：启停、卸载重载、数据替换、排序筛选、虚拟化及原生输入。行为桥接测试归 Resources/Behaviors。
