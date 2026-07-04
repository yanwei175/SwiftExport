# SwiftExport.Client.Wpf Codex Handoff

## 用途

这个文件用于在 Codex 上下文压缩、换窗口或交接时快速恢复 `.NET 10 WPF` 新客户端推进状态。

正式长期规则仍以 `wiki/` 为准；本文件只记录当前执行状态、阶段进度、关键决策、构建结果和下一步。

## 当前阶段

- 已完成：阶段 1、阶段 2、阶段 3、阶段 4、阶段 5、阶段 6、阶段 7。
- 下一步：阶段 8，产品页面 / DataGrid 基础样板。
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
- 阶段 7 已决定移除 WPF UI，后续基于原生 WPF + 自有资源体系演进。
- 阶段 7 后已按官方主题规则清理自定义 XAML：不再定义隐式全局 `Button` / `TextBox` / `DataGrid` 样式，项目样式均使用 `x:Key` 显式引用。
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

- 移除 `SwiftExport.Client.Wpf` 对 WPF UI 的依赖：
  - 删除 `WPF-UI` 包引用。
  - `ShellWindow` 从 `FluentWindow` 改回原生 WPF `Window`。
  - 清理 `Wpf.Ui` / `wpfui` / `FluentWindow` 引用。
- 新增 `SwiftExport.Client.Wpf/Navigation/`：
  - `NavigationRoute`
  - `NavigationMenuItem`
  - `NavigationTabItem`
  - `INavigationRouteRegistry` / `NavigationRouteRegistry`
  - `INavigationService` / `NavigationService`
- Shell 使用左侧菜单 + 多标签内容区：
  - 菜单：`首页`、`产品`、`销售`、`计划`、`采购`、`出货`。
  - 默认打开 `首页`。
  - 同一路由主页不重复打开，点击已打开菜单时激活现有标签页。
  - `首页` 不可关闭，其他模块标签可关闭。
- 新增第一版占位模块：
  - `Features/Home`
  - `Features/Products`
  - `Features/Sales`
  - `Features/Planning`
  - `Features/Purchasing`
  - `Features/Shipping`
- 本阶段不做权限菜单、不读取后端 `SysViewRoute`、不做未保存变更拦截、不接业务 API。

## 关键架构边界

- 不在 `SwiftExport.Client.Wpf` 中引用旧 `SwiftExport.View.FW`。
- 不在 `SwiftExport.Client.Wpf` 中引用 `MateWpfThemes.FW`。
- 不在 `SwiftExport.Client.Wpf` 中引用 VSTO 项目。
- 不复制旧 MVVMLight、旧 `RelayCommand`、旧 Dialog、旧 DI、旧 TokenStore。
- `SwiftExport.Contracts` 已有共用 wrapper，前端错误包装优先使用 `ServiceResult<T>`。
- WPF UI 已移除；新客户端基于原生 WPF + 自有主题资源演进 Shell、Navigation、Dialog 和业务控件。
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
- 导航服务：`SwiftExport.Client.Wpf/Navigation/NavigationService.cs`
- 导航路由：`SwiftExport.Client.Wpf/Navigation/NavigationRouteRegistry.cs`
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
- 最近结果：无命中，WPF UI 已从新客户端移除。
- 已运行：
  ```powershell
  dotnet build .\SwiftExport-WebAPI.slnx
  ```
- 最近结果：成功，0 warning，0 error。
- 静态检查：`SwiftExport.Client.Wpf` 未引用 `SwiftExport.View.FW`、`MateWpfThemes.FW`、`SwiftExport.VSTO`、`MvvmLight/GalaSoft`。

## 下一阶段：阶段 8 产品页面 / DataGrid 基础样板

建议目标：

- 选择 `产品` 作为第一个真实业务页面样板。
- 设计 .NET 10 前端 DataGrid 行模型 / 集合模型最小可用版本。
- 接入 Products 相关 Contracts Refit API。
- 建立查询、分页、编辑、批量保存、删除、验证和状态栏消息的第一版闭环。

建议默认边界：

- 不在阶段 8 修改旧 `SwiftExport.View.FW` / VSTO。
- 不把 DataGrid 行模型设计成依赖页面级 `ViewModelBaseEx`。
- 不从后端动态加载菜单；阶段 7 的本地路由表继续保留。

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
