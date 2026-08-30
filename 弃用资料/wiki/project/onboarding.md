# SwiftExport ERP Onboarding

## 1. What To Open First

后端任务：

1. `wiki/index.md`
2. `wiki/skills/backend/db-session-context.md`
3. `wiki/skills/backend/dapper-repository.md`
4. `SwiftExport-WebAPI.slnx`
5. `SwiftExport.WebApi/Program.cs`
6. `SwiftExport.Application/DependencyInjection.cs`
7. `SwiftExport.Infrastructure/DependencyInjection.cs`
8. 目标模块 Controller / Service / Repository / DTO / Entity

前端任务：

1. `wiki/index.md`
2. `wiki/workflows/frontend/wpf-vsto-workflow.md`
3. `SwiftExport.slnx`
4. `SwiftExport.DevApp/App.xaml.cs`
5. `SwiftExport.DevApp/MainWindow.xaml.cs`
6. `SwiftExport.View.FW/DI/DependencyInjection.cs`
7. `SwiftExport.View.FW/DI/DependencyInjection.Pages.cs`
8. 目标模块 `Views` / `ViewModels` / `Models`

API 契约任务：

1. `SwiftExport.Contracts/Apis`
2. `SwiftExport.Contracts/DTOs`
3. `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs`
4. 匹配的后端 Controller

## 2. Build And Run

后端构建：

```powershell
dotnet build .\SwiftExport-WebAPI.slnx
```

后端运行：

```powershell
dotnet run --project .\SwiftExport.WebApi\SwiftExport.WebApi.csproj --launch-profile https
```

完整解决方案构建：

```powershell
msbuild .\SwiftExport.slnx /p:Configuration=Debug /p:Platform=AnyCPU
```

WPF DevApp：

- 推荐从 Visual Studio 设置 `SwiftExport.DevApp` 为启动项目。
- 构建后可运行 `.\SwiftExport.DevApp\bin\Debug\SwiftExport.DevApp.exe`。

VSTO：

- 推荐从 Visual Studio 调试 `SwiftExport.VSTO`，确保 Excel / VSTO 工具链可用。

数据库种子：

```powershell
dotnet run --project .\_tools\DbSeed\DbSeed.csproj
```

## 3. Development Notes

新增或修改后端 API 常见触点：

1. `SwiftExport.Core` Entity / ReadModel / Repository Interface。
2. `SwiftExport.Infrastructure` Repository。
3. `SwiftExport.Application` Service。
4. `SwiftExport.WebApi` Controller。
5. `SwiftExport.Contracts` DTO / Refit API。
6. `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs` 前端 API 注册。

新增 WPF 页面常见触点：

1. 正确业务模块下新增 View。
2. 新增或复用 ViewModel。
3. 新增 UI Model 或 Mapping。
4. 注册到 `DependencyInjection.Pages.cs`。
5. 如由路由打开，检查 `Routing/ViewRoutes.cs` 或 DB 路由配置。

新增 Repository 常见触点：

1. `SwiftExport.Core/Interfaces` 新增接口。
2. `SwiftExport.Infrastructure/Repositories/<Module>` 新增实现。
3. `SwiftExport.Infrastructure/DependencyInjection.cs` 注册。
4. Application Service 注入接口。

## 4. Current Risks

- 前端 API BaseUrl 在 `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs` 中硬编码为本地开发地址。
- 配置或工具文件中可能存在本地 SQL Server 连接字符串和凭据，不要复制到回复或公开文档。
- 中文注释如有编码异常，修改时保持 UTF-8。
- 当前未发现独立 xUnit/NUnit/MSTest 自动化测试项目。
- 完整解决方案包含 .NET Framework WPF/VSTO 项目，命令行构建依赖本机 Visual Studio/MSBuild/VSTO 工作负载。
- 后端目标框架为 `net10.0`，本机 SDK 版本会影响构建。
