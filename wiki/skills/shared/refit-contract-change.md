# Skill: SwiftExport Refit Contract Change

## Description

当任务涉及 `SwiftExport.Contracts` 中的 DTO、Request、Response、Refit API、枚举、统一返回结构，或涉及前后端 API 契约同步时使用本 Skill。

典型触发场景：

- 新增或修改 Refit API Interface。
- 新增或修改前端依赖 DTO。
- 修改 Controller 路由、HTTP 方法或返回结构。
- 调整 ServiceResult 或前端错误解析。
- 前端页面需要新的后端接口契约。

## Required Reading

- [DTO Guide](../../guides/shared/dto.md)
- [Refit API Guide](../../guides/shared/refit-api.md)
- [ServiceResult Guide](../../guides/shared/service-result.md)
- [WebApi Controller Guide](../../guides/backend/webapi-controller.md)
- 相关 Controller、Application Service、前端调用方和绑定代码。

## Workflow

1. 先定位相关 DTO、Refit API、Controller、Application Service、前端调用方和 XAML/ViewModel 绑定。
2. 判断契约变化是否影响字段名、字段类型、路由、HTTP 方法、返回包装、错误处理和兼容性。
3. 前端任务中可以写入 `SwiftExport.Contracts/**` 的 Refit API、DTO、枚举和通用结果包装；除非用户明确要求，不借机修改后端实现。
4. 修改 Refit 路由时必须核对 Controller 路由和 HTTP 方法。
5. 修改 DTO 字段时必须核对前端绑定、序列化名称、Controller 入参、Service 映射和 Repository 查询字段别名。
6. 修改返回结构时必须核对 ServiceResult、前端解析和错误展示。

## Rules

- `SwiftExport.Contracts` 是前后端共享契约，不承载 UI 临时状态。
- DTO 不直接暴露 Entity，也不放 WPF 控件、Excel COM 对象、窗口状态或弹窗逻辑。
- Refit Interface 的路由、HTTP 方法和参数必须与 WebApi Controller 保持一致。
- 破坏性契约变更必须在最终回复中说明兼容风险和受影响调用方。
- 如果后端接口不存在，前端任务只能定义契约需求或 DTO，不自行补 WebApi、Application、Infrastructure、Core。

## Validation

- 纯 Contracts 变更可优先运行相关项目构建。
- 同时改后端时运行 `dotnet build .\SwiftExport-WebAPI.slnx`。
- 涉及 WPF/VSTO 调用时，如环境具备，运行 `msbuild .\SwiftExport.slnx /p:Configuration=Debug /p:Platform=AnyCPU`。

## Output

最终回复用中文说明：

- 修改了哪些 DTO、Refit API、Controller 或前端调用方。
- 字段、路由、返回结构是否发生变化。
- 是否影响前端、后端、数据库或兼容性。
- 是否运行构建/测试；未运行时说明原因。
