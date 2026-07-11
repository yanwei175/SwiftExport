# Contracts 兼容性规则

## 当前源码事实

`SwiftExport.Contracts.csproj`：

```xml
<TargetFramework>netstandard2.0</TargetFramework>
<PackageReference Include="Refit" Version="10.0.1" />
<PackageReference Include="System.Buffers" Version="4.6.1" />
```

该项目同时服务：

- WebApi Controller 和 Application Service。
- Infrastructure Dapper 只读 DTO 映射。
- 当前 .NET 10 WPF 前端。
- 可能存在的旧客户端兼容引用。

## 语法限制

允许：

- 普通 `class`。
- 普通 `enum`。
- 传统块级 namespace。
- `Task<T>`。
- `List<T>` 初始化为 `new List<T>()`。
- 表达式成员在现有代码中已使用，可谨慎沿用。

禁止新增：

- `record` / `record struct`。
- `init` setter。
- `required`。
- `string?` 等 nullable reference type 标注。
- file-scoped namespace。
- global using。
- collection expression，如 `[]`。
- primary constructor。

## 分层边界

- DTO 不返回 Entity。
- DTO 不承载 UI 状态，如 `IsSelected`、`IsDirty`、控件对象。
- 枚举放 Contracts，供前后端共用。
- Refit API 放 Contracts，必须与 WebApi Controller 路由一致。
- 通用响应和批量保存包装放 Wrappers。

## 验证

修改 Contracts 后优先运行：

```powershell
dotnet build .\SwiftExport-WebAPI.slnx
dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj
```

如果只改契约但不改调用方，最终回复说明未做联动验证的风险。
