# Workflow: SwiftExport WPF / VSTO 前端开发工作流

## Description

当任务涉及以下内容时使用本工作流：

- WPF 页面、窗口、UserControl、XAML 样式。
- ViewModel、前端 Model、前端 Mapping。
- Refit API 调用、前端 API 注册。
- Excel VSTO 启动、TaskPane、Excel 宿主集成。
- `SwiftExport.View.FW` 或 `SwiftExport.VSTO` 下的前端功能。

## 1. 必读入口

开始前端任务前，先读取：

- `wiki/index.md`
- `wiki/project/project-map.md`
- `wiki/project/onboarding.md`
- `wiki/guides/frontend-wpf/wpf-mvvmlight.md`
- `wiki/guides/frontend-wpf/wpf-datagrid.md`
- `wiki/guides/frontend-wpf/wpf-dialog-navigation.md`
- `wiki/guides/frontend-wpf/wpf-theme-resource.md`
- `SwiftExport.View.FW/DI/DependencyInjection.cs`
- `SwiftExport.View.FW/DI/DependencyInjection.Pages.cs`
- `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs`
- 当前业务模块已有 `Views`、`ViewModels`、`Models`

涉及 VSTO 承载时再读取：

- `SwiftExport.VSTO/ThisAddIn.cs`
- `SwiftExport.VSTO/DependencyInjectionVSTO.cs`
- `SwiftExport.VSTO/SwiftExportTaskPane/**`

## 2. 写入边界

前端任务主要允许写入：

- `SwiftExport.View.FW/**`
- `SwiftExport.VSTO/**`

允许写入 `SwiftExport.Contracts/**` 中前端依赖的：

- Refit API Interface
- DTO
- 枚举
- 通用结果包装或 UI 契约

允许只读 `SwiftExport.Core/**` 中的：

- Entity
- ReadModel
- Repository Interface

只读 Core 的目的仅限辅助建立 DTO、页面字段和前端展示逻辑。

## 3. 禁止事项

前端任务禁止借机修改：

- `SwiftExport.WebApi/**`
- `SwiftExport.Application/**`
- `SwiftExport.Infrastructure/**`
- `SwiftExport.Core/**`

除非用户明确要求实现后端功能，否则不要补 Controller、Service、Repository 或 SQL。

禁止：

- 在 WPF ViewModel 中写 SQL。
- 在 `Contracts` DTO 中放 WPF 控件、Excel COM 对象、窗口状态或弹窗逻辑。
- 让 `SwiftExport.View.FW` 依赖 WebApi、Application、Infrastructure。
- 把 VSTO 宿主逻辑塞进通用 WPF ViewModel。
- 大面积重写现有 MVVM 基类、DI、路由体系。

## 4. WPF 模块结构

优先沿用现有业务模块结构：

- `XxxViews/Views`
- `XxxViews/ViewModels`
- `XxxViews/Models`
- `Mappings`

新增页面时通常需要：

1. 新增 View。
2. 新增或复用 ViewModel。
3. 新增 UI Model 或 DTO Mapping。
4. 在 `DependencyInjection.Pages.cs` 注册 View 和 ViewModel。
5. 如果页面由路由打开，检查 `Routing/ViewRoutes.cs` 或后端路由配置。

## 5. API 与 DTO

新增前端 API 依赖时：

- Refit Interface 放在 `SwiftExport.Contracts/Apis/<业务模块>`。
- DTO 放在 `SwiftExport.Contracts/DTOs/<业务模块>`。
- 前端 API 注册放在 `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs`。
- Refit 路由必须与后端 Controller 路由保持一致；如果后端不存在该接口，只能定义前端契约需求，不能自行实现后端。

DTO 设计规则：

- DTO 是前后端传输模型，不放 UI 临时状态。
- 可参考 `SwiftExport.Core/Entities/**` 字段命名，但不要为了页面修改 Entity。
- 页面状态、选中状态、校验状态放在 `SwiftExport.View.FW/**/Models` 或 ViewModel。

## 6. DataGrid 与可编辑行

涉及 DataGrid 编辑时还要读取：

- `wiki/skills/frontend-wpf/wpf-datagrid-editing.md`

规则：

- 可编辑行优先使用前端 Model / ListModel，不直接把 DTO 当作 UI 状态容器。
- 脏数据、删除池、撤销、只读状态和批量保存流程沿用当前模块既有模式。
- ViewModel 不直接调用 Repository，不写 SQL。
- code-behind 只做必要的 WPF 交互桥接，不承载业务判断。

## 7. 主题与资源

涉及 ResourceDictionary、样式、主题和 DataGrid 视觉修复时还要读取：

- `wiki/skills/frontend-wpf/wpf-theme-fix.md`

规则：

- 单页视觉问题优先局部修复。
- 全局资源修改必须先搜索引用范围，并在最终回复说明影响面。
- 优先复用已有资源键和 MateWpfThemes.FW / 项目自定义控件风格。
- 新增搜索选择控件时优先使用 `MateWpfThemes.FW.Controls.SearchableTextBoxPlus`；旧 `SearchableTextBox` 仅用于兼容既有页面。
- 迁移旧页面到 `SearchableTextBoxPlus` 时必须逐页检查 `Text`、`SelectedItem`、`SelectedValue` 绑定语义，避免把“搜索关键字”和“选中显示值”混用。

## 8. VSTO 边界

`SwiftExport.VSTO` 是 Excel Add-in 宿主，负责：

- Excel 启动与关闭。
- TaskPane 注册与承载。
- WPF 应用环境初始化。
- Excel 上下文服务接入。

通用业务页面和 ViewModel 优先放在 `SwiftExport.View.FW`，不要只因为入口来自 Excel 就把业务 UI 写进 VSTO。

## 9. 验证建议

前端改动优先运行完整解决方案构建：

```powershell
msbuild .\SwiftExport.slnx /p:Configuration=Debug /p:Platform=AnyCPU
```

如果本机缺少 Visual Studio / VSTO / .NET Framework 构建环境，最终回复说明未运行原因。

涉及纯 Contracts 且不依赖 WPF/VSTO 时，可考虑运行相关项目构建。
