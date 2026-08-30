# Skill: Refit Contract Change

## When to use

当任务涉及 Shared DTO、Request、Response、Refit API、Controller 路由、接口返回结构或前后端契约同步时使用。

## Required reading

- `skills/backend/dotnet-webapi-template/skill.md`
- `guides/shared/refit-api-guide.md`
- `guides/shared/dto-guide.md`
- `guides/shared/service-result-guide.md`
- `guides/backend/webapi-controller-guide.md`

## Workflow

1. 先定位相关文件：Shared DTO、Request/Response、RefitApi、Controller、Application Service、前端调用方和绑定。
2. 先给计划，说明契约变化、调用方、兼容性和风险；除非用户明确要求直接执行，否则不要跳过计划。
3. 如果项目使用 YanWei WebAPI 默认模板，参考 `dotnet-webapi-template` 检查 Contracts/Apis 与 WebApi Controller 路由是否对应。
4. 判断是否影响 Shared、前端、后端、数据库和测试。
5. 修改 DTO 字段时同时检查序列化名称、前端绑定、Controller 入参、Service 映射和 Repository 读写。
6. 修改 Refit 路由时必须检查 Controller 路由和 HTTP 方法。
7. 修改接口返回结构时必须检查 ServiceResult、前端解析和错误处理。
8. 修改后运行 `dotnet build`；有契约/API/前端调用测试时运行相关测试。

## Rules

- Shared DTO / Request / Response / RefitApi 是前后端契约。
- 契约变更必须双向检查服务端和客户端。
- DTO 不放 UI 临时状态。
- 返回结构遵守项目 ServiceResult 或既有错误模型。
- 破坏性变更必须显式说明兼容风险。
- 默认 WebAPI 模板中，Contracts/Apis 的 Refit 路由应与 WebApi Controller 路由保持一致。

## Do not

- 不要只改 Shared 不改调用方。
- 不要只改 Controller 不改 Refit。
- 不要把 UI 临时状态放进 Shared DTO。
- 不要直接暴露 Entity。
- 不要无说明地改变字段名、字段类型、路由或返回结构。

## Validation

- 运行 `dotnet build`。
- 检查 Controller 路由和 Refit 路由一致。
- 检查前端绑定和 ServiceResult 解析。
- 有测试时运行 API 契约和调用方测试。
- TODO：待人工确认统一契约快照或 OpenAPI 检查方式。

## Output

- 汇报修改的 DTO、Request/Response、Refit、Controller 和调用方。
- 说明字段、路由、返回结构变化。
- 说明前端、后端、Shared 和数据库影响。
- 说明验证命令、结果和兼容性风险。
