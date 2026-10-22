---
name: backend-document
description: SwiftExport 后端单据写入的技术形状。用于草稿保存/删除、状态命令、库存类过账/反向、CommandId 幂等、事务与锁顺序。只提供技术规则，不定义任何业务字段、状态值或数量规则。
---

# 单据写入（技术形状）

业务规则（状态有哪些、能否流转、数量怎么算、哪些字段关联）一律以 `.agents/references` 和真实实体为准；本技能不提供、也不推断业务规则。

## 先读

- [CommandExecutor](../../../../Handoff/Claude/ReferenceDesign/src/SwiftExport.Application/Common/CommandExecutor.md)、[CommandReceiptStore](../../../../Handoff/Claude/ReferenceDesign/src/SwiftExport.Infrastructure/Persistence/CommandReceiptStore.md)、[Operations](../../../../Handoff/Claude/ReferenceDesign/src/SwiftExport.Application/Common/Operations.md)
- 形状样板（**示意业务，非真实规则**）：`ReferenceDesign/src/*/Procurement/PurchaseOrders/*`、`Inventory/PurchaseReceipts/*`

## 三种写入形状

| 写入类型 | 事务 | 幂等 | 并发控制 |
|---|---|---|---|
| 普通保存/删除 | `uow.ExecuteAsync` | 默认不用 | `Version` 条件更新，冲突 409 |
| 正式状态命令 | `CommandExecutor.ExecuteIdempotentAsync` | `command_receipt` | 目标行 `FOR UPDATE` + `Version` |
| 改变下游事实的命令（库存、回写来源） | 同上 | 同上 | 固定锁顺序：本单 → 关联单（按 Id 升序）→ 汇总行（按键升序） |

某个普通保存是否需要幂等（例如重复执行会产生重复单据），由业务文档决定；需要时改用第二种形状，不另造机制。

## CommandId

- 放在请求体，不用 HTTP Header；客户端每次操作新生成，重试复用。
- 指纹只含用户意图，不含 `CommandId`、不含整个实体。
- 重放返回首次结果，不执行业务。

## 预览 / 预检

- 不做“预览 Token / 预览快照重放 / 预览与提交一致性校验”。正式命令在事务内校验即可，失败返回 422 及原因。
- 不为正式命令单独提供“预检”接口，除非业务文档说明用户必须在执行前看到结果。
- 需要向用户展示“执行后影响”时，用普通只读 `GET` 查询实现，与命令无绑定。

## 归属

| 内容 | 位置 |
|---|---|
| 状态能否流转 | 实体方法，违反抛业务异常（422） |
| 跨表、查库校验 | Application Service |
| 审计 | 与业务同事务，用 `Operations.*` 常量 |
| 错误转换 | 全局异常处理器；Service 不写 try/catch 转错误 |
| 权限 | Controller 特性；Service 不查权限 |

## 必须先询问用户

表间依赖、字段引用、唯一约束、数据库结构的任何改变：停止实施，给出原因与方案，等待确认。

## 禁止

- 新增命令管道、Support、Builder、执行器包装、自定义锁服务。
- 为某类单据新建幂等表。
- 在仓储里或 Service 外开启/提交事务。
