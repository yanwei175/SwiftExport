# 前端 .NET 10 工作流

## 适用

`SwiftExport.Client.Wpf` 当前 .NET 10 WPF + CommunityToolkit MVVM 前端。

## 步骤

1. 先读 `AGENTS.md`、`.agents/router.toml`、`.agents/frontend.toml`。
2. 先读 `.codex/skills/wpf-dotnet10-design/index/SKILL.md`，再按任务读取页面、MVVM、控件、样式子 skill。
3. 涉及 ViewModel / Model / Command 时必须读 `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`。
4. 涉及 lepoco/WPF-UI 控件、FluentWindow、TitleBar、InfoBar、Snackbar、ContentDialog、SymbolIcon 时读 `.codex/skills/wpf-dotnet10-design/wpfui-controls/SKILL.md`。
5. 涉及异步加载、搜索防抖、保存或业务命令、Busy、导航生命周期、页面关闭、错误反馈、事件/Messenger/Timer/CTS 释放时，读 `.codex/skills/wpf-dotnet10-design/runtime-safety/SKILL.md` 及其 `references/运行时安全门禁.md`。
6. 涉及 ViewModel/API/Feature Service 职责、页面权限、普通表单验证、导航参数、Document Tab identity 或 Dialog/抽屉/子页面返回协议时，读 `.codex/skills/wpf-dotnet10-design/page-architecture/SKILL.md` 及其 `references/页面架构边界决策表.md`。
7. 涉及前端测试证据、可访问性、本地化/文化格式、性能、日志/敏感信息、缓存或文件导入导出时，读 `.codex/skills/wpf-dotnet10-design/quality-gates/SKILL.md` 及其 `references/前端质量门禁矩阵.md`；完成前读 `references/检查清单与代表性验收.md`。
8. 涉及 `SwiftExport.Contracts/**` 时必须读 `.codex/skills/aspdotnet10/contracts-refit-dto/SKILL.md` 和 `.codex/skills/aspdotnet10/contracts-netstandard20/SKILL.md`。
9. 前端任务新增或复用 Contracts DTO 时，必须读 `.codex/skills/aspdotnet10/contracts-dto-models/references/DTO复用与文件组织门禁.md`，搜索现有契约、API 和调用方，并记录复用或新建理由。
10. 涉及 Contracts DTO 与 ViewModel、EditModel、LineModel、TreeItemModel 转换时，必须读 `.codex/skills/aspdotnet10/application-mapster-mapping/SKILL.md`，并检查 `SwiftExport.Client.Wpf/Mappings/MapsterConfig.cs` 与目标 Feature 的 `Mappings` 配置。
11. 读取 `.agents/references/工程架构总纲.md`；涉及业务页面同时读业务架构、数据模型；按 `.agents/references/业务模块能力与页面契约治理规则.md` 的模块地图索引读取命中地图；跨采购—入库—库存交接须读三份地图，并检查当前事实与偏差 ID。
12. 读取目标 Feature、View、ViewModel、Model、Service、Navigation、API 注册、API Runner、UI Service 和 XAML，对比能力地图和偏差账本。
13. 列出页面依赖并判定直接 Refit 或 Feature Service；分别定义导航访问、页面操作、字段编辑权限；标明字段/跨字段/异步/服务端验证 owner；写明打开参数、Tab identity、重复打开/激活策略和 Dialog 返回协议。
14. 为每个异步入口标注替代型查询、显式刷新/初始化、写命令或延迟 UI 工作；确认取消策略、Busy owner、页面存活检查、反馈 owner 和释放 owner。
15. 当前任务会修改、复制或扩大偏差时，先输出门禁偏差提醒，说明规则、事实、影响、最小重构和兼容方案，然后暂停等待用户决定。
16. 按 `Features/{业务域}/{功能单元}/Views|ViewModels|Models|Services|Components` 组织新增文件。
17. ViewModel / Model 使用 `partial`、`[ObservableProperty]`、`[RelayCommand]`。
18. 页面专用 UI 状态放前端 Model；DTO 按 API 用例设计，只有满足强语义复用条件时才跨页面或跨接口复用。
19. DTO 与前端模型的结构性字段复制默认使用 Mapster；复杂字段差异集中配置，不新增大段逐字段转换或只包装逐字段复制的 Mapping 类。业务计算、状态决策和跨来源组装保留显式代码。
20. 查询同时具备取消旧请求与提交前身份/存活验证；写命令入口拒绝并发，同一用户操作只有一个最外层 Busy owner。
21. 导航初始化与重复激活必须幂等；离开/释放时对称清理事件、Messenger、Timer、CTS 和子对象；搜索、刷新、分页、切换和关闭前检查未保存变更。
22. 错误按 HTTP、业务失败、字段验证、并发冲突、认证授权、网络/超时和未知异常分类；未知技术异常由基础设施记录，ViewModel 只提供业务上下文和一次用户反馈。
23. 不在构造函数中手写 `new RelayCommand` / `new AsyncRelayCommand`。
24. 不从 Git 历史或归档分支把旧 MVVMLight、VSTO、MateWpfThemes 规则带入新客户端。
25. 按风险明确普通单元、STA/WPF、契约、构建与人工验收证据；检查键盘/Automation/高对比度、文化格式、UI 性能、日志脱敏、缓存失效和导入导出安全的适用项与豁免理由。
26. 优先运行 `dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj`；涉及行为、Grid 或契约时运行对应测试项目或说明未运行原因。

## 边界

- 前端任务默认只写 `SwiftExport.Client.Wpf/**`、必要的 `SwiftExport.Contracts/**`，以及 `SwiftExport.Client.Wpf.Tests/**` 中本次相关测试、夹具和测试数据。
- 补测不允许删除有效断言或绕过失败；后端缺陷交还后端角色，不借补测改变后端行为。
- EnterpriseGrid 框架及其专项测试由 Grid Architect 按授权负责；运行专项测试不授予前端写入权限。
- 不借前端任务修改 WebApi、Application、Infrastructure、Core。
