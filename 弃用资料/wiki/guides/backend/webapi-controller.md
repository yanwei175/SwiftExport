# Web API Controller Guide

## 适用场景

用于 ASP.NET Core Web API Controller、HTTP 路由、请求模型和响应模型维护。

## SwiftExport 规则

- Controller 只负责 HTTP 输入输出、参数绑定、认证授权入口和调用 Application Service。
- 复杂业务流程放到 Application Service。
- 请求模型和响应模型优先使用 `SwiftExport.Contracts` 中的 DTO。
- Controller 路由必须与 `SwiftExport.Contracts/Apis/**` 中 Refit 接口保持一致。
- 错误格式、分页格式、认证授权应遵循项目既有中间件和返回模型。
- Controller 不再继承泛型 `BaseController<TEntity,TDto>`。每个 Controller 应显式声明自己的 `[HttpGet]`、`[HttpPost]`、`[HttpPut]`、`[HttpDelete]` action，保留既有公开路由形状。
- `PreventDuplicateRequest` 只加在具体写操作 action 上，例如新增、修改、删除、批量保存和业务命令。

## 修改前检查

- 路由、HTTP 方法、认证授权、请求/响应模型是否已确认。
- 是否影响现有客户端、Refit 接口或前端页面。
- 是否需要兼容旧字段、旧路由或旧分页格式。

## 禁止事项

- 禁止 Controller 直接写 SQL 或调用 Dapper。
- 禁止 Controller 直接管理数据库事务。
- 禁止把业务规则堆在 Action 方法里。
- 禁止随意改变公开路由、请求字段和响应字段。
- 禁止把内部异常堆栈直接返回给客户端。
- 禁止通过泛型 Controller 基类隐式暴露某个业务并不支持的 CRUD、分页或批量保存接口。

