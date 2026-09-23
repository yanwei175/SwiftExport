---
name: aspdotnet10-application-document-command
description: SwiftExport 复杂单据与业务命令技能。用于草稿、确认、取消确认、过账、反过账、作废、红冲、CommandId、ExpectedVersion、Preview、命令回执、业务审计、多仓储事务和稳定锁顺序；不用于只有单表 CRUD 的主数据维护。
---

# Complex Document / Business Command Skill

## 必读

- `../api-application-service/SKILL.md`
- `../application-service-context/SKILL.md`
- `../backend-file-organization/SKILL.md`
- `.agents/references/业务架构总纲.md`
- `.agents/references/数据模型与状态规则.md`
- 目标 Controller、Contracts API/DTO、Application Interface/Service、Entity、Repository、DI 和测试

## 什么时候使用

- 单据草稿、确认、取消确认、过账、反过账、作废、红冲或关闭。
- 使用 `CommandId`、`CommandItemId`、`ExpectedVersion`、Preview Token 或持久化执行结果。
- 一个命令需要锁定多个聚合、调用多个 Repository、生成库存或下游事实。
- 设计命令回执、业务日志、事务隔离级别、锁顺序和失败恢复。

## 核心规则

- `Version` 防止基于旧事实覆盖新事实；`CommandId` 标识可重试的业务意图；`CommandItemId` 标识命令内部不可重复的明细事实；`CorrelationId` 只追踪一次调用链。四者不得互相代替。
- 正式写命令必须使用持久化幂等和数据库约束；页面 Busy 与 `PreventDuplicateRequest` 只是体验补充。
- 相同命令身份和相同指纹返回原结果；相同身份用于不同用户、操作、聚合或内容必须返回幂等冲突。
- 命令回执保存机器可重放结果；`BusinessOperationLog` 保存人可读业务审计。除已登记兼容实现外，不新增依赖审计日志 JSON 承担唯一命令回执的设计。
- Preview 只读且不预留资源；正式提交必须在事务内重新锁定并重新验证，不能信任 Preview 后状态仍未变化。
- 普通查询不机械开启事务或使用 `Serializable`；写命令按真实一致性风险选择隔离级别并记录稳定锁顺序。
- 最外层 Application Service 拥有事务。内部端口不得提交、释放或清空外层事务；失败必须全量回滚，回滚清理异常不能覆盖原始失败。
- 权限检查先于历史结果重放；模块访问权限不能自动替代确认、过账、反向、作废等高风险细权限。
- 新命令使用稳定错误码和可选字段路径，不把中文消息、SQL 异常文本或异常类型作为客户端分支协议。
- Controller、Application、Repository 全链传递 `CancellationToken`；提交前响应取消，提交后的结果通过原 `CommandId` 查询或重放确认。

## 详细参考

- 设计或实现幂等、回执、审计和错误协议时读 `references/业务命令执行协议.md`。
- 设计 Preview、事务、隔离级别、锁集合和反向流程时读 `references/事务锁与预览规则.md`。
- 完成实现或审查时读 `../backend-quality-gates/SKILL.md`。

