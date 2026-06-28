# SwiftExport ERP Project Map

来源：整理自 `docs/ai/PROJECT_MAP.md`，后续以源码事实和本文件当前版本为准。

## 1. Repository Shape

SwiftExport 是 C# ERP 解决方案，主要包含：

- ASP.NET Core Web API 后端：`SwiftExport.WebApi`
- 后端分层：`SwiftExport.Application`、`SwiftExport.Core`、`SwiftExport.Infrastructure`、`SwiftExport.Contracts`
- WPF 桌面 UI 类库：`SwiftExport.View.FW`
- WPF 开发宿主：`SwiftExport.DevApp`
- Excel VSTO Add-in 宿主：`SwiftExport.VSTO`
- WPF 主题/控件库：`MateWpfThemes`
- SQL / 工具：`tools/sql`、`SwiftExport.Infrastructure/Migrations`、`_tools/DbSeed`

解决方案：

- `SwiftExport.slnx`：完整解决方案。
- `SwiftExport-WebAPI.slnx`：后端解决方案。

本仓库没有 JavaScript SPA 入口；这里的“前端”指 WPF / .NET Framework 桌面 UI。

## 2. Root Directory Map

| Path | Role |
| --- | --- |
| `AGENTS.md` | Codex 行为规则入口。 |
| `wiki/` | AI Wiki 索引和日志。 |
| `raw/` | AI 资产源：项目地图、skill、workflow。 |
| `docs/` | 历史文档和项目资料。 |
| `MateWpfThemes/` | WPF 主题/控件库。 |
| `SwiftExport.Application/` | 后端 Application 服务层。 |
| `SwiftExport.Contracts/` | DTO、Refit API、枚举、通用包装。 |
| `SwiftExport.Core/` | Entity、Repository Interface、ReadModel。 |
| `SwiftExport.DevApp/` | WPF 开发宿主。 |
| `SwiftExport.Infrastructure/` | Dapper、SQL Server、Repository、DbSession。 |
| `SwiftExport.View.FW/` | WPF 前端视图库。 |
| `SwiftExport.VSTO/` | Excel VSTO Add-in 宿主。 |
| `SwiftExport.WebApi/` | ASP.NET Core Web API。 |
| `tools/sql/` | 独立 SQL 脚本。 |
| `_tools/DbSeed/` | 数据库种子工具。 |

## 3. Frontend Entry Points

WPF UI 类库：

- `SwiftExport.View.FW/SwiftExport.View.FW.csproj`
- 不是可执行入口，不包含 `App.xaml`
- 由 `SwiftExport.DevApp` 或 `SwiftExport.VSTO` 承载

WPF 开发宿主：

- `SwiftExport.DevApp/App.xaml`
- `SwiftExport.DevApp/App.xaml.cs`
- `SwiftExport.DevApp/MainWindow.xaml`
- `SwiftExport.DevApp/MainWindow.xaml.cs`

Excel VSTO 宿主：

- `SwiftExport.VSTO/ThisAddIn.cs`
- `SwiftExport.VSTO/DependencyInjectionVSTO.cs`
- `SwiftExport.VSTO/SwiftExportTaskPane/**`

## 4. Backend Entry Points

后端入口：

- `SwiftExport.WebApi/Program.cs`

后端解决方案：

- `SwiftExport-WebAPI.slnx`

运行配置：

- `SwiftExport.WebApi/appsettings.json`
- `SwiftExport.WebApi/appsettings.Development.json`
- `SwiftExport.WebApi/Properties/launchSettings.json`

开发地址：

- `https://localhost:7086/swagger`
- `http://localhost:5057/swagger`

## 5. Layer Map

WebApi：

- `SwiftExport.WebApi/Controllers/**`
- `SwiftExport.WebApi/Middlewares/**`
- `SwiftExport.WebApi/Filters/**`

Application：

- `SwiftExport.Application/Interfaces/**`
- `SwiftExport.Application/Services/**`
- `SwiftExport.Application/Mappings/**`

Core：

- `SwiftExport.Core/Entities/**`
- `SwiftExport.Core/Interfaces/**`
- `SwiftExport.Core/ReadModels/**`

Infrastructure：

- `SwiftExport.Infrastructure/Data/DbSession.cs`
- `SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs`
- `SwiftExport.Infrastructure/Repositories/**`
- `SwiftExport.Infrastructure/DependencyInjection.cs`

Contracts：

- `SwiftExport.Contracts/Apis/**`
- `SwiftExport.Contracts/DTOs/**`
- `SwiftExport.Contracts/SysEnums/**`
- `SwiftExport.Contracts/Wrappers/**`
- `SwiftExport.Contracts/UiInterface/**`

## 6. Frontend Directory Map

`SwiftExport.View.FW` 中业务模块通常采用：

- `XxxViews/Views`
- `XxxViews/ViewModels`
- `XxxViews/Models`

重要基础设施：

- DI：`SwiftExport.View.FW/DI`
- HTTP：`SwiftExport.View.FW/Http`
- Routing：`SwiftExport.View.FW/Routing`
- MVVM：`SwiftExport.View.FW/Mvvm`
- Session / Dialog：`SwiftExport.View.FW/Services`
- Permission：`SwiftExport.View.FW/Security`

## 7. Request Flow

典型前后端调用链：

1. WPF ViewModel 调用 `SwiftExport.Contracts/Apis/**` 中的 Refit 接口。
2. `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs` 注册的 Refit Client 发送 HTTP 请求。
3. `SwiftExport.WebApi/Controllers/**` 接收请求。
4. `SwiftExport.Application/Services/**` 编排业务。
5. `SwiftExport.Core/Interfaces/**` 定义仓储接口。
6. `SwiftExport.Infrastructure/Repositories/**` 使用 Dapper 访问 SQL Server。
7. 连接和事务来自 `DbSession`。

