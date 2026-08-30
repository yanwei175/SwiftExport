# Refit API Guide

## 适用场景

适用于使用 Refit 或类似声明式 HTTP Client 的 API 契约维护、字段变更和客户端同步。

## 推荐做法

- API 契约变更先确认服务端路由、HTTP 方法、请求模型和响应模型。
- Refit 接口应与服务端公开 API 保持一致。
- 默认 WebAPI 模板将 Refit 接口放在 `Contracts/Apis`，通用 CRUD 契约可参考 `IGenericApi<TDto>`。
- 破坏性字段变更需要兼容策略或版本说明。
- DTO 变更要同步检查前端页面、Application Service 和测试数据。
- 契约命名应表达业务意图，避免泄露数据库实现细节。

## 禁止事项

- 禁止只改服务端不改客户端契约。
- 禁止随意变更公开路由或字段名称。
- 禁止让 Refit 接口返回数据库 Entity。
- 禁止把认证、分页、错误模型写成每个接口各自一套。
- 禁止隐藏破坏性变更。

## Codex 修改前检查清单

- 服务端 API 和 Refit 接口是否都在当前任务范围内。
- `Contracts/Apis` 中的 Refit 路由是否与 WebApi Controller 路由一致。
- 是否影响现有调用方和部署版本。
- 错误返回、分页、认证头是否有统一约定。
- 是否需要新增兼容字段或保留旧接口。
- TODO：待人工确认本仓库未来 Refit 标准契约格式。

## 常见错误

- 忘记同步请求 DTO 和响应 DTO。
- Controller 路由和 Refit 属性不一致。
- 异常直接变成客户端不可解析的字符串。
- 分页参数命名不统一。
- 把内部审计字段暴露给客户端。
