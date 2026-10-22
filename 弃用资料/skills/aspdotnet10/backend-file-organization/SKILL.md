---
name: aspdotnet10-backend-file-organization
description: SwiftExport 后端文件与职责拆分技能。用于规划或重构复杂 Controller、Application Service、Interface、Policy、Repository、Contracts DTO/API 的目录与文件边界，识别 Manager/God Service、partial 仅切文件、页面驱动聚合和入出库镜像复制；不改变未确认业务规则。
---

# Backend File / Responsibility Organization Skill

## 必读

- `../api-application-service/SKILL.md`
- 目标模块的 Controller、Contracts API/DTO、Application Interface/Service、Repository Interface/实现、DI 和测试
- 目标模块能力地图；没有能力地图时先列出真实 API 用例和消费者

## 判定原则

- 页面不是后端服务边界；一个页面可以组合多个窄 API，一个服务也可以服务多个真实消费者。
- 按稳定用例族拆分：`Query`、`Source/Lookup`、`Draft`、`Confirmation`、`Posting`、`Reversal`、`Trace`。模块只创建实际需要的族，不机械凑齐。
- Controller 按资源或相关 HTTP 能力分组；Application Interface 按调用方需要保持窄小；Service 按业务事务边界组织；Repository 按读模型或写入事实组织。
- `partial` 只解决物理文件长度，不代表职责已经拆分。若同一类型仍承担多个用例族、依赖集合和事务边界，应拆接口和实现。
- 公共化执行机制，不公共化领域判断。允许复用事务/幂等/错误包装；禁止建立隐藏采购、入库、出库差异的万能 `DocumentService<T>`。
- 标准单表 CRUD 可使用 `BaseService`；复杂单据不继承通用 CRUD 来暴露不合法写入口。

## 必须评估拆分的信号

- 一个 Service 同时负责查询、草稿、确认、过账和反向中的三个或更多用例族。
- 多个 Controller 注入同一宽接口，而每个 Controller 只使用少量不同方法。
- 构造函数依赖持续增长，不同方法使用互不相交的依赖子集。
- 同一类存在多个独立事务入口、权限族、错误包装器或回执协议。
- 入库/出库等平行模块复制大段执行框架，只替换实体和文案。
- `Manager`、`Service`、`Helper` 文件名无法表达查询、命令、策略或基础设施职责。

这些信号触发分析，不以固定行数或方法数自动判错。拆分必须减少职责耦合，并保持事务原子性和调用方兼容。

## 文件组织

- 一个公开顶级 DTO 默认一个文件；历史聚合文件触碰时小步治理。
- Interface 与实现按同一业务用例命名，例如 `IInboundDraftService` / `InboundDraftService`。
- 纯业务判断放 `Policy` 或专用 Builder；它不持有 Controller、连接或事务。
- 查询 SQL 与命令写入 SQL差异明显时拆 QueryRepository / CommandRepository；不要为每个方法建立 Repository。
- 共享执行器、上下文和错误映射放稳定公共目录，不放进某个业务模块后再被跨域引用。
- 文件移动或拆接口前建立调用方矩阵；旧接口只有在消费者迁移并验证后才能删除。

## 详细参考

- 规划复杂单据目录、接口和文件清单时读 `references/复杂单据文件切片.md`。
- 涉及业务命令、事务或幂等时同时读 `../application-document-command/SKILL.md`。
- 完成重构时读 `../backend-quality-gates/SKILL.md`。

