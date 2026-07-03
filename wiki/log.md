# SwiftExport AI Wiki Log

## 2026-07-03 ingest | 整理 .NET 10 前端 MVVM 基础设计

- 读取 `raw/wpf-communitytoolkit-mvvm.md`、`raw/wpf-mvvm-template.md`、`raw/wpf-navigation-service.md`、`raw/wpf-dialog-service.md`、`raw/wpf-message-service.md`、`raw/wpf-designer-page-navigator.md`、`raw/wpf-designer-Image.md`、`raw/mapster-mapping.md` 等新增来源。
- 新增 `wiki/project/dotnet10-frontend-mvvm-blueprint.md`，作为未来 .NET 10 WPF 前端的 MVVM 基础设计总纲，明确 CommunityToolkit、基类层次、DataGrid 行模型、UI Service、分页、图片列和 Mapster 边界。
- 新增 `wiki/guides/frontend-wpf/wpf-communitytoolkit-mvvm.md`，作为未来新前端具体页面开发时的 CommunityToolkit MVVM guide。
- 更新 `wiki/index.md`、`wiki/guides/index.md` 和 `wiki/project/前端页面总纲.md`，区分当前 .NET Framework 4.8 / MVVMLight 前端与未来 .NET 10 / CommunityToolkit 前端。
- 本次只整理规则，不迁移现有 WPF 页面，不修改业务代码。

## 2026-07-02 backend | 建立 ASP.NET 后端 API 工作流

- 新增 `wiki/workflows/backend/aspnet-api-workflow.md`，固化 SwiftExport 后端 API 开发顺序：Contracts DTO/API/枚举、Application Interface、Application Service、必要时 Core Repository Interface、Infrastructure Dapper 实现、WebApi Controller 转发和 Refit 契约同步。
- 明确 WebApi 只做 HTTP 边界转发，重度业务逻辑、校验、事务、状态流转和跨仓储编排放在 Application 层。
- 明确前端/API 可见的联表只读模型优先放在 `SwiftExport.Contracts` DTO，允许 Infrastructure Dapper 直接映射 DTO 返回给服务层；Core 默认不承载前端需要的枚举和只读模型。
- 更新 `wiki/index.md`、`wiki/workflows/index.md`、Application Service Guide、Dapper Repository Skill 和 DTO Guide，补充该工作流入口和分层边界。

## 2026-07-02 planning | 推进模块细化总纲

- 新增 `wiki/project/数据迁移与种子总纲.md`，规定 SQL 幂等脚本、基础字典、单号规则、`_tools/DbSeed`、本地开发数据和不可破坏数据边界。
- 新增 `wiki/project/产品BOM模块细化总纲.md`，细化产品、客户产品、供应商产品、BOM、`MaterialType`、`SupplyType` 和 BOM 拆解基础资料规则。
- 新增 `wiki/project/销售订单需求链路细化总纲.md`，细化销售订单确认、毛需求、净需求、拆解、转采购入口、取消确认和改单边界。
- 新增 `wiki/project/采购入库PackingList闭环细化总纲.md`，细化净需求转采购、采购入库、出库、PackingList 过账、客供件扣减和库存流水闭环。
- 更新 `wiki/index.md`，为四份细化总纲增加快速路由。

## 2026-07-02 qa | 建立测试与验收总纲

- 新增 `wiki/project/测试与验收总纲.md`，作为 SwiftExport 开发任务完成后的验证入口。
- 明确 `MateWpfThemes.FW.slnx` 是主题/控件库解决方案，`SwiftExport-Frontend.slnx` 是前端解决方案，`SwiftExport-WebAPI.slnx` 是后端 WebApi 解决方案，`SwiftExport.slnx` 是总项目解决方案。
- 规定按改动范围选择构建命令，并补充纯文档、后端、前端、Contracts、数据库、主题库等验收路径。
- 补充 ERP 主流程手工验收清单，覆盖销售订单、毛需求、净需求、采购、入库、出库、PackingList、改单、反过账和红冲。
- 更新 `wiki/index.md`，增加测试与验收总纲路由。

## 2026-07-02 frontend | 建立前端页面总纲

- 新增 `wiki/project/前端页面总纲.md`，作为 SwiftExport WPF/VSTO 页面开发的总入口。
- 明确本总纲只覆盖当前桌面端前端，不纳入未来 BS 页面规范；未来 BS 前端通过 API 契约总纲保持接口可消费。
- 参考现有 SalesOrder 页面设计，规定 Manager 页面负责查询、主从列表、业务命令和打开详情，Detail 页面负责头行编辑、行集合编辑、保存和关闭。
- 强化 View 不轻易拆、ViewModel 可按业务职责拆分，以及 `BaseLineModel` / `BaseListModel` 作为可编辑行、批量保存和行状态追踪基础设施的优先地位。
- 更新 `wiki/index.md`，增加前端页面总纲路由。

## 2026-07-02 contract | 建立 API 契约总纲

- 新增 `wiki/project/API契约总纲.md`，定义 WebApi Controller、Contracts DTO、Refit Interface、Swagger/OpenAPI 与未来 BS 前端之间的契约边界。
- 明确 Refit 是当前 WPF/VSTO 强类型客户端，长期稳定契约应落在 HTTP 路由、Request/Response、统一错误和 OpenAPI 文档上。
- 规定新增 API 优先区分查询、保存、业务命令、过账/反过账/红冲、Lookup 等类型，避免把复杂业务动作伪装成普通 Update。
- 更新 `wiki/index.md`，增加 API 契约总纲路由。

## 2026-07-02 business | 建立领域模块总纲

- 新增 `wiki/project/领域模块总纲.md`，按产品、BOM、客户、客户产品、供应商、仓库、销售订单、毛需求、净需求、采购、入库、出库、PackingList、投诉补货、生产和外协拆分模块边界。
- 明确各模块职责、不应负责的事项、主要源码入口和上下游交互规则。
- 明确新功能归属判断顺序，避免后续实现绕开净需求、正式单据或库存流水。
- 更新 `wiki/index.md`，增加领域模块总纲路由。

## 2026-07-02 data | 建立数据模型总纲

- 新增 `wiki/project/数据模型总纲.md`，作为 SwiftExport 数据库、实体、DTO、Repository、状态机和重构工作的模型地基。
- 明确新表统一使用 `Uid` 技术主键，历史 `Id` 表按兼容迁移逐步处理。
- 明确业务编码唯一可读、弱外键优先、业务表软删除、库存流水不可变、`DemandExecutionLink` 作为需求执行主追溯表。
- 更新 `wiki/index.md`，增加数据模型总纲路由。

## 2026-07-02 business | 建立单据状态机与轻量改单机制

- 新增 `wiki/project/document-state-and-change-policy.md`，沉淀适合小团队内部 ERP 的状态分层、业务命令和改单边界。
- 明确轻量改单策略：库存流水之前允许安全回退并重跑流程；库存流水之后必须反过账或红冲。
- 明确 `OrderStatus` 只做展示汇总，确认状态、生命周期、执行进度和过账状态应分层表达。
- 更新 `wiki/index.md`，增加单据状态机与轻量改单机制路由。

## 2026-07-01 backend | BaseService/BaseController 去泛型模板化

- 将 `BaseService<TEntity,TDto>` 替换为轻量 `ApplicationServiceBase`，只承载 `ServiceBaseContext`、`DbSession` 和 `Logger`。
- 删除有效编译路径中的 `IBaseService<TEntity,TDto>` 与 `BaseController<TEntity,TDto>`，Controller 改为显式声明自身 HTTP action，保留既有路由形状。
- 各业务 Service 通过组合方式复用单实体 CRUD，批量保存和复杂查询由具体 Service/Repository 显式公开，不再通过继承隐式获得。
- 删除 `DapperRepositoryComplexHelper`，具体仓储中的动态分页/排序逻辑改为仓储私有方法；`DapperRepository<TEntity>` 不再依赖该 helper。
- 更新后端 Dapper、Application Service、WebApi Controller guide，记录“业务服务/控制器不再使用泛型大基类”的边界。
- 已运行 `dotnet build .\SwiftExport-WebAPI.slnx`，后端构建 0 warning / 0 error。

## 2026-07-01 backend | Dapper 泛型仓储瘦身

- 将 `IRepository<TEntity>` 收敛为轻量单实体 CRUD 与基础按 Uid 查询接口，不再默认暴露批量写入、动态查询和分页。
- 重写 `DapperRepository<TEntity>` 为轻量基类，保留 `AddAsync`、`UpdateAsync`、`DeleteAsync`、`DeleteSoftAsync` 等默认实现，并拆出可重写的写入钩子。
- 新增仓储能力接口：`IBatchWriteRepository<TEntity>`、`IWhereQueryRepository<TEntity>`、`IObjectQueryRepository<TEntity>`、`ICriteriaQueryRepository<TEntity>`、`IPagedCriteriaRepository<TEntity>`。
- 将批量写入、动态条件和分页复用逻辑移入 Infrastructure 内部 helper，由具体仓储显式公开复杂能力。
- 调整 `BaseService`，复杂查询/批量保存改为检测能力接口；订单、收货、PackingList、产品、客户产品、货币等调用点改为依赖具体仓储能力。
- 已运行 `dotnet build .\SwiftExport-WebAPI.slnx`，后端构建通过。

## 2026-07-01 backend | Core 订单实体关系优化

- 按 ERP 业务总纲补强 Core 订单实体：BOM 父子 SKU/Uid 关系、单据明细行号、净需求执行关系、正式出库单头/行。
- 删除废弃实体 `SwiftExport.Core/Entities/Orders/BillOfMaterial.cs`；数据库历史表如存在不在本次自动删除。
- 新增 `DemandExecutionLink` 承接净需求到采购、生产、外协等执行单据的关系，为“净需求多对一聚合到采购行”保留正式追溯模型。
- 新增 `OutboundHead`、`OutboundLine`，明确后续出库必须通过正式业务单据承接，库存流水只做审计和库存变动记录。
- 新增幂等 SQL 脚本 `tools/sql/20260701_core_order_entities_optimization.sql`，补齐相关表和字段。

## 2026-06-30 business | 建立 ERP 业务总纲

- 新增 `wiki/project/erp-business-blueprint.md`，作为后续智能体理解 SwiftExport ERP 业务流程的总入口。
- 明确业务主线：销售订单、手工需求、投诉补货进入毛需求，BOM 拆解形成净需求，净需求转采购，入库/出库/PackingList 过账完成闭环。
- 明确客供件、自动入库/出库、PackingList 智能过账、展示状态与执行状态分离等长期业务默认规则。
- 更新 `wiki/index.md`，增加 ERP 业务总纲路由；更新 `AGENTS.md`，要求业务流程类任务先读业务总纲。

## 2026-06-30 frontend | MVVM 基类职责解耦

- 新增 `SwiftExport.View.FW.Mvvm.ObservableObjectBase` 作为轻量可绑定对象基类，只承载属性通知、`SetProperty` 和 `DisabledStateScope`。
- 调整 `BaseLineModel` 从页面级 `ViewModelBaseEx` 解耦，继续保留行状态、脏数据、选中、删除、验证和 DTO 加载职责。
- 修正树节点/分类树纯 UI 状态赋值路径，避免 `IsExpanded`、`IsSelected`、`IsVisible`、`IsMatch` 触发行脏数据。
- 更新 MVVMLight 与 DataGrid guide，明确页面 ViewModel、轻量模型基类和行模型基类的职责边界。

## 2026-06-30 ingest | SwiftExport WPF/MVVM raw 草稿项目化

- 根据 Git 新增文件读取 `raw/inbox/erp-datagrid-editing.md`、`mapster-mapping.md`、`wpf-*.md` 等 9 个 raw 草稿。
- 用户允许本次改写新增 raw 来源；已将 CommunityToolkit / .NET 8 模板口径调整为 SwiftExport 默认的 .NET Framework 4.8 + MVVMLight + Refit + MateWpfThemes 口径。
- 新增正式 wiki：`wiki/guides/shared/mapster-mapping.md`、`wiki/guides/frontend-wpf/wpf-ui-services.md`、`wiki/guides/frontend-wpf/wpf-pagination.md`、`wiki/guides/frontend-wpf/wpf-image-cell.md`。
- 更新 `wiki/index.md` 和 `wiki/guides/index.md`，为 Mapster、UI Services、Pagination、Image Cell 增加链接式路由。
- 增强 MVVMLight、DataGrid、Dialog Navigation 和 WPF DataGrid Editing 规则，明确 SwiftExport 优先使用 MVVMLight、`IDialogService`、现有 Routing、DataGridExcelAssist / UndoManager。

## 2026-06-28 frontend | 新增 SearchableTextBoxPlus 规则

- 在 `MateWpfThemes.FW` 新增搜索选择控件 `SearchableTextBoxPlus`，旧 `SearchableTextBox` 保持兼容不改动。
- 更新前端工作流和主题资源 guide：后续新增搜索选择场景优先使用 `SearchableTextBoxPlus`。
- 明确旧页面迁移时需逐页检查 `Text`、`SelectedItem`、`SelectedValue` 绑定语义。

## 2026-06-28 ingest | Inbox 模板与 Guide 提炼

- 读取 `raw/inbox/guides/**`、`raw/inbox/skills/**`、`raw/inbox/templates/**` 中与 SwiftExport 技术栈匹配的模板资料。
- 新增 `wiki/project/context-summary.md`，压缩记录当前 AI Wiki 分层、项目架构、入口和工作流规则。
- 新增 `wiki/guides/**`，提炼 Application Service、Dapper Repository、数据库结构、Controller、DTO、Refit、ServiceResult、MVVMLight、DataGrid、Dialog/Navigation、Theme/Resource、安全重构和 Codex 任务书写规范。
- 新增 `wiki/skills/index.md`、`wiki/workflows/index.md`，并将总入口 `wiki/index.md` 改为“Markdown 链接 + 什么时候读取”的混合索引。
- 新增正式 skill：`wiki/skills/shared/refit-contract-change.md`、`wiki/skills/frontend-wpf/wpf-datagrid-editing.md`、`wiki/skills/frontend-wpf/wpf-theme-fix.md`。
- 新增正式 workflow：`wiki/workflows/safe-refactoring.md`，并增强 WPF/VSTO 前端工作流与 Dapper Repository skill。
- 保留 `raw/inbox/**` 原文不删除、不移动、不重写；模板源码和通用假设不整段搬入 wiki，只提炼与 SwiftExport 当前架构一致的规则。

## 2026-06-28 workflow | 调整为严格 LLM Wiki 分层

- 将正式 skill、workflow、项目地图从 `raw/**` 移入 `wiki/**`。
- 更新 `AGENTS.md`：`raw/` 定义为默认只读的原始资料层，`wiki/` 定义为正式知识层。
- 更新 `wiki/index.md`：任务路由全部指向 `wiki/skills/**`、`wiki/workflows/**`、`wiki/project/**`。
- 保留 `raw/` 作为后续 ingest 的来源层，不再承载正式可执行规则。

## 2026-06-28 workflow | 清理无效工作流引用

- 清理 `AGENTS.md` 中关于旧规则来源的说明，保留当前 AI 工作流铁律。
- 清理 `wiki/index.md` 中旧资料索引，仅保留当前有效的 `raw/**` 路由。
- 清理 `raw/project/**` 中的来源说明。
- 删除无效 AI 工作流文件，当前以 `raw/**` + `wiki/index.md` 为准。

## 2026-06-28 workflow | 任务路由迁移到 index

- 调整 `AGENTS.md`：移除具体后端、前端、项目地图任务路由条目。
- 将具体任务路由集中到 `wiki/index.md`，后续新增 skill / workflow 只更新 index 和 log。
- 保留 `AGENTS.md` 中的稳定铁律：任务先读 index、AI 资产 ingest 必须更新 index 和 log、源码事实优先。

## 2026-06-28 workflow | 补充 AI 资产 Ingest 规则

- 更新 `AGENTS.md`，新增“AI 资产 Ingest 工作流”。
- 明确手动新增 skill、workflow、project doc 后，应读取新资产并更新 `wiki/index.md`。
- 明确 ingest 必须更新 `wiki/log.md`，并在 raw/wiki 与源码冲突时标记“可能过期”。
- 明确 ingest 文档不应修改业务源码，也不应自动删除用户手动新增的 raw 文件。

## 2026-06-28 workflow | 建立 AI Wiki + Codex 工作流

- 重写 `AGENTS.md`，将其定位为 Codex 行为规则层。
- 新建 `raw/` + `wiki/` 工作流结构。
- 新建 `wiki/index.md`，作为 Codex 查询、skill、workflow、项目文档的统一入口。
- 新建 `raw/skills/backend/**` 的后端 skill。
- 新建 `raw/workflows/frontend/wpf-vsto-workflow.md`，定义 WPF/VSTO 前端开发边界。
- 新建 `raw/project/**`，沉淀项目地图和入门说明。
