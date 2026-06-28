# Skill: SwiftExport 后端 Dapper Repository 开发规范

## Description

当任务涉及 SwiftExport ERP 后端 Dapper 仓储层时使用本 Skill。

典型触发语句包括：

- “新增 Repository”
- “修改仓储”
- “写 Dapper 查询”
- “写 SQL 查询”
- “实现分页查询”
- “实现批量保存”
- “注册仓储 DI”
- “修复 Repository 报错”
- “新增后端数据访问”
- “改 Infrastructure/Repositories”
- “改 Core/Interfaces”

本 Skill 依赖基础 Skill：

- `wiki/skills/backend/db-session-context.md`

补充 Guide：

- [Application Service Guide](../../guides/backend/application-service.md)
- [Dapper Repository Guide](../../guides/backend/dapper-repository.md)
- [Database Schema Guide](../../guides/backend/database-schema.md)
- [DTO Guide](../../guides/shared/dto.md)

## Instructions

### 1. 先读项目上下文

开始编码前，必须先阅读：

- `AGENTS.md`
- `wiki/index.md`
- `wiki/project/project-map.md`
- `wiki/project/onboarding.md`
- `wiki/skills/backend/db-session-context.md`
- `SwiftExport.Infrastructure/DependencyInjection.cs`
- `SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs`
- `SwiftExport.Infrastructure/Data/DbSession.cs`
- `SwiftExport.Infrastructure/Repositories/Base/DapperRepository.cs`

如果是修改某个业务模块，还必须阅读：

- 目标模块已有 Repository
- 对应 `IxxxRepository`
- 对应 Entity
- 对应 Application Service
- 对应 DTO 或 ReadModel
- 对应 Controller

> 中文注释：本项目仓储模式已经存在，新增代码必须贴合现有写法，不能重新发明数据访问框架。

### 2. 分层边界

Repository 接口放在：

- `SwiftExport.Core/Interfaces`

Repository 实现放在：

- `SwiftExport.Infrastructure/Repositories/<业务模块>`

实体放在：

- `SwiftExport.Core/Entities/<业务模块>`

DTO 放在：

- `SwiftExport.Contracts/DTOs/<业务模块>`

Application Service 放在：

- `SwiftExport.Application/Services/<业务模块>`

Controller 放在：

- `SwiftExport.WebApi/Controllers/<业务模块>`

严格禁止：

- Controller 直接写 SQL。
- Controller 直接依赖 Infrastructure 具体实现。
- Application Service 直接创建 `SqlConnection`。
- Core 引用 Infrastructure。
- Infrastructure 引用 Application。
- Repository 处理 UI 逻辑。
- Repository 硬编码连接字符串。

### 3. 新增 Repository 标准步骤

新增 Repository 时，按以下顺序执行：

1. 确认 Entity 是否已存在于 `SwiftExport.Core/Entities/**`。
2. 在 `SwiftExport.Core/Interfaces` 新增或扩展 `IxxxRepository`。
3. 在 `SwiftExport.Infrastructure/Repositories/<Module>` 新增 `XxxRepository`。
4. 能复用 `DapperRepository<TEntity>` 时优先继承它。
5. 构造函数优先依赖 `RepositoryBaseContext`。
6. 查询使用当前 `Connection`。
7. 事务使用当前 `Transaction`。
8. 在 `SwiftExport.Infrastructure/DependencyInjection.cs` 注册接口与实现。
9. 在 Application Service 中注入仓储接口。
10. 如新增 API 返回结构，优先使用 `SwiftExport.Contracts/DTOs` 中的 DTO。
11. 最后运行后端构建命令验证。

推荐仓储骨架：

```csharp
using Dapper;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

public class XxxRepository : DapperRepository<XxxEntity>, IXxxRepository
{
    public XxxRepository(RepositoryBaseContext context) : base(context)
    {
    }

    public async Task<XxxEntity?> GetByNoAsync(string documentNo)
    {
        // 中文注释：所有外部输入必须通过参数传入，禁止直接拼接到 SQL 字符串中。
        const string sql = @"
SELECT
    Uid,
    DocumentNo,
    CreatedDate,
    UpdatedDate
FROM dbo.XxxTable
WHERE DocumentNo = @DocumentNo
  AND ISNULL(Deleted, 0) = 0;";

        return await Connection.QueryFirstOrDefaultAsync<XxxEntity>(
            sql,
            new { DocumentNo = documentNo },
            transaction: Transaction);
    }
}
```

### 4. 修改 Repository 安全检查

修改已有 Repository 前先检查：

- 该方法是否被多个 Application Service 调用。
- 返回类型是 Entity、DTO 还是 ReadModel。
- SQL 字段别名是否与返回模型属性匹配。
- 是否依赖软删除字段，例如 `Deleted`、`IsDeleted`。
- 是否依赖审计字段，例如 `CreatedDate`、`CreatedBy`、`UpdatedDate`、`UpdatedBy`。
- 是否运行在当前 `DbSession.Transaction` 中。
- 是否需要同步修改 DTO、ViewModel、Mapping、Controller 或 Refit 接口。
- 是否正在恢复或沿用某个通用模板中的 QueryCriteria / BuildConditionSql；只有当前模块已有这种模式时才继续沿用，不要把模板工具类强行引入 SwiftExport。

修改后必须检查：

- 参数化查询是否完整。
- 是否引入 SQL 注入风险。
- 是否破坏分页排序稳定性。
- 是否破坏现有批量保存逻辑。
- 是否忘记传入 `transaction: Transaction`。

### 5. Dapper SQL 编写规范

必须遵守：

- 使用参数化查询。
- 禁止把用户输入拼进 SQL。
- 列表查询不要使用 `SELECT *`。
- DTO/ReadModel 查询必须显式写字段别名。
- SQL Server 表名建议带 `dbo.` 前缀。
- 分页查询必须有稳定 `ORDER BY`。
- `IN @Ids` 类型查询要确认 Dapper 参数是集合。
- 复杂查询要在 SQL 前写中文注释解释业务意图。

分页查询示例：

```csharp
// 中文注释：分页查询必须使用稳定排序，避免翻页时因为数据库默认顺序变化导致重复或漏行。
const string sql = @"
SELECT
    h.Uid,
    h.OrderNo,
    h.CustomerCode,
    h.OrderDate
FROM dbo.SalesOrderHead h
WHERE ISNULL(h.Deleted, 0) = 0
ORDER BY h.OrderDate DESC, h.Uid DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
```

动态排序示例：

```csharp
// 中文注释：排序字段只能来自白名单，避免前端传入任意 SQL 片段。
var orderBy = sortField switch
{
    "OrderDate" => "h.OrderDate",
    "OrderNo" => "h.OrderNo",
    _ => "h.Uid"
};
```

动态条件规则：

- 动态 SQL 只允许拼接由代码生成的白名单字段、排序和固定片段。
- 用户输入只能作为 Dapper 参数传入。
- 可选查询条件优先使用局部 `whereParts` / `DynamicParameters` 等简单结构，除非当前模块已有统一查询对象。
- 不为了单个查询引入大范围查询 DSL 或模板化 QueryCriteria。

### 6. 事务规则

事务规则必须遵循：

- `wiki/skills/backend/db-session-context.md`

本项目仓储事务优先使用当前 `DbSession.Transaction`。

开启事务：

```csharp
// 中文注释：在当前 DbSession 上开启事务，后续仓储操作共享同一个事务上下文。
_session.Transaction = _session.Connection.BeginTransaction();
```

Dapper 执行时传入事务：

```csharp
await Connection.ExecuteAsync(sql, parameters, transaction: Transaction);
```

不要默认把 UoW 写成首选方案。只有当前模块已有明确 UoW 模式时，才沿用该模块写法。

### 7. 审计字段规则

新增或更新数据前，检查目标 Entity 是否继承 `BaseEntity` 或包含审计字段。

相关文件：

- `SwiftExport.Core/Common/BaseEntity.cs`
- `SwiftExport.Core/Interfaces/Auth/IAuditStampService.cs`
- `SwiftExport.Infrastructure/Services/Auth/AuditStampService.cs`
- `SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs`

规则：

- 新增数据时按现有模式填充创建人、创建时间。
- 修改数据时按现有模式填充更新人、更新时间。
- 不要在 Controller 中手工打审计字段。
- 优先复用 `RepositoryBaseContext.AuditStamp`。

### 8. DTO / Entity / ReadModel 边界

Entity：

- 放在 `SwiftExport.Core/Entities`
- 对应数据库核心表结构或领域对象
- 不要为了某个页面临时拼接字段污染 Entity

DTO：

- 放在 `SwiftExport.Contracts/DTOs`
- 用于前后端传输
- Refit API 和 Controller 返回优先使用 DTO

ReadModel：

- 放在 `SwiftExport.Core/ReadModels`
- 用于后端内部只读查询模型

本项目约定：

- 如果查询结果是前端/API 明确需要的传输结构，可以直接使用 Contracts DTO。
- 不要为了只读查询强行新增没有复用价值的 Entity。
- Repository 返回 DTO 时，SQL 字段别名必须严格匹配 DTO 属性。

### 9. DI 注册规则

新增 Repository 后，必须在以下文件注册：

- `SwiftExport.Infrastructure/DependencyInjection.cs`

注册方式：

```csharp
// 中文注释：仓储接口统一注册为 Scoped，确保同一次请求内共享 DbSession 与事务上下文。
services.AddScoped<IXxxRepository, XxxRepository>();
```

不要注册为 Singleton。

不要在 WebApi 层重复注册 Infrastructure Repository。

### 10. 禁止事项

严禁：

- 引入 EF Core 替代 Dapper。
- 把 SQL 写到 Controller。
- 把连接字符串写进 Repository。
- 直接拼接用户输入生成 SQL。
- 在 Repository 中 new `SqlConnection` 绕过 `DbSession`。
- 在 Infrastructure 引用 Application。
- 在 Core 引用 Infrastructure。
- 在 Repository 中处理 WPF UI 状态。
- 为了一个查询大面积重构现有仓储基类。
- 删除用户已有注释或未提交改动。

### 11. 验证命令

优先运行后端构建：

```powershell
dotnet build .\SwiftExport-WebAPI.slnx
```

如只改了单个后端项目，也可以运行：

```powershell
dotnet build .\SwiftExport.WebApi\SwiftExport.WebApi.csproj
```

### 12. 最终回复格式

任务完成后，用中文按以下格式汇报：

```text
已完成：
- 修改了哪些 Repository / Interface / DI / Service。
- 新增或调整了哪些 SQL。
- 是否使用 RepositoryBaseContext + DbSession.Transaction。
- 是否涉及 DTO / Entity / ReadModel 边界。
- 是否运行 build/test。

注意：
- 如未运行验证命令，说明原因。
- 如发现连接字符串、编码、历史未提交改动等风险，简要提示。
```
