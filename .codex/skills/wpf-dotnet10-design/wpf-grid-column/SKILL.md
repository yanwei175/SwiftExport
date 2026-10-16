---
name: wpf-grid-column
description: SwiftExport WPF 原生列与必要元数据技能。用于原生列引用、软验证字段映射及客户端业务填充边界；不创建自定义 Numeric、Lookup 或 Enum 列。
---

# WPF Grid Column

## 兼容策略

- 任意功能必须先支持 `DataGridColumn`、`DataGridTextColumn` 和 `DataGridTemplateColumn`。
- GridColumn 类型及 Key、FieldPath 已退出。验证错误使用原生列对象归属，View 中确需识别列时使用 x:Name 引用，不建立字符串身份体系。
- 不创建新的 EnterpriseColumn 基类，不要求页面把已有列迁移到自定义体系。

## 列类型规范

- 文本和数值列优先使用原生 `DataGridTextColumn`，视觉修改走 `ElementStyle/EditingElementStyle`。
- ComboBox 列必须使用 `DataGridTemplateColumn`，在 `CellTemplate` 或 `CellEditingTemplate` 中显式放置 `ComboBox`；禁止使用原生 `DataGridComboBoxColumn`。
- CheckBox 列必须使用 `DataGridTemplateColumn`，分别在 `HeaderTemplate` 和 `CellTemplate` 中放置 CheckBox；禁止使用原生 `DataGridCheckBoxColumn`。
- 复选列的 Item/All Binding 由页面显式提供，不约定 BaseLineModel 属性名，也不要为了微调对齐建立参与测量的多层包装模板。
- 图片列、Lookup 和其他自定义编辑器同样使用 `DataGridTemplateColumn`；编辑器较重时，显示态使用轻量 `CellTemplate`，只在 `CellEditingTemplate` 实例化编辑控件。
- Numeric 优先组合 `DataGridTextColumn`、Binding 转换和验证。
- NumericDataGridColumn、LookupDataGridColumn 及其 Provider/编辑器体系已退出，不重新引入便利列。
- 数字输入约束使用原生 Binding.ValidationRules；候选查询由宿主负责，Grid 不加载 API、不缓存业务字典、不写业务状态。
- DataGridColumn 不在可视树中，禁止假设它自动继承 DataContext；ItemsSource/Provider 必须是显式 Binding 或宿主服务。

## 静态枚举列

- 静态枚举使用 `DataGridTemplateColumn + ComboBox`，`ItemsSource` 通过 `x:Static` 或应用级稳定资源提供；禁止让候选源依赖页面 DataContext 中会随卸载变为 null 的临时 `CollectionViewSource`。
- 字符串枚举使用 TwoWay `SelectedValue/SelectedItem` Binding，并覆盖页面卸载、重建、空值和历史未知值场景；初始化候选源不得回写空值制造误脏。
- 用户主动选择候选项才允许回写并推进 Modified；页面加载、卸载、重建和候选源初始化不得修改业务值。

## 验证与操作

- `DataGridTemplateColumn` 无法从原生列类型推断业务 Binding；需要字段验证的模板列必须显式设置 `GridValidation.FieldPath`。
- 原生复制只读取 ClipboardContentBinding（绑定列默认回退 Binding），不读取 FieldPath 或客户端填充字段。模板数据列显式设置 OneWay ClipboardContentBinding，使用名称、编码等业务展示字段，不默认输出隐藏 ID；数值、日期和勾选复制原生值，原生 StringFormat 不保证剪贴板显示格式。图片、操作按钮、拖拽手柄及编辑状态标记保持空白，不创建专用复制列或通用格式转换器。
- CanFilter、CanExport、ExportHeader、ValueAccessor 已随未接入模块退出，不在页面声明或重建。

## 模板内 Lookup 编辑器

分离 `SelectedValue`、`DisplayMember`、搜索文本和候选源。远程查找通过宿主已有 VM/服务完成，支持取消、去抖、空值、无匹配与显示值回填，不把 Supplier 等业务类型写进框架。

## 共同能力

错误归属直接使用原生 DataGridColumn，重排不改变身份。DisplayIndex、FrozenColumn、Visibility 使用原生属性，不增加布局持久化契约。

不恢复导航元数据。仅确有模型字段错误的模板列设置 GridValidation.FieldPath；普通绑定列从 Binding.Path 推断，不从 ClipboardContentBinding 推断业务验证或填充。

## 客户端向下填充

- DataGridFillDownBehavior 位于客户端 Resources/Behaviors，通过 Interaction.Behaviors 挂接 Command，列显式声明 Field；未声明默认禁止，不从 Binding/FieldPath 推断写入许可。
- Field 是页面识别的业务字段，不是反射路径；行为只输出 Mvvm/FillDownRequest，不直接写业务属性，也不依赖 EnterpriseGrid 开关。
- 页面命令在 CanExecute 和执行入口检查当前权限、Busy、行归属、行可编辑状态和字段白名单，按强类型 setter 或既有业务更新方法写入。
- 供应商、主型号、颜色等关系完整维护 Id/编码/名称和相关分类；Lookup 确认与填充复用同一业务更新方法。主图标志、图片地址、排序号、唯一编码、操作列不开放通用填充。
- 同时填充计算依赖与可手动覆盖字段时，由页面固定业务优先级，不沿用列显示顺序。中性产品先填采购价、汇率、利润率，再应用显式选择的美元售价（含 null）；未选售价时沿用自动计算，不抑制模型通知。
- 保留模型通知和脏状态，不调用 SuppressChangeTracking 接纳用户填充，也不新建反射赋值/自动历史框架。原有页面放弃修改恢复基线；采购候选等非 BaseLineModel 页面不自动获得基线恢复。
- 仅连续矩形且 Cell/CellOrRowHeader 可用，按视图顺序取首行；过滤隐藏/只读/未开放列，页内行级规则由 VM 决定。先提交原生编辑，转换失败不写任何目标。
