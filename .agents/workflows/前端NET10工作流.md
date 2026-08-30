# 前端 .NET 10 工作流

## 适用

`SwiftExport.Client.Wpf` 当前 .NET 10 WPF + CommunityToolkit MVVM 前端。

## 步骤

1. 先读 `AGENTS.md`、`.agents/router.toml`、`.agents/frontend.toml`。
2. 先读 `.codex/skills/wpf-dotnet10-design/index/SKILL.md`，再按任务读取页面、MVVM、控件、样式子 skill。
3. 涉及 ViewModel / Model / Command 时必须读 `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`。
4. 涉及 lepoco/WPF-UI 控件、FluentWindow、TitleBar、InfoBar、Snackbar、ContentDialog、SymbolIcon 时读 `.codex/skills/wpf-dotnet10-design/wpfui-controls/SKILL.md`。
5. 涉及 `SwiftExport.Contracts/**` 时必须读 `.codex/skills/aspdotnet10/contracts-refit-dto/SKILL.md` 和 `.codex/skills/aspdotnet10/contracts-netstandard20/SKILL.md`。
6. 前端任务新增或复用 Contracts DTO 时，必须读 `.codex/skills/aspdotnet10/contracts-dto-models/references/DTO复用与文件组织门禁.md`，搜索现有契约、API 和调用方，并记录复用或新建理由。
7. 涉及 Contracts DTO 与 ViewModel、EditModel、LineModel、TreeItemModel 转换时，必须读 `.codex/skills/aspdotnet10/application-mapster-mapping/SKILL.md`，并检查 `SwiftExport.Client.Wpf/Mappings/MapsterConfig.cs` 与目标 Feature 的 `Mappings` 配置。
8. 读取 `.agents/references/工程架构总纲.md`；涉及业务页面时读取业务架构和数据模型参考。
9. 读取目标 Feature、View、ViewModel、Model、Service 和 XAML。
10. 按 `Features/{业务域}/{功能单元}/Views|ViewModels|Models|Services|Components` 组织新增文件。
11. ViewModel / Model 使用 `partial`、`[ObservableProperty]`、`[RelayCommand]`。
12. 页面专用 UI 状态放前端 Model；DTO 按 API 用例设计，只有满足强语义复用条件时才跨页面或跨接口复用。
13. DTO 与前端模型的结构性字段复制默认使用 Mapster；复杂字段差异集中配置，不新增大段逐字段转换或只包装逐字段复制的 Mapping 类。业务计算、状态决策和跨来源组装保留显式代码。
14. 不在构造函数中手写 `new RelayCommand` / `new AsyncRelayCommand`。
15. 不从 Git 历史或归档分支把旧 MVVMLight、VSTO、MateWpfThemes 规则带入新客户端。
16. 优先运行 `dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj`。

## 边界

- 前端任务默认只写 `SwiftExport.Client.Wpf/**` 和必要的 `SwiftExport.Contracts/**`。
- 不借前端任务修改 WebApi、Application、Infrastructure、Core。
