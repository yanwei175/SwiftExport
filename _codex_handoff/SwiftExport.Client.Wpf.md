# SwiftExport.Client.Wpf Codex Handoff

## 用途

这个文件用于在 Codex 上下文压缩、换窗口或交接时快速恢复 `.NET 10 WPF` 新客户端推进状态。

正式长期规则仍以 `wiki/` 为准；本文件只记录当前执行状态、阶段进度、关键决策、构建结果和下一步。

## 当前阶段

- 已完成：阶段 1、阶段 2、阶段 3、阶段 4、阶段 5、阶段 6、阶段 7、阶段 8 基础样板。
- 下一步：阶段 8 后续打磨，产品页面控件化与 DataGrid 视觉 / 编辑体验细化。
- 当前主线：`SwiftExport.Client.Wpf` 作为新的 `.NET 10 WPF` 管理端。
- 独立边界：Excel / VSTO 仍作为 `.NET Framework` 项目单独维护，不迁入 `.NET 10 WPF` 客户端。

## 已完成内容

### 阶段 1：创建新客户端项目

- 新建 `SwiftExport.Client.Wpf`。
- 目标框架：`net10.0-windows`。
- 启用 `UseWPF`、`Nullable`、`ImplicitUsings`。
- 加入基础 NuGet 包：
  - `CommunityToolkit.Mvvm`
  - `Microsoft.Extensions.Hosting`
  - `Microsoft.Extensions.DependencyInjection`
  - `Microsoft.Extensions.Configuration.Json`
  - `Microsoft.Extensions.Logging`
  - `Refit.HttpClientFactory`
  - `WPF-UI`
- 加入 `SwiftExport.slnx` 和 `SwiftExport-Frontend.slnx`。
- 不引用旧 `SwiftExport.View.FW`、`MateWpfThemes.FW` 或 VSTO 项目。

### 阶段 2：主题资源

- 主题和样式资源集中放在 `SwiftExport.Client.Wpf/Resources/`。
- 当前资源包括：
  - `Tokens.xaml`
  - `Colors.xaml`
  - `Typography.xaml`
  - `Spacing.xaml`
  - `Button.xaml`
  - `DataGrid.xaml`
- 当前已重新引用 lepoco `WPF-UI 4.3.0`，用于主题资源、`FluentWindow`、`TitleBar`、`InfoBar`、`Snackbar`、`ContentDialog` 和 Shell 导航外观。
- 阶段 7 后已按官方主题规则清理自定义 XAML：不再定义隐式全局 `Button` / `TextBox` / `DataGrid` 样式，项目样式均使用 `x:Key` 显式引用。
- 现阶段主题策略调整为：WPF-UI 主题资源 + SwiftExport 命名业务样式；不恢复旧 `MateWpfThemes.FW`，不覆盖默认基础控件 Style。
- 已新增官方主题与 Fluent 布局规则：
  - `wiki/guides/frontend-wpf/wpf-official-theme-style-guide.md`
  - `wiki/guides/frontend-wpf/wpf-fluent-layout-guide.md`
- 注意：新客户端代码命名空间使用 `SwiftExportClient.*`，避免项目名 `SwiftExport.Client.Wpf` 与第三方 `Wpf.Ui` 命名空间在 XAML 生成代码中冲突。

### 阶段 3：Host / DI / 配置 / MVVM 基础

- 移除 `App.xaml` 的 `StartupUri`。
- 通过 `Microsoft.Extensions.Hosting` 启动。
- 新增 `appsettings.json`：
  - `Api:BaseUrl`
  - `Client:ApplicationName`
  - `Client:Version`
- 新增 `Bootstrapper/`：
  - `AppHost.cs`
  - `ApiOptions.cs`
  - `ClientOptions.cs`
  - `ServiceCollectionExtensions.cs`
  - `ViewRegistrationExtensions.cs`
- 新增 `Mvvm/`：
  - `ObservableObjectEx`
  - `ViewModelBaseEx`
- 新增 `ShellViewModel`，通过 DI 注入到 `ShellWindow`。
- 阶段 3 只建立页面级 MVVM 基础；`BaseLineModel<TDto>`、`BaseListModel<TLine,TDto>`、`GridChangeSet<TDto>` 留到 Products / DataGrid 前置阶段。

### 阶段 4：Contracts / Refit / API Client

- `SwiftExport.Client.Wpf` 已引用 `SwiftExport.Contracts`。
- 新增 `Bootstrapper/ApiRegistrationExtensions.cs`，注册现有 Contracts Refit API。
- API 注册规则：
  - `IAuthApi` 使用站点根地址 `Api:BaseUrl`，因为接口方法已包含 `/api/auth/...`。
  - 其他业务 API 使用 controller 根地址，例如 `Api:BaseUrl + "/api/SysViewRoute"`，因为通用接口是 `/enabled`、`/page`、`/{id}` 这类相对路径。
- 新增 `Services/Api/`：
  - `IApiCallRunner`
  - `ApiCallRunner`
- `ApiCallRunner` 捕获 `Refit.ApiException`、`HttpRequestException`、`TaskCanceledException`，并返回已有的 `SwiftExport.Contracts.Wrappers.ServiceResult<T>`。
- 没有新增第二套 `ClientResult<T>`。
- 没有接登录、Token、Bearer Handler、导航加载或业务页面。

### 阶段 5：客户端版本检查

- `SwiftExport.Contracts` 新增版本检查契约：
  - `DTOs/Updates/ClientVersionInfo.cs`
  - `Apis/SystemApis/IClientVersionApi.cs`
- `SwiftExport.WebApi` 新增匿名只读接口：
  - `GET /api/client-version`
  - Controller：`Controllers/Systems/ClientVersionController.cs`
  - 返回 `ServiceResult<ClientVersionInfo>`。
- 后端 `appsettings.json` 新增 `ClientVersion` 配置段：
  - `LatestVersion`
  - `MinimumSupportedVersion`
  - `DownloadUrl`
  - `ReleaseNotes`
- `SwiftExport.Client.Wpf` 新增 `Updates/`：
  - `IClientVersionService`
  - `ClientVersionService`
  - `ClientVersionCheckResult`
  - `UpdateRequirement`
- 客户端启动流程：
  - Host 启动后先执行版本检查。
  - 当前版本低于最低支持版本时提示并退出。
  - 当前版本低于最新版本但仍受支持时提示并继续。
  - 后端不可达或检查失败时记录日志并继续，避免本地开发被卡死。
- 不做自动下载，不接 ClickOnce，不接登录或 Token。

### 阶段 6：登录 / 退出登录 / Token 恢复

- 新增 `SwiftExport.Client.Wpf/Authentication/`：
  - `LoginWindow`
  - `LoginViewModel`
  - `IAuthService` / `AuthService`
  - `ITokenStore` / `LocalTokenStore`
  - `ICurrentUserService` / `CurrentUserService`
  - `ISessionService` / `SessionService`
  - `SessionEndReason`
  - `SessionEndedEventArgs`
- `LocalTokenStore` 使用当前 Windows 用户范围 DPAPI 保存 token 到 `%AppData%/SwiftExport/Client/token.cache`。
- 不保存密码，不迁移旧项目的“记住密码”。
- 新增 `Services/Api/AuthTokenHandler.cs`：
  - 自动给授权请求附加 `Authorization: Bearer ...`。
  - 对非匿名接口返回 401 时清理会话并通知 App 回登录。
  - 登录、重置密码、版本检查接口不触发全局退出。
- 启动流程调整：
  - Host 启动。
  - 版本检查。
  - 读取本地 token 并调用 `/api/auth/me` 验证。
  - token 有效直接进 Shell。
  - token 缺失、无效或过期则显示 LoginWindow。
- Shell 增加“退出登录”按钮，通过 `ISessionService` 清 token 并回登录。
- `ShellWindow`、`ShellViewModel`、`LoginWindow`、`LoginViewModel` 注册为 transient，避免关闭后重开拿到已关闭窗口实例。
- 本阶段未实现重置密码、修改密码、注册、权限过滤和正式导航菜单。

### 阶段 7：自建多标签导航服务 / Shell 工作区

- 当前 `SwiftExport.Client.Wpf` 已重新使用 lepoco `WPF-UI 4.3.0`：
  - `ShellWindow` 使用 `ui:FluentWindow`。
  - Shell 顶部使用 WPF-UI `TitleBar`，保留最小化、最大化、关闭按钮。
  - `App.xaml` 合并 `ui:ThemesDictionary` 和 `ui:ControlsDictionary`。
- 新增 `SwiftExport.Client.Wpf/Navigation/`：
  - `NavigationRoute`
  - `NavigationMenuItem`
  - `NavigationTabItem`
  - `INavigationRouteRegistry` / `NavigationRouteRegistry`
  - `INavigationService` / `NavigationService`
- Shell 使用左侧层级导航 + 多标签内容区：
  - 左侧导航当前使用原生 `Expander` + 命名 `Button` Style 实现 TreeView 风格，避免脱离完整 `NavigationView` 宿主的 `NavigationViewItem` 不渲染问题。
  - 导航间距按 `raw/inbox/Fluent  Microsoft 风格布局规范.md` 的 4px 倍数系统整理，`Spacing.xaml` 已补齐 `Space4` 到 `Space48`。
  - 菜单：`首页`、`产品`、`销售`、`计划`、`采购`、`出货`。
  - 产品下已有子项：`产品资料`、`BOM`、`客户产品`、`供应商产品`。
  - 当前只有 `产品资料` 接入 `ProductsViewModel`；其他产品子项先作为后续路由占位。
  - 默认打开 `首页`。
  - 同一路由主页不重复打开，点击已打开菜单时激活现有标签页。
  - `首页` 不可关闭，其他模块标签可关闭。
- Shell 已建立全局 WPF-UI 宿主：
  - `InfoBar`
  - `SnackbarPresenter`
  - `ContentDialogHost`
- 新增 `Services/UI/`：
  - `IMessageService` / `WpfUiMessageService`
  - `IDialogService` / `WpfUiDialogService`
  - `IStatusMessageService` / `StatusMessageService`
  - `UiMessageSeverity`
- 新增 DataGrid / 行集合基础类型：
  - `LineState`
  - `BaseLineModel<TDto>`
  - `BaseListModel<TLine,TDto>`
  - `GridChangeSet<TDto>`
- 新增第一版占位模块：
  - `Features/Home`
  - `Features/Products`
  - `Features/Sales`
  - `Features/Planning`
  - `Features/Purchasing`
  - `Features/Shipping`
- 本阶段不做权限菜单、不读取后端 `SysViewRoute`、不做未保存变更拦截、不接业务 API。

### 阶段 8：产品页面 / DataGrid 基础样板

- `产品 > 产品资料` 已从占位页替换为真实 API 驱动的 DataGrid 页面。
- 新增 `Mvvm/IAsyncInitializable`，`NavigationService` 新建 tab 后会调用支持异步初始化的 ViewModel；产品页打开后自动加载第一页。
- 新增 `Features/Products/ProductLineModel`：
  - 继承 `BaseLineModel<ProductDto>`，不继承页面级 `ViewModelBaseEx`。
  - 第一版编辑核心列：SKU、主 SKU、英文描述、中文描述、分类、材质、供货类型、BOM、人民币价、美元价、备注。
  - 对暂未展示的 `ProductDto` 字段做保留回写，避免保存核心列时清空隐藏字段。
  - 行级验证包含 SKU 必填、BOM 版本非负、价格非负。
- `ProductsViewModel` 已接入：
  - `IProductApi.GetProductPagedListAsync(ProductSearchRequest)` 查询、刷新、分页。
  - `IProductApi.BatchSaveAsync(BatchRequestDto<ProductDto>)` 批量保存。
  - `IApiCallRunner` 统一处理 Refit / HTTP 异常。
  - `IMessageService` 显示非阻塞状态。
  - `IDialogService` 处理删除确认和未保存变更拦截。
- 批量保存适配规则：
  - 新增行进入 `BatchRequestDto<ProductDto>.AddList`。
  - 修改行进入 `UpdateList`。
  - 删除行从 `GridChangeSet<ProductDto>.Deleted` 中取 `Uid`，映射到 `DeleteIdList`。
- `ProductsView.xaml` 已按 Fluent DataGrid 页面分层：
  - Header
  - 查询区
  - 工具栏
  - 原生 WPF `DataGrid` + `SwiftExportDataGrid`
  - 状态 / 分页区
- 本阶段仍不修改旧 `SwiftExport.View.FW`、`MateWpfThemes.FW`、`SwiftExport.VSTO`，也不修改后端业务层。

### 阶段 8 后续：分页与 Lookup 控件迁移

- 已在 `SwiftExport.Client.Wpf/Resources/Controls/` 建立新客户端控件目录，命名空间统一为 `SwiftExportClient.Controls`。
- 已从旧 `MateWpfThemes.FW` 迁移 `PageNavigator`：
  - 保留 `TotalCount`、`PageIndex`、`PageSize`、`PageSizeOptions`、`PageChangeCommand`。
  - 保留命令驱动分页模式，控件只发出 `PageChangeRequest`，是否真正切页由页面 ViewModel 决定。
  - 已迁移 `PageItem` 和 `PageChangeRequest`。
  - 旧主题键 `Brush_Txt_Primary`、`Brush_Txt_Secondary`、`BaseComboBox`、`ButtonText` 未迁入，新控件改用 WPF-UI 默认控件样式和 SwiftExport token。
- 已从旧 `SearchableTextBoxPlus` 迁移并改名为 `LookupTextBox`：
  - 保留本地/远程搜索模式、过滤、键盘选择、鼠标选择、清空、选中显示、`SelectedItem` / `SelectedValue` 双向绑定。
  - 搜索模式枚举改为 `LookupTextBoxSearchMode`。
  - 已迁移 `LookupCommitReason`、`LookupSelectionCommittedEventArgs`、`LookupCommitContext`、`CommitParameterMode`。
  - 本次未迁移旧 `DataGridSearchableColumn`，后续 DataGrid Lookup 列单独设计。
- `Typography.xaml` 增加显式命名样式 `SecondaryTextBlock`、`LabelTextBlock`，弱文本使用 `SwiftExportBrush.TextSecondary`，不依赖未确认稳定的 WPF Fluent 内部资源键。
- `ProductsView` 底部分页已替换为新 `PageNavigator`，`ProductsViewModel` 已新增 `PageChangeCommand`；切页和切换 PageSize 会继续经过未保存变更拦截。

### 阶段 8 后续：Shell 左侧导航 Gallery 风格改造

- 保留 SwiftExport 自建 `INavigationService` 和多标签工作区，不切换到完整 WPF-UI `NavigationView`。
- Shell 左侧导航已从手写 `Button + Expander` 改为数据驱动模板：
  - `ShellViewModel.FilteredMenuItems` 来自路由表 `NavigationRouteRegistry.MenuItems`。
  - `MenuSearchText` 支持本地菜单过滤。
  - `SelectedRouteKey` 用于菜单选中态高亮。
  - `ToggleNavigationGroupCommand` 控制产品等分组展开/收起。
  - `OpenSettingsCommand` 作为设置占位入口，通过 Shell InfoBar 提示后续接入。
- 新增 Shell 视觉辅助类型：
  - `ShellNavigationItem`
  - `ShellNavigationIconConverter`
  - `RouteSelectionConverter`
- 新增 `Resources/ShellNavigation.xaml`：
  - 导航项高度约 40px。
  - 图标使用 WPF-UI `SymbolIcon`。
  - 选中项使用浅灰背景 + 左侧 Accent 竖条。
  - 子菜单使用缩进布局，不再显示成一组小表单按钮。
- `ShellWindow.xaml` 左栏宽度调整为 376px，增加搜索框和底部 Settings 入口，主工作区保留多标签但视觉边框变轻。

## 关键架构边界

- 不在 `SwiftExport.Client.Wpf` 中引用旧 `SwiftExport.View.FW`。
- 不在 `SwiftExport.Client.Wpf` 中引用 `MateWpfThemes.FW`。
- 不在 `SwiftExport.Client.Wpf` 中引用 VSTO 项目。
- 不复制旧 MVVMLight、旧 `RelayCommand`、旧 Dialog、旧 DI、旧 TokenStore。
- `SwiftExport.Contracts` 已有共用 wrapper，前端错误包装优先使用 `ServiceResult<T>`。
- 新客户端当前使用 lepoco `WPF-UI 4.3.0` 承载 Shell 级 Fluent 窗口、标题栏、通知和弹窗；业务 MVVM、DataGrid 行模型、集合模型和多标签导航仍由 SwiftExport 自建。
- VSTO 只承接 Excel 导入相关页面和 Office 交互，不承载完整 ERP 管理业务。

## 当前重要文件

- 新客户端：`SwiftExport.Client.Wpf/`
- 契约层：`SwiftExport.Contracts/`
- 新客户端入口：`SwiftExport.Client.Wpf/App.xaml.cs`
- Host 启动：`SwiftExport.Client.Wpf/Bootstrapper/AppHost.cs`
- DI 注册：`SwiftExport.Client.Wpf/Bootstrapper/ServiceCollectionExtensions.cs`
- API 注册：`SwiftExport.Client.Wpf/Bootstrapper/ApiRegistrationExtensions.cs`
- Shell：`SwiftExport.Client.Wpf/Shell/ShellWindow.xaml`
- Shell VM：`SwiftExport.Client.Wpf/Shell/ShellViewModel.cs`
- Shell 导航样式：`SwiftExport.Client.Wpf/Resources/ShellNavigation.xaml`
- 导航服务：`SwiftExport.Client.Wpf/Navigation/NavigationService.cs`
- 导航路由：`SwiftExport.Client.Wpf/Navigation/NavigationRouteRegistry.cs`
- UI 服务：`SwiftExport.Client.Wpf/Services/UI/`
- DataGrid 基础模型：`SwiftExport.Client.Wpf/Mvvm/BaseLineModel.cs`、`SwiftExport.Client.Wpf/Mvvm/BaseListModel.cs`
- 新客户端控件：`SwiftExport.Client.Wpf/Resources/Controls/PageNavigator.xaml`、`SwiftExport.Client.Wpf/Resources/Controls/LookupTextBox.xaml`
- 占位模块：`SwiftExport.Client.Wpf/Features/`
- API 调用包装：`SwiftExport.Client.Wpf/Services/Api/ApiCallRunner.cs`
- Bearer Handler：`SwiftExport.Client.Wpf/Services/Api/AuthTokenHandler.cs`
- 认证入口：`SwiftExport.Client.Wpf/Authentication/LoginWindow.xaml`
- 认证服务：`SwiftExport.Client.Wpf/Authentication/AuthService.cs`
- Token 存储：`SwiftExport.Client.Wpf/Authentication/LocalTokenStore.cs`
- 版本检查服务：`SwiftExport.Client.Wpf/Updates/ClientVersionService.cs`
- 版本检查契约：`SwiftExport.Contracts/Apis/SystemApis/IClientVersionApi.cs`
- 版本检查后端入口：`SwiftExport.WebApi/Controllers/Systems/ClientVersionController.cs`
- 路线图：`C:/Users/BigBigBigDv/Documents/Codex/2026-06-30/c/outputs/SwiftExport-WPF-Frontend-Roadmap.md`
- 架构文档：`C:/Users/BigBigBigDv/Documents/Codex/2026-06-30/c/outputs/SwiftExport-WPF-Frontend-Architecture.md`
- 官方主题规则：`wiki/guides/frontend-wpf/wpf-official-theme-style-guide.md`
- Fluent 布局规则：`wiki/guides/frontend-wpf/wpf-fluent-layout-guide.md`

## 最近构建记录

- 已运行：
  ```powershell
  dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj
  ```
- 最近结果：成功，0 warning，0 error。
- 已运行：
  ```powershell
  rg "<Style" .\SwiftExport.Client.Wpf\Resources .\SwiftExport.Client.Wpf\Shell .\SwiftExport.Client.Wpf\Authentication .\SwiftExport.Client.Wpf\Features --glob "*.xaml"
  ```
- 最近结果：新客户端自定义 Style 均带 `x:Key`，未发现隐式全局控件 Style。
- 已运行：
  ```powershell
  rg "Wpf\.Ui|WPF-UI|wpfui|FluentWindow" .\SwiftExport.Client.Wpf
  ```
- 最近结果：当前有命中，`SwiftExport.Client.Wpf` 已重新使用 WPF-UI 主题库和 `ui:FluentWindow`。
- 已运行：
  ```powershell
  dotnet build .\SwiftExport-WebAPI.slnx
  ```
- 最近结果：成功，0 warning，0 error。
- 静态检查：`SwiftExport.Client.Wpf` 未引用 `SwiftExport.View.FW`、`MateWpfThemes.FW`、`SwiftExport.VSTO`、`MvvmLight/GalaSoft`。

## 下一阶段：阶段 8 后续打磨，产品页面控件化与 DataGrid 视觉细化

建议目标：

- 基于当前产品页继续打磨查询控件、工具栏按钮语义和 DataGrid 可编辑体验。
- 基于已迁移的 `LookupTextBox` 设计产品分类、材质、客户/供应商等 Lookup 使用方式。
- 提取可复用的查询区 / 工具栏布局控件或命名样式，避免后续页面复制 XAML。
- 设计 DataGrid 行状态、脏数据、删除态、验证态的视觉样式。
- 后续再扩展图片预览、DataGrid Lookup 列、列冻结和批量选择。
- 继续根据 WPF-UI Gallery 参考图打磨 Shell 主内容区、Tab 样式和 Home 首屏视觉。

建议默认边界：

- 不在阶段 8 修改旧 `SwiftExport.View.FW` / VSTO。
- 不把 DataGrid 行模型设计成依赖页面级 `ViewModelBaseEx`。
- 不从后端动态加载菜单；阶段 7 的本地路由表继续保留。

## 新窗口继续提示词

```text
继续 SwiftExport .NET 10 WPF 前端项目。

请先读取：
1. wiki/index.md
2. _codex_handoff/SwiftExport.Client.Wpf.md
3. wiki/project/dotnet10-frontend-mvvm-blueprint.md
4. wiki/guides/frontend-wpf/wpf-official-theme-style-guide.md
5. wiki/guides/frontend-wpf/wpf-fluent-layout-guide.md
6. wiki/guides/frontend-wpf/wpf-communitytoolkit-mvvm.md

当前状态：
- SwiftExport.Client.Wpf 是新的 .NET 10 WPF 管理端。
- 阶段 1-7 已完成：项目创建、官方主题资源、Host/DI/MVVM、Contracts/Refit、版本检查、登录/退出登录/token 恢复、自建多标签导航 Shell。
- 已重新引用 lepoco WPF-UI；当前使用 WPF-UI 主题、`ui:FluentWindow`、`ui:TitleBar`、Shell 全局 `InfoBar` / `Snackbar` / `ContentDialogHost`。
- 不覆盖默认 Button/TextBox/ComboBox/ListBox/TabControl 等 Style；只使用带 x:Key 的命名 Style。
- 当前 Shell 是 WPF-UI TitleBar + Gallery 风格左侧导航 + 多标签工作区，菜单为：首页、产品、销售、计划、采购、出货；产品下已有产品资料、BOM、客户产品、供应商产品。
- 已建立 Fluent 布局规则：4px spacing、页面 Header、查询区、工具栏、DataGrid、状态/分页区分层。
- 阶段 8 基础样板已完成：产品资料页接入真实 API、DataGrid、查询、分页、增删改、批量保存和未保存变更拦截。
- 已迁移 `PageNavigator` 和 `LookupTextBox` 到 `SwiftExport.Client.Wpf/Resources/Controls/`；产品页底部已使用新 `PageNavigator`。
- Shell 左侧导航已按 WPF-UI Gallery 风格重做：搜索框、图标菜单、选中 Accent 竖条、分组展开和底部 Settings 占位入口。
- 下一步是阶段 8 后续打磨：产品页面控件化与 DataGrid 视觉 / 编辑体验细化。

请继续时遵守：
- 不碰旧 SwiftExport.View.FW、MateWpfThemes.FW、SwiftExport.VSTO。
- 不引入 MVVMLight / GalaSoft。
- 不新增第二套 Result，继续使用 SwiftExport.Contracts.Wrappers.ServiceResult<T>。
- 不把 DataGrid 行模型继承页面级 ViewModelBaseEx。
- 新页面遵守官方主题规则和 Fluent 布局规范。
- 修改后更新 _codex_handoff/SwiftExport.Client.Wpf.md。
```

## 风险和注意事项

- 旧 `.NET Framework` WPF / VSTO 项目完整构建可能依赖本机 Visual Studio / OfficeTools 工作负载。
- `SwiftExport-Frontend.slnx` 可能因旧项目工具链问题失败；阶段 1-4 已优先验证新客户端单项目构建。
- 后端配置文件中已有本地连接字符串；不要在回复或文档中复制具体值。
- 不要把本地连接字符串、Token、密码写入本文件。
- 如果发现 `wiki/`、路线图和源码事实冲突，源码事实优先，并应在 `wiki/log.md` 记录“可能过期”。

## 后续维护规则

- 每完成一个阶段，更新本文件：
  - 当前阶段
  - 已完成内容
  - 最近构建记录
  - 下一阶段
  - 风险和注意事项
- 如果改动影响长期架构规则或 AI Wiki 任务路由，再同步更新 `wiki/index.md` 或 `wiki/log.md`。
- 本文件不替代正式 wiki，只作为 Codex 交接和恢复上下文的轻量工作台。
