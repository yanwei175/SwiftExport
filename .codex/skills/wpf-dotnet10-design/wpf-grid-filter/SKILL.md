---
name: wpf-grid-filter
description: SwiftExport WPF 原生集合视图与页面筛选边界；GridFilter/FilterRow 封装已退出，不恢复无业务调用模块。
---

# WPF Grid 筛选边界

- EnterpriseGrid 的 Filtering、FilterRow、描述模型和远程命令桥接因无业务入口已退出，GridColumn.CanFilter 也退出。
- 本地筛选、排序、分组直接使用 ICollectionView / CollectionViewSource，保留宿主视图所有权，不复制业务集合。
- 远程查询使用页面既有查询条件和命令；取消、去抖、最新请求获胜及错误反馈由 VM/Feature Service 负责。
- 不因筛选 UI 需求重新创建统一 Grid 描述模型；先明确真实页面字段、空值、文化、组合条件和查询语义。
- 应用视图变化前处理原生编辑提交失败，不无声丢值；当前项被筛掉后的选择由页面决定。
- 验证清除筛选、多个独立视图、编辑中筛选和虚拟化。相关原生视图兼容测试继续保留，不运行已退出封装的测试。

## 客户页独立筛选（2026-10-17）

- DataGridExtensions 2.9.1 只负责筛选。顶部关键词筛选已退出，扩展独占 ICollectionView.Filter；页面不复制业务集合，也不订阅 FilterChanged、列筛选属性或 RowEditEnding 协调业务。不得新增耦合业务状态的筛选 Behavior。
- 使用默认单行表头及 MultipleChoiceFilter Popup（HasTextFilter=True）。短模板选择库控件，命名样式重写视觉模板但保留库筛选逻辑；IsAutoFilterEnabled 本身不等于自动选择多选 Popup。模板列声明 SortMemberPath，勾选列禁用筛选。
- 共享 Resources/DataGrid.xaml 合并 DataGridFilterResources.xaml，提供 SwiftExportColumnFilter 和 SwiftExportFilterColumnHeader。命名筛选样式重写 MultipleChoiceFilter.ControlTemplate，按钮、输入框、复选框和列表显式使用 WPF-UI 样式及主题资源，Popup 允许透明；不再通过隐式样式或系统画刷键间接修改库模板。保留 PART_ListBox、Values/Text/IsPopupOpen 绑定及库的筛选算法。FilterPopupFocusBehavior 负责初始焦点、失焦与关闭键；FilterSelectAllBehavior 负责候选全选三态，不依赖业务 VM。模板样式使用静态引用，列表多选模式保留局部值，确保卸载清理旧选区时不会回退 Single；事件在 Detach 解除，排队焦点在关闭/卸载时取消。
- 客户页退出产品抽屉、筛选等待镜像和新增前清除条件的事件桥接。产品管理由已有 customer-products 独立工作区负责，产品切换和关闭继续检查其自身草稿。
- 原生转换失败保留输入；修正或取消后由用户重新操作筛选，不承诺自动重试。新增保持现有条件，不匹配的新行可能隐藏；清除筛选后可见。保存、放弃修改及关闭保护覆盖全部客户草稿，包含隐藏行。
- 全选/反选及结果统计读取实际集合视图，不依赖扩展事件。GetAll 加载、失败保留草稿和取消迟到结果继续由 VM 负责。
- 列显隐使用独立 DataGridColumnVisibilityBehavior，仅操作原生 Visibility，不引用 DataGridExtensions 或客户 VM，不主动重算筛选。隐藏列条件何时重算沿用库行为，不承诺即时暂停/恢复筛选。
- 选区参数直接绑定原生 SelectedItems，由 VM 命令处理；公共清除命令先提交原生编辑，再清除列条件，不创建业务事件订阅链。
- 不启用该包的剪贴板、编辑快捷键或选择同步。回归组合筛选、Popup、无效输入、隐藏草稿、原生编辑、Ctrl+D 以及独立工作区关闭保护。

筛选 Popup 顶部左侧为全选 ToggleButton，右侧为“清除全部筛选”按钮；右键菜单中的清除入口退出。按钮绑定 Resources/Commands/DataGridFilterCommands.Clear，通过模板内 FilterButton.Tag 的祖先绑定取得宿主 DataGrid，不依赖业务 VM。Popup 空白布局背景参与命中，FilterPopupFocusBehavior 截断未处理的左键冒泡，避免触发列头排序。命令只访问 DataGrid，先提交 Cell/Row 编辑，失败保留原输入和条件，成功后调用 GetFilter().Clear()。客户页后台只保留 InitializeComponent；顶部搜索框、Keyword、SearchCommand、CustomerFilter 和 GlobalFilter 绑定退出。筛选隐藏的草稿仍参与保存及关闭保护。
