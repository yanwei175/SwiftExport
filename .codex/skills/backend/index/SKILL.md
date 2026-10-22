---
name: backend
description: SwiftExport 后端（目标架构）任务入口。用于按新治理规则新增或重构 WebApi/Application/Infrastructure/Contracts 代码，尤其是正在治理的模块（首个为采购）。按任务选择子技能；未治理的旧代码只做最小修改时改用 aspdotnet10。
---

# SwiftExport 后端任务入口（目标架构）

## 定位

本组技能描述**重构后的目标架构**。现有代码中大量 Support、执行器、Pipeline、BaseService、ServiceResult 等属于过度设计，按本组规则逐模块重构替换。

- 新增功能、正在治理的模块：使用本组技能。
- 未纳入治理的旧模块只修 Bug、不重构：使用 `aspdotnet10`，不顺手迁移。
- 两套规则冲突时，在已治理/正在治理的范围内以本组为准。

## 技术与业务分离（硬规则）

- 本组技能**只规定技术形状**：分层、命名、文件组织、事务、幂等、错误、权限、日志、第三方库。
- **不包含任何业务事实**（字段、状态值、数量/金额规则、表关系）。业务以 `.agents/references` 与 Core 实体为准；业务文档过期时，由治理任务重写业务文档，不把业务写进本技能。
- ReferenceDesign 中的产品/采购单/入库单是**示意业务**，只演示代码形状，不代表 SwiftExport 真实规则。
- 表间依赖、字段引用、唯一约束、表结构的任何变更：先询问用户，给出原因与方案。

## 不可动的部分

- **Core 实体（`SwiftExport.Core/Entities/**`）保留**：字段、表名、DbCheck/DbIndex 特性不因重构而改。
- `command_receipt`（`CommandReceipt`）是唯一幂等回执表，沿用不改名。
- `SwiftExport.Contracts` 固定 `netstandard2.0`，WPF/VSTO/后端共用。

## 命名与模块（已确认）

- 一个业务对象一个文件夹，文件夹内只放固定角色文件：`XxxController`、`XxxService`（写）、`XxxQueries`（读）、`XxxRepository`、`XxxQueries.Sql`、`XxxValidators`。文件过大按用例拆 partial（`XxxService.Confirm.cs`），不新建类。
- 同一概念只用一个名字，与实体名一致（例：`PurchaseOrder`，`Procurement` 仅作模块名）。
- 路由资源式：`/api/{资源复数-kebab}/{id}/{动词}`；客户端 Refit 同步。
- 订单跟踪（OrderTracking）是唯一的跨模块追踪模块，各模块从自己的切面进入，不各自实现追踪。

## 单一规则来源（只引用，不复制）

| 文档 | 内容 |
|---|---|
| [Backend-Architecture-Guide](../../../../Handoff/Claude/Backend-Architecture-Guide.md) | 分层、授权、事务、幂等、错误、日志的原则与理由 |
| [ReferenceDesign/README](../../../../Handoff/Claude/ReferenceDesign/README.md) | **硬性禁止项**与样板索引，任何后端任务必读 |
| [Toolbox](../../../../Handoff/Claude/ReferenceDesign/Toolbox.md) | 第三方库用法；只用 System.Text.Json |
| `.agents/references/**` | 业务事实（非本技能维护） |

样板目录：`Handoff/Claude/ReferenceDesign/src/<项目>/<模块>/<功能>/<类>.md`，伪代码，照抄形状，按真实实体字段落地。

## 子技能路由

| 任务 | 加载 |
|---|---|
| 单据写入：普通保存/删除、正式状态命令、改变下游事实的命令、CommandId | [backend-document](../backend-document/SKILL.md) |
| 列表、详情、下拉、报表、导出数据、Excel 平表导入 | [backend-query](../backend-query/SKILL.md) |
| 按模块重构旧代码（首个：采购） | [backend-refactor](../backend-refactor/SKILL.md) |
| 改表、加字段、加索引、DbUp 脚本 | [backend-schema-change](../backend-schema-change/SKILL.md) |
| 构建、测试、完成判定 | [backend-delivery](../backend-delivery/SKILL.md) |

多项命中时同时加载。

## 全局硬规则（摘要，详见 ReferenceDesign/README）

1. 新功能先找最相似样板：主数据 → `MasterData/Products`；确认型单据 → `Procurement/PurchaseOrders`；库存单据 → `Inventory/PurchaseReceipts` + `Inventory/Stock`。
2. 禁止新增 `*Support`、`*Helper`、`*Manager`、`*Pipeline`、`*Executor`（`CommandExecutor` 除外）、自定义中介者/映射器/校验框架/锁服务；不引入 MediatR。
3. 权限只在 Controller `[HasPermission]`；异常只在全局 `IExceptionHandler` 转 ProblemDetails；事务只由 `IUnitOfWork`；幂等只由 `CommandExecutor`；状态流转判断只在实体方法；SQL 只在 Infrastructure。
4. 一个用例在一个 Service 方法里从上到下读完。
5. 成功直接返回 DTO，不包 `ServiceResult`。
6. 操作名用 `Operations.*` 常量；权限码用 Contracts `Permissions.*`。
7. 不确定的事实标注“待核实”，不凭样板路径声称已存在。

## 写入边界

由用户本次授权范围决定。不得未经授权：写数据库、修改已发布迁移脚本、跨模块顺手重构、删除旧模块代码、推送或部署。
