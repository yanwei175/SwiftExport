# SwiftExport AI Wiki Router

低 token 路由入口。Codex 每次任务先读本页，然后只读取命中的最小文件集。完整目录见 [Catalog](catalog.md)。

## 通用

- 接手/定位项目：读 [项目地图](project/project-map.md)。
- 重要规则变更/知识维护：读 [Wiki Ingest Workflow](workflows/wiki-ingest-workflow.md)，完成后更新 [工作日志](log.md)。
- 大范围重构：读 [Safe Refactoring Workflow](workflows/safe-refactoring.md)。
- 测试/验收/构建选择：读 [测试与验收总纲](project/测试与验收总纲.md)。

## 业务与数据

- ERP 主流程、需求、采购、入库、出库、PackingList、客供件：读 [ERP 业务总纲](project/erp-business-blueprint.md)。
- 确认、取消确认、改单、反过账、红冲：读 [单据状态机与轻量改单机制](project/document-state-and-change-policy.md)。
- 数据库表、实体、主键、业务键、库存流水、追溯关系：读 [数据模型总纲](project/数据模型总纲.md)。
- 模块归属和业务边界：读 [领域模块总纲](project/领域模块总纲.md)。
- 数据迁移、SQL 脚本、种子数据：读 [数据迁移与种子总纲](project/数据迁移与种子总纲.md)。

## 细化业务链路

- 产品、客户产品、供应商产品、BOM、物料类型、供给方式：读 [产品 BOM 模块细化总纲](project/产品BOM模块细化总纲.md)。
- 销售订单、毛需求、净需求、拆解、转采购、需求追溯：读 [销售订单需求链路细化总纲](project/销售订单需求链路细化总纲.md)。
- 采购、入库、出库、PackingList、客供件扣减、库存流水闭环：读 [采购入库 PackingList 闭环细化总纲](project/采购入库PackingList闭环细化总纲.md)。

## 后端 API

- 新增/调整后端 API、Application Service、Controller、Repository、Dapper 查询：读 [ASP.NET 后端 API 工作流](workflows/backend/aspnet-api-workflow.md)。
- 只改 Repository/SQL/事务：补读 [Dapper Repository](skills/backend/dapper-repository.md)。
- 只改 DbSession/事务上下文：补读 [DbSession 与 RepositoryBaseContext](skills/backend/db-session-context.md)。
- Controller/Refit/DTO 契约同步：补读 [API 契约总纲](project/API契约总纲.md) 和 [Refit Contract Change](skills/shared/refit-contract-change.md)。

## 当前前端：.NET 10 WPF

- 当前前端页面、MVVM、基类、Feature 目录、DataGrid 行集合模型：读 [.NET 10 前端 MVVM 基础设计](project/dotnet10-frontend-mvvm-blueprint.md)。
- CommunityToolkit 具体写法：补读 [WPF CommunityToolkit MVVM](guides/frontend-wpf/wpf-communitytoolkit-mvvm.md)。
- 页面布局、4px 间距、表单、卡片、DataGrid 页面：补读 [.NET 10 WPF Fluent Layout](guides/frontend-wpf/wpf-fluent-layout-guide.md)。
- 官方 Fluent 主题、ThemeMode、系统强调色、命名 Style：补读 [.NET 10 WPF Official Theme Style](guides/frontend-wpf/wpf-official-theme-style-guide.md)。
- DTO、Refit、Mapster：按需读 [DTO](guides/shared/dto.md)、[Refit API](guides/shared/refit-api.md)、[Mapster Mapping](guides/shared/mapster-mapping.md)。

## 旧前端兼容维护

- 旧 `.NET Framework 4.8 + MVVMLight + VSTO` 页面：读 [旧 WPF/VSTO 页面总纲](project/前端页面总纲.md)。
- 旧 MVVMLight 代码：补读 [WPF MVVMLight](guides/frontend-wpf/wpf-mvvmlight.md)。
- 旧 VSTO/TaskPane/`SwiftExport.View.FW` 工作流：补读 [WPF / VSTO 前端工作流](workflows/frontend/wpf-vsto-workflow.md)。

## 索引

- 完整 wiki 目录：[Catalog](catalog.md)。
- Workflow 子目录：[Workflows Index](workflows/index.md)。
- Skill 子目录：[Skills Index](skills/index.md)。
- Guide 子目录：[Guides Index](guides/index.md)。
