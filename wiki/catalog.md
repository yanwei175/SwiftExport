# SwiftExport AI Wiki Catalog

本页是人类浏览用完整目录。Codex 日常任务只读 [Router](index.md)，不要把本页作为默认入口。

## Project

- [项目上下文摘要](project/context-summary.md) - AI Wiki 分层、项目边界和上下文压缩记录。
- [项目地图](project/project-map.md) - 项目结构、入口、分层和构建命令。
- [项目接手说明](project/onboarding.md) - 接手新功能时的阅读顺序和风险提示。
- [ERP 业务总纲](project/erp-business-blueprint.md) - ERP 主业务流程。
- [单据状态机与轻量改单机制](project/document-state-and-change-policy.md) - 单据状态、改单、反过账和红冲。
- [数据模型总纲](project/数据模型总纲.md) - 数据库、实体、主键、业务键、库存流水和追溯。
- [领域模块总纲](project/领域模块总纲.md) - 产品、客户、销售、需求、采购、仓库、PackingList 等模块边界。
- [数据迁移与种子总纲](project/数据迁移与种子总纲.md) - SQL、DbSeed、基础字典和本地样本数据。
- [产品 BOM 模块细化总纲](project/产品BOM模块细化总纲.md) - 产品/BOM/物料类型/供给方式。
- [销售订单需求链路细化总纲](project/销售订单需求链路细化总纲.md) - 销售订单到毛需求、净需求和采购入口。
- [采购入库 PackingList 闭环细化总纲](project/采购入库PackingList闭环细化总纲.md) - 采购、入库、出库、PackingList 和库存闭环。
- [API 契约总纲](project/API契约总纲.md) - Controller、Refit、DTO、Request/Response 和未来 API 消费。
- [.NET 10 前端 MVVM 基础设计](project/dotnet10-frontend-mvvm-blueprint.md) - 当前 .NET 10 WPF 前端主线。
- [旧 WPF/VSTO 页面总纲](project/前端页面总纲.md) - 旧 .NET Framework 4.8 / MVVMLight / VSTO 兼容维护。
- [测试与验收总纲](project/测试与验收总纲.md) - 构建、测试和手工验收策略。

## Workflows

- [ASP.NET 后端 API 工作流](workflows/backend/aspnet-api-workflow.md) - 后端 API、Application、Repository、Controller、Contracts 开发顺序。
- [WPF / VSTO 前端工作流](workflows/frontend/wpf-vsto-workflow.md) - 旧 WPF/VSTO 兼容维护工作流。
- [Safe Refactoring Workflow](workflows/safe-refactoring.md) - 大范围重构、行为保持和分阶段迁移。
- [Wiki Ingest Workflow](workflows/wiki-ingest-workflow.md) - raw 来源整理到正式 wiki 的流程。
- [Workflows Index](workflows/index.md) - Workflow 子索引。

## Skills

- [DbSession 与 RepositoryBaseContext](skills/backend/db-session-context.md) - 数据库连接、事务和仓储上下文。
- [Dapper Repository](skills/backend/dapper-repository.md) - Dapper 仓储、SQL、分页、批量保存和 DI。
- [Refit Contract Change](skills/shared/refit-contract-change.md) - DTO、Refit、Controller 路由和契约同步。
- [WPF DataGrid Editing](skills/frontend-wpf/wpf-datagrid-editing.md) - 旧/通用 WPF DataGrid 编辑、行模型和删除池。
- [WPF Theme Fix](skills/frontend-wpf/wpf-theme-fix.md) - 旧/通用 WPF 主题资源和视觉修复。
- [Skills Index](skills/index.md) - Skill 子索引。

## Guides

- [Application Service](guides/backend/application-service.md) - Application Service 编排、事务和仓储调用。
- [Dapper Repository Guide](guides/backend/dapper-repository.md) - 仓储实现检查清单。
- [WebApi Controller](guides/backend/webapi-controller.md) - Controller 边界、路由和返回结构。
- [Database Schema](guides/backend/database-schema.md) - 表结构、审计、软删除和 SQL 字段映射。
- [DTO](guides/shared/dto.md) - DTO、Request、Response 和传输模型边界。
- [Mapster Mapping](guides/shared/mapster-mapping.md) - Entity、DTO、LineModel、Request/Response 映射。
- [Refit API](guides/shared/refit-api.md) - Refit Interface、HTTP 方法和路由。
- [Service Result](guides/shared/service-result.md) - 统一返回、错误处理和前端解析。
- [WPF CommunityToolkit MVVM](guides/frontend-wpf/wpf-communitytoolkit-mvvm.md) - 当前 .NET 10 WPF / CommunityToolkit MVVM 细则。
- [.NET 10 WPF Fluent Layout](guides/frontend-wpf/wpf-fluent-layout-guide.md) - 当前 .NET 10 WPF 页面布局。
- [.NET 10 WPF Official Theme Style](guides/frontend-wpf/wpf-official-theme-style-guide.md) - 当前 .NET 10 WPF 官方 Fluent 主题。
- [WPF MVVMLight](guides/frontend-wpf/wpf-mvvmlight.md) - 旧 MVVMLight 兼容维护。
- [WPF DataGrid](guides/frontend-wpf/wpf-datagrid.md) - WPF DataGrid、LineModel、批量保存和脏数据。
- [WPF Dialog Navigation](guides/frontend-wpf/wpf-dialog-navigation.md) - 弹窗、导航和未保存变更提示。
- [WPF UI Services](guides/frontend-wpf/wpf-ui-services.md) - Dialog、Message、Routing 等 UI Service 边界。
- [WPF Pagination](guides/frontend-wpf/wpf-pagination.md) - 分页控件和未保存变更拦截。
- [WPF Image Cell](guides/frontend-wpf/wpf-image-cell.md) - 图片列、缩略图和预览。
- [WPF Theme Resource](guides/frontend-wpf/wpf-theme-resource.md) - 旧/通用 WPF 主题资源。
- [Safe Refactoring Guide](guides/workflow/safe-refactoring.md) - 重构检查清单。
- [Codex Task Writing](guides/workflow/codex-task-writing.md) - Codex 任务书写规范。
- [Guides Index](guides/index.md) - Guide 子索引。

## Log

- [工作日志](log.md) - AI Wiki 规则变更、ingest 和重要知识维护记录。

## Raw

`raw/` 是来源层，不参与 Codex 日常任务路由。已提炼进 wiki 或不适合当前 SwiftExport 架构的 raw 来源不在本目录中逐项索引；需要整理 raw 时读取 [Wiki Ingest Workflow](workflows/wiki-ingest-workflow.md)。
