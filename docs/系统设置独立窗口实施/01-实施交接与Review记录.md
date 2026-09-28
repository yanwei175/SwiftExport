# 系统设置独立窗口：实施交接与 Review 记录

状态：**实施完成，待统一 Review**。范围与验收依据为 [实施方案](00-实施方案.md)。本节是执行者自查；统一 Review 由原规划聊天填写。

## 执行基线与所有权

- 执行聊天：`01a0e0ec-0bf9-7541-9599-9e7555839bf2`；原规划/审查聊天：`01a0e0ce-3b78-71a2-94a5-bd9b3f0efcce`。
- 工作目录 `D:\Documents\source\CSharp\SwiftExport`，分支 `main`，HEAD `439df0df2d312e8b183bfa7e3bbdf4dba910fa06`；开始时已有大量 RBAC、迁移、测试及文档的未提交变更和未跟踪文件，均予保留。
- 本轮单一写入者仅写 WPF、当前设置相关 WPF 测试与本目录。未写后端、Contracts、数据库，未提交、推送或部署，未启动 Agent 或新聊天。
- 前端主角色按 frontend 与前端 NET10 工作流实施；读取了适用的窗口、MVVM、页面架构、运行时安全、布局及质量门禁规则。

## 分阶段记录

| 阶段 | 状态 | 实际变更与结果 | 未验证项 |
|---|---|---|---|
| 1 盘点与拆分 | 完成 | 核对设置中心、RBAC/字典 ViewModel、四个旧窗口壳、DI 注册、IAuthApi/ISystemRbacApi 与现有测试；保留权限实现及 R6 四范围修订号 | 后台权限语义沿用既有实现 |
| 2 窗口与入口 | 完成 | 新建 `Workspace/Views/SystemSettingsWindow.xaml(.cs)`；左侧四页、按权限首次加载、窗口本地 Dialog/消息、Shell Owner、非模态单实例与会话关闭；设置中心只留一个系统入口及个人功能 | 真人多屏/DPI/键盘操作未验 |
| 3 用户/角色/权限及表单 | 完成 | 新建三页 View；用户列表和右侧资料/授权、创建与固定目标重置；角色列表、独立新建草稿和完整权限集合保存；只读权限目录；RBAC VM 增加按页读取、刷新及撤权清理。重用现有注册/重置 VM、API Runner、RBAC 写入链，不改 API/Contracts | 真实 API/用户资料及密码流程未验 |
| 4 字典宿主迁移 | 完成 | 原 `DictionaryManagerView/VM` 直接嵌入；`CommitPendingEdits` 返回真实 DataGrid Cell/Row 提交结果，提交失败阻止切页和关闭；保留字典权限和写入逻辑 | 真人编辑器、弹窗及焦点流未验 |
| 5 清理与自查 | 完成 | 删除旧 RBAC、字典、注册、重置窗口壳和无调用 DI；迁移 STA 窗口测试；`rg` 确认旧窗口/打开方法在 WPF 与测试内无引用；diff 空白检查通过 | 完整人工 UI 与跨端联验未验 |

## 文件与行为边界

- 主要业务代码：`SettingsCenter/ViewModels/SettingsCenterViewModel.cs`、`SettingsCenter/Views/SettingsCenterView.xaml`、`Services/UI/ISettingsWindowService.cs` 与 `SettingsWindowService.cs`、`Bootstrapper/ViewRegistrationExtensions.cs`、`RbacManagement/ViewModels/RbacManagementViewModel.cs`、`Dictionaries/Views/DictionaryManagerView.xaml.cs`、`Workspace/Views/*`。
- 删除窗口壳：`RbacManagementWindow`、`DictionaryManagerWindow`、`UserRegistrationWindow`、`ResetPasswordWindow` 的 XAML/code-behind；个人 `ChangePasswordWindow` 保留。
- 测试变更：`SettingsViewModelTests.cs`、`SettingsWindowInteractionTests.cs`、`DictionaryManagerLifecycleTests.cs`、`RbacManagement/RbacManagementViewModelTests.cs`。原授权写入、版本及延迟响应断言保留；新增真实 STA 窗口、单实例/Owner、权限切页、表单、密码清理及 DataGrid 提交失败回归。
- 用户创建成功后再刷新、定位并单独分配角色，不宣称跨接口事务；角色权限搜索只过滤呈现，保存仍用完整集合；只有字典 View 的身份不请求用户/角色管理 API。撤权后清空受影响缓存与编辑字段，失去当前页时转到仍可访问的页面。
- Contracts、WebApi、Application、Core、Infrastructure、数据库和迁移脚本本轮零改动。上述目录的既有工作区变更属于前序权限任务，不应归入本轮 diff。

## 验证与证据

以下命令均在仓库根目录执行；结果均为退出码 0，除单独说明的初次尝试。WPF 测试项目的常规沙箱写入 `bin/obj` 曾被拒绝，随后使用已授权的隔离执行权限运行。

| 命令 | 结果 | 证据范围 |
|---|---|---|
| `dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj --no-restore -v:q` | 0 错误；2 个既有 ProductDetailViewModel CS8618 告警 | WPF 主项目编译 |
| `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSettings" -v:q` | 30/30 通过，0 失败/跳过 | 设置中心、真实 STA 窗口/页面、字典、RBAC VM 定向行为 |
| `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-build --filter "FullyQualifiedName~RbacManagementViewModelTests\|FullyQualifiedName~AuthRefreshSessionTests\|FullyQualifiedName~SettingsWindowInteractionTests\|FullyQualifiedName~DictionaryManagerLifecycleTests" -v:q` | 26/26 通过，0 失败/跳过 | R6 延迟响应、会话与窗口回归；复用已编译程序集 |
| `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSettings\|FullyQualifiedName~AuthRefreshSessionTests\|FullyQualifiedName~CurrentUserPermissionTests" -v:q` | 35/35 通过，0 失败/跳过；重编译测试项目，有既有测试告警 | 最终设置/会话/权限定向回归 |
| `git -c core.safecrlf=false diff --check` | 退出码 0 | 跟踪文件无空白错误；未跟踪新页面与 VM 须另读 |
| `rg -n "RbacManagementWindow\|DictionaryManagerWindow\|UserRegistrationWindow\|ResetPasswordWindow\|ShowUserRegistration\|ShowResetPassword\|ShowRbacManagement\|ShowDictionaryManager" SwiftExport.Client.Wpf SwiftExport.Client.Wpf.Tests` | 退出码 1、零命中 | 旧系统窗口/入口无 WPF 消费者 |

期间的编译/测试失败已修复后重跑：窗口限位 `Math.Clamp` 参数错误、旧测试命名空间引用、权限测试身份缺少 View 键、DataGrid 测试类型名歧义；它们不算通过证据。STA `RenderTargetBitmap` 曾输出缺失主题绘制的黑底图，已删除，不作为实现截图。**没有可供复核的清晰实现截图**；设计稿 `已确认布局参考.png` 仅是布局参考。

自动化包括真实 WPF STA 视觉树和 DataGrid 编辑提交，但 API 使用测试替身。尚未做真人窗口尺寸/键盘/弹窗、真实 API 与测试库、完整业务 Shell 登录后操作；不能把编译或内存替身结果称作这些验证。没有操作真实库或任何数据库。

## 交付与续做位置

实施代码与测试已停止写入，停止时间：2026-09-27 21:06 +08:00；审查者从本文件、方案、当前 HEAD 和完整工作区（包括未跟踪文件）开始只读复核。重点复核按权限加载和撤权、创建/重置表单的密码清理、字典待提交单元格、角色完整集合保存及窗口单实例生命周期。发现问题由执行者另获后续修复授权并记录验证。当前状态仅为**待统一 Review**，执行者未填写“Review 通过”。

## P1/P2 定向修复记录（2026-09-27 21:21 +08:00）

用户指出三个窗口生命周期问题后，执行者继续单一写入；沿用上述 `main`/HEAD 和全部已有未提交变更。本轮仅触及 WPF 窗口、用户表单、字典 VM、两份对应 WPF 测试及本记录，未写后端、Contracts 或数据库。

1. 加载途中撤权：`SystemSettingsWindow.NavigateAsync` 在异步读取后先核对窗口和当前权限，再访问页面；权限变化与加载重叠时登记待处理导航，待当前导航退出后切到剩余可读页。新增 STA 延迟用户列表响应测试，证明撤权后旧用户结果不回写且自动进入权限目录。
2. 页内标签草稿：`UserManagementView` 按离开的标签索引检查创建/重置密码草稿，创建表单的邮箱和电话也计入；拒绝放弃时恢复原标签并保留输入。新增 STA 测试覆盖两个表单的拒绝切换。
3. 字典确认放弃：`DictionaryManagerViewModel.CanLeavePageAsync` 在确认后恢复类型与字典值快照；新建类型草稿被丢弃并标记返回时重新初始化。窗口切页调用该方法。新增测试验证类型和值同时修改后确认离页，缓存不再带脏内容。

仓库根目录验证：

| 命令 | 退出码与实际结果 |
|---|---|
| `dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj --no-restore -v:q` | 0；0 错误，2 个既有 ProductDetailViewModel CS8618 告警 |
| `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSettingsWindow_RevocationDuringLoadDefersAuthorizedNavigation\|FullyQualifiedName~SystemSettingsWindow_RejectDiscardKeepsBothFormDrafts\|FullyQualifiedName~CanLeavePage_ShouldDiscardConfirmedTypeAndValueDrafts" -v:q` | 0；新增回归 3/3，通过，0 失败/跳过；测试项目另有既有分析器告警 |
| `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-build --filter "FullyQualifiedName~SystemSettings\|FullyQualifiedName~AuthRefreshSessionTests\|FullyQualifiedName~CurrentUserPermissionTests" -v:q` | 首次退出码 1：37/38，简略输出未列失败用例；随后以 `--logger "console;verbosity=normal"` 复跑退出码 0、38/38，再以 `-v:q` 复跑退出码 0、38/38；初次间歇失败仍列为未解释限制 |
| `git -c core.safecrlf=false diff --check` | 0；无跟踪文件空白错误，新增未跟踪文件需单独审阅 |

最终将字典离页提示改为“离开页面”后再次运行 WPF 主项目构建，退出码 0、0 错误、2 个既有告警；`dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~CanLeavePage_ShouldDiscardConfirmedTypeAndValueDrafts" -v:q` 退出码 0，1/1 通过，0 失败/跳过。修复写入停止时间：2026-09-27 21:23 +08:00。

这些用例使用真实 STA/WPF 页面事件与内存 API 替身，不等于真人 UI、真实 API 或数据库验证。原交接中的未验证限制仍有效。三个问题均为**已修复，待统一 Review 复核**，实施者不填写审查通过。

## 统一 Review（审查者填写）

2026-09-27 统一 Review：**未通过，以下 3 项待修复后复核**。本次范围为独立系统设置窗口及其入口、页内表单、按页加载、字典宿主迁移和相关测试；不重新审查前序权限整改、数据库脚本或其他混杂工作区变更。

| 编号 | 优先级 | 文件/行号 | 问题与影响 | 处理与复核证据 |
|---|---|---|---|---|
| R1 | P1 | `SwiftExport.Client.Wpf/Features/SystemSettings/Workspace/Views/SystemSettingsWindow.xaml.cs:126-127` | 用户页初始化等待 HTTP 期间，同用户撤销 `System.Users.View` 并保留其他入口，撤权处理会 Dispose 并将 `_users` 置空（204-205 行）；加载返回后仍执行 `_users.RefreshCount()`，产生空引用异常并向 async void 窗口事件传播。与此同时 `_navigating` 阻挡强制切页，使剩余页面导航丢失。 | 应在异步续体检查宿主/页面实例与权限是否仍有效，并保证撤权导航能在在途导航结束后完成；不能只补空条件调用。补延迟 API + 加载途中撤权的 STA 测试，断言无异常、旧页不可见且自动转到剩余授权页。现有撤权测试仅在初始化完成后撤权。 |
| R2 | P2 | `SwiftExport.Client.Wpf/Features/SystemSettings/Workspace/Views/UserManagementView.xaml.cs:110-120` | `SelectionChanged` 已更新 `EditorTabs.SelectedIndex`，但 `HasFormDraft()` 按新标签检查草稿；从创建用户或重置密码切回资料页时返回 false，随后 `ClearForms()` 无确认清除输入。创建表单仅填写邮箱/电话时也未被草稿检查覆盖。 | 按离开的标签/独立表单草稿判断，取消时保留原标签及全部输入；补拒绝确认测试，覆盖创建、重置、仅邮箱/电话输入。当前测试只断言切页清空密码，没有验证是否询问或允许取消。 |
| R3 | P2 | `SwiftExport.Client.Wpf/Features/SystemSettings/Workspace/Views/SystemSettingsWindow.xaml.cs:112-116` | 离开字典页调用 `CanCloseAsync`，该方法仅确认、不回滚草稿。旧窗口关闭会释放 VM，而新宿主缓存 `_dictionary`，再进入时 `InitializeAsync` 幂等返回，用户已确认放弃的类型/字典值修改仍显示并可保存；关闭窗口还会再次询问。 | 确认离开后实际丢弃字典草稿，或释放并在重新进入时重建字典页面/VM，继续复用既有 API。补编辑→确认放弃→切回字典的测试，验证恢复服务端值且无脏状态；取消时则保留当前页与输入。 |

验证：审查者复跑 `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSettings|FullyQualifiedName~AuthRefreshSessionTests|FullyQualifiedName~CurrentUserPermissionTests" -v:q`，35/35 通过，0 失败/跳过。首次沙箱构建因无法写入 coverage 临时文件失败，随后通过授权执行成功。`git -c core.safecrlf=false diff --check` 通过。上述发现来自真实源码调用链与现有测试覆盖核对，并非宣称已新增回归测试或完成真人复现。

本轮仅更新本交接文档，未修改业务代码或测试。现有通过测试未覆盖上述场景，不能据此标记 Review 通过。真人全链操作、清晰实现截图、多屏/DPI、真实 API 联验仍未完成；实施阶段构建证据沿用，不将测试替身等同于真实后端。未提交、推送、部署或执行数据库操作。

### 三项修复复核（2026-09-27）

结论：**R2、R3 关闭；R1 原空引用及导航丢失路径已修正，但回归验证仍未通过，暂不签署整体 Review 通过。**

- R1：续体增加生命周期/权限/页面空值检查；新增待处理权限导航在当前导航 finally 释放门禁后重评。源码已覆盖原报告路径。
- R2：按 previousIndex 检查离开的表单，并纳入邮箱/电话；拒绝切换测试覆盖创建和重置密码输入保留，本次通过。
- R3：离页确认后恢复类型与行快照，新建类型草稿清除并允许重新初始化；类型和值放弃测试本次通过。

新增 **R4 / P2**：`SwiftExport.Client.Wpf.Tests/Features/SystemSettings/SettingsWindowInteractionTests.cs:343-348` 的撤权回归没有等待导航完成。response 使用 RunContinuationsAsynchronously，SetResult 后两次 Dispatcher 空闲不保证线程池续体及后续导航已入队/完成，测试可能在 CurrentPageName 仍为 null 时断言。应使用有超时的导航完成信号或最终状态等待，并保留无迟到数据和自动导航断言；不能删除断言、固定延时或重复跑到通过。该证据本身不能证明产品永久停留空白页面。

审查者执行 `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-build --filter "FullyQualifiedName~SystemSettings|FullyQualifiedName~AuthRefreshSessionTests|FullyQualifiedName~CurrentUserPermissionTests" --logger "console;verbosity=normal" -v:q`，退出码 1：**37/38 通过**。唯一失败为 `SystemSettingsWindow_RevocationDuringLoadDefersAuthorizedNavigation`，348 行 expected `Permissions`，actual `null`。这定位了本轮复现的间歇失败；前次未保留用例名的失败不能直接认定同源。其余两项新增回归均通过。`git diff --check` 通过；沿用实施构建证据，未重复构建，未改源码/测试或操作数据库。

### R4 等待时序修复（实施者，2026-09-27 21:30 +08:00）

仅修改 `SwiftExport.Client.Wpf.Tests/Features/SystemSettings/SettingsWindowInteractionTests.cs` 与本记录。撤权回归不再用两次 Dispatcher 空闲作为导航完成条件，改由 DispatcherTimer 检查最终状态：当前页为 `Permissions`、权限目录页存在且 RBAC VM 不忙；5 秒超时会报告当前页。保留旧用户结果为空、旧用户页不可见、自动导航到目录页三项断言。产品代码未修改，保留原规划/审查记录和全部既有工作区改动。

仓库根目录验证：`dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSettingsWindow_RevocationDuringLoadDefersAuthorizedNavigation" --logger "console;verbosity=normal"` 退出码 0，1/1 通过；等待条件进一步加入目录已显示及 VM 不忙后，同一筛选 `-v:q` 退出码 0，1/1 通过。最终测试程序集上，`dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-build --filter "FullyQualifiedName~SystemSettings|FullyQualifiedName~AuthRefreshSessionTests|FullyQualifiedName~CurrentUserPermissionTests" -v:q` 连续两次退出码 0，均为 38/38 通过、0 失败/跳过。修改前的 37/38 失败保留为历史事实，不将其改写为通过。

当前仅能说明替身 API 的 STA 回归达到上述最终状态；真实 API、真人 UI 及原有其他验证限制不变。R4 由实施者标记为**已修复、待审查者复核**，不代填整体 Review 通过。修复写入在本记录完成后停止。

### 最终复核结论（审查者，2026-09-27）

**Review 通过（本次系统设置独立窗口代码审查范围），R1—R4 全部关闭。No findings.** 前述未通过结论及 37/38 失败作为历史记录保留，以本节为当前结论。

R4 已改为有 5 秒超时的最终状态等待，同时要求权限目录实际位于视觉树中、当前页为 Permissions、VM 加载结束；计时器在 finally 停止并退订。旧用户数据为空、旧用户页不可见和自动导航断言均保留，未通过放宽断言规避问题。结合此前产品代码复核，R1 的验证缺口关闭。

审查者本轮执行 `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-build --filter "FullyQualifiedName~SystemSettingsWindow_RevocationDuringLoadDefersAuthorizedNavigation" --logger "console;verbosity=normal" -v:q`：退出码 0，1/1 通过。复用实施者最终测试程序集连续两次 38/38 的合并回归证据，不再重复全组测试或构建。`git diff --check` 通过。

本轮仅更新本交接记录，未修改业务代码或测试。通过结论不等于真人 UI、真实 API、数据库或多屏/DPI 验收完成；这些已列明的验证限制仍保留。未提交、推送、部署或执行数据库操作。

### 后续结构实施：NavigationView 与模态表单（实施者，2026-09-27 22:45 +08:00）

用户另行授权把设置窗口四个目的地改为 WPF-UI 4.3.0 的真实 `NavigationView` 和 `Page`，并把创建用户、重置密码从用户页标签移入以系统设置为 Owner 的模态 `FluentWindow`。本节记录该后续实施，不改写上方针对旧结构的审查历史；当前新结构状态为**待统一 Review**，实施者未填写“Review 通过”。

- `Workspace/Views/SystemSettingsWindow.xaml(.cs)`：侧栏改为 `NavigationView`，按权限延迟创建用户、角色、权限目录、字典四个 `Page`；字典页用薄包装继续承载原 `DictionaryManagerView`。切页保留待提交编辑、草稿确认、撤权后重选页面与会话关闭。模态子窗由宿主创建，用户页撤权或管理员资格撤销时强制关闭，成功创建后返回用户名供父页刷新定位。
- `Workspace/Views/UserManagementView.xaml(.cs)`：保留用户列表、右侧资料及角色授权，移除创建/重置页内标签，按钮分别打开模态子窗。角色页和权限目录页根类型也改为 `Page`。
- `UserRegistration/Views/UserRegistrationView.xaml(.cs)` 与 `UserRegistrationDialogWindow.xaml(.cs)`、`Passwords/Views/ResetPasswordView.xaml(.cs)` 与 `ResetPasswordDialogWindow.xaml(.cs)`：表单用独立 `UserControl`，窗口用 `ShowDialog`；重置目标从父页选中用户固定传入，不另请求全用户列表。成功、确认放弃和会话强制关闭均清除密码；写入中普通关闭被阻止。子窗使用独立本地确认宿主，不占用系统设置窗口的确认宿主。
- `Services/UI/SettingsWindowService.cs` 与对应窗口测试调用更新构造参数，移除系统设置窗口不再使用的 `IDialogService` 依赖。未改 API、Contracts、后端或数据库。

测试迁移按行为覆盖：确认放弃清理、拒绝放弃保留两类密码草稿、创建成功请求及返回、提交期间禁用与阻止普通关闭、固定重置目标和成功清理、会话切换时在途创建的父子窗关闭及迟到响应不重开。旧标签测试逐项改为新结构下的等价交互，并新增 `NavigationViewItem.IsActive` 拒绝切页后恢复断言。曾尝试直接删除旧标签测试，被自动审批以“缺少等价覆盖证明”为由拒绝；随后改为逐项迁移并保留有效断言，没有绕过拒绝。

仓库根目录执行及结果：

| 命令 | 结果 |
|---|---|
| `dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj --no-restore -v:q` | 退出码 0，最终 0 错误、0 告警；此前重编译有既有 ProductDetailViewModel CS8618 告警 |
| `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSettings|FullyQualifiedName~FormDialogs|FullyQualifiedName~AuthRefreshSessionTests|FullyQualifiedName~CurrentUserPermissionTests" --blame-hang-timeout 30s -v:q` | 退出码 0，43/43 通过，0 失败/跳过；测试项目编译有既有分析器/可空性告警 |
| `git -c core.safecrlf=false diff --check -- SwiftExport.Client.Wpf/Features/SystemSettings SwiftExport.Client.Wpf/Services/UI/SettingsWindowService.cs SwiftExport.Client.Wpf.Tests/Features/SystemSettings` | 退出码 0，本轮范围无空白错误 |

一次普通沙箱测试构建因测试项目 `bin` 写入受限退出，授权执行后通过。一次合并测试在迁移过程中因子窗实际确认弹窗无人响应而停滞，已中止并改为注入可控确认服务的等价测试；最终 43/43 是修复后的重编译结果。全仓 `git diff --check` 另报 `SwiftExport.Client.Wpf/Resources/DataGrid.xaml` 三处已有尾随空格，本轮未修改该文件。

尚未做真人 UI、真实 API、测试库或多屏/DPI 验证；本轮测试使用真实 WPF STA 视觉树及 API 替身。保留工作区全部既有改动，不提交、推送、部署或操作数据库。后续审查应从本节、当前 HEAD 与包含未跟踪新 Page/子窗的完整工作区开始，重点核对导航选中态、模态 Owner/关闭及会话撤权。

### 后续结构实施：DataGrid 行操作、本地 ContentDialog 与目录同步（实施者，2026-09-28）

> 当前结论见下方“直接审查与修复”；其后的实施者原始交接保留为历史。

### 直接审查与修复（2026-09-28，当前结论）

本轮由当前聊天直接接管，已先确认原执行窗口 idle，未再委派。审查范围是上一轮 DataGrid/ContentDialog 改造与权限目录同步，保留所有其他工作区改动。发现的问题已直接修正；本范围代码审查通过，真人 UI、真实业务 API 与多屏/DPI 仍未验收。

- 保存期间四个 RBAC 表单原先只锁按钮，仍能输入，随后成功关闭可能丢失在途新增输入；现在整个编辑控件绑定 IsNotBusy。新增真实 STA 延迟写入用例覆盖四类表单锁定及恢复。
- 权限搜索原先修改共享默认集合视图，后一个弹窗可能继承上一次搜索；现在每个权限弹窗独立 ListCollectionView，保存仍读取完整集合。新增两弹窗隔离测试。
- 角色资料、用户/角色状态修改后 DTO 不发通知，列表可能仍显示旧值；成功后重新读取对应列表。活动表单期间阻止普通导航，避免争用本窗口 ContentDialogHost。
- DataGrid 选中行不再自动读取授权聚合，避免行选择先发请求进入 Busy，吞掉随后首次点击的行按钮；具体读取由行操作固定目标触发。真实行编辑测试加入“选择另一行不会切换已加载聚合”的断言，资料/授权行操作仍通过。
- Sales 分组已正确包含五项操作和一项查看。本次保持现有键值与 ID，不做未约定的权限键迁移；销售目录改为复用 PermissionKeys 常量，新增 SalesOrderView 常量。PermissionCatalog.All 返回不可修改包装，避免调用方转换回数组篡改定义；新增目录分组、唯一性、只读测试。
- 同步工具大小写不敏感匹配会静默接纳错误拼写，而运行时按精确键查找；现在在任何写入前明确拒绝大小写漂移，保留重复键检查。原初始化、状态/关系保留、目录外报告和事务语义不变。

验证记录：

1. `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSettings|FullyQualifiedName~FormDialogs|FullyQualifiedName~AuthRefreshSessionTests|FullyQualifiedName~CurrentUserPermissionTests" --logger "console;verbosity=normal" -v:q`：重编译 WPF/测试成功；49/50 通过。唯一失败为旧授权交互用例在弹窗关闭动画完成前立即导航，被新增宿主门禁正确拒绝。改为等待“持久化成功且表单已关闭”，保留授权断言后单独复跑 1/1 通过。
2. 最后行选择修复后，仅重跑 `SystemSettingsWindow_RowProfileFormsSaveSelectedTargets|SystemSettingsWindow_CheckboxesAndButtonsBindToVersionedWrites`，2/2 通过。没有声称最终版本重新跑过整组 50/50，也没有运行全解决方案测试。
3. `dotnet build .\_tools\DbSeed\DbSeed.csproj --no-restore -v:q` 成功，0 错误；有 NU1900 离线漏洞源告警。WPF 有既有 ProductDetailViewModel 可空性告警和既有测试分析器告警。
4. 在专用 LocalDB 实例 SwiftExportPermissionSync_20260927 新建 PermissionCaseReview_20260928：插入小写 sales.order.view 后执行 --sync-permissions --dry-run，按预期拒绝；验证记录数、原键和禁用状态均未改变。已删除本轮创建的审查库，未访问 dv_next。其他同步场景复用实施者隔离库证据。
5. 本轮所改跟踪文件 diff --check 通过；未跟踪表单/目录/工具源码另行读取核对。保留历史失败，不以重复跑到通过替代定位。

使用同步工具时，在自己的终端设置 `SWIFTEXPORT_DBSEED_CONNECTION` 指向目标库，先运行 `dotnet run --project .\_tools\DbSeed\DbSeed.csproj -- --sync-permissions --dry-run` 查看差异，再运行去掉 `--dry-run` 的同一命令写入。该模式不需要管理员账号或密码，不创建用户或角色；不会自动重命名权限键。这里只交付命令，没有对用户业务库执行。

本轮修改了 WPF 业务代码、Contracts 权限目录/常量、DbSeed、相关 WPF 测试及本记录；未提交、推送或部署。下方保留实施者前次原始说明，不能将其中旧“待统一 Review”覆盖本节结论。

用户另行授权将用户、角色页改为 DataGrid 主体，资料/授权分别以独立 `UserControl` 在系统设置窗口的本地 `ContentDialog` 中编辑，并将五项 `Orders.SalesOrder.*` 权限归入 `Sales`，新增独立权限目录同步模式。本节仅记录此后续实施；上方 Review 通过属于此前结构，**本轮状态为待统一 Review**。

WPF 范围：`Workspace/Views/UserManagementView`、`RoleManagementView` 改为行内操作；新增 `UserProfileFormView`、`UserRolesFormView`、`RoleProfileFormView`、`RolePermissionsFormView` 及 `SettingsFormDialogSession`。宿主 `SystemSettingsWindow` 固定每个行按钮目标并管理本地弹窗、放弃确认、撤权/会话强制关闭。移除旧创建与重置密码独立 FluentWindow 外壳，保留独立 `UserControl`。`RbacManagementViewModel` 增加按资料/授权范围的草稿判断和成功写入修订号；失败不关闭表单。创建/重置请求失败时保留密码输入供修改，成功、确认放弃和强制关闭时擦除；两个 VM 在释放后忽略迟到响应。测试扩展到行按钮目标、完整权限集合、资料写入、密码失败/成功、放弃确认、写入中锁定和会话切换。旧表单覆盖逐项迁移，未删除有效断言。

目录范围：`PermissionCatalog.cs` 中五项销售订单键只调整 GroupKey 为 `Sales`；`_tools/DbSeed/Program.cs` 增加 `--sync-permissions [--dry-run]`，实现位于 `PermissionCatalogSync.cs`。同步按键更新展示名、分组和排序，保留既有 Uid、启停/删除状态、角色关系与目录外键；单事务失败回滚，缺结构和重复键明确退出。原初始化模式仍需原有管理员参数。仓库没有独立的权限目录数据初始化 SQL；`SwiftExport.Infrastructure/Database/Generated/SwiftExport.Schema.sql` 是自动生成的结构文件，因此没有手改生成 SQL，空测试库的目录由新同步命令建立。

验证均在仓库根目录执行；数据库只使用新建的专用 LocalDB 测试实例 `SwiftExportPermissionSync_20260927` 与其中的测试库，没有连接真实库：

| 命令/场景 | 结果 |
|---|---|
| `dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj --no-restore -v:q` | 退出码 0，0 错误、0 告警 |
| `dotnet build .\_tools\DbSeed\DbSeed.csproj --no-restore -v:q` | 退出码 0，0 错误；NU1900 离线漏洞源告警 1 项 |
| `dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-build --filter "FullyQualifiedName~SystemSettings\|FullyQualifiedName~FormDialogs\|FullyQualifiedName~AuthRefreshSessionTests\|FullyQualifiedName~CurrentUserPermissionTests" --blame-hang-timeout 30s -v:q` | 退出码 0，47/47 通过，0 失败/跳过；包括原 R4 超时最终状态等待 |
| 空库同步预览、首次同步、再次同步 | 预览 111 新增且库内仍 0 行；首次插入 111；再次 0 新增/0 更新；目录外测试键保留并报告 |
| 改旧销售订单分组并禁用、挂角色关系后同步 | 预览只报 1 更新且不写；同步后 GroupKey 为 Sales，原 Uid、禁用状态、关系均保留，五项键归属 Sales |
| 测试库触发器注入中途失败、重复键、缺结构 | 中途失败后两个先后待更新键均维持旧分组；重复键明确退出；另建空结构测试库执行 `--sync-permissions --dry-run` 退出码 1，明确报告缺结构且不迁移 |
| 范围内 `git -c core.safecrlf=false diff --check` | 退出码 0；全仓既有 `Resources/DataGrid.xaml` 三处尾随空格不属于本轮改动 |

曾用新增测试复现创建/重置服务端失败后密码过早清空，修复后两项通过；用户/角色行内资料测试初版未等 DataGrid 页面生成而失败，改为等待页面和行视觉树后通过。上述失败保留为实施过程事实，不作为通过证据。自动化使用真实 WPF STA 视觉树和 API 替身；数据库仅验证目录同步，不代表真实业务 API、真人 UI 或多屏/DPI 验收。未提交、推送、部署或操作真实库。审查者应复核本节所列文件、旧模态外壳无消费者、完整集合保存和会话撤权边界；**实施者不填写本轮“Review 通过”**。
