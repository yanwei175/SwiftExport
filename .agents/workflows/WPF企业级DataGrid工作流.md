# WPF 企业级 DataGrid 工作流

## 适用

原生 WPF `DataGrid` 客户端适配的架构、API、实现、专项审查和演进。

## 执行顺序

1. 读取 `AGENTS.md`、`.agents/router.toml` 和 `.agents/wpf-enterprise-grid-architect.toml`。
2. 读取 `.codex/skills/wpf-dotnet10-design/wpf-enterprise-grid/SKILL.md` 及其 `references/总体架构.md`。
3. 搜索真实源码：目标 DataGrid、样式、业务行模型、选择绑定和现有测试；源码优先于文档。
4. 判断任务属于架构、核心、选择、编辑、列、验证、状态、筛选或导出，并只加载命中的子 Skill。
5. 涉及 ViewModel/LineModel 时，额外读取 `communitytoolkit-mvvm/SKILL.md`；涉及主题时读取 `official-theme/SKILL.md`，不把业务弹窗或服务移入通用适配。
6. 先确认原生行为、事件顺序、编辑事务和虚拟化影响，再设计 API；不得从视觉模板反推业务状态。
7. 公开 API 优先使用附加属性、命令、Binding 和小型策略接口；保持可禁用、可解绑和幂等。
8. 用户只要求架构或审查时，不修改业务代码。用户明确要求实现后，按一个可验收能力切片落地。
9. 每个切片检查：原生列兼容、鼠标/键盘、验证失败、虚拟化、原生绑定方向、内存释放、依赖边界（Behaviors 包与填充行为仅属于客户端）。
10. 实现阶段优先运行客户端 Grid 单元测试、WPF STA 交互测试和客户端构建。

## Skill 选择

| 任务 | 必读 Skill |
|---|---|
| 客户端行号、附加属性、生命周期、资源 | `wpf-grid-core` |
| 原生 CurrentItem/SelectedItem、原生 SelectedItems/SelectedCells 命令参数 | `wpf-grid-selection` |
| 原生双击、F2、Enter、Tab、Esc 兼容 | `wpf-grid-editing` |
| 客户端 Ctrl+D、编辑器撤销及页面放弃修改 | `wpf-grid-column` + `wpf-grid-editing` + `communitytoolkit-mvvm` |
| 原生列引用、软验证字段映射、填充许可与模板 | `wpf-grid-column` |
| Cell/Row Validation、错误聚合和软提示 | `wpf-grid-validation` |
| 业务状态到视觉状态的适配 | `wpf-grid-state` |
| 原生 ICollectionView / 页面远程筛选 | `wpf-grid-filter` |
| 客户端业务 CSV/Excel 导出边界 | `wpf-grid-export` |
| 客户端业务行拖拽与 LineNumber / SortNo | `wpf-row-drag-reorder`；不增加 Grid 模块或 Gong 依赖 |

跨模块任务由 `wpf-enterprise-grid-architect` 作为唯一主 Agent 统一 API 与事件所有权。所有能力归客户端且独立管理生命周期。没有总开关或协调器。

## 架构评审门槛

- 不替换 `DataGrid` 或重建 `DataGridColumn` 基类体系。
- 不让框架引用业务 Model、业务状态枚举、Contracts 或服务层。
- 不使用 VisualTree 扫描导出数据，不关闭行/列虚拟化来换取功能。
- 编辑按键由原生 DataGrid/编辑器拥有；客户端填充行为不得抢占双击、F2、Enter、Tab 或 Esc。
- 选区通过命令读取原生集合，不持续镜像；已有适配 Loaded/Unloaded/Detach 成对订阅解绑。
- 业务软验证允许离开 Cell；只有原生转换提交失败时保持焦点，任何模式都不得静默丢弃编辑值。
- 公开 API 具备默认值、绑定方向、空值、只读和版本兼容说明。

## 原生选择接入

- CurrentItem、CurrentCell、SelectedItem 使用原生属性；SelectedItems/SelectedCells 由 DataGrid 提供。
- DataGridSelectionBehavior 无业务调用，已删除实现及专属测试。命令执行时通过 CommandParameter 读取原生集合，跨 await 前复制需要的数据。
- 不将格子涉及的行伪装为完整行选择，不向 SelectedItem 补写 CurrentCell.Item。
- 原生选择语义遵循 wpf-grid-selection；声明示例见 [使用与迁移指南](../../docs/wpf-datagrid/使用与迁移指南.md#41-直接读取原生选区)。

## 输出

1. 当前源码事实与约束。
2. 决策及备选方案。
3. 公共 API 变化。
4. 事件顺序和 MVVM 数据流。
5. 测试与验收场景。
6. 风险、未决问题和下一切片。

## 当前精简基线（2026-10-16）

- 已退出 GridSelection 镜像、SelectionSnapshot、点击后备同步、CanNavigate 及 Numeric/Lookup 自定义列体系。
- 活动行按页面语义直接绑定原生 SelectedItem 或 CurrentItem；不得同时双向绑定同一个 VM 属性。
- 列表重置、删除后由既有 VM/列表清空或恢复活动项；模板内命令直接传行参数。
- 无业务调用的多选桥接已退出；Microsoft.Xaml.Behaviors.Wpf 仍用于客户端 Ctrl+D 等已有行为。
- EnterpriseGrid 独立项目、测试项目、总入口及协调器已退出。行号与必要软验证归客户端 Resources/Behaviors，有效测试迁入客户端。Filtering、Layout、Exporting 不作为待开发路线图。
- GridVirtualization 退出，虚拟化由客户端 SwiftExportDataGrid 命名样式设置原生属性；不再有 EnterpriseGrid 总入口。
- GridSelectionUnitConverter、GridCurrentRowStateConverter、GridCurrentCellStateConverter 及对应测试归客户端 Resources/Converters；GridRowPresentation 及测试归客户端 Resources/Behaviors。
- GridColumn.Key/FieldPath 全部退出。错误使用原生列对象，View 列判断使用具名列引用。模板软验证仅保留 GridValidation.FieldPath（当前为销售 UnitSnapshot），普通列从 Binding.Path 推断。填充字段显式声明客户端 DataGridFillDownBehavior.Field，默认不允许；Grid Undo 及 CanUndo 退出；验证仅保留 Errors、ErrorCount、CellState、RowState，不提供首错导航封装。
- Operations 已整体退出，Ctrl+D 迁至客户端薄行为和页面强类型命令。行为只负责连续矩形选区、视图顺序、原生编辑提交及生命周期，页面检查白名单、当前许可和行归属，并复用业务关系更新方法。主图标志、图片地址、排序号、唯一编码及操作列禁止填充；固定 FullRow 页不新增填充入口。Grid 提交历史退出，不将 Ctrl+Z 绑定成整表 RejectChanges。验证卸载重载、宿主命令保留、关系联动和既有放弃修改。Ctrl+C 完全使用原生 DataGrid，模板业务列显式设置 ClipboardContentBinding；Grid 级 Ctrl+V、Delete 清空和 TSV 管线已退出，不重建剪贴板适配器。编辑器内复制、粘贴、Delete 继续由原生编辑器处理；宿主保持 CanUserDeleteRows=False，关联对象沿用已有业务清空命令。
- 活动样式通过客户端 DataGridPresentation.Owner 继承宿主引用，再读取原生 CurrentCell；不使用容器 FindAncestor，不建立选择镜像。验证未挂载、滚动回收、卸载重载的绑定诊断及活动/验证视觉。
- 共享选择/样式变化须做 Grid 回归及代表性客户端 STA 场景；已有测试宿主共享 Application 冲突时按方法隔离进程，不删除有效断言。
- 当前架构与迁移边界见 Grid Skill 的 references/总体架构.md 和docs/wpf-datagrid/使用与迁移指南.md。

- 软验证只在颜色、字典数据、销售明细、入库行、出库行五处挂接；没有模型错误来源的页面移除挂载，原生验证及保存校验保留。新增挂载须证明真实提示需求。

- GridRowPresentation 仅保留 MarkerBrush/MarkerText，MarkerGlyph 与无读取方 Setter 已删除。软验证只聚合模型错误；仅 IDataErrorInfo 通过 PropertyChanged 通知刷新，INotifyDataErrorInfo 适配分支已退出。通知按 Dispatcher 合并、卸载取消，取消排序且不承诺快照顺序；原生转换/ValidationRule 错误直接由 WPF 和共享样式处理，不二次聚合。回归覆盖原生提交失败与提示、软错误、通知合并、后台事件、换源和待刷新时卸载。

- 采购候选直接展示 ValidationDisplayMessage，退出无显示消费者的 GridValidation 挂载；保留模型业务通知和保存/预览校验。入出库数量删除无消费者的 NotifyOnValidationError=True，原生转换错误与提示不变。
- 中性产品两处 Ctrl+D 由页面固定价格填充顺序：采购价、汇率、利润率先应用，选区明确包含美元售价时最后应用手动值（含 null）；未选择售价时保持模型自动计算。不得由列 DisplayIndex 决定最终价格，也不得抑制业务通知。覆盖正反字段顺序、空手动价、后续编辑计算及放弃修改。
