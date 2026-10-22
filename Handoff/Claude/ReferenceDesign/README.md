# ReferenceDesign — 后端参考设计（Agent 必读）

本目录是 SwiftExport 后端的**目标结构样板**。每个 `.md` 文件代表一个将来的 `.cs` 类，内容为伪代码。
目录结构就是真实项目应有的结构：**项目 / 功能模块 / 功能 / 类**。

上位文档：`../Backend-Architecture-Guide.md`（原则与理由）。本目录给出可直接照抄的形状。

> **示意业务声明**：本目录中的产品、采购单、入库单、库存及 `DocumentLifecycle.md` 的状态与字段均为**虚构示意**，只用来演示代码形状（分层、事务、幂等、锁、命名）。SwiftExport 真实业务规则以 `.agents/references` 与 Core 实体为准，不得把样板中的字段、状态或数量规则当作真实需求实现。

## 1. Agent 行为边界（硬性规则）

1. **新功能必须找到本目录中最相似的样板照抄形状**：
   - 主数据（无状态流转）→ `MasterData/Products`
   - 有确认流程的业务单据 → `Procurement/PurchaseOrders`
   - 影响库存的单据（过账）→ `Inventory/PurchaseReceipts` + `Inventory/Stock`
2. **不得新增**：`*Support`、`*Helper`、`*Manager`、`*Pipeline`、`*Builder`、`*Policy`（静态规则类除外，见下）、自定义中介者、自定义映射器、自定义校验框架、自定义锁服务。
3. **不得在以下位置写这些东西**：

   | 不准写的东西 | 应该在 |
   |---|---|
   | 权限检查代码 | Controller 上 `[HasPermission]` |
   | `try/catch` 转错误结果 | `Api/Errors/GlobalExceptionHandler` |
   | `BeginTransaction/Commit/Rollback` | `Infrastructure/Persistence/DbSession`（实现 `IUnitOfWork`） |
   | 幂等记录读写 | `Application/Common/CommandExecutor` |
   | 状态是否允许流转的 if | 实体方法（如 `PurchaseOrder.Confirm()`） |
   | 跨单据/查库校验 | Application Service |
   | SQL | Infrastructure Repository / Queries |
   | 格式校验（必填、长度、范围） | FluentValidation `XxxValidator` |
   | 对象属性复制 | Mapster |
   | 操作名/审计动作字符串 | `Application/Common/Operations` 常量，禁止字面量 |
   | JSON 序列化 | 只用 System.Text.Json，禁止 Newtonsoft |

4. 一个用例的代码必须能**在一个 Service 方法里从上到下读完业务步骤**。
5. 改动 `Contracts` 时遵守 netstandard2.0 约束（见 `src/SwiftExport.Contracts/README.md`）。
6. 第三方库用法见 `Toolbox.md`；不得重复造已有库的轮子，也不得未经说明引入新库。

## 2. 目录

```
ReferenceDesign/
  README.md                  ← 本文件
  Toolbox.md                 ← 第三方库：用什么、怎么用、不准自己写什么
  DocumentLifecycle.md       ← 单据生命周期、状态、必备字段总表
  src/
	SwiftExport.Contracts/   ← 前后端共用 DTO（netstandard2.0）
	SwiftExport.Domain/      ← 实体、状态机
	SwiftExport.Application/ ← 用例、事务边界、校验器、映射配置
	SwiftExport.Infrastructure/ ← Dapper SQL、DbSession、幂等存储
	SwiftExport.Api/         ← Controller、授权、异常处理、Program
```

## 3. 三种 API 形状速查

| 类型 | 例 | 链路 |
|---|---|---|
| 查询 | 产品列表、采购单详情 | Controller → `XxxQueries` → Dapper → DTO |
| 普通写入 | 产品新建/修改、单据草稿保存 | Controller → Validator(自动) → `XxxService` → `uow.ExecuteAsync` → 实体方法 → Repository |
| 单据命令 | 确认/取消确认/过账/冲销/作废 | Controller → `XxxService` → `commands.ExecuteIdempotentAsync` → 实体方法 → Repository → 审计 |
