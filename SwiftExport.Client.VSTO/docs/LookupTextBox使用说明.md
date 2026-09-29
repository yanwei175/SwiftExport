# LookupTextBox 使用说明

## 位置与边界

控件位于 `Resources/Controls/LookupTextBox.xaml`，命名空间为 `SwiftExport.Client.VSTO.Resources.Controls`。从 .NET 10 客户端移植控件及其必要的文本匹配、提交原因、提交事件和自定义候选宿主接口，适配 net48／C# 7.3。

控件使用现有 VSTO 主题资源，必须在宿主 WPF 资源初始化后创建。不要把这个 UserControl 当作 ResourceDictionary 合并进 Themes.xaml。它不依赖 EnterpriseGrid，不创建 Application、不持有 Excel COM、不调用 API，也不加载缓存。页面负责提供候选、查询命令和手动补充资料的模型。

## 使用缓存候选

已移植的客户和销售产品包装器以及 HTTPS 缩略图见 [客户与销售产品选择控件](客户与销售产品选择控件.md)。

页面构造函数注入 `IReferenceDataCacheService`，暴露为 `ReferenceData`；页面首次使用时异步调用 `EnsureLoadedAsync()`，成功后候选集合通过绑定更新。缓存加载约束见 [基础资料缓存接入说明](基础资料缓存接入说明.md)。

```xml
<!-- 根页面声明：
xmlns:controls="clr-namespace:SwiftExport.Client.VSTO.Resources.Controls"
-->
<controls:LookupTextBox
    SearchMode="Local"
    ItemsSource="{Binding ReferenceData.Customers}"
    DisplayMemberPath="CustomerName"
    FilterMemberPaths="CustomerCode,CustomerName"
    SelectedValuePath="Uid"
    SelectedItem="{Binding SelectedCustomer, Mode=TwoWay}"
    SelectedValue="{Binding SelectedCustomerId, Mode=TwoWay}"
    Placeholder="输入客户代码或名称"
    ShowDropDownButton="True"
    ClearSelectionOnEdit="False"
    RequireItemMatch="True"
    SelectionCommittedCommand="{Binding CustomerSelectedCommand}"
    SelectionClearedCommand="{Binding CustomerClearedCommand}" />
```

`SelectedCustomer` 是页面自己的选择状态；不要修改候选 DTO 的属性。需要补充或调整资料时，把值写到页面独立导入模型，最后组装 DTO。

回填已有选择请设置 `SelectedItem`。`SelectedValue` 是依据 `SelectedValuePath` 提取的值，单独设置 ID 不会自动反查候选。`Text` 表示搜索输入，不是已选择对象的显示文字。程序回填选择不会执行提交命令。

## 保留的交互接口

| 属性／入口 | 行为 |
|---|---|
| `SearchMode="Local"` | 在现有候选中搜索；缓存场景建议显式设置 |
| `SearchMode="Remote"` | 编辑防抖后执行 SearchCommand，控件不请求网络 |
| `SearchMode="Auto"` | Text 没有绑定时本地过滤；有绑定时由调用方提供候选 |
| `FilterMemberPaths` | 多字段以逗号、分号或竖线分隔；空格片段按顺序匹配 |
| `SearchDelay`／`MinimumSearchLength` | 远程防抖间隔默认 300 ms，最短查询默认 1 字符 |
| `SelectionCommittedCommand` | 显式提交时执行；默认参数为候选，可另设参数 |
| `SelectionCommitted`／`CommitReason` | 获取 Enter、Tab、鼠标等提交来源 |
| `SelectionClearedCommand` | 用户点清除时执行，清除不触发搜索 |
| `QueryCommand` | 无可提交候选时按 Enter 执行，参数为输入文字 |
| `ClearSelectionOnEdit=False` | 搜索期间保留原选择；Esc／离开时可恢复显示 |
| `ItemTemplate` | 定义候选展示；与 DisplayMemberPath 互斥应用 |
| `ShowDropDownButton` | 显示显式展开按钮，默认关闭 |
| `StretchToHost` | 允许填满原生 DataGrid 编辑单元格高度 |
| `MatchSuggestionFontSize` | 可选让候选跟随控件 FontSize，默认保持候选独立字号 |

空输入时 Space／Down 可以展开候选，方向键导航，Enter 提交，Tab 提交后保留宿主焦点导航，Esc 关闭或恢复原选择。初始化和仅获得焦点不自动发起搜索或弹出列表。

远程模式下，页面命令仍需实现取消、过期请求拦截、会话版本检查和错误提示。候选集合必须在 UI 线程更新；控件中的调度防护不等于允许跨线程修改 ObservableCollection。

## VSTO 适配

- 替换 nullable reference、集合表达式、主构造函数、Math.Clamp 和 StringSplitOptions.TrimEntries，保留原功能，不使用源生成器。
- 控件内部修改 SelectedItem／SelectedValue／IsDropDownOpen 使用 SetCurrentValue，避免覆盖调用方绑定。
- 集合采用弱事件并在 Loaded／Unloaded 对称重连；重复开关页面后仍接收缓存刷新。
- 隐藏／卸载停止防抖、关闭弹层；排队 FocusEditor 操作核对生命周期，关闭后不抢焦点或清除选择。
- 查看文字与占位文字跟随控件字号，边框使用当前 VSTO 已有的 TextControlBorderThemeThickness。
- 没有迁入业务 Lookup 包装器、独立 Grid 框架或未被控件使用的 LookupCommitContext。

## 验证与人工验收

独立 STA 验证入口：

```powershell
& .\SwiftExport.Client.VSTO.WpfTests\bin\Debug\net48\SwiftExport.Client.VSTO.WpfTests.exe --lookup-only
```

专项检查实际 XAML／模板、固定 Light 字典资源、字号、本地过滤隔离、双向绑定、路由键提交、清除命令、重载订阅和卸载后的迟到焦点。测试使用不激活的独立窗口和合成路由事件，不发送全局键盘输入；不等于 Excel 内真实键鼠验收。

页面接入后检查：Excel 内中文输入法、鼠标选项、Tab／Esc、弹层跨窗口定位、DPI、滚动、实际远程搜索和关闭过程。控件不改变宿主窗口的模态／非模态输入策略。
