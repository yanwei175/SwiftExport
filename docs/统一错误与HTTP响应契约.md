# 统一错误与 HTTP 响应契约

Application 决定失败的业务含义，WebApi 决定 HTTP 状态，客户端读取结构化错误并保留用户输入。

## 文件夹职责

```text
SwiftExport.Contracts/
  DTOs/Errors/                  公共错误码、错误字段
  Wrappers/                    ServiceResult<T> 与非泛型结果视图
SwiftExport.WebApi/
  ErrorHandling/               唯一状态映射、失败包装、MVC 结果过滤器、全局异常
  Configuration/              认证失败、模型校验和请求管道接入
  RequestProtection/          防抖，返回 429 和 Retry-After
  Observability/              X-Correlation-ID 与日志关联
SwiftExport.Client.Wpf/
  Services/Api/               非 2xx 正文读取、网络和超时错误归类
```

Controller 不再单独维护错误码到 HTTP 的映射；Application、Core 和 Infrastructure 不引用 HTTP 状态。

## 公共分类

| 错误码 | HTTP | 含义 |
|---|---:|---|
| VALIDATION | 400 | 参数缺失或格式无效 |
| UNAUTHENTICATED | 401 | 缺少有效身份 |
| FORBIDDEN | 403 | 无操作权限 |
| NOT_FOUND | 404 | 目标不存在 |
| VERSION_CONFLICT | 409 | 版本冲突 |
| COMMAND_CONFLICT | 409 | 同一 CommandId 被不同内容占用 |
| STATE_BLOCKED | 409 | 当前状态不允许操作 |
| COMMAND_BUSY | 409 | 同一命令正在执行 |
| BUSINESS_BLOCKED | 422 | 业务规则阻断 |
| RATE_LIMITED | 429 | 请求过于频繁，查看 Retry-After |
| UNEXPECTED | 500 | 未预期的服务端错误 |
| SERVICE_UNAVAILABLE | 503 | 服务暂不可用 |
| OUTCOME_UNKNOWN | 503 | 无法确认命令结果 |

模块专属代码在 `ServiceErrorHttpMapper` 显式登记。不根据代码前缀或中文消息猜测；未登记代码进入安全的 500 出口并记录日志。

## 响应形状

```json
{
  "success": false,
  "message": "单据已变化，请刷新后重试。",
  "data": null,
  "error": {
    "code": "VERSION_CONFLICT",
    "message": "单据已变化，请刷新后重试。",
    "field": "ExpectedVersion"
  }
}
```

失败必须有 Error.Code，两个 Message 一致，Field 可为空。HTTP 出口清空失败 Data，包括值类型结果；逐来源阻断信息通过 Preview 查询。成功接口保持各自现有数据契约。

`Fail(message)` 明确表示 BUSINESS_BLOCKED；参数、权限、版本或技术故障必须使用带代码的工厂。技术异常详情仅写服务器日志。

认证、模型校验、防抖、未找到路由与未处理异常均接入相同包装。已开始的响应不能改写；客户端主动断开不再尝试写错误正文。网关或连接中断可能没有此 JSON，客户端必须有安全降级处理。

## 客户端处理

Refit 非 2xx 会抛 ApiException，公共读取器从正文还原 Code、Message 和 Field。409 是明确冲突，不一律当作网络故障，也不一律等于版本冲突。

500、503、超时或断线不能证明写入未提交。入库、出库和箱单保留原 CommandId 与原请求内容供确认或重试；不自动生成新 CommandId 重放。错误出口不负责业务幂等，幂等仍由模块事务和持久化结果保证。

## 验证范围

HTTP 集成测试启动独立本地端点，验证真实 MVC 校验、认证、过滤器和异常管道，不连接业务数据库。客户端测试验证非 2xx 解析、损坏正文降级，以及单据待确认命令的保留。数据库回滚与持久化回执仍由业务模块专项测试验证。
