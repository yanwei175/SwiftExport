# 前端 .NET 10 工作流

## 适用

`SwiftExport.Client.Wpf` 当前 .NET 10 WPF + CommunityToolkit MVVM 前端。

## 步骤

1. 先读 `AGENTS.md`、`.agents/router.toml`、`.agents/frontend.toml`。
2. 先读 `.codex/skills/wpf-dotnet10-design/index/SKILL.md`，再按任务读取页面、MVVM、控件、样式子 skill。
3. 涉及 ViewModel / Model / Command 时必须读 `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`。
4. 涉及 lepoco/WPF-UI 控件、FluentWindow、TitleBar、InfoBar、Snackbar、ContentDialog、SymbolIcon 时读 `.codex/skills/wpf-dotnet10-design/wpfui-controls/SKILL.md`。
5. 涉及 `SwiftExport.Contracts/**` 时必须读 `.codex/skills/aspdotnet10/contracts-refit-dto/SKILL.md` 和 `.codex/skills/aspdotnet10/contracts-netstandard20/SKILL.md`。
6. 读取 `.agents/references/工程架构总纲.md`；涉及业务页面时读取业务架构和数据模型参考。
7. 读取目标 Feature、View、ViewModel、Model、Service 和 XAML。
8. 按 `Features/{业务域}/{功能单元}/Views|ViewModels|Models|Services|Components` 组织新增文件。
9. ViewModel / Model 使用 `partial`、`[ObservableProperty]`、`[RelayCommand]`。
10. 不在构造函数中手写 `new RelayCommand` / `new AsyncRelayCommand`。
11. 不把旧 MVVMLight、VSTO、MateWpfThemes 规则带入新客户端。
12. 优先运行 `dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj`。

## 边界

- 前端任务默认只写 `SwiftExport.Client.Wpf/**` 和必要的 `SwiftExport.Contracts/**`。
- 不借前端任务修改 WebApi、Application、Infrastructure、Core。
