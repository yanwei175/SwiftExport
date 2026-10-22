# 单据状态流转技术契约（不含业务）

> 本文件只规定"状态流转类代码"的技术形状，不定义任何业务状态、字段、数量规则或权限码。
> 真实的状态枚举、字段、可执行操作及前置条件以 `.agents/references` 与 Core 实体为准；Core 实体与表结构保留，表间依赖、字段引用、唯一约束及结构变更必须先问用户（决策 #2、#3）。

## 1. 状态检查放在哪里

| 检查 | 位置 | 失败时 |
|---|---|---|
| 当前用户能否执行这类操作 | Api：端点权限声明 + 授权策略（决策 #9） | 403 |
| 单据当前状态是否允许该操作 | Domain 实体方法内部 | `BusinessException` → 422 |
| 需要查库的跨单据条件 | Application Service（事务内） | `BusinessException` → 422 |
| 客户端看到的版本是否过期 | Repository 版本条件更新 | 409 |

权限与状态互不替代：权限回答"这个人能不能做这类操作"，状态回答"这张单现在能不能做"。

## 2. 实体方法形状

- 每个状态流转是实体上的一个方法；方法内先检查允许的来源状态，再修改状态及该实体已有的操作人/时间字段。
- 不在 Controller、Service 或 SQL 中写"状态是否允许流转"的 if。
- 具体有哪些方法、允许哪些来源状态，以业务文档和 Core 实体为准；`ReferenceDesign/src/**` 中的 Draft/Confirmed/Posted 等仅为虚构示意。

## 3. 写入保护

| 机制 | 契约 |
|---|---|
| 事务 | 只在 Application Service / `CommandExecutor` 经 `IUnitOfWork` 开启（决策 #6） |
| 并发 | 沿用 Core 实体现有 `Version`：`UPDATE ... WHERE version = @Version`，影响 0 行即 409 |
| 共享资源 | 读后写共享余额等资源时使用行锁 `FOR UPDATE` 或唯一约束；不新增应用级锁服务（决策 #7） |
| 幂等 | 状态流转命令经 `CommandExecutor.ExecuteIdempotentAsync`；`CommandId` 放请求体，记录沿用现有 `command_receipt` / `CommandReceipt`（决策 #4、#5） |

## 4. 不做的事

- 不在本文件或技术样板中新增必备字段、系统表或状态枚举。
- 不为状态流转建立通用状态机框架、规则引擎或配置表。
- 无法用行锁、唯一约束和 `Version` 覆盖的并发场景，停下问用户。