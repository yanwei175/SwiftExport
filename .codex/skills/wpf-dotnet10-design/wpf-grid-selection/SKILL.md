---
name: wpf-grid-selection
description: SwiftExport 原生 DataGrid 选择绑定和选区命令参数；用于 CurrentItem、SelectedItem、SelectedItems、SelectedCells 及活动样式。
---

# WPF Grid Selection

## 原生状态与绑定

- 原生 DataGrid 是唯一选择和活动位置真相源，不恢复 GridSelection、CurrentRow、CurrentColumnKey 或 SelectionSnapshot 镜像。
- 固定整行选择且需要选中项的页面直接绑定 SelectedItem；需要活动单元格所在行的页面绑定 CurrentItem，显式 Mode=TwoWay。
- CurrentItem 对应 CurrentCell.Item；SelectedItem 在多选时不一定是活动行。不要让二者双向绑定到同一个 VM 属性。
- CurrentCell/CurrentColumn 直接使用原生属性；业务 VM 通常只需要数据项。
- CurrentItem 不承诺在集合重置或移除行后自动清空；页面/列表用例负责清空或恢复有效项。程序化滚动在 Loaded 后由宿主显式 ScrollIntoView。
- 不通过 Preview/冒泡鼠标补写选择，不自动 Focus、BeginEdit 或提交编辑；模板内命令直接传当前行参数。
- GridSelectionUnitConverter、GridCurrentRowStateConverter、GridCurrentCellStateConverter 及测试归客户端 Resources/Converters，框架无 Selection 运行时模块。
- 活动样式通过 MultiBinding 和无状态 Converter 比较原生 CurrentCell，不存储第二份选择状态，不缓存虚拟化容器。客户端 DataGridPresentation.Owner 仅继承宿主引用，由表格样式绑定自身；容器使用 Self 路径读取 Owner.CurrentCell，避免未挂载或回收期间 FindAncestor 警告，不订阅选择事件。

## 原生多选集合

- DataGridSelectionBehavior 无业务调用，已与专属测试一起删除；不预留持续同步快照或双向选区包装。
- 命令执行时通过 CommandParameter 读取原生 SelectedItems/SelectedCells；跨 await 使用前提取本次所需数据，避免持有实时集合。
- FullRow：SelectedItems 是整行集合；Cell：SelectedItems 为空；CellOrRowHeader：SelectedItems 仅包含选中的整行。
- “选中格子涉及的行”须显式从 SelectedCells.Item 去重，不能伪装成 SelectedItems，空选区也不能补入活动行。
- 不自动同步业务 IsChecked/CheckedItems，不改变原生 Ctrl/Shift、焦点和编辑。
- 集合内容变化不等于 CommandParameter 引用变化；依赖数量的命令可用性由页面既有机制通知。
- Microsoft.Xaml.Behaviors.Wpf 继续服务客户端 Ctrl+D 等已有行为，不因删除选择桥接而移除包。

## 验收

原生活动项、编辑兼容、行号与软验证测试归客户端 Resources/DataGrid；现有填充行为测试归 Resources/Behaviors。
覆盖原生 CurrentItem 双向绑定、列表用例清空活动项、选区与活动项区别及已有命令的选区读取。
复用原生鼠标、F2、双击、键盘编辑兼容测试；不保留已退出的选择快照或“点击任意子控件强制选行”断言。
