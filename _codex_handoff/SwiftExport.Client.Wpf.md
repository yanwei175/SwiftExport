# SwiftExport.Client.Wpf Codex Handoff

## 用途

这个文件用于在 Codex 上下文压缩、换窗口或交接时快速恢复 `.NET 10 WPF` 新客户端推进状态。

正式长期规则仍以 `wiki/` 为准；本文件只记录当前执行状态、阶段进度、关键决策、构建结果和下一步。

## 当前阶段

- 已完成：阶段 1、阶段 2、阶段 3、阶段 4、阶段 5、阶段 6、阶段 7、阶段 8 基础样板。
- 下一步：阶段 8 后续打磨，产品颜色 / 产品资料页面的 DataGrid 编辑体验、Lookup 列和 Excel 化交互。
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

### 阶段 8 后续：PopupTextBox 多行弹出编辑控件

- 新增 `Resources/Controls/PopupTextBox.xaml` / `.xaml.cs`，命名空间为 `SwiftExportClient.Controls`。
- 控件用于紧凑表单和 DataGrid 长文本编辑：
  - 平时显示为单行 / 限高预览，不撑高布局。
  - 点击或聚焦后打开 `Popup`。
  - Popup 内使用官方 WPF 默认 `TextBox`，显式设置 `TextWrapping="Wrap"` 和 `AcceptsReturn="True"`。
  - `Text` 双向绑定并实时写回，不做确认 / 取消缓存。
- 已提供依赖属性：
  - `Text`
  - `Placeholder`
  - `PopupHeight`
  - `PopupWidth`
  - `IsPopupOpen`
  - `OpenOnFocus`
  - `OpenOnClick`
  - `MaxLength`
  - `IsReadOnly`
- 键盘交互：
  - `Esc` 关闭 Popup，不回滚内容。
  - `Ctrl+Enter` 关闭 Popup。
  - Popup 失焦自动关闭。
- `ProductsView` 备注列已改为 `DataGridTextColumn`，通过 `SwiftExportDataGridPopupTextBoxEditingStyle` 和 `PopupTextBoxAssist` 给普通编辑 `TextBox` 注入弹出多行编辑能力。

### 阶段 8 后续：产品资料页 DataGrid 样式第一轮

- `Resources/DataGrid.xaml` 已扩展为新客户端自己的 Fluent DataGrid 命名样式集合，不整块迁移旧 `MateWpfThemes.FW` DataGrid 资源。
- 当前 DataGrid 样板包括：
  - `SwiftExportDataGrid`
  - `SwiftExportDataGridColumnHeader`
  - `SwiftExportDataGridRow`
  - `SwiftExportDataGridCell`
  - 文本展示 / 长文本展示 / 数字展示样式
  - 文本编辑 / 数字编辑样式
- `Colors.xaml` / `Tokens.xaml` 已补充 DataGrid 专用 token：表头背景、hover、selected、grid line、deleted soft、error、DataGrid header / cell padding 等。
- 行状态视觉已从默认行样式拆出为 opt-in 样式 `SwiftExportStateDataGridRow`，避免普通模型没有 `State` 时产生绑定警告：
  - `Added` 使用柔和成功底色。
  - `Modified` 使用柔和警告底色。
  - `Deleted` 使用柔和删除底色、弱文本和透明度。
- 单元格校验视觉已建立：
  - 去掉默认 `Validation.ErrorTemplate`。
  - 使用底部错误边框和 Tooltip 显示错误信息。
- 默认 `SwiftExportDataGrid` 不再内置空数据占位；具体页面可按自身需求放文字、加载动画或其他空状态。
- `ProductsView` 已接入第一版 DataGrid 样板：
  - 普通文本列使用显式 DataGrid 文本样式。
  - 人民币价 / 美元价使用右对齐数字样式。
  - 备注列使用 `DataGridTextColumn`，编辑态通过 `SwiftExportDataGridPopupTextBoxEditingStyle` 弹出多行编辑，不撑高行高。
- 本阶段未迁移 `DataGridExcelAssist`、`UndoManager` / `CellTrackerState`、`DataGridSearchableColumn`、旧 `DataGridAssist.ApplyMateThemeColumnStyles` 和旧项目 DataGrid 隐式全局样式。

### 阶段 8 后续：产品颜色管理页

- 新增 `Features/Products/Colors/` 功能单元：
  - `Models/ProductColorLineModel.cs`
  - `ViewModels/ProductColorsViewModel.cs`
  - `Views/ProductColorsView.xaml`
- `ProductColorLineModel` 继承 `BaseLineModel<ProductColorDto>`，不继承页面级 ViewModel；显式映射 `ColorCodeUnique`、`ColorCode`、`ColorNameEn`、`ColorNameZh`，并保留 `Uid`、`Version` 和审计字段回写。
- `ProductColorsViewModel` 已接入：
  - `IProductColorApi`
  - `IApiCallRunner`
  - `IMessageService`
  - `IDialogService`
- 页面行为：
  - 默认打开 tab 后加载第一页，分页默认 `PageIndex=1`、`PageSize=20`。
  - 关键词 500ms 防抖搜索，“立即查询”会取消等待并直接查询；请求会同时填 `Keyword` 和单项 `BatchKeywords`，以匹配当前 `ProductColorController` 的颜色字段模糊查询实现。
  - 切页、刷新、立即查询前会经过未保存变更拦截。
  - 新增、删除、撤销、保存使用 `BaseListModel` 和 `BatchRequestDto<ProductColorDto>`。
  - 更多菜单使用 WPF-UI `DropDownButton`，当前“导入颜色 / 导出颜色 / 批量规范化代码”为占位动作，通过 InfoBar / Snackbar 提示后续接入。
- 左侧导航“产品”分组新增 `产品颜色管理`，route 为 `product-colors`，作为正式 tab 页面打开。
- Shell 已注册 `ProductColorsViewModel -> ProductColorsView` DataTemplate，DI 已注册 `ProductColorsViewModel`。
- 源码事实修正：`ProductColorController` 的分页 GET 位于控制器根路由 `GET /api/ProductColor`，因此 `IProductColorApi` 增加 `GetPagedRootAsync([Query] PagedRequestDto request)` 用于颜色页分页；未修改后端 Controller 或 DTO。

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

### 阶段 8 后续：Shell 导航点击区域与产品占位路由修复

- 修复 Gallery 风格左侧菜单的命中区域：
  - `ItemsControl` 增加 `SwiftExportShellNavigationItemContainer`，让 `ContentPresenter` 横向撑满。
  - 菜单模板根 Grid、Button、Border、内部 Grid 均显式 `HorizontalAlignment="Stretch"`。
  - 菜单项 Border 保持 `Background="Transparent"`，空白区域也参与 hit test。
  - 导航 Button 命名样式关闭默认焦点框，避免选中项出现黑色边框。
- 补齐产品子菜单占位路由：
  - `product-bom` -> `ProductBomViewModel`
  - `customer-products` -> `CustomerProductsViewModel`
  - `supplier-products` -> `SupplierProductsViewModel`
- 新增 `ProductPlaceholderView` 和产品占位 ViewModel，点击 `BOM`、`客户产品`、`供应商产品` 会打开对应 tab，不再静默失败。
- `NavigationService.OpenAsync` 遇到未知路由时通过 Shell InfoBar 显示“页面路由未注册”，便于后续菜单维护。

### 阶段 8 后续：Shell 可折叠导航与产品分类管理 Dialog

- Shell 左侧导航已增加折叠 / 展开能力：
  - `ShellViewModel.IsNavigationPaneExpanded` 控制左栏宽度。
  - 展开宽度当前为 220px，折叠宽度为 56px。
  - 折叠后隐藏搜索框、导航文字、分组箭头和子菜单，只保留图标入口。
  - 导航项 hover 背景使用 `SwiftExportBrush.SurfaceMuted`，与选中背景保持一致。
- 产品资料页工具栏新增“分类管理”按钮，不打开新 tab。
- 新增产品分类管理阻断式弹窗：
  - 内容主体是 `ProductCategoryManagerView` 用户控件。
  - 通过 WPF-UI `ContentDialogHost` 阻断式显示。
  - `IDialogService` 增加 `ShowContentAsync`，用于承载自定义 UserControl 内容。
  - `ProductCategoryManagerViewModel` 使用 `IProductCategoryApi.GetAllAsync()` 加载分类，使用 `BatchSaveAsync(BatchRequestDto<ProductCategoryDto>)` 批量保存。
  - 支持分类树展示、选中编辑、新增一级 / 同级 / 子级、删除、刷新、保存和拖拽排序。
  - 新客户端已引入 `gong-wpf-dragdrop 4.0.0`，只复用拖拽库和旧页面拖放语义，不迁移旧 XAML / 旧主题资源。
  - 拖拽规则：拖到节点中部变为子级，拖到上 / 下边缘调整同级排序，拖到空白处变为顶级，禁止拖入自身子孙。
- Shell 左侧“产品”分组新增 `产品分类管理` 菜单项，点击后打开同一个阻断式 Dialog，不创建 tab。

### 阶段 8 后续：Features 业务目录结构整理

- `SwiftExport.Client.Wpf/Features/` 已明确为新客户端具体业务视图聚集地。
- 新目录规则已写入 `wiki/project/dotnet10-frontend-mvvm-blueprint.md`：
  - 按 `Features/{业务域}/{功能单元}/Views|ViewModels|Models|Services|Components` 组织。
  - 不在 `Features` 根下建立全局 `Views` / `ViewModels` / `Models` 大桶。
  - 不把多个业务功能继续堆在业务域根目录。
  - 全局控件继续放 `Resources/Controls`，全局 MVVM 基类继续放 `Mvvm`，Shell / Navigation / UI Service 不放进 `Features`。
- 产品模块已按新规则迁移：
  - `Products/ProductList`：产品资料页、`ProductsViewModel`、`ProductLineModel`。
  - `Products/Categories`：产品分类管理 Dialog、ViewModel、TreeItemModel、Dialog Service。
  - `Products/Placeholders`：BOM / 客户产品 / 供应商产品暂用占位 View 和占位 ViewModel。
  - 已预留 `Products/Bom`、`Products/CustomerProducts`、`Products/SupplierProducts` 功能目录。
- DI 注册、路由注册、Shell DataTemplate 和分类管理弹窗服务已同步更新到新命名空间。

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
- 产品资料页：`SwiftExport.Client.Wpf/Features/Products/ProductList/Views/ProductsView.xaml`
- 产品资料 VM：`SwiftExport.Client.Wpf/Features/Products/ProductList/ViewModels/ProductsViewModel.cs`
- 产品行模型：`SwiftExport.Client.Wpf/Features/Products/ProductList/Models/ProductLineModel.cs`
- 产品颜色页：`SwiftExport.Client.Wpf/Features/Products/Colors/Views/ProductColorsView.xaml`
- 产品颜色 VM：`SwiftExport.Client.Wpf/Features/Products/Colors/ViewModels/ProductColorsViewModel.cs`
- 产品颜色行模型：`SwiftExport.Client.Wpf/Features/Products/Colors/Models/ProductColorLineModel.cs`
- 产品分类管理 Dialog：`SwiftExport.Client.Wpf/Features/Products/Categories/Views/ProductCategoryManagerView.xaml`
- 产品分类管理 VM：`SwiftExport.Client.Wpf/Features/Products/Categories/ViewModels/ProductCategoryManagerViewModel.cs`
- 产品分类管理 Dialog Service：`SwiftExport.Client.Wpf/Features/Products/Categories/Services/ProductCategoryManagerDialogService.cs`
- 产品占位页：`SwiftExport.Client.Wpf/Features/Products/Placeholders/Views/ProductPlaceholderView.xaml`
- 产品占位 VM：`SwiftExport.Client.Wpf/Features/Products/Placeholders/ViewModels/ProductPlaceholderViewModel.cs`
- UI 服务：`SwiftExport.Client.Wpf/Services/UI/`
- DataGrid 基础模型：`SwiftExport.Client.Wpf/Mvvm/BaseLineModel.cs`、`SwiftExport.Client.Wpf/Mvvm/BaseListModel.cs`
- 新客户端控件：`SwiftExport.Client.Wpf/Resources/Controls/PageNavigator.xaml`、`SwiftExport.Client.Wpf/Resources/Controls/LookupTextBox.xaml`、`SwiftExport.Client.Wpf/Resources/Controls/PopupTextBox.xaml`
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
  dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj --no-restore
  ```
- 最近结果：成功，0 warning，0 error。
- 备注：普通沙箱内构建曾因 `obj/Debug/net10.0-windows` 下 WPF 生成文件访问被拒绝失败；提升权限后同一项目构建成功，确认本次 DataGrid XAML / 资源改动可编译。
- 已运行：
  ```powershell
  rg "MateWpfThemes\.FW|SwiftExport\.View\.FW|SwiftExport\.VSTO|MVVMLight|MvvmLight|GalaSoft|NavigationViewItem" SwiftExport.Client.Wpf
  ```
- 最近结果：无命中。
- 已运行：
  ```powershell
  Select-String -Path SwiftExport.Client.Wpf/Resources/DataGrid.xaml,SwiftExport.Client.Wpf/Features/Products/ProductList/Views/ProductsView.xaml -Pattern '<Style\s+TargetType=\"\{x:Type (DataGrid|TextBox|ComboBox)\}\"','<Style\s+TargetType=\"(DataGrid|TextBox|ComboBox)\"','Margin=\"[^\"]*(18|20)[^\"]*\"','Padding=\"[^\"]*(18|20)[^\"]*\"'
  ```
- 最近结果：无命中；DataGrid 样板未新增隐式全局 `DataGrid/TextBox/ComboBox` Style，未发现 `Margin/Padding` 的 18/20 游离值。
- 已运行：
  ```powershell
  rg "Brush_Txt_Primary|Brush_DataGridHeaderBck|Brush_Control_Background|Brush_DataGridMouseOver|Brush_DataGridSelectionCell|DataGridExcelAssist|UndoManager|CellTrackerState|DataGridSearchableColumn|ApplyMateThemeColumnStyles" SwiftExport.Client.Wpf/Resources/DataGrid.xaml SwiftExport.Client.Wpf/Features/Products/ProductList/Views/ProductsView.xaml
  ```
- 最近结果：无命中；本次未迁入旧主题键或旧 DataGrid 行为 Assist。
- 已运行：
  ```powershell
  dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj
  ```
- 最近结果：成功，0 warning，0 error。
- 已运行：
  ```powershell
  dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj
  ```
- 最近结果：成功，0 warning，0 error。
- 已运行：
  ```powershell
  Select-String -Path SwiftExport.Client.Wpf\Resources\Controls\PopupTextBox.xaml -Pattern 'Margin=\"[^\"]*(18|20)[^\"]*\"|Padding=\"[^\"]*(18|20)[^\"]*\"|<Style(\s|>)|Brush_Txt_|BaseTextBox|BaseComboBox|ButtonText'
  ```
- 最近结果：无命中；`PopupTextBox` 未定义隐式 Style，未使用旧主题键，未发现 `Margin/Padding` 的 18/20 游离值。
- 已运行：
  ```powershell
  Select-String -Path SwiftExport.Client.Wpf\Shell\*.xaml,SwiftExport.Client.Wpf\Resources\ShellNavigation.xaml -Pattern 'Margin=\"[^\"]*(18|20)[^\"]*\"|Padding=\"[^\"]*(18|20)[^\"]*\"'
  ```
- 最近结果：无命中；Shell 相关 XAML 未发现 `Margin/Padding` 的 18/20 游离值。
- 已运行：
  ```powershell
  dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj --no-restore
  ```
- 最近结果：成功，0 warning，0 error。
- 已运行：
  ```powershell
  rg "MateWpfThemes\.FW|SwiftExport\.View\.FW|SwiftExport\.VSTO|MVVMLight|GalaSoft" SwiftExport.Client.Wpf SwiftExport.Contracts
  ```
- 最近结果：无命中。
- 已运行：
  ```powershell
  rg "Brush_Txt_Primary|Brush_DataGridHeaderBck|Brush_Control_Background|Brush_Txt_Secondary|BaseComboBox|ButtonText" SwiftExport.Client.Wpf/Resources SwiftExport.Client.Wpf/Features/Products
  ```
- 最近结果：无命中。
- 已运行：
  ```powershell
  Select-String -Path SwiftExport.Client.Wpf\Resources\DataGrid.xaml -Pattern 'State|暂无数据|VisualBrush|SwiftExportDataGridRow|SwiftExportStateDataGridRow'
  ```
- 最近结果：`State` 只存在于 `SwiftExportStateDataGridRow`；默认 `SwiftExportDataGridRow` 不绑定 `State`，默认 `SwiftExportDataGrid` 不包含 `VisualBrush` / “暂无数据”。
- 已运行：
  ```powershell
  Select-String -Path SwiftExport.Client.Wpf\Resources\DataGrid.xaml,SwiftExport.Client.Wpf\Features\Products\Colors\Views\ProductColorsView.xaml,SwiftExport.Client.Wpf\Features\Products\ProductList\Views\ProductsView.xaml -Pattern 'Margin="[^\"]*(18|20)|Padding="[^\"]*(18|20)|Height="(18|20)"|Width="(18|20)"'
  ```
- 最近结果：无命中。
- 已运行：
  ```powershell
  rg "NavigationViewItem|MateWpfThemes\.FW|SwiftExport\.View\.FW|SwiftExport\.VSTO|MVVMLight|MvvmLight|GalaSoft" SwiftExport.Client.Wpf
  ```
- 最近结果：无命中。
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

## 下一阶段：阶段 8 后续打磨，DataGrid 编辑能力与产品模块页面

建议目标：

- 基于产品资料页和产品颜色页继续打磨查询控件、工具栏按钮语义和 DataGrid 可编辑体验。
- 基于已迁移的 `LookupTextBox` 设计产品分类、材质、客户/供应商等 Lookup 使用方式。
- 提取可复用的查询区 / 工具栏布局控件或命名样式，避免后续页面复制 XAML。
- 后续再扩展 DataGrid Lookup 列、Excel 化粘贴、Ctrl+D、撤销栈、列冻结和批量选择。
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
- 当前 Shell 是 WPF-UI TitleBar + Gallery 风格左侧导航 + 多标签工作区，菜单为：首页、产品、销售、计划、采购、出货；产品下已有产品资料、产品分类管理、产品颜色管理、BOM、客户产品、供应商产品。
- 已建立 Fluent 布局规则：4px spacing、页面 Header、查询区、工具栏、DataGrid、状态/分页区分层。
- 阶段 8 基础样板已完成：产品资料页接入真实 API、DataGrid、查询、分页、增删改、批量保存和未保存变更拦截。
- 已迁移 `PageNavigator` 和 `LookupTextBox` 到 `SwiftExport.Client.Wpf/Resources/Controls/`；产品页底部已使用新 `PageNavigator`。
- 已新增 `PopupTextBox` 到 `SwiftExport.Client.Wpf/Resources/Controls/`，产品资料页备注列已使用它做弹出多行编辑。
- 产品资料页已接入第一版 Fluent DataGrid 样板：opt-in 行状态、校验视觉、文本/数字列样式和备注弹出编辑；默认 DataGrid 不内置空状态。
- 产品颜色管理页已新增为正式 tab 页面，接入真实 `IProductColorApi`、分页查询、防抖搜索、批量保存和 WPF-UI DropDownButton 更多菜单。
- Shell 左侧导航已按 WPF-UI Gallery 风格重做：搜索框、图标菜单、选中 Accent 竖条、分组展开和底部 Settings 占位入口。
- 下一步是阶段 8 后续打磨：产品页面控件化与 DataGrid 视觉 / 编辑体验细化。
- 产品资料页和左侧导航均可打开“分类管理”阻断式 Dialog；分类树已接入 GongSolutions 拖拽排序。
- `Features` 已按业务域 / 功能单元整理；后续新业务页面按 `Views` / `ViewModels` / `Models` / `Services` / `Components` 分层落在对应功能单元目录。

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
