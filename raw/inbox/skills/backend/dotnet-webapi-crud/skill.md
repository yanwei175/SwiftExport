# Skill: .NET Web API CRUD

## When to use

当任务涉及新增或修改 ASP.NET Core Web API CRUD、Controller、Application Service、Repository、DTO、主从表保存或接口返回结构时使用。

## Required reading

- `skills/backend/dotnet-webapi-template/skill.md`
- `skills/backend/backend-contexts/skill.md`
- `templates/dotnet-webapi/docs/backend-architecture.md`
- `guides/backend/webapi-controller-guide.md`
- `guides/backend/application-service-guide.md`
- `guides/backend/dapper-repository-guide.md`
- `guides/shared/service-result-guide.md`
- `guides/shared/dto-guide.md`

## Workflow

1. 先定位相关文件：Controller/Endpoint、Application Service、Repository、DTO、Shared 契约、前端调用方。
2. 先给计划，说明路由、请求/响应、业务层、数据层和兼容性影响；除非用户明确要求直接执行，否则不要跳过计划。
3. 判断是否影响 Shared、前端、后端、数据库和 Refit/API 客户端。
4. 新项目或模板同步时，先执行 `dotnet-webapi-template`，确认五层结构和默认显式 Service/Controller 路线。
5. 如果项目采用 WebAPI 默认架构，执行 `backend-contexts` Skill，确认 scoped `DbSession`、Service/Repository 上下文和事务边界。
6. 按 Controller -> Application Service -> Repository -> DTO 的分工实现 CRUD。
7. 主从表保存由 Application Service 编排，Repository 只执行数据访问；默认由 Service 通过 scoped `DbSession` 控制事务。
8. 修改接口返回结构时检查 ServiceResult、前端绑定和客户端解析。
9. 修改后运行 `dotnet build`；有 API/Service 测试时运行相关测试。
10. 输出修改文件、接口变化、调用方影响、风险和验证结果。

## Rules

- Controller 只负责 HTTP 边界和调用 Application Service。
- Application Service 负责业务用例和事务边界。
- Repository 负责数据访问。
- WebAPI 默认项目优先使用 scoped `DbSession + ServiceBaseContext + RepositoryBaseContext` 链路。
- 默认生成显式 Controller 和显式 Service，不默认生成 `BaseController`、`IBaseService`、`BaseService`。
- DTO/Request/Response 表达契约，不直接暴露 Entity。
- ServiceResult 或项目已有结果模型必须保持一致。
- 批量保存可以使用 `BatchRequestDto` / `BatchResultDto`，但由具体 Service 显式实现。

## Do not

- 不要在 Controller 写业务逻辑。
- 不要在 Controller 直接调用 Dapper 或 Repository 实现。
- 不要直接暴露 Entity 给 API 客户端。
- 不要只改后端接口而不检查 Shared 和前端影响。
- 不要无确认地改变公开路由、字段名或返回结构。
- 不要恢复泛型 BaseService / BaseController 默认链路。

## Validation

- 运行 `dotnet build`。
- 有测试时运行相关 Controller、Application Service、Repository 测试。
- 检查 Swagger/OpenAPI 或 Refit 契约是否需要同步。
- 用 `rg "BaseService|IBaseService|BaseController"` 确认未恢复默认泛型服务链。
- TODO：待人工确认本库未来统一 ServiceResult 和 Web API 模板。

## Output

- 汇报新增/修改的接口、DTO、Service、Repository。
- 说明 CRUD 和主从表保存流程。
- 说明 Shared、前端、数据库影响。
- 说明验证命令和结果。
- 标出兼容性风险和待人工确认项。
