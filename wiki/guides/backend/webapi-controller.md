# Web API Controller Guide

## 适用场景

用于 ASP.NET Core Web API Controller、HTTP 路由、请求模型和响应模型维护。

## SwiftExport 规则

- Controller 只负责 HTTP 输入输出、参数绑定、认证授权入口和调用 Application Service。
- 复杂业务流程放到 Application Service。
- 请求模型和响应模型优先使用 `SwiftExport.Contracts` 中的 DTO。
- Controller 路由必须与 `SwiftExport.Contracts/Apis/**` 中 Refit 接口保持一致。
- 错误格式、分页格式、认证授权应遵循项目既有中间件和返回模型。

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

