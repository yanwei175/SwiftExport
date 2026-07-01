# SwiftExport AI Wiki Log

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
