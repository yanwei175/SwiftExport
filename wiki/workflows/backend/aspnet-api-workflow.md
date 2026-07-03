# ASP.NET 后端 API 工作流

## 适用场景

用于新增或调整 SwiftExport 后端 API、Application Service、Controller、Contracts DTO / Refit 接口、Repository 接口和 Dapper 查询。

典型触发语句：

- “新增 API”
- “新增后端接口”
- “给前端提供查询接口”
- “新增 Controller action”
- “新增 Application Service 方法”
- “复杂联表查询返回给前端”
- “新增 Repository 接口或 Dapper 实现”

## 开发顺序

新增 API 默认按以下顺序推进：

1. 先确认业务场景和模块边界，必要时读取业务总纲、领域模块总纲、API 契约总纲和数据模型总纲。
2. 设计 `SwiftExport.Contracts` 中的 DTO、Request、Response、Refit API 和前后端共享枚举。
3. 在 `SwiftExport.Application/Interfaces` 中声明 Application Service 接口方法。
4. 在 `SwiftExport.Application/Services/<Module>` 中实现业务用例。
5. 优先复用现有 Core Repository 接口；只有现有接口无法表达业务意图、复杂联表查询或性能需要时，才新增专用 Repository 接口。
6. 如需新增仓储能力，在 `SwiftExport.Core/Interfaces` 声明接口，在 `SwiftExport.Infrastructure/Repositories/<Module>` 用 Dapper 实现。
7. 在 `SwiftExport.WebApi/Controllers/<Module>` 增加 Controller action，仅做 HTTP 边界转发。
8. 如前端需要调用，同步或新增 `SwiftExport.Contracts/Apis/**` Refit 接口。
9. 按改动范围运行后端、Contracts 或完整解决方案构建。

## 分层职责

### Contracts

`SwiftExport.Contracts` 是前后端共享契约层：

- API Request、Response、DTO 放在 `SwiftExport.Contracts/DTOs/**`。
- Refit Interface 放在 `SwiftExport.Contracts/Apis/**`。
- 前端需要共享的枚举放在 `SwiftExport.Contracts`，不要放到 Core。
- 前后端可见的只读联表模型优先设计为 Contracts DTO。
- DTO 不承载 UI 状态、持久化规则或业务流程状态机本身。

### Application

`SwiftExport.Application` 是后端业务用例中心：

- 所有重度业务逻辑、校验、事务、状态流转和跨仓储编排都放在 Application Service。
- 新 API 先在 `Interfaces` 中声明服务方法，再在 `Services` 中实现。
- Service 调用 Core Repository 接口，不直接依赖 Infrastructure 实现。
- Service 不写 SQL、不创建 `SqlConnection`、不处理 WPF UI 状态。
- Entity 到 DTO 的复杂映射优先放在既有后端 Mapster 配置中，不散落在 Controller、Repository 或临时代码里。

### Core

`SwiftExport.Core` 是领域实体和后端抽象层：

- Entity 放在 `SwiftExport.Core/Entities/**`。
- Repository Interface 放在 `SwiftExport.Core/Interfaces/**`。
- Core 默认不新增前端/API 可见的只读模型；前端要看的只读模型放 Contracts DTO。
- Core 不负责前端需要的枚举。
- 纯后端内部使用、不会暴露给 API 或前端的 ReadModel，可以按已有源码模式谨慎保留或新增。

### Infrastructure

`SwiftExport.Infrastructure` 是 Dapper 和外部资源实现层：

- Repository 实现放在 `SwiftExport.Infrastructure/Repositories/<Module>`。
- Dapper 查询优先使用 `RepositoryBaseContext`、当前 `Connection` 和 `DbSession.Transaction`。
- 联表查询如果直接服务 API/前端，可以直接返回 Contracts DTO；SQL 字段别名必须匹配 DTO 属性。
- Infrastructure 不引用 Application，不处理业务流程编排，不硬编码连接字符串。

### WebApi

`SwiftExport.WebApi` 是 HTTP 边界层：

- Controller 只负责路由、模型绑定、认证授权边界、调用 Application Service 和返回结果。
- Controller 不写业务逻辑、不写 SQL、不直接依赖 Infrastructure Repository。
- Controller 路由、HTTP 方法和返回结构必须与 Refit / API 契约保持一致。

## Repository 决策规则

新增或修改 API 时，先问：

1. 现有 Application Service 是否已经提供此业务能力。
2. 现有 Repository 接口是否能支撑查询或写入。
3. 是否只是 DTO / Mapping / Controller / Refit 契约缺口。
4. 是否确实需要复杂联表、分页、聚合或性能优化。

只有第 4 点成立，或现有接口无法清楚表达业务意图时，才新增专用 Repository 方法或 Repository 接口。

新增复杂查询时：

- 查询结果给前端或 API 使用：优先返回 Contracts DTO。
- 查询结果只用于后端内部决策：可返回 Entity、值对象或内部 ReadModel。
- 不为了页面显示字段污染 Entity。
- 不把复杂查询塞回通用泛型 Repository。

## Mapster 与 DTO 规则

- Dapper SQL 字段已能直接映射 Contracts DTO 时，不需要额外 Mapster。
- Entity / DTO 之间存在复杂转换、字段重命名、忽略审计字段或组合字段时，使用既有 Mapster 配置。
- 后端 Mapster 配置优先放在 `SwiftExport.Application/Mappings/**`。
- 前端 DTO 与 UI Model / LineModel 映射放在 `SwiftExport.View.FW/Mappings/**`。
- 不在 Controller、Repository、ViewModel 中注册全局 Mapster 配置。

## 验证

按影响范围选择验证命令：

```powershell
dotnet build .\SwiftExport-WebAPI.slnx
```

如果改动了 Contracts 并影响前端 Refit 调用，优先补充：

```powershell
msbuild .\SwiftExport-Frontend.slnx /p:Configuration=Debug /p:Platform=AnyCPU
```

如果改动跨越后端、Contracts、前端或项目文件，运行：

```powershell
msbuild .\SwiftExport.slnx /p:Configuration=Debug /p:Platform=AnyCPU
```

## 禁止事项

- 禁止把业务逻辑写进 Controller。
- 禁止 Controller 或 ViewModel 绕过 Application Service 直接调用 Repository。
- 禁止 Application Service 直接写 Dapper SQL。
- 禁止 Infrastructure 引用 Application。
- 禁止为了前端显示在 Core 新增前端可见枚举或临时 ReadModel。
- 禁止把前端 UI 状态写入 DTO。
- 禁止为单个查询恢复大而全的泛型 Service、泛型 Controller 或全局复杂 Repository 模板。
