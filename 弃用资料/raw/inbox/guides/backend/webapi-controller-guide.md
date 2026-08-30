# Web API Controller Guide

## 适用场景

适用于 ASP.NET Core Web API 的 Controller、Endpoint 或类似 HTTP 入口层。

## 推荐做法

- Controller 只负责 HTTP 输入输出、参数绑定、认证授权入口和调用 Application Service。
- 请求模型和响应模型保持明确，避免直接暴露数据库实体。
- 复杂业务流程放到 Application Service。
- HTTP 状态码、错误格式、分页格式应遵循项目现有约定。
- DTO、Refit 契约和前端调用方可能受影响时，先列出兼容性影响。

## 禁止事项

- 禁止 Controller 直接写 SQL 或调用 Dapper。
- 禁止 Controller 直接管理数据库事务。
- 禁止把业务规则堆在 Action 方法里。
- 禁止随意改变公开路由、请求字段和响应字段。
- 禁止把内部异常堆栈直接返回给客户端。

## Codex 修改前检查清单

- 路由、HTTP 方法、认证授权、请求/响应模型是否已确认。
- 是否已有统一返回模型、异常处理中间件或验证方式。
- 是否影响现有客户端、Refit 接口或前端页面。
- 是否需要兼容旧字段、旧路由或旧分页格式。
- TODO：待人工确认本仓库未来是否采用统一 Web API 模板。

## 常见错误

- 为了快速实现让 Controller 同时承担校验、业务和持久化。
- 返回 Entity 导致内部字段泄漏。
- PATCH/PUT/POST 语义混乱。
- 错误码和错误消息风格不一致。
- 忘记同步更新客户端契约。
