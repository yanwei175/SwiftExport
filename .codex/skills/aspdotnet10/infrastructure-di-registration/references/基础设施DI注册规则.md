# 基础设施 DI 注册规则

## 当前注册入口

`DependencyInjection.cs` 位于 `SwiftExport.Infrastructure/DependencyInjection.cs`。

入口方法：

```csharp
public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
```

由 WebApi `Program.cs` 调用。

## 核心注册

当前基础注册：

- `services.AddLogging()`
- `IDbConnectionFactory` 由 `PostgreSqlConnectionFactory` 从配置创建 PostgreSQL 连接，`DbSession` 使用显式工厂注册
- `IDbSession` 用 `GetRequiredService<DbSession>()` 注册别名，保证共享实例与事务
- `IDatabaseExceptionClassifier` 使用 `PostgreSqlExceptionClassifier` 无状态分类器
- `services.AddScoped<RepositoryBaseContext>()`
- `services.AddScoped<IAuditStampService, AuditStampService>()`
- `services.AddScoped(typeof(IRepository<>), typeof(PostgreSqlDapperRepository<>))`

## 业务 Repository 注册

新增业务 Repository 时必须同时具备：

- Core 中的 Repository Interface。
- Infrastructure 中的 Repository 实现。
- `DependencyInjection.cs` 中 scoped 注册。

示例：

```csharp
services.AddScoped<IProductRepository, PostgreSqlProductRepository>();
```

## 依赖包

`SwiftExport.Infrastructure.csproj` 当前关键依赖：

- `Dapper`
- `Npgsql`
- `Microsoft.Extensions.Logging`

新增包前先确认是否已有项目模式可复用，不为单个 Repository 引入重量级依赖。

## 禁止事项

- 禁止 Infrastructure 注册 Application Service。
- 禁止 Infrastructure 引用 WebApi 具体类型。
- 禁止把 `DbSession` 注册为 singleton。
- 禁止遗漏 Repository 注册后只在构造函数里硬 new。
