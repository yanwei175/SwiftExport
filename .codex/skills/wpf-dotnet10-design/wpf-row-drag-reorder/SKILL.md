---
name: wpf-row-drag-reorder
description: SwiftExport WPF 行拖拽重排。用于共享 RowDragReorder 手柄行为、SortNo 或订单 LineNumber 重编号、保存和撤销，以及箱单受限包装分组重排；不用于跨列表转移或树节点改父级。
---

# WPF 行拖拽重排

业务行重排由客户端承担，不属于 EnterpriseGrid 模块。按本次改动选择 MVVM、控件和测试规则，不因消费已有行为重做整个页面设计。

## 共享入口

- 视觉桥接：`SwiftExport.Client.Wpf/Resources/Behaviors/RowDragReorder.cs`；命令参数：`Mvvm/RowReorderRequest.cs`、`Mvvm/RowDropPosition.cs`，后两者相对客户端根目录。
- `RowDragReorder.IsEnabled` 绑定页面的 `CanReorder`；`MoveCommand` 绑定 CommunityToolkit 生成的 `ReorderLineCommand`；行首元素设置 `IsHandle=True`。
- `AllowBusinessGrouping` 默认 false，仅箱单等拥有明确业务分组规则的页面显式开启。可选 `CommitEdits` 回调负责组头等额外编辑器；缺省提交 DataGrid 当前单元格和行。
- 行首手柄及命名列由页面 XAML 声明；框架不注入排序列。手柄列只读，禁用排序、筛选和导出，提供 Automation 名称及操作说明。
- 复用现有 Gong 包的阈值、取消、边缘滚动和插入线；此行为拥有该表格的 Gong 拖放接线，不与其他 DragHandler/DropHandler 混用，不调用默认 DropHandler 改集合。

## 命令协议与职责

- `RowReorderRequest(SourceItem, TargetItem, Position)` 使用实际模型对象和目标锚点；位置为 `Before`、`After` 或无目标行时的 `End`。
- 平面页面用 `request.TryResolve(Lines.Items, out line, out finalIndex)` 解析移除源行后的最终索引；拒绝过期源/目标、原地及非法落点。不要再对结果减一。
- View/共享行为负责命中、编辑提交、键盘与插入提示；ViewModel 负责权限、业务状态、集合顺序、编号和保存。Gong 类型不进入业务 ViewModel。
- `ReorderLineCommand.CanExecute` 和执行入口都检查当前业务门禁，不能仅依靠手柄 IsEnabled。命令仅改当前编辑集合，不调用 API。
- 使用 `Lines.Move(line, finalIndex)`；`BaseListModel.Items` 为只读可观察集合，不让拖拽库直接 Add/Remove，也不复制 DTO 或重建整表。
- 现有接线实例：客户端 `Features/Products/Colors`、`Features/Sales/Orders`、`Features/Procurement/PurchaseOrders`、`Features/Shipping/PackingLists` 下的详情 View 和 ViewModel。

## 交互与门禁

- 只从手柄拖单行，多选与勾选不扩大范围。开始及落下时提交编辑；原生转换/提交失败阻止移动，保存仍负责最终业务校验。
- 保留虚拟化，不缓存 DataGridRow/DataGridCell。每次从实际命中元素读取当前数据对象，拒绝外部表格、删除行和旧快照。
- Busy、只读、权限不足或业务状态禁止编辑时拒绝重排。筛选、额外排序和 CustomSort 期间禁止移动；清空搜索恢复 `Filter=null`，不清除用户的其他视图状态来强行重排。
- `RowReorderRequest.IsViewInSourceOrder` 供页面复核视图；共享行为还检查原生表格配置。分页不能冒充完整业务顺序。
- `Alt+↑ / Alt+↓` 使用同一重排命令；原生编辑器和已处理输入优先。行为在禁用、卸载时解除订阅，重载时幂等接线。

## 各页面业务约定

- 颜色以完整列表重编 `SortNo=1..N`；销售以完整有效明细重编 `LineNumber=1..N`，遵守现有维护权限、只读、Busy 和待决命令门禁。
- 采购仅创建态新增行允许重排，不扩大已保存订单编辑能力。订单的编号仅在各自单据内连续。
- 箱单允许现有 `DisplayGroup` 包装分组视图。拼箱成员只能落在同组成员前后；普通行落在拼箱成员上时定位到整个块之前或之后，不得插入拼箱内部。拒绝组头及组间空白落点。
- 箱单整组移动保留工具栏“拼箱上移／下移”按钮，不增加组头拖动；分组支持不代表可跨组改变 PackingGroupId。
- 在 `Lines.SuppressListEvents()` 内移动及重编号，由现有字段跟踪产生 Modified。不要用 SuppressChangeTracking 或 AcceptChanges 消除排序草稿。
- 保持 Uid、Version、产品、数量、金额和来源/上下游关联不变；不能用 LineNumber 替代行身份。新增、删除是否立即重编号沿用页面原有约定。
- 沿用整单/批量保存、Version 和事务边界；失败保留草稿，成功按已有协议接纳/重载，撤销同时恢复编号和集合顺序。本技能不授权后端、其他页面或数据库变更。

## 定向验证

- 优先复用已有测试；新增行为覆盖上下跨多行、首尾、原地、外部及过期对象、编辑失败、权限/Busy/只读、筛选及恢复。
- 共享行为使用 STA 测试验证真实手柄、非手柄拒绝、键盘、启停/卸载重载、多 Grid、ItemsSource 更换和 200 行容器回收。
- 页面测试断言编号、身份、选择、脏状态、保存内容、失败保留及撤销；箱单验证组边界、普通行跨块、整组按钮和包装数据不变。
- 完整客户端启动和截图不是默认验收步骤；实际鼠标拖放、插入线、边缘滚动与 Esc 取消按交互风险按需验收，记录未执行项。
