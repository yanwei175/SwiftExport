# SwiftExport .NET 10 WPF 前端实施路线图

## 总体节奏

不要一次性重写所有功能。新前端应该按“骨架先行、模块验证、逐步迁移”的方式推进。

推荐顺序：

```text
阶段 0：盘点旧项目
阶段 1：创建新 .NET 10 WPF 项目
阶段 2：接入 WPF UI 和主题
阶段 3：建立 Host / DI / 配置
阶段 4：接入 SwiftExport.Contracts 和 API
阶段 5：实现版本检查
阶段 6：实现登录 / 退出登录
阶段 7：实现 Shell / 菜单 / 导航
阶段 8：迁移 Products 模块
阶段 9：接入 ClickOnce 测试发布
阶段 10：迁移订单、PackingList 和导入模块
阶段 11：完善权限、审计、异常和体验
独立路线：Excel / VSTO 插件作为 .NET Framework 项目单独设计
```

## 阶段 0：盘点旧项目

目标：明确哪些东西要复用，哪些要重做。

要整理：

```text
旧登录接口
旧登录返回 DTO
旧 token 保存方式
旧 ViewModelBase / RelayCommand / Messenger 用法
旧主题资源
旧页面列表
旧 API 调用方式
旧弹窗和消息提示
旧 DataGrid 样式
旧 Excel 导入流程只做边界盘点，不迁入 .NET 10 WPF 工程
```

产出：

```text
旧项目迁移清单
要保留的主题资源清单
要迁移的业务页面清单
要废弃的旧代码清单
```

完成标准：

```text
知道第一批迁移哪些文件
知道登录接口怎么调用
知道 `SwiftExport.Contracts` 如何引用
知道旧主题哪些能直接搬
```

## 阶段 1：创建新 .NET 10 WPF 项目

目标：建立干净的新前端，而不是在旧 MVVMLight 项目里硬改。

建议项目名：

```text
SwiftExport.Client.Wpf
```

安装基础包：

```text
CommunityToolkit.Mvvm
Microsoft.Extensions.Hosting
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Configuration.Json
Microsoft.Extensions.Logging
Refit.HttpClientFactory
WPF-UI
```

完成标准：

```text
项目能编译
空窗口能启动
没有业务代码
没有旧 MVVMLight 依赖
```

## 阶段 2：接入 WPF UI 和主题

目标：先把现代外壳和主题加载跑通。

要做：

```text
引入 WPF UI 资源字典
创建 ShellWindow，继承或使用 FluentWindow
迁入现有 Themes / Styles
建立 Tokens.xaml、Colors.xaml、Typography.xaml
建立 Button/TextBox/DataGrid 基础样式文件
```

注意：

```text
不要同时混用官方 Fluent Theme 和 WPF UI 全量主题作为根主题
普通业务控件优先使用原生 Button/TextBox/DataGrid
WPF UI 主要用于 Shell、Navigation、Dialog、Snackbar
```

完成标准：

```text
ShellWindow 能显示
WPF UI 主题正常
旧主题资源无明显冲突
基础 Button/TextBox/DataGrid 可显示
```

## 阶段 3：建立 Host / DI / 配置

目标：让 WPF 项目具备现代 .NET 应用的启动结构。

要做：

```text
创建 AppHost
创建 ServiceCollectionExtensions
加载 appsettings.json
注册服务、ViewModel、Window、Page
接入 Logging
```

建议配置：

```json
{
  "Api": {
    "BaseUrl": "https://localhost:5001"
  },
  "Client": {
    "ApplicationName": "SwiftExport",
    "Version": "1.0.0"
  }
}
```

完成标准：

```text
App.xaml.cs 不再直接 new 大量对象
服务能从 DI 获取
配置能读取 Api:BaseUrl
日志服务可用
```

## 阶段 4：接入 SwiftExport.Contracts 和 API

目标：验证新 WPF 前端能直接使用现有 `SwiftExport.Contracts` 契约层。

要做：

```text
引用 `SwiftExport.Contracts` 项目
注册 Refit API 接口
配置 HttpClient BaseAddress
做一个最简单的 API 健康检查或用户信息接口调用
```

完成标准：

```text
`SwiftExport.Contracts` DTO 可用
`SwiftExport.Contracts` enum 可用
Refit API 可注册
WPF 能成功调用后端接口
API 异常能被捕获
```

## 阶段 5：实现版本检查

目标：为 ClickOnce 和多人协作迭代预留更新控制。

要建立：

```text
Updates/IClientVersionService.cs
Updates/ClientVersionService.cs
Updates/ClientVersionInfo.cs
Updates/UpdateRequirement.cs
```

后端版本接口建议返回：

```json
{
  "latestVersion": "1.0.3",
  "minimumSupportedVersion": "1.0.0",
  "downloadUrl": "\\\\server\\SwiftExport\\Client\\setup.exe",
  "releaseNotes": "修复订单导入问题"
}
```

完成标准：

```text
启动时能检查版本
低于最低版本能阻止进入
有新版本能提示
暂时不要求自动下载安装
```

## 阶段 6：实现登录 / 退出登录

目标：新前端从一开始就有正确的认证边界。

第一版要做：

```text
LoginWindow
LoginViewModel
IAuthService
AuthService
ITokenStore
LocalTokenStore
CurrentUserService
```

第一版功能：

```text
登录
退出登录
记住用户名
保存 token
启动时验证 token
API 请求带 Authorization
401 后回到登录页
```

暂缓功能：

```text
重置密码
修改密码
记住密码
多用户切换
复杂权限
```

完成标准：

```text
未登录只能看到登录窗体
登录成功进入 Shell
退出登录能清 token 并回到登录窗体
重启 App 能识别已有 token
token 失效能回到登录
```

## 阶段 7：实现 Shell / 菜单 / 导航

目标：建立管理端的基本使用框架。

要做：

```text
ShellWindow
ShellViewModel
NavigationItem
INavigationService
NavigationService
NavigationMenuProvider
```

第一版菜单：

```text
Dashboard
Products
Sales Orders
Packing Lists
Import Templates
Import History
Customers
Settings
```

原则：

```text
菜单先写在本地代码
暂时不从数据库读取菜单
NavigationView 可以用 WPF UI
业务页面 ViewModel 通过 DI 创建
```

完成标准：

```text
登录后显示 Shell
左侧菜单能显示
点击菜单能切换页面
当前页面状态能正确显示
退出登录按钮可用
```

## 阶段 8：迁移 Products 模块

目标：用一个相对清楚的模块验证整套前端架构。

推荐先迁 `Products`，不要先迁订单。

要做：

```text
ProductsPage
ProductListViewModel
ProductEditDialog
ProductEditViewModel
ProductApiService 或直接封装 `SwiftExport.Contracts` 中的 Refit API
```

功能范围：

```text
列表
搜索
刷新
新增
编辑
停用/删除
保存
错误提示
加载状态
```

完成标准：

```text
Products 模块能形成完整 CRUD 闭环
弹窗服务可用
通知服务可用
API 错误能显示
DataGrid 基础样式可用
```

## 阶段 9：接入 ClickOnce 测试发布

目标：尽早验证客户端部署链路。

要做：

```text
配置 ClickOnce 发布路径
发布到局域网共享目录或内网站点
在测试机安装
测试版本升级
测试坏版本前进式回滚
```

版本策略：

```text
普通更新：发布新版本
坏版本回滚：旧内容重新发布成更高版本
重大 API 变更：后端提高 minimumSupportedVersion
```

完成标准：

```text
测试机可以安装
测试机可以升级
坏版本可以通过新版本覆盖修复
客户端能读取后端版本策略
```

## 阶段 10：迁移订单、PackingList 和导入模块

目标：迁移核心业务模块，但不要把 Excel / VSTO 代码放进 .NET 10 WPF 工程。

推荐顺序：

```text
Import Templates
Import History
Sales Orders
Packing Lists
```

原因：

```text
模板和导入记录是后续 Excel / VSTO 对接的基础
订单和 PackingList 更复杂，应该在基础导入能力稳定后再做
```

完成标准：

```text
能维护导入模板
能查看导入记录
能查看订单
能查看 PackingList
能处理导入错误结果
```

## 阶段 11：完善权限、审计、异常和体验

目标：把 MVP 变成可长期使用的内部 ERP。

后续完善：

```text
权限菜单
用户管理
重置密码
修改密码
操作日志
全局异常捕获
API 401/403 统一处理
导入失败日志
DataGrid 高级筛选
列配置
用户偏好设置
主题切换
```

完成标准：

```text
不同用户可以有不同权限
关键操作可追踪
导入失败可定位
UI 使用体验稳定
频繁发布不会影响正常业务
```

## 推荐 MVP

第一版只做到这里就可以开始内部试用：

```text
.NET 10 WPF 新项目
WPF UI Shell
现有主题基础迁入
Host / DI / appsettings
`SwiftExport.Contracts` Refit API 可用
版本检查
登录 / 退出登录
Shell / 菜单 / 导航
Products 模块完整 CRUD
ClickOnce 测试发布
```

不要在 MVP 里塞太多：

```text
不要一开始做数据库动态菜单
不要一开始做复杂权限
不要一开始迁所有订单功能
不要一开始重做所有 DataGrid 高级能力
不要一开始重写 VSTO 插件；Excel / VSTO 作为 .NET Framework 项目单独规划
```

## 每次推进的工作方式

每个阶段都按这个节奏：

```text
1. 先定义完成标准
2. 再改代码
3. 跑 build
4. 手动打开验证关键路径
5. 写迁移记录
6. 再进入下一阶段
```

建议每次只让 Codex 做一个阶段，或者一个阶段里的一个闭环任务。这样项目会稳得多，也方便你审查。
