# Refit API Guide

## 适用场景

用于维护 `SwiftExport.Contracts/Apis/**` 中的 Refit API 契约、路由、请求模型和响应模型。

## SwiftExport 规则

- Refit 接口应与 WebApi Controller 路由、HTTP 方法、请求模型和响应模型保持一致。
- 新增 API 接口后，前端需要在 `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs` 注册。
- DTO 变更要同步检查前端页面、Application Service、Controller 和测试数据。
- 契约命名表达业务意图，避免泄露数据库实现细节。
- 破坏性字段变更需要兼容策略或明确说明影响范围。

## 修改前检查

- 服务端 API 和 Refit 接口是否都在当前任务范围内。
- 是否影响现有调用方和部署版本。
- 错误返回、分页、认证头是否有统一约定。
- 是否需要新增兼容字段或保留旧接口。

## 禁止事项

- 禁止只改服务端不改客户端契约。
- 禁止随意变更公开路由或字段名称。
- 禁止让 Refit 接口返回数据库 Entity。
- 禁止隐藏破坏性变更。

