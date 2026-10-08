# 流量管理前端

## 入口与操作

入口为「品管 → 流量管理」。列表按物料、SKU 快照和测试日期范围查询，每页 20 组；新建及打开详情采用文档页签。保存和删除后，列表通过「刷新」读取最新结果。

详情编辑测试日期、市场说明、公共配件配置、备注及功能测量行。新建时选择物料，保存后物料固定。功能与限流器从既有字典中选择；每行可有不同限流器。纠正已有记录可保存；更换限流器重新测试或再次实测，应在列表中新建测试组。

1–8 bar 测量列单位为 L/min，20–100 PSI 测量列单位为美国 GPM。空值表示未测，0 表示实测零值，不提供换算、插值或标称规格填入实测值。表格横向滚动，功能与限流器保持冻结。

## 所属职责和契约

- `Views` 负责原生编辑提交、转换错误与焦点；不调用 API。
- `ViewModels` 直接调用单个整组用例的现有 `IFlowApi`，不另建只有转发职责的 Feature Service。
- `Models` 复用 `BaseLineModel` 与 `BaseListModel` 维护身份、版本、脏状态及删除池；页面复用 `ViewModelBaseEx` 的 Busy/写入入口，命令与属性使用 CommunityToolkit 生成。
- 读写 DTO、请求包装、Refit 路由及权限编码均复用现有 Contracts；本次不新增共享契约。结构映射集中在 `Mappings/FlowMappings.cs`。
- 物料搜索复用产品选择器和窄查询接口；字典复用会话缓存及 `FlowTestFunction`、`FlowLimiterSpecification` 分类。刷新字典不改历史快照，不制造脏状态。

权限分别使用 `QualityControl.Flow.View/Create/Edit/Delete`。菜单和详情路由要求 View，字段按 Create/Edit 控制，Delete 独立于 Edit。后端仍负责最终校验、事务、快照和并发版本。

持久化页签键为 `flow:头Uid`；每次新建为 `flow:new:临时Guid`，保存成功后转换为持久化键。重复打开同一组由公共导航激活已有页签。未保存输入和无法转换的文本参与关闭确认，写入期间不能关闭。

## 异步及验证边界

列表与物料搜索是替代型查询，取消旧请求并检查请求身份、权限及页面存活。详情初始化幂等，显式重新读取先确认放弃输入；每次写入只有一个最外层 Busy。页面释放取消查询并解绑事件，迟到结果不回写。

前端检查必填、长度、测量点、负值、四位小数和重复功能；字典新选择要求启用，既有停用项可保持。绑定转换错误由视图阻断，业务错误在每次保存时重新验证，服务端字段键仅用于定位。冲突和失败保留输入、版本及删除池；提交结果未定时禁止重发，先在列表核实或重新读取已有组。

原生 DatePicker 解析错误会恢复旧选择，因此本页面在日期解析失败时清空选择并提示修正，防止误提交旧日期。控件行为参考 [WPF DatePicker 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/DatePicker.cs)。

## 验证与限制

自动验证在 `SwiftExport.Client.Wpf.Tests/Features/QualityControl/FlowManagement`，使用离线 API、真实模型和真实 WPF 控件。STA 测试复用项目常驻 Application 宿主，窗口放在屏幕外且透明，加载真实 App.xaml 资源；预览保存于忽略的 TestResults 目录。

覆盖完整新增与整组增改删、两套压力列、空值/零值、各列负值、精度、重复功能、字典停用和历史快照、失败及版本冲突保留、未决提交、重复写入、关闭保护、替代查询、释放、路由身份与权限。真实表格检查 zh-CN/de-DE 小数输入、错误数字和日期的修正、只读编辑及视图卸载。

使用原生键盘编辑、文字行状态及 Automation 名称，保留主题资源；不修改 EnterpriseGrid 框架。未引入导入导出，因此文件安全门禁不适用；固定分页和虚拟化避免加载全部历史，尚未进行大规模性能测量。

真实服务端增删改联调、屏幕阅读器、高对比度、200% 缩放和完整键盘巡检仍需人工验收。本次不写运行数据库、不自动部署；有写库验收需要先明确隔离实例、库名和初始化/清理范围。后端、Contracts、Grid 框架及其他任务改动不在本次写入范围。

2026-10-08 验证：流量模块 32 项及导航权限／生命周期 27 项，共 59 项通过；客户端项目构建通过。命令为：

```powershell
dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter 'FullyQualifiedName~FlowManagement|FullyQualifiedName~NavigationServiceTests|FullyQualifiedName~NavigationAuthorizationTests|FullyQualifiedName~NavigationLifecycleTests'
dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj --no-restore
```

测试编译仍提示已有产品页面、导航及其他模块测试警告；本次新模块没有新增编译警告。早期混合导航 STA 测试等待停滞后，流量视图测试已改为复用项目常驻 Application 宿主；最终结果以上述明确范围为准，未声称运行全部客户端测试。
