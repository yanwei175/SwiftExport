---
name: wpf-row-drag-reorder
description: SwiftExport WPF 行拖拽重排。用于 DataGrid 行首手柄拖动、插入位置换算，以及 SortNo、订单明细 LineNumber 重编号、保存和撤销；不用于跨列表转移或树节点改父级。
---

# WPF 行拖拽重排

适用于当前 .NET 10 WPF 客户端的平面业务列表。先按 WPF 技能组选择页面、MVVM 和验证规则；本技能不授予修改 EnterpriseGrid 框架、后端或其他订单页面的权限。

## 实现位置与现有能力

- 项目已引用 `gong-wpf-dragdrop`。优先复用其拖动阈值、取消、插入线和边缘滚动；使用自定义拖拽源及 DropHandler，不让默认 DropHandler 直接改业务集合。
- View/code-behind 负责 WPF 命中、手柄、编辑提交和插入槽转换；ViewModel 负责门禁、集合顺序和业务编号。Gong 的 `IDragInfo`、`IDropInfo` 不进入业务 ViewModel。
- 现有实例：仓库根目录下 `SwiftExport.Client.Wpf/Features/Products/Colors/Views/ProductColorsView.xaml` 及同目录 `ProductColorsView.xaml.cs`；业务入口为 `SwiftExport.Client.Wpf/Features/Products/Colors/ViewModels/ProductColorsViewModel.cs`。
- `BaseListModel.Items` 是只读可观察集合。通过 `Lines.Move(line, finalIndex)` 移动已有对象；不复制 DTO、不重建整表，不调用 `AcceptChanges()` 掩盖未保存重排。

## 拖放边界

- 可编辑表格优先仅从行首手柄发起拖动，保留文字选择、勾选、编辑及 DataGrid 原生键盘行为。明确单行或多行语义；颜色页是单行，勾选和多选不自动扩大拖动范围。
- 开始拖动前提交当前单元格和行，验证失败阻止拖动；落下时再次检查提交及门禁。拖动本身不调用保存 API。
- 来源必须属于当前表格、当前编辑集合；拒绝其他窗口/列表、过期快照、删除行、无效目标。DragOver 与 Drop 均检查，不能只依赖手柄的 IsEnabled。
- 忙碌、无写权限、业务状态禁止编辑时拒绝重排。全局顺序只能在完整列表上调整；搜索、筛选、分组、表头排序或 CustomSort 生效时禁止拖动。清空搜索恢复 `Filter = null`。分页数据不得冒充完整顺序。
- 保留虚拟化。每次读取当前命中元素的 DataContext，拖动载荷使用稳定模型对象/身份；不要缓存 DataGridRow/DataGridCell，也不要把创建控件时捕获的对象当成永久行参数。
- 提供插入线和可理解的手柄名称、操作说明，并提供符合页面快捷键约定的键盘等价入口。焦点和当前行跟随移动对象。

## 插入槽与最终索引

Gong 的 `InsertIndex` 表示尚未移除源行的插入槽，范围为 `0..Count`。单行在同一完整列表内移动时：

```csharp
var oldIndex = Lines.Items.IndexOf(line);
var finalIndex = insertIndex > oldIndex ? insertIndex - 1 : insertIndex;
```

先检查来源、成员资格和插入槽范围，再换算并检查最终索引；原地及紧邻自身的插入槽不产生脏状态。ViewModel 接收最终索引，避免 UI 和 ViewModel 重复减一。

## SortNo 与订单 LineNumber

- 先确认编号语义及保存链路：颜色 `SortNo` 是全局显示顺序；订单 `LineNumber` 通常是单据内行序，但须检查实际 Entity、DTO、LineModel、映射、仓储读取及消费者，不默认所有订单都可编辑。
- 对约定连续编号的列表，移动后在 `Lines.SuppressListEvents()` 中按完整有效列表顺序赋值 `1..N`，由现有行属性跟踪产生 Modified。不要在 `SuppressChangeTracking()` 中消除业务编号变更。
- 保持 Uid、Version、产品、数量、金额和上下游关联不变。不得用 LineNumber 替换明细身份；如消费者把行号当关联键，先核实调整规则。
- 按单据自身草稿/确认/过账状态、权限及并发规则决定是否允许重排；同一单据的 LineNumber 不得跨单据连续编号。
- 保留原批量/整单保存、Version 和事务边界。失败保留草稿；成功按现有协议接纳、重载并刷新实际受影响的缓存。撤销须同时恢复字段编号和集合顺序。
- 新增/删除是否立即重编号遵循页面既有业务约定，不因加入拖拽顺带改变；不要为尚未授权的订单页预先接线或新增通用框架。

## 验证重点

- 测试向上/向下跨多行、首尾、原地/相邻槽、越界、外部来源及过期对象；断言最终对象顺序、编号、选择、身份和脏状态。
- 测试搜索禁用及清空恢复、Busy/只读/释放、拖动中门禁变化、无效编辑阻断、多选仍只移动目标行。
- STA 测试加载真实 DataGrid，验证手柄命中及非手柄拒绝，连续重排并滚动回收容器，再移动当前显示行。不得用关闭虚拟化让测试通过。
- 覆盖保存失败保留、撤销恢复和保存请求中的业务编号；人工验收实际鼠标拖放、插入线、边缘滚动、Esc 取消和键盘等价路径。记录未执行的人工项。
